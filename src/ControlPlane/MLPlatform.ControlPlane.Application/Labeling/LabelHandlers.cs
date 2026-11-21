using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Label submitted from the labeling tool (M05 §3.1: image coordinates normalized to 0–1).</summary>
public sealed record AnnotationInput(string Kind, int ClassId, string Geometry, string Attributes, string Source, double? Confidence);

public sealed record ItemView(ItemStateRow State, string Url, string Classes, IReadOnlyList<AnnotationRow> Annotations, IReadOnlyList<CommentRow> Comments);

public static class LabelRules
{
    public static readonly string[] Kinds = ["tag", "bbox", "obb", "polygon", "mask", "keypoints", "span", "segment", "relation", "text_pair", "preference", "value", "transcript"];
    public static readonly string[] Roles = ["lead", "annotator", "reviewer"];

    public static ValidationException Invalid(string field, string msg) => new(new Dictionary<string, string> { [field] = msg });

    /// <summary>classes = [{id:int, name:string, color?:string}], unique ids.</summary>
    public static HashSet<int> ParseClasses(string json)
    {
        var ids = new HashSet<int>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                throw Invalid("classes", "At least one class is required");
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                if (!c.TryGetProperty("id", out var id) || !id.TryGetInt32(out var i) || !c.TryGetProperty("name", out var n)
                    || n.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(n.GetString()))
                    throw Invalid("classes", "Each class requires an id (number) and a name");
                if (!ids.Add(i)) throw Invalid("classes", $"Duplicate class id {i}");
            }
        }
        catch (JsonException) { throw Invalid("classes", "Invalid JSON"); }
        return ids;
    }

    /// <summary>Validates against the schema (R: class exists, geometry is well-formed, bbox is inside the image). Throws LABEL_SCHEMA_VIOLATION with the indexes of invalid labels.</summary>
    public static void Validate(IReadOnlyList<AnnotationInput> items, HashSet<int> classes)
    {
        var errors = new Dictionary<string, string>();
        for (var i = 0; i < items.Count; i++)
        {
            var a = items[i];
            var err = Check(a, classes);
            if (err is not null) errors[$"annotations[{i}]"] = err;
        }
        if (errors.Count > 0) throw new InvalidArgumentException("LABEL_SCHEMA_VIOLATION", "Invalid labels", errors);
    }

    private static string? Check(AnnotationInput a, HashSet<int> classes)
    {
        if (!Kinds.Contains(a.Kind)) return $"kind '{a.Kind}' is not supported";
        if (a.Kind is not ("relation" or "text_pair" or "preference" or "value" or "transcript") && !classes.Contains(a.ClassId))
            return $"class {a.ClassId} is not in the schema";
        try
        {
            using var g = JsonDocument.Parse(string.IsNullOrEmpty(a.Geometry) ? "{}" : a.Geometry);
            using var _ = JsonDocument.Parse(string.IsNullOrEmpty(a.Attributes) ? "{}" : a.Attributes);
            var r = g.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return "geometry must be an object";
            switch (a.Kind)
            {
                case "bbox":
                    if (!Num(r, "x", out var x) || !Num(r, "y", out var y) || !Num(r, "w", out var w) || !Num(r, "h", out var h))
                        return "bbox requires x, y, w, h";
                    const double eps = 1e-6;
                    if (x < -eps || y < -eps || w <= 0 || h <= 0 || x + w > 1 + eps || y + h > 1 + eps) return "bbox is outside the image";
                    break;
                case "polygon":
                    if (!r.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array || pts.GetArrayLength() < 3)
                        return "polygon requires ≥ 3 points";
                    foreach (var p in pts.EnumerateArray())
                        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2 || p[0].GetDouble() is < 0 or > 1 || p[1].GetDouble() is < 0 or > 1)
                            return "polygon point is outside the image";
                    break;
                case "span":
                    if (!Num(r, "start", out var s) || !Num(r, "end", out var e) || s < 0 || e <= s) return "span requires start < end";
                    break;
                case "segment":   // audio/video time range in seconds
                    if (!Num(r, "start", out var ss) || !Num(r, "end", out var se) || ss < 0 || se <= ss) return "segment requires start < end (seconds)";
                    break;
                case "keypoints":
                    if (!r.TryGetProperty("points", out var kps) || kps.ValueKind != JsonValueKind.Array || kps.GetArrayLength() == 0)
                        return "keypoints requires points";
                    foreach (var p in kps.EnumerateArray())
                        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2 || p[0].GetDouble() is < 0 or > 1 || p[1].GetDouble() is < 0 or > 1)
                            return "keypoint is outside the image";
                    break;
                case "mask":      // run-length encoding over the image grid: {"rle": {"size": [h, w], "counts": [...]}}
                    if (!r.TryGetProperty("rle", out var rle) || !rle.TryGetProperty("size", out var size) || size.GetArrayLength() != 2
                        || !rle.TryGetProperty("counts", out var counts) || counts.ValueKind != JsonValueKind.Array)
                        return "mask requires rle.size [h, w] and rle.counts";
                    long cells = (long)size[0].GetInt32() * size[1].GetInt32(), sum = 0;
                    foreach (var n in counts.EnumerateArray()) sum += n.GetInt64();
                    if (cells <= 0 || sum != cells) return "mask counts do not cover the image";
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return "geometry/attributes is not valid JSON"; }
        if (a.Confidence is < 0 or > 1) return "confidence is outside [0, 1]";
        return a.Source is "human" or "model" or "import" or "ai" ? null : "source must be human/model/import/ai";   // ai = AI proposal a person accepted
    }

    private static bool Num(JsonElement o, string name, out double v)
    {
        v = 0;
        return o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out v);
    }

    /// <summary>Label list → JSON (for history/drafts/label sets) — Utf8JsonWriter, no reflection (AOT).</summary>
    public static string ToJson(IEnumerable<(string Kind, int ClassId, string Geometry, string Attributes, string Source)> items)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var a in items) WriteOne(w, null, a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source);
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void WriteOne(Utf8JsonWriter w, string? path, string kind, int classId, string geometry, string attributes, string source)
    {
        w.WriteStartObject();
        if (path is not null) w.WriteString("path", path);
        w.WriteString("kind", kind);
        w.WriteNumber("class_id", classId);
        w.WritePropertyName("geometry"); w.WriteRawValue(string.IsNullOrEmpty(geometry) ? "{}" : geometry);
        w.WritePropertyName("attributes"); w.WriteRawValue(string.IsNullOrEmpty(attributes) ? "{}" : attributes);
        w.WriteString("source", source);
        w.WriteEndObject();
    }
}


