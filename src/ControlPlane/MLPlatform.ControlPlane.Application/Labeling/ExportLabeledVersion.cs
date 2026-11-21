using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Labels → a dataset version a recipe can train on: the labeled files, a YOLO `.txt` next to every image
/// (`class cx cy w h`, normalized, class = position in the schema).</summary>
[RequiresPermission("dataset.write")]
public sealed record ExportLabeledVersionCommand(Guid WorkspaceId, Guid ProjectId, bool AllLabeled, string Note) : ICommand<LabelExport>;

/// <summary>Copies the labeled files inside storage, writes the label files next to them and records the new
/// version.</summary>
public sealed class ExportLabeledVersionCommandHandler(IWriteSession db, IClock clock, LabelAccess access, WorkspaceAuthorizer auth, S3Client s3)
    : ICommandHandler<ExportLabeledVersionCommand, LabelExport>
{
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    public async ValueTask<LabelExport> Handle(ExportLabeledVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "dataset.read", ct);
        var paths = (await db.ExportItemsAsync(p.Id, c.AllLabeled, ct)).Select(i => i.Path).ToList();
        if (paths.Count == 0)
            throw new PreconditionException("LABEL_EXPORT_EMPTY", c.AllLabeled ? "No submitted items yet" : "No accepted items yet — accept items in review, or include items still in review");
        if (paths.Count > 50_000) throw new PreconditionException("LABEL_EXPORT_TOO_LARGE", "More than 50,000 items — export in parts");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct) ?? throw new PreconditionException("LABEL_SCHEMA_REQUIRED", "Dataset has no label schema yet");
        var (classIndex, classNames) = Classes(schema.Classes);

        VersionRow? src = p.VersionId is { } vid ? await db.GetVersionAsync(vid, c.WorkspaceId, ct) : null;
        var sizes = src is not null
            ? (await db.ListVersionFilesAsync(src.Id, ct)).ToDictionary(f => f.Path, f => (f.SizeBytes, f.Url))
            : (await db.ListFilesAsync(p.DatasetId, ct)).ToDictionary(f => f.Path, f => (f.SizeBytes, (string?)null));
        var srcPrefix = src?.Prefix ?? Buckets.Raw(c.WorkspaceId, p.DatasetId);
        paths = paths.Where(sizes.ContainsKey).ToList();
        if (paths.Count == 0) throw new PreconditionException("LABEL_EXPORT_EMPTY", "The labeled files are no longer in the dataset");

        var rows = await db.ExportAnnotationsAsync(p.DatasetId, p.Id, c.AllLabeled, ct);
        var byPath = rows.ToLookup(r => r.Path);
        var n = await db.NextVersionNumberAsync(p.DatasetId, c.WorkspaceId, ct);
        var prefix = Buckets.Version(c.WorkspaceId, p.DatasetId, n);
        var id = Ids.New();
        var files = new List<(string Path, long Size, string? Url)>();
        var taken = paths.ToHashSet();
        long boxes = 0, other = 0;
        using var jsonl = new MemoryStream();
        foreach (var path in paths)
        {
            var (size, url) = sizes[path];
            if (url is null) await s3.CopyAsync(Buckets.Datasets, srcPrefix + path, Buckets.Datasets, prefix + path, ct);
            files.Add((path, size, url));
            var yolo = new StringBuilder();
            foreach (var a in byPath[path])
            {
                using (var w = new Utf8JsonWriter(jsonl)) LabelRules.WriteOne(w, a.Path, a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source);
                jsonl.WriteByte((byte)'\n');
                if (a.Kind == "bbox" && classIndex.TryGetValue(a.ClassId, out var ci) && Box(a.Geometry) is { } b)
                {
                    yolo.Append(CultureInfo.InvariantCulture, $"{ci} {b.X + b.W / 2:0.######} {b.Y + b.H / 2:0.######} {b.W:0.######} {b.H:0.######}\n");
                    boxes++;
                }
                else other++;
            }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var txt = Path.ChangeExtension(path, ".txt");
            if (ImageExt.Contains(ext) && taken.Add(txt)) files.Add(await PutAsync(prefix, txt, Encoding.UTF8.GetBytes(yolo.ToString()), "text/plain", ct));
        }
        if (taken.Add("labels.jsonl")) files.Add(await PutAsync(prefix, "labels.jsonl", jsonl.ToArray(), "application/x-ndjson", ct));
        if (boxes > 0)
        {
            var names = JsonSerializer.Serialize(classNames, LabelExportJson.Default.ListString);
            if (taken.Add("data.yaml"))
                files.Add(await PutAsync(prefix, "data.yaml", Encoding.UTF8.GetBytes($"# Labels of labeling project \"{p.Name.Replace('"', ' ')}\"\nnc: {classNames.Count}\nnames: {names}\n"), "text/yaml", ct));
            if (taken.Add("classes.txt")) files.Add(await PutAsync(prefix, "classes.txt", Encoding.UTF8.GetBytes(string.Join('\n', classNames) + "\n"), "text/plain", ct));
        }
        var now = clock.UtcNow;
        var note = string.IsNullOrWhiteSpace(c.Note) ? $"Labels of project {p.Name}: {paths.Count} items" : c.Note.Trim();
        var total = files.Sum(f => f.Size);
        await db.InsertVersionAsync(id, p.DatasetId, n, prefix, files.Count, total, note, uid, now, ct);
        foreach (var f in files)
            if (f.Url is null) await db.InsertVersionFileAsync(id, f.Path, f.Size, ct);
            else await db.InsertVersionFileUrlAsync(id, f.Path, f.Size, f.Url, ct);
        return new LabelExport(new VersionRow(id, p.DatasetId, n, prefix, files.Count, total, note, now, "copied", null), paths.Count, boxes, other);
    }

    private async Task<(string Path, long Size, string? Url)> PutAsync(string prefix, string path, byte[] body, string type, CancellationToken ct)
    {
        await s3.PutAsync(Buckets.Datasets, prefix + path, body, type, ct);
        return (path, body.Length, null);
    }

    /// <summary>Schema classes in order: class id → YOLO index, and the names by index.</summary>
    private static (Dictionary<int, int> Index, List<string> Names) Classes(string json)
    {
        var index = new Dictionary<int, int>();
        var names = new List<string>();
        using var doc = JsonDocument.Parse(json);
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            index[c.GetProperty("id").GetInt32()] = names.Count;
            names.Add((c.GetProperty("name").GetString() ?? "").Replace('\n', ' ').Trim());
        }
        return (index, names);
    }

    private static (double X, double Y, double W, double H)? Box(string geometry)
    {
        try
        {
            using var g = JsonDocument.Parse(geometry);
            var r = g.RootElement;
            return (r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("w").GetDouble(), r.GetProperty("h").GetDouble());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
