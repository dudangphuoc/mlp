using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Every metric of a run without its points — the run page lists them and loads charts on demand.</summary>
[RequiresPermission("run.read")]
public sealed record GetMetricSummaryQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<MetricSummaryRow>>;

/// <summary>Reads the per-metric summary after checking the run belongs to the workspace.</summary>
public sealed class GetMetricSummaryQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMetricSummaryQuery, IReadOnlyList<MetricSummaryRow>>
{
    public async ValueTask<IReadOnlyList<MetricSummaryRow>> Handle(GetMetricSummaryQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        if (r is null || r.WorkspaceId != q.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        return await db.MetricSummaryAsync(q.RunId, ct);
    }
}