/// <summary>Per-project permissions (M05 §2.1): project_members is the single source of labeling roles.</summary>
public sealed class LabelAccess(IReadSession readDb, WorkspaceAuthorizer auth, IClock clock)
{
    private readonly IReadSession db = readDb;

    public async Task<(Guid Uid, ProjectRow Project, HashSet<string> Roles)> LoadAsync(Guid ws, Guid projectId, string permission, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(ws, permission, ct);
        var p = await db.GetProjectAsync(projectId, ws, ct) ?? throw new NotFoundException("LABEL_PROJECT_NOT_FOUND", "Labeling project not found");
        var roles = (await db.MemberRolesAsync(projectId, uid, ct)).Select(r => r.Role).ToHashSet();
        // Whoever manages labeling in the workspace (owner, admin) leads every project: a project never ends up with nobody able to run it.
        if (await auth.HasAsync(ws, "labeling.manage", ct)) roles.Add("lead");
        return (uid, p, roles);
    }

    public static void RequireActive(ProjectRow p)
    {
        if (p.State != "active") throw new PreconditionException("LABEL_PROJECT_NOT_ACTIVE", "Project is not active / is paused");
    }

    /// <summary>The caller holds an unexpired lock — required for Save/Submit/Skip.</summary>
    public void RequireLock(ItemStateRow s, Guid uid)
    {
        if (s.LockOwner != uid)
            throw s.LockOwner is null || s.LockExpiresAt < clock.UtcNow
                ? new PreconditionException("LABEL_LOCK_EXPIRED", "Item lock has expired")
                : new PreconditionException("LABEL_ITEM_LOCKED", "Item is being labeled by someone else");
        if (s.LockExpiresAt < clock.UtcNow) throw new PreconditionException("LABEL_LOCK_EXPIRED", "Item lock has expired");
    }

