using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Which files of a dataset become items: images for image tasks; for other tasks every file except metadata (yaml, json).</summary>
public static class LabelSeeding
{
    private static readonly string[] TextTasks = ["text", "tabular", "generate", "asr", "tts", "audio", "speech"];

    public static bool ImageTask(string task) => !TextTasks.Any(t => task.StartsWith(t, StringComparison.Ordinal) || task.Contains("-" + t, StringComparison.Ordinal));

    public static Task<long> SeedAsync(IWriteSession db, Guid projectId, Guid datasetId, Guid? versionId, string task, CancellationToken ct)
    {
        var images = ImageTask(task);
        var exts = FileKinds.Extensions("image");
        return versionId is { } v ? db.SeedItemsFromVersionAsync(projectId, datasetId, v, images, exts, ct) : db.SeedItemsAsync(projectId, datasetId, images, exts, ct);
    }

    /// <summary>The YOLO label file that may sit next to an image: …/images/x.jpg ↔ …/labels/x.txt (or yololabels), or x.txt in the same folder.</summary>
    public static IReadOnlyList<string> YoloCandidates(string imagePath)
    {
        var stem = imagePath[..^Path.GetExtension(imagePath).Length];
        var list = new List<string>();
        foreach (var dir in new[] { "/labels/", "/yololabels/", "/yolo_labels/" })
        {
            var c = (stem.StartsWith("images/", StringComparison.Ordinal) ? "/" + stem : stem).Replace("/images/", dir).TrimStart('/') + ".txt";
            if (c != stem + ".txt") list.Add(c);
        }
        list.Add(stem + ".txt");
        return list;
    }

    /// <summary>"class cx cy w h" lines (normalized centre) → platform boxes {x, y, w, h} (normalized top-left); malformed lines are skipped.</summary>
    public static List<AnnotationInput> ParseYolo(string text, IReadOnlySet<int>? classes)
    {
        var res = new List<AnnotationInput>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || !int.TryParse(p[0], out var cls) || cls < 0 || (classes is { Count: > 0 } && !classes.Contains(cls))) continue;
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
                || !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) continue;
            var x = Math.Clamp(cx - w / 2, 0, 1); var y = Math.Clamp(cy - h / 2, 0, 1);
            w = Math.Min(w, 1 - x); h = Math.Min(h, 1 - y);
            if (w <= 0 || h <= 0) continue;
            var geom = new JsonObject { ["x"] = Math.Round(x, 6), ["y"] = Math.Round(y, 6), ["w"] = Math.Round(w, 6), ["h"] = Math.Round(h, 6) }.ToJsonString();
            res.Add(new AnnotationInput("bbox", cls, geom, "{}", "import", null));
        }
        return res;
    }
}

/// <summary>Open one chosen item for editing — from the item list or the dataset preview — instead of taking the next one in the queue.</summary>
public sealed record ClaimLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<ItemView>;

/// <summary>Locks the item for the caller; refuses when someone else holds it.</summary>
public sealed class ClaimLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock, S3Client s3) : ICommandHandler<ClaimLabelItemCommand, ItemView>
{
    public async ValueTask<ItemView> Handle(ClaimLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        if (!roles.Contains("annotator") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not an Annotator of this project");
        var now = clock.UtcNow;
        _ = await db.GetItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (await db.ClaimItemAsync(p.Id, c.Path, uid, now, now.AddMinutes(p.LockTtlMin), ct) is null)
            throw new PreconditionException("LABEL_ITEM_LOCKED", "Item is being labeled by someone else");
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "open", null, null, uid, now, ct);
        return await access.ViewAsync(p, c.Path, s3, ct, db);
    }
}

/// <summary>
/// Bring a project up to the dataset's latest version: the project now labels that version and its new files become items. Items and labels
/// already made stay.
/// </summary>
public sealed record SyncLabelingProjectCommand(Guid WorkspaceId, Guid ProjectId) : ICommand<long>;

