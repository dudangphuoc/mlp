using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Internal for M11: artifacts + final metrics of a run (no permission check — the caller already
/// checked).</summary>
public sealed record GetRunForRegistryQuery(Guid RunId) : IQuery<(RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?>;

public sealed class GetRunForRegistryQueryHandler(IReadSession db)
    : IQueryHandler<GetRunForRegistryQuery, (RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?>
{
    public async ValueTask<(RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?> Handle(GetRunForRegistryQuery q, CancellationToken ct)
    {
        if (await db.GetRunAsync(q.RunId, ct) is not { } run) return null;
        return (run, await db.ListRunArtifactsAsync(run.Id, ct), await db.FinalMetricsAsync(run.Id, ct));
    }
}
