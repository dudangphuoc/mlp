using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/*
 * Hermes label checks (phase 2). The analysis itself (rules → decision model) runs in the api (HermesLabelChecker);
 * this layer loads what it needs with the caller's rights, stores each check with all its steps, applies auto-accept
 * inside a locked transaction, and lets a Lead/Reviewer undo it. Nothing changes an item without a stored, visible check.
 */

public sealed record HermesCheckRow(Guid Id, Guid ProjectId, string Path, int Revision, string Trigger, string Verdict, double? Confidence, string DecidedBy,
    string Summary, string StepsJson, string? Suggestion, Guid? AiRunId, string Action, Guid? ActorId, string? ActorName, DateTimeOffset CreatedAt,
    Guid? UndoneBy, string? UndoneByName, DateTimeOffset? UndoneAt, string? Explanation, string? ExplanationLang, Guid? ExplanationRunId,
    DateTimeOffset? ExplainedAt);

public sealed record HermesStatRow(string Verdict, string Action, long N, double? AvgConfidence);

public static partial class HermesSql
{
    [SqlQuery("Label/InsertHermesCheck.sql")]
    public static partial Task InsertHermesCheckAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string path, int revision, string trigger,
        string verdict, double? confidence, string decidedBy, string summary, string stepsJson, string? suggestion, Guid? aiRunId, string action, Guid? actorId,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/GetHermesCheck.sql")]
    public static partial Task<HermesCheckRow?> GetHermesCheckAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/ListHermesChecks.sql")]
    public static partial Task<List<HermesCheckRow>> ListHermesChecksAsync(this IReadSession db, Guid workspaceId, Guid projectId, string verdict, string path,
        Guid? onlyAnnotator, int lim, CancellationToken ct);
    [SqlQuery("Label/HermesStats.sql")]
    public static partial Task<List<HermesStatRow>> HermesStatsAsync(this IReadSession db, Guid workspaceId, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/MarkHermesUndone.sql")]
    public static partial Task MarkHermesUndoneAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid undoneBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/SetHermesSettings.sql")]
    public static partial Task SetHermesSettingsAsync(this IWriteSession db, Guid id, Guid workspaceId, bool enabled, double threshold, CancellationToken ct);
}

/// <summary>Where the item's content lives (read by the api to give text to the decision model).</summary>
public sealed record HermesSource(string? Bucket, string? Key, string? ExternalUrl);

/// <summary>Everything one check needs, loaded with the caller's rights.</summary>
public sealed record HermesContext(Guid ActorId, ProjectRow Project, ItemStateRow State, string Classes, List<AnnotationRow> Annotations, HermesSource Source,
    string DatasetTask);

/// <summary>Loads an item's context once the caller is authorized (shared by user requests and background jobs).</summary>
public static class LabelContextLoader
{
    public static async Task<HermesContext> LoadAsync(IReadSession db, ProjectRow p, string path, Guid actor, CancellationToken ct)
    {
        var s = await db.GetItemStateAsync(p.Id, path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        HermesSource src;
        if (p.VersionId is { } vid)
        {
            var v = await db.GetVersionAsync(vid, p.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
            var f = await db.GetVersionFileAsync(vid, path, ct);
            src = f?.Url is { } u ? new HermesSource(null, null, u) : new HermesSource(Buckets.Datasets, v.Prefix + path, null);
        }
        else src = new HermesSource(Buckets.Datasets, Buckets.Raw(p.WorkspaceId, p.DatasetId) + path, null);
        var ds = await db.GetDatasetAsync(p.DatasetId, p.WorkspaceId, ct);
        return new HermesContext(actor, p, s, schema?.Classes ?? "[]", await db.ListAnnotationsAsync(p.DatasetId, path, ct), src, ds?.Task ?? "");
    }
}

public sealed record GetHermesContextQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<HermesContext>;
public sealed class GetHermesContextQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetHermesContextQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(GetHermesContextQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, uid, ct);
    }
}

/// <summary>Stores a finished check (internal: called by the api checker after the caller was authorized by GetHermesContextQuery).</summary>
public sealed record RecordHermesCheckCommand(Guid Id, Guid WorkspaceId, Guid ProjectId, string Path, int Revision, string Trigger, string Verdict,
    double? Confidence, string DecidedBy, string Summary, string StepsJson, string? Suggestion, Guid? AiRunId, string Action, Guid? ActorId)
    : ICommand<bool>, IInternalCommand;
public sealed class RecordHermesCheckCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordHermesCheckCommand, bool>
{
    public async ValueTask<bool> Handle(RecordHermesCheckCommand c, CancellationToken ct)
    {
        await db.InsertHermesCheckAsync(c.Id, c.WorkspaceId, c.ProjectId, c.Path, c.Revision, c.Trigger, c.Verdict, c.Confidence, c.DecidedBy, c.Summary,
            c.StepsJson, c.Suggestion, c.AiRunId, c.Action, c.ActorId, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>
/// Auto-accept after a passing check (internal). Applies only if the item is still waiting for review at the checked revision —
/// otherwise nothing changes and the caller records why.
/// </summary>
public sealed record HermesAutoAcceptCommand(Guid ProjectId, Guid DatasetId, string Path, int Revision, Guid ActorId) : ICommand<string?>, IInternalCommand;
public sealed class HermesAutoAcceptCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<HermesAutoAcceptCommand, string?>
{
    /// <returns>null when accepted; otherwise the reason it was not.</returns>
    public async ValueTask<string?> Handle(HermesAutoAcceptCommand c, CancellationToken ct)
    {
        var s = await db.LockItemStateAsync(c.ProjectId, c.Path, ct);
        if (s is null) return "Item not found";
        if (s.Status != "in_review") return $"Item is {s.Status}, not waiting for review";
        if (s.Revision != c.Revision) return "Labels changed after the check";
        var now = clock.UtcNow;
        await db.UpdateItemStateAsync(c.ProjectId, c.Path, "accepted", null, null, null, null, false, true, now, ct);
        await db.InsertHistoryAsync(Ids.New(), c.DatasetId, c.Path, "hermes.accept", null, null, c.ActorId, now, ct);
        return null;
    }
}

/// <summary>Undo an automatic accept: the item goes back to review. Lead or Reviewer.</summary>
public sealed record UndoHermesAcceptCommand(Guid WorkspaceId, Guid ProjectId, Guid CheckId) : ICommand<HermesCheckRow>;
public sealed class UndoHermesAcceptCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<UndoHermesAcceptCommand, HermesCheckRow>
{
    public async ValueTask<HermesCheckRow> Handle(UndoHermesAcceptCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead or Reviewer can undo an automatic accept");
        var check = await db.GetHermesCheckAsync(c.CheckId, p.WorkspaceId, ct);
        if (check is null || check.ProjectId != p.Id) throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        if (check.Action != "auto_accepted") throw new PreconditionException("HERMES_NOT_AUTO_ACCEPTED", "This check did not accept the item");
        var s = await db.LockItemStateAsync(p.Id, check.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.Status != "accepted") throw new PreconditionException("LABEL_PROJECT_STATE", $"Item is {s.Status} now; nothing to undo");
        var now = clock.UtcNow;
        await db.UpdateItemStateAsync(p.Id, check.Path, "in_review", null, null, null, null, false, false, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, check.Path, "hermes.undo", null, null, uid, now, ct);
        await db.MarkHermesUndoneAsync(check.Id, p.WorkspaceId, uid, now, ct);
        return (await db.GetHermesCheckAsync(check.Id, p.WorkspaceId, ct))!;
    }
}

public sealed record SetHermesSettingsCommand(Guid WorkspaceId, Guid ProjectId, bool Enabled, double Threshold) : ICommand<ProjectRow>;
public sealed class SetHermesSettingsCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<SetHermesSettingsCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(SetHermesSettingsCommand c, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "dataset.read", ct);
        if (!roles.Contains("lead"))
            await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);   // not a Lead → needs the workspace right
        if (c.Threshold is < 0 or > 1) throw LabelRules.Invalid("threshold", "Threshold must be between 0 and 1");
        await db.SetHermesSettingsAsync(p.Id, p.WorkspaceId, c.Enabled, Math.Round(c.Threshold, 3), ct);
        return (await db.GetLabelProjectAsync(p.Id, p.WorkspaceId, ct))!;
    }
}

public sealed record ListHermesChecksQuery(Guid WorkspaceId, Guid ProjectId, string Verdict, string Path, int Limit) : IQuery<List<HermesCheckRow>>;
public sealed class ListHermesChecksQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListHermesChecksQuery, List<HermesCheckRow>>
{
    public async ValueTask<List<HermesCheckRow>> Handle(ListHermesChecksQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        Guid? only = roles.Count > 0 && roles.All(r => r == "annotator") ? uid : null;
        return await db.ListHermesChecksAsync(p.WorkspaceId, p.Id, q.Verdict ?? "", q.Path ?? "", only, Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}

public sealed record GetHermesCheckQuery(Guid WorkspaceId, Guid ProjectId, Guid CheckId) : IQuery<HermesCheckRow>;
public sealed class GetHermesCheckQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetHermesCheckQuery, HermesCheckRow>
{
    public async ValueTask<HermesCheckRow> Handle(GetHermesCheckQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var c = await db.GetHermesCheckAsync(q.CheckId, p.WorkspaceId, ct);
        if (c is null || c.ProjectId != p.Id) throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && (await db.GetItemStateAsync(p.Id, c.Path, ct))?.AnnotatorId != uid)
            throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        return c;
    }
}

public sealed record GetHermesStatsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, List<HermesStatRow> Stats, HashSet<string> Roles)>;
/// <remarks>Roles = the caller's project roles, plus "manage" when the workspace role allows labeling.manage.</remarks>
public sealed class GetHermesStatsQueryHandler(IReadSession db, LabelAccess access, WorkspaceAuthorizer auth)
    : IQueryHandler<GetHermesStatsQuery, (ProjectRow, List<HermesStatRow>, HashSet<string>)>
{
    public async ValueTask<(ProjectRow, List<HermesStatRow>, HashSet<string>)> Handle(GetHermesStatsQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct)) roles.Add("manage");
        return (p, await db.HermesStatsAsync(p.WorkspaceId, p.Id, ct), roles);
    }
}
