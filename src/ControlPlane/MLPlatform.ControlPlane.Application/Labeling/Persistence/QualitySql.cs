using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public static partial class QualitySql
{
    [SqlQuery("Label/QualityByAnnotator.sql")]
    public static partial Task<List<QualityRow>> QualityByAnnotatorAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/CalibrationRows.sql")]
    public static partial Task<List<CalibrationRow>> CalibrationRowsAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/SetHermesExplanation.sql")]
    public static partial Task SetHermesExplanationAsync(this IWriteSession db, Guid id, Guid workspaceId, string explanation, string lang, Guid? runId,
        DateTimeOffset now, CancellationToken ct);
}
