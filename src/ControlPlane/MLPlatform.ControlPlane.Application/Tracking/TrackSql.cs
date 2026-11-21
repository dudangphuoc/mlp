using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record ProjectRow(Guid Id, Guid WorkspaceId, string Slug, string Name, string Task, DateTimeOffset CreatedAt);
public sealed record RunRow(Guid Id, Guid ProjectId, Guid WorkspaceId, string Name, string State, string Recipe, string Variant,
    string Task, Guid? JobId, string Params, string PrimaryMetricKey, double? PrimaryMetricValue, DateTimeOffset CreatedAt,
    int Number, Guid? DatasetVersionId, bool FromScratch, int Gpus, double Cpus, DateTimeOffset? EndedAt, Guid UserId);
/// <summary>One metric of a run without its points: how many, the last step and value, the range.</summary>
public sealed record MetricSummaryRow(string Key, long Points, long LastStep, double MinValue, double MaxValue, double LastValue);
/// <summary>Run counts and the best result of a project, for the project list.</summary>
public sealed record ProjectStatsRow(Guid ProjectId, long RunCount, long ActiveCount, DateTimeOffset? LastRunAt, int? BestNumber, string? BestName,
    string? BestMetricKey, double? BestMetricValue);
/// <summary>A project's own copy of a Catalog recipe (D-49).</summary>
public sealed record ProjectRecipeRow(Guid Id, Guid WorkspaceId, Guid ProjectId, Guid OriginRecipeId, string OriginName, string OriginManifest,
    string OriginManifestSha256, DateTimeOffset CreatedAt, int LatestRevision);
/// <summary>One person's working copy of a project recipe plus what they want to train it on.</summary>
public sealed record DraftRow(Guid Id, Guid ProjectRecipeId, Guid UserId, Guid? BaseRevisionId, string Variant, string Params, Guid? DatasetVersionId,
    string StartKind, string TrainKind, Guid? RetrainOfRunId, Guid? HostId, int Gpus, double Cpus, string RunName, int Etag);
/// <summary>An immutable snapshot of a draft's recipe content: manifest, variant and the parameter values that were set.</summary>
public sealed record RevisionRow(Guid Id, Guid ProjectRecipeId, int Number, Guid? ParentRevisionId, string Manifest, string ManifestSha256, string Variant,
    string Params, string ContentSha256, Guid CreatedBy, DateTimeOffset CreatedAt);
/// <summary>A revision as the history list shows it.</summary>
public sealed record RevisionListRow(int Number, string Variant, string Params, int? Parent, Guid CreatedBy, DateTimeOffset CreatedAt, long RunCount);
/// <summary>What a run cooked: revision number, how it started, which machine it was pinned to.</summary>
public sealed record RunExtraRow(Guid RunId, Guid? RevisionId, int? RevisionNumber, string? TrainKind, string? StartKind, int? RetrainOfNumber, Guid? HostId);
/// <summary>A trial (round 1 R15–R22) with the revision number it tried and the job of its hidden run.</summary>
public sealed record TrialRow(Guid Id, Guid WorkspaceId, Guid ProjectId, Guid ProjectRecipeId, Guid RevisionId, Guid RunId, string TrialKey,
    Guid? DatasetVersionId, string StartKind, Guid? HostId, int Gpus, string State, string Stages, double? StepsPerS, long? PeakVramMb,
    bool VramEstimated, double? ValSPerBatch, int TrainImages, int ValImages, string Error, Guid CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt, int RevisionNumber, Guid? JobId);
public sealed record MetricRow(string Key, long Step, DateTimeOffset At, double Value);
public sealed record ArtifactRow(string Path, long SizeBytes);
public sealed record FinalMetricRow(string Key, double Value);

