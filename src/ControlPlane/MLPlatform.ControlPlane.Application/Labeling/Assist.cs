using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/*
 * AI label proposals (Hermes phase 3, M05 §4.2). A project lists the sources it may use (AI models from the AI page, or the
 * workspace's own deployments), maps model labels to schema classes, and opts in to external APIs. Proposals are stored per
 * item as "suggestion sets" — pending until a person applies or dismisses them; applied labels become annotations with
 * source = model and their confidence. Calling the models happens in the api (LabelProposer).
 */

public sealed record AssistSettingsRow(Guid ProjectId, string SourcesJson, string ClassMappingJson, bool AllowExternal, double MinConfidence, DateTimeOffset UpdatedAt);

public sealed record SuggestionSetRow(Guid Id, Guid ProjectId, string Path, int Revision, string SourceKind, Guid SourceId, string SourceLabel, bool External,
    string State, string Detail, string ItemsJson, string UnmappedJson, Guid? AiRunId, int LatencyMs, Guid? JobId, string Decision, string? CreatedByName,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed record PrelabelJobRow(Guid Id, Guid ProjectId, string SourceKind, Guid SourceId, string SourceLabel, int Total, int Done, int Failed, string State,
    string? Error, string? CreatedByName, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public sealed record OpenPrelabelJobRow(Guid Id, Guid ProjectId, string SourceKind, Guid SourceId, string SourceLabel, int Total, int Done, int Failed, string State,
    string? Error, string? CreatedByName, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, Guid WorkspaceId);

public static partial class AssistSql
{
    [SqlQuery("Label/GetAssistSettings.sql")]
    public static partial Task<AssistSettingsRow?> GetAssistSettingsAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/UpsertAssistSettings.sql")]
    public static partial Task UpsertAssistSettingsAsync(this IWriteSession db, Guid projectId, Guid workspaceId, string sourcesJson, string classMappingJson,
        bool allowExternal, double minConfidence, Guid updatedBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/InsertSuggestionSet.sql")]
    public static partial Task InsertSuggestionSetAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string path, int revision, string sourceKind,
        Guid sourceId, string sourceLabel, bool external, string state, string detail, string itemsJson, string unmappedJson, Guid? aiRunId, int latencyMs, Guid? jobId,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ListSuggestionSets.sql")]
    public static partial Task<List<SuggestionSetRow>> ListSuggestionSetsAsync(this IReadSession db, Guid workspaceId, Guid projectId, string path, Guid? jobId,
        Guid? onlyAnnotator, int lim, CancellationToken ct);
    [SqlQuery("Label/GetSuggestionSet.sql")]
    public static partial Task<SuggestionSetRow?> GetSuggestionSetAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/DecideSuggestionSet.sql")]
    public static partial Task DecideSuggestionSetAsync(this IWriteSession db, Guid id, Guid workspaceId, string decision, Guid decidedBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/InsertPrelabelJob.sql")]
    public static partial Task InsertPrelabelJobAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string sourceKind, Guid sourceId, string sourceLabel,
        int total, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ListPrelabelJobs.sql")]
    public static partial Task<List<PrelabelJobRow>> ListPrelabelJobsAsync(this IReadSession db, Guid workspaceId, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/GetPrelabelJob.sql")]
    public static partial Task<PrelabelJobRow?> GetPrelabelJobAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/ListOpenPrelabelJobs.sql")]
    public static partial Task<List<OpenPrelabelJobRow>> ListOpenPrelabelJobsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Label/UpdatePrelabelJob.sql")]
    public static partial Task UpdatePrelabelJobAsync(this IWriteSession db, Guid id, int done, int failed, string state, string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/CancelPrelabelJob.sql")]
    public static partial Task CancelPrelabelJobAsync(this IWriteSession db, Guid id, Guid workspaceId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/CountPrelabelTargets.sql")]
    public static partial Task<long> CountPrelabelTargetsAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/NextPrelabelPaths.sql")]
    public static partial Task<List<PathRow>> NextPrelabelPathsAsync(this IReadSession db, Guid projectId, Guid jobId, int lim, CancellationToken ct);
    [SqlQuery("Label/GetProjectInternal.sql")]
    public static partial Task<ProjectRow?> GetProjectInternalAsync(this IReadSession db, Guid id, CancellationToken ct);
}

/// <summary>Effective settings (defaults when never saved).</summary>
public sealed record ProjectAssistSettings(List<(string Kind, Guid Id)> Sources, Dictionary<string, int> ClassMapping, bool AllowExternal, double MinConfidence, bool Saved)
{
    public static ProjectAssistSettings From(AssistSettingsRow? r)
    {
        if (r is null) return new([], [], false, 0.3, false);
        var sources = new List<(string, Guid)>();
        try
        {
            foreach (var n in JsonNode.Parse(r.SourcesJson) as JsonArray ?? [])
                if (n?["kind"]?.ToString() is { } k && Guid.TryParse(n["id"]?.ToString(), out var id)) sources.Add((k, id));
        }
        catch (System.Text.Json.JsonException) { }
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(r.ClassMappingJson) is JsonObject o)
                foreach (var (k, v) in o) if (v is JsonValue jv && jv.TryGetValue<int>(out var cid)) map[k] = cid;
        }
        catch (System.Text.Json.JsonException) { }
        return new(sources, map, r.AllowExternal, r.MinConfidence, true);
    }

    /// <summary>AI assist is on by default: a project that never saved its settings uses every internal source (data stays on the platform).</summary>
    public bool IsEnabled(string kind, Guid id, bool external) => Saved ? Sources.Contains((kind, id)) : !external;
}

/// <summary>Who may do what in assist: everyone in the project reads; Lead (or labeling.manage) configures and runs batches.</summary>
internal static class AssistAccess
{
    public static async Task<(Guid Uid, ProjectRow P, HashSet<string> Roles)> ManageAsync(LabelAccess access, WorkspaceAuthorizer auth, Guid ws, Guid pid, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(ws, pid, "dataset.read", ct);
        if (!roles.Contains("lead") && !await auth.HasAsync(ws, "labeling.manage", ct))
            throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead can change AI assist settings or run batches");
        return (uid, p, roles);
    }
}

public sealed record GetAssistSettingsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, ProjectAssistSettings Settings, bool CanManage)>;
public sealed class GetAssistSettingsQueryHandler(IReadSession db, LabelAccess access, WorkspaceAuthorizer auth)
    : IQueryHandler<GetAssistSettingsQuery, (ProjectRow, ProjectAssistSettings, bool)>
{
    public async ValueTask<(ProjectRow, ProjectAssistSettings, bool)> Handle(GetAssistSettingsQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var manage = roles.Contains("lead") || await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct);
        return (p, ProjectAssistSettings.From(await db.GetAssistSettingsAsync(p.Id, p.WorkspaceId, ct)), manage);
    }
}

/// <summary>Internal (background jobs): settings by project, no caller.</summary>
public sealed record GetAssistSettingsInternalQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<ProjectAssistSettings>;
public sealed class GetAssistSettingsInternalQueryHandler(IReadSession db) : IQueryHandler<GetAssistSettingsInternalQuery, ProjectAssistSettings>
{
    public async ValueTask<ProjectAssistSettings> Handle(GetAssistSettingsInternalQuery q, CancellationToken ct) =>
        ProjectAssistSettings.From(await db.GetAssistSettingsAsync(q.ProjectId, q.WorkspaceId, ct));
}

public sealed record SaveAssistSettingsCommand(Guid WorkspaceId, Guid ProjectId, string SourcesJson, string ClassMappingJson, bool AllowExternal, double MinConfidence)
    : ICommand<ProjectAssistSettings>;
public sealed class SaveAssistSettingsCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock)
    : ICommandHandler<SaveAssistSettingsCommand, ProjectAssistSettings>
{
    public async ValueTask<ProjectAssistSettings> Handle(SaveAssistSettingsCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        if (c.MinConfidence is < 0 or > 1) throw LabelRules.Invalid("min_confidence", "Minimum confidence must be between 0 and 1");
        // Normalize: only known kinds, valid ids, no duplicates; mapping values must be schema class ids.
        var sources = new JsonArray();
        var seen = new HashSet<string>();
        try
        {
            foreach (var n in JsonNode.Parse(string.IsNullOrEmpty(c.SourcesJson) ? "[]" : c.SourcesJson) as JsonArray ?? [])
            {
                var k = n?["kind"]?.ToString();
                if (k is not ("model" or "deployment") || !Guid.TryParse(n!["id"]?.ToString(), out var id)) throw LabelRules.Invalid("sources", "Each source needs kind model|deployment and an id");
                if (seen.Add($"{k}:{id}")) sources.Add((JsonNode)new JsonObject { ["kind"] = k, ["id"] = id.ToString() });
            }
        }
        catch (System.Text.Json.JsonException) { throw LabelRules.Invalid("sources", "Invalid JSON"); }
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        var ids = LabelRules.ParseClasses(schema?.Classes ?? "[]");
        var map = new JsonObject();
        try
        {
            if (JsonNode.Parse(string.IsNullOrEmpty(c.ClassMappingJson) ? "{}" : c.ClassMappingJson) is JsonObject o)
                foreach (var (k, v) in o)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (v is not JsonValue jv || !jv.TryGetValue<int>(out var cid) || !ids.Contains(cid)) throw LabelRules.Invalid("class_mapping", $"\"{k}\" maps to a class that is not in the schema");
                    map[k.Trim()] = cid;
                }
        }
        catch (System.Text.Json.JsonException) { throw LabelRules.Invalid("class_mapping", "Invalid JSON"); }
        await db.UpsertAssistSettingsAsync(p.Id, p.WorkspaceId, sources.ToJsonString(), map.ToJsonString(), c.AllowExternal, Math.Round(c.MinConfidence, 3), uid, clock.UtcNow, ct);
        return ProjectAssistSettings.From(await db.GetAssistSettingsAsync(p.Id, p.WorkspaceId, ct));
    }
}

/// <summary>Context for a background proposal (no caller: the job creator was authorized when the job started).</summary>
public sealed record GetLabelContextInternalQuery(Guid ProjectId, string Path, Guid Actor) : IQuery<HermesContext>;
public sealed class GetLabelContextInternalQueryHandler(IReadSession db) : IQueryHandler<GetLabelContextInternalQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(GetLabelContextInternalQuery q, CancellationToken ct)
    {
        var p = await db.GetProjectInternalAsync(q.ProjectId, ct) ?? throw new NotFoundException("LABEL_PROJECT_NOT_FOUND", "Labeling project not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, q.Actor, ct);
    }
}

public sealed record RecordSuggestionSetCommand(Guid Id, Guid WorkspaceId, Guid ProjectId, string Path, int Revision, string SourceKind, Guid SourceId,
    string SourceLabel, bool External, string State, string Detail, string ItemsJson, string UnmappedJson, Guid? AiRunId, int LatencyMs, Guid? JobId, Guid CreatedBy)
    : ICommand<bool>, IInternalCommand;
public sealed class RecordSuggestionSetCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordSuggestionSetCommand, bool>
{
    public async ValueTask<bool> Handle(RecordSuggestionSetCommand c, CancellationToken ct)
    {
        await db.InsertSuggestionSetAsync(c.Id, c.WorkspaceId, c.ProjectId, c.Path, c.Revision, c.SourceKind, c.SourceId, c.SourceLabel, c.External, c.State,
            c.Detail, c.ItemsJson, c.UnmappedJson, c.AiRunId, c.LatencyMs, c.JobId, c.CreatedBy, clock.UtcNow, ct);
        return true;
    }
}

public sealed record ListSuggestionSetsQuery(Guid WorkspaceId, Guid ProjectId, string Path, Guid? JobId, int Limit) : IQuery<List<SuggestionSetRow>>;
public sealed class ListSuggestionSetsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListSuggestionSetsQuery, List<SuggestionSetRow>>
{
    public async ValueTask<List<SuggestionSetRow>> Handle(ListSuggestionSetsQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        Guid? only = roles.Count > 0 && roles.All(r => r == "annotator") ? uid : null;
        return await db.ListSuggestionSetsAsync(p.WorkspaceId, p.Id, q.Path ?? "", q.JobId, only, Math.Clamp(q.Limit <= 0 ? 50 : q.Limit, 1, 500), ct);
    }
}

public sealed record GetSuggestionSetQuery(Guid WorkspaceId, Guid ProjectId, Guid Id) : IQuery<SuggestionSetRow>;
public sealed class GetSuggestionSetQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetSuggestionSetQuery, SuggestionSetRow>
{
    public async ValueTask<SuggestionSetRow> Handle(GetSuggestionSetQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetSuggestionSetAsync(q.Id, p.WorkspaceId, ct);
        if (s is null || s.ProjectId != p.Id) throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && (await db.GetItemStateAsync(p.Id, s.Path, ct))?.AnnotatorId != uid)
            throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        return s;
    }
}

