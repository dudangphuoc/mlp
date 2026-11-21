using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

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
