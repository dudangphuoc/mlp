using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>A version with its dataset and its train/val/test split, as the training canvas shows it.</summary>
public sealed record VersionSplitInfo(VersionRow Version, string DatasetName, string Task, string State, IReadOnlyDictionary<string, long> Counts);

/// <summary>
/// Fixed dataset splits (D-49). The same rule lives in <c>recipes/_shared/converters/to_coco.py</c>: an image's split is
/// <c>sha256("{seed}:{key}")</c> (first 8 bytes, big endian) mod 10 000, compared with the ratios — so it depends on the path only.
/// </summary>
public static class SplitRules
{
    public const string File = "mlp-split.json";
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>train | val | test from a directory name in the path (same aliases as the converter), or null.</summary>
    public static string? FolderSplit(string path)
    {
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
            switch (parts[i].ToLowerInvariant())
            {
                case "train" or "training": return "train";
                case "val" or "valid" or "validation": return "val";
                case "test": return "test";
            }
        return null;
    }

    public static int Bucket(string seed, string key) =>
        (int)(BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + key))) % 10_000UL);

    public static string CountsJson(IReadOnlyDictionary<string, long> counts) =>
        new JsonObject { ["train"] = counts.GetValueOrDefault("train"), ["val"] = counts.GetValueOrDefault("val"), ["test"] = counts.GetValueOrDefault("test") }.ToJsonString();

    public static Dictionary<string, long> ParseCounts(string? json)
    {
        var res = new Dictionary<string, long>();
        if (string.IsNullOrEmpty(json) || JsonNode.Parse(json) is not JsonObject o) return res;
        foreach (var (k, v) in o) if (v is JsonValue jv && jv.TryGetValue<long>(out var n)) res[k] = n;
        return res;
    }
}

/// <summary>Reads a version's split (looking at its files once and remembering the answer) for the training canvas.</summary>
public sealed record DescribeVersionSplitCommand(Guid WorkspaceId, Guid VersionId) : ICommand<VersionSplitInfo>;