/// <summary>The annotator applied (labels copied into the editor, saved with the item) or dismissed a suggestion set.</summary>
public sealed record DecideSuggestionSetCommand(Guid WorkspaceId, Guid ProjectId, Guid Id, string Decision) : ICommand<SuggestionSetRow>;
public sealed class DecideSuggestionSetCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<DecideSuggestionSetCommand, SuggestionSetRow>
{
    public async ValueTask<SuggestionSetRow> Handle(DecideSuggestionSetCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (c.Decision is not ("applied" or "dismissed")) throw LabelRules.Invalid("decision", "Decision must be applied or dismissed");
        var s = await db.GetSuggestionSetAsync(c.Id, p.WorkspaceId, ct);
        if (s is null || s.ProjectId != p.Id) throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        await db.DecideSuggestionSetAsync(s.Id, p.WorkspaceId, c.Decision, uid, clock.UtcNow, ct);
        return (await db.GetSuggestionSetAsync(s.Id, p.WorkspaceId, ct))!;
    }
}

/// <summary>Who triggered a proposal and with what rights (checked in the api before any model is called).</summary>
public sealed record AuthorizeProposalQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<HermesContext>;
public sealed class AuthorizeProposalQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<AuthorizeProposalQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(AuthorizeProposalQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "labeling.annotate", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (!roles.Contains("lead") && !roles.Contains("reviewer") && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, uid, ct);
    }
}