    /// <summary>Reads an item with its labels. Inside a command, pass the write session so the lock just taken in the same transaction is visible.</summary>
    public async Task<ItemView> ViewAsync(ProjectRow p, string path, S3Client s3, CancellationToken ct, IReadSession? session = null)
    {
        var db = session ?? this.db;
        var s = await db.GetItemStateAsync(p.Id, path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        string url;
        if (p.VersionId is { } vid)
        {
            // Version project: reference files come from their source, copied files from the version folder in storage.
            var v = await db.GetVersionAsync(vid, p.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
            var f = await db.GetVersionFileAsync(vid, path, ct);
            url = f?.Url is { } u ? SourceUrls.Resolve(s3, u) : s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + path, TimeSpan.FromHours(1));
        }
        else url = s3.Presign(HttpMethod.Get, Buckets.Datasets, Buckets.Raw(p.WorkspaceId, p.DatasetId) + path, TimeSpan.FromHours(1));
        return new ItemView(s, url, schema?.Classes ?? "[]", await db.ListAnnotationsAsync(p.DatasetId, path, ct), await db.ListCommentsAsync(p.Id, path, ct));
    }
}

// ───────────── Schema ─────────────

public sealed record CreateLabelSchemaCommand(Guid WorkspaceId, Guid DatasetId, string Task, string Classes) : ICommand<SchemaRow>;

public sealed class CreateLabelSchemaCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateLabelSchemaCommand, SchemaRow>
{
    public async ValueTask<SchemaRow> Handle(CreateLabelSchemaCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var ds = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        LabelRules.ParseClasses(c.Classes);
        await db.InsertSchemaAsync(Ids.New(), c.DatasetId, string.IsNullOrEmpty(c.Task) ? ds.Task : c.Task, c.Classes, uid, clock.UtcNow, ct);
        return (await db.GetCurrentSchemaAsync(c.DatasetId, ct))!;
    }
}

public sealed record GetLabelSchemaQuery(Guid WorkspaceId, Guid DatasetId, int Version) : IQuery<SchemaRow>;

public sealed class GetLabelSchemaQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetLabelSchemaQuery, SchemaRow>
{
    public async ValueTask<SchemaRow> Handle(GetLabelSchemaQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return (q.Version > 0 ? await db.GetSchemaVersionAsync(q.DatasetId, q.Version, ct) : await db.GetCurrentSchemaAsync(q.DatasetId, ct))
               ?? throw new NotFoundException("LABEL_SCHEMA_NOT_FOUND", "Dataset has no label schema yet");
    }
}

// ───────────── Project ─────────────

/// <summary>VersionId: label the files of that dataset version (copied or reference); null = the dataset's uploaded files.</summary>
public sealed record CreateLabelingProjectCommand(Guid WorkspaceId, Guid DatasetId, string Name, string GuidelineMd, string ReviewMode, int LockTtlMin,
    Guid? VersionId = null, double AutoAcceptThreshold = 0) : ICommand<ProjectRow>;

public sealed class CreateLabelingProjectCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateLabelingProjectCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(CreateLabelingProjectCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "labeling.manage", ct);
        var ds = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var schema = await db.GetCurrentSchemaAsync(c.DatasetId, ct) ?? throw new PreconditionException("LABEL_SCHEMA_REQUIRED", "Define a label schema first");
        var mode = string.IsNullOrEmpty(c.ReviewMode) ? "none" : c.ReviewMode;
        // A project labels a version: the one asked for, else the latest. Without any version there is nothing stable to label.
        var versionId = c.VersionId ?? (await db.LatestVersionAsync(c.DatasetId, c.WorkspaceId, ct))?.Id
                        ?? throw new PreconditionException("LABEL_VERSION_REQUIRED", "Freeze a version of the dataset first");
        if (mode is not ("none" or "all")) throw LabelRules.Invalid("review_mode", "none | all");
        if (string.IsNullOrWhiteSpace(c.Name)) throw LabelRules.Invalid("name", "Project name is required");
        if ((await db.GetVersionAsync(versionId, c.WorkspaceId, ct))?.DatasetId != c.DatasetId)
            throw LabelRules.Invalid("version_id", "The version does not belong to this dataset");
        if (c.AutoAcceptThreshold is < 0 or > 1) throw LabelRules.Invalid("auto_accept_threshold", "Must be between 0 and 1 (0 = off)");
        var id = Ids.New();
        await db.InsertProjectAsync(id, c.WorkspaceId, c.DatasetId, c.Name.Trim(), c.GuidelineMd, schema.Version, mode,
            c.LockTtlMin is > 0 and <= 240 ? c.LockTtlMin : 10, uid, clock.UtcNow, versionId, c.AutoAcceptThreshold, ct);
        // The person who creates a project can label in it right away: lead and annotator, items loaded, project active.
        await db.UpsertMemberAsync(id, uid, "lead", 50, ct);
        await db.UpsertMemberAsync(id, uid, "annotator", 500, ct);
        await LabelSeeding.SeedAsync(db, id, c.DatasetId, versionId, ds.Task, ct);
        await db.SetProjectStateAsync(id, "active", ct);
        return (await db.GetProjectAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListLabelingProjectsQuery(Guid WorkspaceId, Guid? DatasetId) : IQuery<IReadOnlyList<ProjectRow>>;

public sealed class ListLabelingProjectsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListLabelingProjectsQuery, IReadOnlyList<ProjectRow>>
{
    public async ValueTask<IReadOnlyList<ProjectRow>> Handle(ListLabelingProjectsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListProjectsAsync(q.WorkspaceId, q.DatasetId, ct);
    }
}

/// <summary>activate (seeds items from the dataset's files) / pause / complete.</summary>
public sealed record SetLabelingProjectStateCommand(Guid WorkspaceId, Guid ProjectId, string State) : ICommand<ProjectRow>;

public sealed class SetLabelingProjectStateCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SetLabelingProjectStateCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(SetLabelingProjectStateCommand c, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        _ = roles;
        var ok = (p.State, c.State) switch
        {
            ("draft" or "paused" or "completed", "active") => true,   // a finished project can be opened again
            ("active", "paused") => true,
            ("active" or "paused", "completed") => true,
            _ => false,
        };
        if (!ok) throw new PreconditionException("LABEL_PROJECT_STATE", $"Cannot change state {p.State} → {c.State}");
        if (c.State == "active")
        {
            var task = (await db.GetDatasetAsync(p.DatasetId, p.WorkspaceId, ct))?.Task ?? "";
            // A project made before projects always labelled a version has none: it takes the dataset's latest, or it would open with no items.
            var versionId = p.VersionId;
            if (versionId is null && await db.LatestVersionAsync(p.DatasetId, c.WorkspaceId, ct) is { } latest)
            {
                await db.SetProjectVersionAsync(p.Id, c.WorkspaceId, latest.Id, ct);
                versionId = latest.Id;
            }
            await LabelSeeding.SeedAsync(db, p.Id, p.DatasetId, versionId, task, ct);
        }
        await db.SetProjectStateAsync(p.Id, c.State, ct);
        if (c.State == "completed")
            await Notifier.SendAsync(db, clock, p.WorkspaceId,
                (await db.ListProjectMembersAsync(p.Id, ct)).Where(m => m.Role == "lead").Select(m => m.UserId).Concat(await Notifier.AdminsAsync(db, p.WorkspaceId, ct)),
                "labeling.project_completed", $"Labeling project \"{p.Name}\" is complete", $"#/labeling/{p.Id}", $"labelproject/{p.Id}", "success", ct);
        return (await db.GetProjectAsync(p.Id, c.WorkspaceId, ct))!;
    }
}

public sealed record AddLabelingMemberCommand(Guid WorkspaceId, Guid ProjectId, Guid UserId, string Role, int MaxOpenItems) : ICommand<bool>;

public sealed class AddLabelingMemberCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<AddLabelingMemberCommand, bool>
{
    public async ValueTask<bool> Handle(AddLabelingMemberCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        if (!LabelRules.Roles.Contains(c.Role)) throw LabelRules.Invalid("role", "lead | annotator | reviewer");
        _ = await db.GetMemberRoleAsync(c.WorkspaceId, c.UserId, ct) ?? throw new NotFoundException("IAM_MEMBER_NOT_FOUND", "This person is not a member of the workspace");
        await db.UpsertMemberAsync(c.ProjectId, c.UserId, c.Role, c.MaxOpenItems > 0 ? c.MaxOpenItems : 50, ct);
        await Notifier.SendAsync(db, clock, c.WorkspaceId, [c.UserId], "labeling.assigned",
            $"You were assigned the {c.Role} role in \"{p.Name}\"", $"#/labeling/{p.Id}", $"labelproject/{p.Id}", "info", ct);
        return true;
    }
}

public sealed record RemoveLabelingMemberCommand(Guid WorkspaceId, Guid ProjectId, Guid UserId, string Role) : ICommand<bool>;

public sealed class RemoveLabelingMemberCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<RemoveLabelingMemberCommand, bool>
{
    public async ValueTask<bool> Handle(RemoveLabelingMemberCommand c, CancellationToken ct)
    {
        _ = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        await db.RemoveMemberAsync(c.ProjectId, c.UserId, c.Role, ct);
        return true;
    }
}

public sealed record ListLabelingMembersQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<ProjectMemberRow>>;

public sealed class ListLabelingMembersQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListLabelingMembersQuery, IReadOnlyList<ProjectMemberRow>>
{
    public async ValueTask<IReadOnlyList<ProjectMemberRow>> Handle(ListLabelingMembersQuery q, CancellationToken ct)
    {
        _ = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return await db.ListProjectMembersAsync(q.ProjectId, ct);
    }
}

public sealed record GetLabelingStatsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, IReadOnlyList<StatRow> Stats)>;

