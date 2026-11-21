using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public static partial class EvaluationSql
{
    [SqlQuery("Registry/InsertEvaluation.sql")]
    public static partial Task InsertEvaluationAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid modelVersionId, Guid datasetVersionId,
        string pathFilter, int maxSamples, double confThreshold, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/GetEvaluation.sql")]
    public static partial Task<EvaluationRow?> GetEvaluationAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Registry/ListEvaluations.sql")]
    public static partial Task<List<EvaluationRow>> ListEvaluationsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
    [SqlQuery("Registry/ListActiveEvaluations.sql")]
    public static partial Task<List<EvaluationRow>> ListActiveEvaluationsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Registry/SetEvaluationState.sql")]
    public static partial Task SetEvaluationStateAsync(this IWriteSession db, Guid id, string state, string? reason, Guid? deploymentId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/SetEvaluationTotal.sql")]
    public static partial Task SetEvaluationTotalAsync(this IWriteSession db, Guid id, int total, CancellationToken ct);
    [SqlQuery("Registry/InsertEvalSample.sql")]
    public static partial Task InsertEvalSampleAsync(this IWriteSession db, Guid evaluationId, int idx, string path, int width, int height, string gt,
        string pred, int tp, int fp, int fn, bool correct, string? error, double latencyMs, CancellationToken ct);
    [SqlQuery("Registry/DoneSampleIndexes.sql")]
    public static partial Task<List<IdxRow>> DoneSampleIndexesAsync(this IReadSession db, Guid evaluationId, CancellationToken ct);
    [SqlQuery("Registry/AllEvalSamples.sql")]
    public static partial Task<List<EvalSampleRow>> AllEvalSamplesAsync(this IReadSession db, Guid evaluationId, CancellationToken ct);
    [SqlQuery("Registry/ListEvalSamples.sql")]
    public static partial Task<List<EvalSampleRow>> ListEvalSamplesAsync(this IReadSession db, Guid evaluationId, bool errorsOnly, int offset, int limit, CancellationToken ct);
    [SqlQuery("Registry/CompleteEvaluation.sql")]
    public static partial Task CompleteEvaluationAsync(this IWriteSession db, Guid id, string metrics, string perClass, string? confusion, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/MergeVersionMetrics.sql")]
    public static partial Task MergeVersionMetricsAsync(this IWriteSession db, Guid id, string metrics, CancellationToken ct);
}