public static partial class TrackSql
{
    [SqlQuery("Track/InsertProject.sql")]
    public static partial Task InsertProjectAsync(this IWriteSession db, Guid id, Guid workspaceId, string slug, string name, string task,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/ListProjects.sql")]
    public static partial Task<List<ProjectRow>> ListProjectsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/ProjectStats.sql")]
    public static partial Task<List<ProjectStatsRow>> ProjectStatsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/GetProject.sql")]
    public static partial Task<ProjectRow?> GetProjectAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/InsertProjectRecipe.sql")]
    public static partial Task InsertProjectRecipeAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, Guid originRecipeId, string originName,
        string originManifest, string originManifestSha256, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/GetProjectRecipe.sql")]
    public static partial Task<ProjectRecipeRow?> GetProjectRecipeAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/GetProjectRecipeByName.sql")]
    public static partial Task<ProjectRecipeRow?> GetProjectRecipeByNameAsync(this IReadSession db, Guid projectId, Guid workspaceId, string originName, CancellationToken ct);
    [SqlQuery("Track/ListProjectRecipes.sql")]
    public static partial Task<List<ProjectRecipeRow>> ListProjectRecipesAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/UpdateProjectRecipeOrigin.sql")]
    public static partial Task UpdateProjectRecipeOriginAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid originRecipeId, string originManifest,
        string originManifestSha256, CancellationToken ct);
    [SqlQuery("Track/GetDraft.sql")]
    public static partial Task<DraftRow?> GetDraftAsync(this IReadSession db, Guid projectRecipeId, Guid userId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/InsertDraft.sql")]
    public static partial Task InsertDraftAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectRecipeId, Guid userId, Guid? baseRevisionId, string variant,
        string paramsJson, Guid? datasetVersionId, string startKind, string trainKind, Guid? retrainOfRunId, Guid? hostId, int gpus, double cpus, string runName,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/UpdateDraft.sql")]
    public static partial Task<long> UpdateDraftAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid? baseRevisionId, string variant, string paramsJson,
        Guid? datasetVersionId, string startKind, string trainKind, Guid? retrainOfRunId, Guid? hostId, int gpus, double cpus, string runName, int etag,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/GetRevision.sql")]
    public static partial Task<RevisionRow?> GetRevisionAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/GetRevisionByNumber.sql")]
    public static partial Task<RevisionRow?> GetRevisionByNumberAsync(this IReadSession db, Guid projectRecipeId, Guid workspaceId, int number, CancellationToken ct);
    [SqlQuery("Track/GetRevisionByContent.sql")]
    public static partial Task<RevisionRow?> GetRevisionByContentAsync(this IReadSession db, Guid projectRecipeId, Guid workspaceId, string contentSha256, CancellationToken ct);
    [SqlQuery("Track/NextRevisionNumber.sql")]
    public static partial Task<long> NextRevisionNumberAsync(this IWriteSession db, Guid projectRecipeId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/InsertRevision.sql")]
    public static partial Task InsertRevisionAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectRecipeId, int number, Guid? parentRevisionId,
        string manifest, string manifestSha256, string variant, string paramsJson, string contentSha256, string trialKey, Guid createdBy, DateTimeOffset now,
        CancellationToken ct);
    [SqlQuery("Track/ListRevisions.sql")]
    public static partial Task<List<RevisionListRow>> ListRevisionsAsync(this IReadSession db, Guid projectRecipeId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/SetRunRevision.sql")]
    public static partial Task SetRunRevisionAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid? revisionId, string? trainKind, string? startKind,
        Guid? retrainOfRunId, Guid? hostId, CancellationToken ct);
    [SqlQuery("Track/RunExtras.sql")]
    public static partial Task<List<RunExtraRow>> RunExtrasAsync(this IReadSession db, Guid workspaceId, Guid[] ids, CancellationToken ct);
    [SqlQuery("Track/InsertRun.sql")]
    public static partial Task InsertRunAsync(this IWriteSession db, Guid id, Guid projectId, Guid workspaceId, string name, Guid jobId,
        Guid userId, string recipe, string variant, string task, string paramsJson, string primaryMetricKey, DateTimeOffset now,
        int number, Guid? datasetVersionId, bool fromScratch, int gpus, double cpus, CancellationToken ct);
    [SqlQuery("Track/NextRunNumber.sql")]
    public static partial Task<long> NextRunNumberAsync(this IWriteSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/RenameRun.sql")]
    public static partial Task RenameRunAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, CancellationToken ct);
    [SqlQuery("Track/DeleteRun.sql")]
    public static partial Task DeleteRunAsync(this IWriteSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/MetricSummary.sql")]
    public static partial Task<List<MetricSummaryRow>> MetricSummaryAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Track/GetMetricsSampled.sql")]
    public static partial Task<List<MetricRow>> GetMetricsSampledAsync(this IReadSession db, Guid runId, bool useKeys, string[] keys, int maxPoints, CancellationToken ct);
    [SqlQuery("Track/ListRuns.sql")]
    public static partial Task<List<RunRow>> ListRunsAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/GetRun.sql")]
    public static partial Task<RunRow?> GetRunAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Track/GetRunByJob.sql")]
    public static partial Task<RunRow?> GetRunByJobAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Track/InsertMetric.sql")]
    public static partial Task InsertMetricAsync(this IWriteSession db, Guid runId, string key, long step, DateTimeOffset at, double value, CancellationToken ct);
    [SqlQuery("Track/SetPrimaryMetric.sql")]
    public static partial Task SetPrimaryMetricAsync(this IWriteSession db, Guid id, double value, CancellationToken ct);
    [SqlQuery("Track/SetRunState.sql")]
    public static partial Task SetRunStateAsync(this IWriteSession db, Guid id, string state, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/GetMetrics.sql")]
    public static partial Task<List<MetricRow>> GetMetricsAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Track/UpsertRunArtifact.sql")]
    public static partial Task UpsertRunArtifactAsync(this IWriteSession db, Guid runId, string path, long sizeBytes, CancellationToken ct);
    [SqlQuery("Track/ListRunArtifacts.sql")]
    public static partial Task<List<ArtifactRow>> ListRunArtifactsAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Track/FinalMetrics.sql")]
    public static partial Task<List<FinalMetricRow>> FinalMetricsAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Track/NextTrialRunNumber.sql")]
    public static partial Task<long> NextTrialRunNumberAsync(this IWriteSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/MarkRunTrial.sql")]
    public static partial Task MarkRunTrialAsync(this IWriteSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/SetRunTrial.sql")]
    public static partial Task SetRunTrialAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid trialId, CancellationToken ct);
    [SqlQuery("Track/InsertTrial.sql")]
    public static partial Task InsertTrialAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, Guid projectRecipeId, Guid revisionId, Guid runId,
        string trialKey, Guid? datasetVersionId, string startKind, Guid? hostId, int gpus, int trainImages, int valImages, Guid createdBy, DateTimeOffset now,
        CancellationToken ct);
    [SqlQuery("Track/GetTrial.sql")]
    public static partial Task<TrialRow?> GetTrialAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/GetTrialByRun.sql")]
    public static partial Task<TrialRow?> GetTrialByRunAsync(this IReadSession db, Guid runId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/LatestTrial.sql")]
    public static partial Task<TrialRow?> LatestTrialAsync(this IReadSession db, Guid projectRecipeId, Guid workspaceId, string trialKey, Guid? datasetVersionId,
        string startKind, Guid? hostId, bool passedOnly, CancellationToken ct);
    [SqlQuery("Track/FinishTrial.sql")]
    public static partial Task FinishTrialAsync(this IWriteSession db, Guid id, Guid workspaceId, string state, string stagesJson, double? stepsPerS, long? peakVramMb,
        bool vramEstimated, double? valSPerBatch, int trainImages, int valImages, string error, Guid? hostId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Track/SetTrialState.sql")]
    public static partial Task SetTrialStateAsync(this IWriteSession db, Guid id, Guid workspaceId, string state, string error, DateTimeOffset now, CancellationToken ct);
}