public sealed class GetLabelingStatsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetLabelingStatsQuery, (ProjectRow Project, IReadOnlyList<StatRow> Stats)>
{
    public async ValueTask<(ProjectRow Project, IReadOnlyList<StatRow> Stats)> Handle(GetLabelingStatsQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return (p, await db.ProjectStatsAsync(p.Id, ct));
    }
}

public sealed record ListMyLabelItemsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<MyItemRow>>;

public sealed class ListMyLabelItemsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListMyLabelItemsQuery, IReadOnlyList<MyItemRow>>
{
    public async ValueTask<IReadOnlyList<MyItemRow>> Handle(ListMyLabelItemsQuery q, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return await db.ListMyItemsAsync(p.Id, uid, ct);
    }
}

// ───────────── Queue ─────────────

public sealed record NextLabelItemCommand(Guid WorkspaceId, Guid ProjectId, bool Review) : ICommand<ItemView?>;

public sealed class NextLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock, S3Client s3) : ICommandHandler<NextLabelItemCommand, ItemView?>
{
    public async ValueTask<ItemView?> Handle(NextLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var now = clock.UtcNow;
        var expires = now.AddMinutes(p.LockTtlMin);
        PathRow? row;
        if (c.Review)
        {
            if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a Reviewer of this project");
            row = await db.ClaimNextReviewAsync(p.Id, uid, now, expires, ct);
        }
        else
        {
            if (!roles.Contains("annotator") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not an Annotator of this project");
            var max = (await db.ListProjectMembersAsync(p.Id, ct)).Where(m => m.UserId == uid && m.Role == "annotator").Select(m => m.MaxOpenItems).DefaultIfEmpty(50).Max();
            if (await db.CountOpenAsync(p.Id, uid, now, ct) >= max)
                throw new ResourceExhaustedException("LABEL_MAX_OPEN_REACHED", "Maximum number of held items reached");
            row = await db.ClaimNextItemAsync(p.Id, uid, now, expires, ct);
        }
        return row is null ? null : await access.ViewAsync(p, row.Path, s3, ct, db);
    }
}

public sealed record GetLabelItemQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<ItemView>;

public sealed class GetLabelItemQueryHandler(IReadSession db, LabelAccess access, S3Client s3) : IQueryHandler<GetLabelItemQuery, ItemView>
{
    public async ValueTask<ItemView> Handle(GetLabelItemQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        // Annotators only see items assigned to them (§2.1); Lead/Reviewer/Editor see everything.
        var onlyAnnotator = roles.Count > 0 && roles.All(r => r == "annotator");
        if (onlyAnnotator && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await access.ViewAsync(p, q.Path, s3, ct);
    }
}

public sealed record HeartbeatLabelLockCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<DateTimeOffset>;

public sealed class HeartbeatLabelLockCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<HeartbeatLabelLockCommand, DateTimeOffset>
{
    public async ValueTask<DateTimeOffset> Handle(HeartbeatLabelLockCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        var exp = clock.UtcNow.AddMinutes(p.LockTtlMin);
        await db.ExtendLockAsync(p.Id, c.Path, uid, exp, ct);
        return exp;
    }
}

/// <summary>R1: draft auto-save — kept when the lock expires so the next person can choose "Use draft".</summary>
public sealed record SaveLabelDraftCommand(Guid WorkspaceId, Guid ProjectId, string Path, IReadOnlyList<AnnotationInput> Annotations) : ICommand<bool>;

public sealed class SaveLabelDraftCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SaveLabelDraftCommand, bool>
{
    public async ValueTask<bool> Handle(SaveLabelDraftCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        await db.SaveDraftAsync(p.Id, c.Path, LabelRules.ToJson(c.Annotations.Select(a => (a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source))), uid, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>R2: submits all labels of the item + etag (= revision). The server replaces them, writes history, bumps the revision.</summary>
public sealed record SaveLabelAnnotationsCommand(Guid WorkspaceId, Guid ProjectId, string Path, int Etag, IReadOnlyList<AnnotationInput> Annotations) : ICommand<int>;

public sealed class SaveLabelAnnotationsCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SaveLabelAnnotationsCommand, int>
{
    public async ValueTask<int> Handle(SaveLabelAnnotationsCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        if (s.Revision != c.Etag) throw new AbortedException("LABEL_REVISION_CONFLICT", "Labels have changed, reload");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        LabelRules.Validate(c.Annotations, LabelRules.ParseClasses(schema!.Classes));
        await LabelStore.ReplaceAsync(db, clock, p, c.Path, uid, c.Annotations, "save", ct);
        _ = roles;
        return s.Revision + 1;
    }
}


internal static class LabelStore
{
    public static async Task ReplaceAsync(IWriteSession db, IClock clock, ProjectRow p, string path, Guid uid, IReadOnlyList<AnnotationInput> items, string action, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var before = await db.ListAnnotationsAsync(p.DatasetId, path, ct);
        await db.DeleteAnnotationsAsync(p.DatasetId, path, now, ct);
        foreach (var a in items)
            await db.InsertAnnotationAsync(Ids.New(), p.DatasetId, path, p.SchemaVersion, a.Kind, a.ClassId,
                string.IsNullOrEmpty(a.Geometry) ? "{}" : a.Geometry, string.IsNullOrEmpty(a.Attributes) ? "{}" : a.Attributes,
                string.IsNullOrEmpty(a.Source) ? "human" : a.Source, a.Confidence, uid, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, path, action,
            LabelRules.ToJson(before.Select(b => (b.Kind, b.ClassId, b.Geometry, b.Attributes, b.Source))),
            LabelRules.ToJson(items.Select(a => (a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source))), uid, now, ct);
        await db.BumpRevisionAsync(p.Id, path, ct);
    }
}

// ───────────── Item state transitions (§4) ─────────────

public sealed record SubmitLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<string>;

public sealed class SubmitLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SubmitLabelItemCommand, string>
{
    public async ValueTask<string> Handle(SubmitLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId != uid || s.Status != "in_progress") throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item is not assigned to you");
        access.RequireLock(s, uid);
        var next = p.ReviewMode == "none" ? "accepted" : "in_review";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, null, null, true, false, clock.UtcNow, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "submit", null, null, uid, clock.UtcNow, ct);
        return next;
    }
}

public sealed record SkipLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Reason) : ICommand<string>;

public sealed class SkipLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SkipLabelItemCommand, string>
{
    public async ValueTask<string> Handle(SkipLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (string.IsNullOrWhiteSpace(c.Reason)) throw LabelRules.Invalid("reason", "A reason for skipping is required");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId != uid || s.Status != "in_progress") throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item is not assigned to you");
        access.RequireLock(s, uid);
        await db.UpdateItemStateAsync(p.Id, c.Path, "skipped", null, null, null, c.Reason.Trim(), false, false, clock.UtcNow, ct);
        return "skipped";
    }
}

public sealed record ReviewLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, bool Accept, string Comment) : ICommand<string>;