/// <summary>Detects train/val folders from the file list the first time; versions made by <see cref="SplitVersionCommand"/> already carry their counts.</summary>
public sealed class DescribeVersionSplitCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DescribeVersionSplitCommand, VersionSplitInfo>
{
    public async ValueTask<VersionSplitInfo> Handle(DescribeVersionSplitCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(c.VersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var ds = await db.GetDatasetAsync(v.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var known = (await db.VersionSplitsAsync([v.Id], ct)).FirstOrDefault();
        if (known?.SplitState is { } state) return new VersionSplitInfo(v, ds.Name, ds.Task, state, SplitRules.ParseCounts(known.SplitCounts));
        var counts = new Dictionary<string, long> { ["train"] = 0, ["val"] = 0, ["test"] = 0 };
        foreach (var f in await db.ListVersionFilesAsync(v.Id, ct))
            if (SplitRules.IsImage(f.Path)) counts[SplitRules.FolderSplit(f.Path) ?? "train"]++;
        var found = counts["val"] > 0 ? "folders" : "none";
        await db.SetVersionSplitAsync(v.Id, found, SplitRules.CountsJson(counts), null, ct);
        return new VersionSplitInfo(v, ds.Name, ds.Task, found, counts);
    }
}

/// <summary>Splits of several versions of one dataset, for the version list.</summary>
public sealed record ListVersionSplitsQuery(Guid WorkspaceId, Guid[] VersionIds) : IQuery<IReadOnlyList<VersionSplitRow>>;

/// <summary>One lookup for the whole list.</summary>
public sealed class ListVersionSplitsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionSplitsQuery, IReadOnlyList<VersionSplitRow>>
{
    public async ValueTask<IReadOnlyList<VersionSplitRow>> Handle(ListVersionSplitsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return q.VersionIds.Length == 0 ? [] : await db.VersionSplitsAsync(q.VersionIds, ct);
    }
}

/// <summary>
/// Makes a new version with a fixed train/val/test split: the same files (referenced, not copied) plus <c>mlp-split.json</c>.
/// Group "folder" keeps every image of one directory in the same split (frames of one video).
/// </summary>
public sealed record SplitVersionCommand(Guid WorkspaceId, Guid VersionId, int TrainPct, int ValPct, int TestPct, string GroupBy, string Note) : ICommand<VersionRow>;

/// <summary>Assigns each image by the hash of its path (or folder), writes the split file into storage and records the new version.</summary>
public sealed class SplitVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<SplitVersionCommand, VersionRow>
{
    public async ValueTask<VersionRow> Handle(SplitVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var src = await db.GetVersionAsync(c.VersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var (train, val, test) = c.TrainPct == 0 && c.ValPct == 0 && c.TestPct == 0 ? (80, 10, 10) : (c.TrainPct, c.ValPct, c.TestPct);
        if (train < 1 || val < 1 || test < 0 || train + val + test != 100)
            throw new InvalidArgumentException("DATA_SPLIT_INVALID", "The split needs train and val above 0 and the three parts must add up to 100");
        var byFolder = c.GroupBy == "folder";
        if (!byFolder && c.GroupBy is not ("" or "file")) throw new InvalidArgumentException("DATA_SPLIT_INVALID", "group_by is file or folder");
        var files = (await db.ListVersionFilesAsync(src.Id, ct)).Where(f => f.Path != SplitRules.File).ToList();
        var images = files.Where(f => SplitRules.IsImage(f.Path)).Select(f => f.Path).ToList();
        if (images.Count < 2) throw new PreconditionException("DATA_SPLIT_EMPTY", "The version needs at least two images to split");
        if (images.Any(p => SplitRules.FolderSplit(p) == "val"))
            throw new PreconditionException("DATA_ALREADY_SPLIT", "This version already has train/val folders");

        string Key(string path) => byFolder && path.LastIndexOf('/') is > 0 and var i ? path[..i] : path;
        var seed = src.DatasetId.ToString();
        var groups = images.Select(Key).Distinct().ToDictionary(k => k, k => SplitRules.Bucket(seed, k));
        var of = groups.ToDictionary(g => g.Key, g => g.Value < train * 100 ? "train" : g.Value < (train + val) * 100 ? "val" : "test");
        // A small dataset can leave a part empty although the ratios are fine: move the train group with the lowest hash over.
        foreach (var need in test > 0 ? new[] { "val", "test" } : ["val"])
            if (!of.ContainsValue(need) && of.Count(g => g.Value == "train") > 1)
                of[of.Where(g => g.Value == "train").MinBy(g => groups[g.Key]).Key] = need;
        if (!of.ContainsValue("val")) throw new PreconditionException("DATA_SPLIT_EMPTY", "Every image is in one folder — split by file instead");

        var counts = new Dictionary<string, long> { ["train"] = 0, ["val"] = 0, ["test"] = 0 };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("seed", seed);
            w.WriteStartObject("ratios"); w.WriteNumber("train", train); w.WriteNumber("val", val); w.WriteNumber("test", test); w.WriteEndObject();
            w.WriteString("group_by", byFolder ? "folder" : "file");
            w.WriteStartObject("files");
            foreach (var p in images.Order(StringComparer.Ordinal))
            {
                var s = of[Key(p)];
                counts[s]++;
                w.WriteString(p, s);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        var body = ms.ToArray();
        var valHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', images.Where(p => of[Key(p)] == "val").Order(StringComparer.Ordinal)))));

        var n = await db.NextVersionNumberAsync(src.DatasetId, c.WorkspaceId, ct);
        var prefix = Buckets.Version(c.WorkspaceId, src.DatasetId, n);
        await s3.PutAsync(Buckets.Datasets, prefix + SplitRules.File, body, "application/json", ct);
        var id = Ids.New();
        var now = clock.UtcNow;
        var note = string.IsNullOrWhiteSpace(c.Note) ? $"v{src.Number} split {train}/{val}/{test}{(byFolder ? " by folder" : "")}" : c.Note.Trim();
        var size = files.Sum(f => f.SizeBytes) + body.Length;
        await db.InsertVersionAsync(id, src.DatasetId, n, prefix, files.Count + 1, size, note, uid, now, ct);
        // The files stay where they are: the new version points at them, so a split costs no copy.
        await db.CopyVersionFilesAsReferencesAsync(id, src.Id, $"s3://{Buckets.Datasets}/{src.Prefix}", SplitRules.File, ct);
        await db.InsertVersionFileAsync(id, SplitRules.File, body.Length, ct);
        await db.SetVersionSplitAsync(id, "file", SplitRules.CountsJson(counts), valHash, ct);
        return new VersionRow(id, src.DatasetId, n, prefix, files.Count + 1, size, note, now, "copied", null);
    }
}