/// <summary>Moves the project to the latest version and seeds what is new; returns how many items were added.</summary>
public sealed class SyncLabelingProjectCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<SyncLabelingProjectCommand, long>
{
    public async ValueTask<long> Handle(SyncLabelingProjectCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        var ds = await db.GetDatasetAsync(p.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var latest = await db.LatestVersionAsync(p.DatasetId, c.WorkspaceId, ct) ?? throw new PreconditionException("LABEL_VERSION_REQUIRED", "Freeze a version of the dataset first");
        if (latest.Id != p.VersionId) await db.SetProjectVersionAsync(p.Id, c.WorkspaceId, latest.Id, ct);
        return await LabelSeeding.SeedAsync(db, p.Id, p.DatasetId, latest.Id, ds.Task, ct);
    }
}

/// <summary>Delete a labeling project. The labels stay with the dataset (they belong to its files, not to the project).</summary>
public sealed record DeleteLabelingProjectCommand(Guid WorkspaceId, Guid ProjectId) : ICommand<bool>;

/// <summary>Marks the project deleted; it leaves every list.</summary>
public sealed class DeleteLabelingProjectCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<DeleteLabelingProjectCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteLabelingProjectCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        await db.SetProjectStateAsync(p.Id, "deleted", ct);
        return true;
    }
}

/// <summary>Where the label file paired with an item lives (null = none, or labels were already made or imported for it).</summary>
public sealed record ResolveItemLabelFileQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<string?>;

/// <summary>Only for items without platform labels that were never imported, in a project that labels a version.</summary>
public sealed class ResolveItemLabelFileQueryHandler(IReadSession db, LabelAccess access, S3Client s3) : IQueryHandler<ResolveItemLabelFileQuery, string?>
{
    public async ValueTask<string?> Handle(ResolveItemLabelFileQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (p.VersionId is not { } vid || FileKinds.Of(q.Path) != "image") return null;
        if (await db.LabelsImportedAsync(p.Id, q.Path, ct) == true) return null;
        if ((await db.ListAnnotationsAsync(p.DatasetId, q.Path, ct)).Count > 0) return null;
        var v = await db.GetVersionAsync(vid, q.WorkspaceId, ct);
        if (v is null) return null;
        var found = await db.FindFilesByPathAsync(vid, p.DatasetId, [.. LabelSeeding.YoloCandidates(q.Path)], ct);
        var order = LabelSeeding.YoloCandidates(q.Path);
        var hit = found.OrderBy(f => order.ToList().IndexOf(f.Path)).FirstOrDefault();
        return hit is null ? null : Registry.Evaluations.FileUrl(s3, v, hit.Path, hit.Url, Audience.Internal);
    }
}

/// <summary>
/// Existing labels of an item (the YOLO txt next to it in the version) become editable platform labels, once — the first time the item is
/// opened. Source "import". An item that already has platform labels is left alone.
/// </summary>
public sealed record ImportItemLabelsCommand(Guid WorkspaceId, Guid ProjectId, string Path, string YoloText) : ICommand<int>, IInternalCommand;

/// <summary>Parses the text, stores the boxes, marks the item imported (also when the file held no valid box).</summary>
public sealed class ImportItemLabelsCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ImportItemLabelsCommand, int>
{
    public async ValueTask<int> Handle(ImportItemLabelsCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "dataset.read", ct);
        if (await db.LabelsImportedAsync(p.Id, c.Path, ct) == true || (await db.ListAnnotationsAsync(p.DatasetId, c.Path, ct)).Count > 0) return 0;
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        var boxes = LabelSeeding.ParseYolo(c.YoloText, schema is null ? null : LabelRules.ParseClasses(schema.Classes));
        if (boxes.Count > 0) await LabelStore.ReplaceAsync(db, clock, p, c.Path, uid, boxes, "import", ct);
        await db.MarkLabelsImportedAsync(p.Id, c.Path, ct);
        return boxes.Count;
    }
}