public sealed class ReviewLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ReviewLabelItemCommand, string>
{
    public async ValueTask<string> Handle(ReviewLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a Reviewer of this project");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId == uid) throw new ForbiddenException("LABEL_SELF_REVIEW", "You cannot review an item you labeled");
        if (s.Status != "in_review") throw new PreconditionException("LABEL_NOT_IN_REVIEW", "Item is not awaiting review");
        if (!c.Accept && string.IsNullOrWhiteSpace(c.Comment))
            throw new InvalidArgumentException("LABEL_REJECT_COMMENT_REQUIRED", "A reason is required when rejecting");
        var now = clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(c.Comment)) await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Comment.Trim(), now, ct);
        // Rejected → goes back to the same annotator (annotator_id unchanged), with high priority.
        var next = c.Accept ? "accepted" : "rejected";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, uid, null, false, true, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, c.Accept ? "accept" : "reject", null, null, uid, now, ct);
        if (!c.Accept && s.AnnotatorId is { } annotator)
            await Notifier.SendAsync(db, clock, p.WorkspaceId, [annotator], "labeling.rejected", $"Item rejected in \"{p.Name}\"",
                $"#/labeling/{p.Id}/annotate", $"labelproject/{p.Id}", "warning", ct);
        return next;
    }
}