public sealed record StartPrelabelCommand(Guid WorkspaceId, Guid ProjectId, string SourceKind, Guid SourceId, string SourceLabel, int Limit) : ICommand<PrelabelJobRow>;
public sealed class StartPrelabelCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock) : ICommandHandler<StartPrelabelCommand, PrelabelJobRow>
{
    public async ValueTask<PrelabelJobRow> Handle(StartPrelabelCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        LabelAccess.RequireActive(p);
        var targets = await db.CountPrelabelTargetsAsync(p.Id, p.WorkspaceId, ct);
        if (targets == 0) throw new PreconditionException("LABEL_NOTHING_TO_PRELABEL", "Every item already has labels or a pending suggestion");
        var total = (int)Math.Min(targets, c.Limit <= 0 ? targets : c.Limit);
        var id = Ids.New();
        await db.InsertPrelabelJobAsync(id, p.WorkspaceId, p.Id, c.SourceKind, c.SourceId, c.SourceLabel, total, uid, clock.UtcNow, ct);
        return (await db.GetPrelabelJobAsync(id, p.WorkspaceId, ct))!;
    }
}

public sealed record CancelPrelabelCommand(Guid WorkspaceId, Guid ProjectId, Guid JobId) : ICommand<PrelabelJobRow>;
public sealed class CancelPrelabelCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock) : ICommandHandler<CancelPrelabelCommand, PrelabelJobRow>
{
    public async ValueTask<PrelabelJobRow> Handle(CancelPrelabelCommand c, CancellationToken ct)
    {
        var (_, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        var j = await db.GetPrelabelJobAsync(c.JobId, p.WorkspaceId, ct);
        if (j is null || j.ProjectId != p.Id) throw new NotFoundException("LABEL_JOB_NOT_FOUND", "Pre-label job not found");
        await db.CancelPrelabelJobAsync(j.Id, p.WorkspaceId, clock.UtcNow, ct);
        return (await db.GetPrelabelJobAsync(j.Id, p.WorkspaceId, ct))!;
    }
}

public sealed record ListPrelabelJobsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(List<PrelabelJobRow> Jobs, long Targets)>;
public sealed class ListPrelabelJobsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListPrelabelJobsQuery, (List<PrelabelJobRow>, long)>
{
    public async ValueTask<(List<PrelabelJobRow>, long)> Handle(ListPrelabelJobsQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return (await db.ListPrelabelJobsAsync(p.WorkspaceId, p.Id, ct), await db.CountPrelabelTargetsAsync(p.Id, p.WorkspaceId, ct));
    }
}

// ───────── background runner (internal) ─────────

public sealed record ListOpenPrelabelJobsQuery : IQuery<List<OpenPrelabelJobRow>>;
public sealed class ListOpenPrelabelJobsQueryHandler(IReadSession db) : IQueryHandler<ListOpenPrelabelJobsQuery, List<OpenPrelabelJobRow>>
{
    public async ValueTask<List<OpenPrelabelJobRow>> Handle(ListOpenPrelabelJobsQuery q, CancellationToken ct) => await db.ListOpenPrelabelJobsAsync(ct);
}

public sealed record NextPrelabelPathsQuery(Guid ProjectId, Guid JobId, int Limit) : IQuery<List<string>>;
public sealed class NextPrelabelPathsQueryHandler(IReadSession db) : IQueryHandler<NextPrelabelPathsQuery, List<string>>
{
    public async ValueTask<List<string>> Handle(NextPrelabelPathsQuery q, CancellationToken ct) =>
        [.. (await db.NextPrelabelPathsAsync(q.ProjectId, q.JobId, q.Limit, ct)).Select(r => r.Path)];
}

public sealed record UpdatePrelabelJobCommand(Guid Id, int Done, int Failed, string State, string? Error) : ICommand<bool>, IInternalCommand;
public sealed class UpdatePrelabelJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdatePrelabelJobCommand, bool>
{
    public async ValueTask<bool> Handle(UpdatePrelabelJobCommand c, CancellationToken ct)
    {
        await db.UpdatePrelabelJobAsync(c.Id, c.Done, c.Failed, c.State, c.Error, clock.UtcNow, ct);
        return true;
    }
}

public sealed record GetPrelabelJobStateQuery(Guid Id, Guid WorkspaceId) : IQuery<string?>;
public sealed class GetPrelabelJobStateQueryHandler(IReadSession db) : IQueryHandler<GetPrelabelJobStateQuery, string?>
{
    public async ValueTask<string?> Handle(GetPrelabelJobStateQuery q, CancellationToken ct) => (await db.GetPrelabelJobAsync(q.Id, q.WorkspaceId, ct))?.State;
}

/// <summary>Deployments of the project's workspace (internal: called after the project was authorized; background jobs too).</summary>
public sealed record ListAssistDeploymentsQuery(Guid WorkspaceId) : IQuery<List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>>;
public sealed class ListAssistDeploymentsQueryHandler(IReadSession db)
    : IQueryHandler<ListAssistDeploymentsQuery, List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>>
{
    public async ValueTask<List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>> Handle(ListAssistDeploymentsQuery q, CancellationToken ct) =>
        await MLPlatform.ControlPlane.Application.Serving.ServingSql.ListDeploymentsAsync(db, q.WorkspaceId, ct);
}