public sealed record ReopenLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Reason) : ICommand<string>;

public sealed class ReopenLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ReopenLabelItemCommand, string>
{
    public async ValueTask<string> Handle(ReopenLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead can reopen items");
        if (string.IsNullOrWhiteSpace(c.Reason)) throw LabelRules.Invalid("reason", "A reason for reopening is required");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.Status is not ("accepted" or "skipped")) throw new PreconditionException("LABEL_PROJECT_STATE", "Only accepted or skipped items can be reopened");
        var now = clock.UtcNow;
        await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Reason.Trim(), now, ct);
        var next = s.AnnotatorId is null ? "unlabeled" : "rejected";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, null, null, false, false, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "reopen", null, null, uid, now, ct);
        if (s.AnnotatorId is { } annotator)
            await Notifier.SendAsync(db, clock, p.WorkspaceId, [annotator], "labeling.rejected", $"Lead reopened an item in \"{p.Name}\"",
                $"#/labeling/{p.Id}/annotate", $"labelproject/{p.Id}", "warning", ct);
        return next;
    }
}

public sealed record AddLabelCommentCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Body) : ICommand<bool>;

public sealed class AddLabelCommentCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<AddLabelCommentCommand, bool>
{
    public async ValueTask<bool> Handle(AddLabelCommentCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (roles.Count == 0) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a member of this project");
        if (string.IsNullOrWhiteSpace(c.Body)) throw LabelRules.Invalid("body", "Comment is empty");
        _ = await db.GetItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Body.Trim(), clock.UtcNow, ct);
        return true;
    }
}

public sealed record GetLabelHistoryQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<IReadOnlyList<HistoryRow>>;

public sealed class GetLabelHistoryQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetLabelHistoryQuery, IReadOnlyList<HistoryRow>>
{
    public async ValueTask<IReadOnlyList<HistoryRow>> Handle(GetLabelHistoryQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (!roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead/Data Owner can view history");
        return await db.ListHistoryAsync(p.DatasetId, q.Path, ct);
    }
}

// ───────────── Label set ─────────────

/// <summary>Commits a label set (§5 step 6): an immutable JSONL snapshot in S3 storage (`…/labelsets/{id}.jsonl`). Parquet later.</summary>
public sealed record CreateLabelSetCommand(Guid WorkspaceId, Guid DatasetId, bool AllLabeled) : ICommand<LabelSetRow>;

public sealed class CreateLabelSetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<CreateLabelSetCommand, LabelSetRow>
{
    public async ValueTask<LabelSetRow> Handle(CreateLabelSetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var schema = await db.GetCurrentSchemaAsync(c.DatasetId, ct) ?? throw new PreconditionException("LABEL_SCHEMA_REQUIRED", "Dataset has no label schema yet");
        var rows = await db.LabelSetRowsAsync(c.DatasetId, c.AllLabeled, ct);
        if (rows.Count == 0) throw new PreconditionException("LABEL_SET_EMPTY", "No eligible labels to commit yet");
        using var ms = new MemoryStream();
        foreach (var r in rows)
        {
            using (var w = new Utf8JsonWriter(ms)) LabelRules.WriteOne(w, r.Path, r.Kind, r.ClassId, r.Geometry, r.Attributes, r.Source);
            ms.WriteByte((byte)'\n');
        }
        var id = Ids.New();
        var key = $"{c.WorkspaceId}/datasets/{c.DatasetId}/labelsets/{id}.jsonl";
        await s3.PutAsync(Buckets.Datasets, key, ms.ToArray(), "application/x-ndjson", ct);
        var mode = c.AllLabeled ? "all_labeled" : "accepted_only";
        var items = rows.Select(r => r.Path).Distinct().LongCount();
        var now = clock.UtcNow;
        var n = await db.InsertLabelSetAsync(id, c.DatasetId, schema.Version, key, items, rows.Count, mode, uid, now, ct);
        return new LabelSetRow(id, n, schema.Version, items, rows.Count, mode, now);
    }
}

public sealed record ListLabelSetsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<LabelSetRow>>;

public sealed class ListLabelSetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListLabelSetsQuery, IReadOnlyList<LabelSetRow>>
{
    public async ValueTask<IReadOnlyList<LabelSetRow>> Handle(ListLabelSetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListLabelSetsAsync(q.DatasetId, ct);
    }
}
