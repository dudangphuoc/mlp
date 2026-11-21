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

public sealed record GetMetricsQuery(Guid WorkspaceId, Guid RunId, IReadOnlyList<string>? Keys = null, int MaxPoints = 0) : IQuery<IReadOnlyList<MetricRow>>;

public sealed class GetMetricsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMetricsQuery, IReadOnlyList<MetricRow>>
{
    public async ValueTask<IReadOnlyList<MetricRow>> Handle(GetMetricsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        if (r is null || r.WorkspaceId != q.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        var keys = q.Keys?.Where(k => k.Length > 0).Distinct().ToArray() ?? [];
        return await db.GetMetricsSampledAsync(q.RunId, keys.Length > 0, keys, q.MaxPoints is > 0 and <= 5000 ? q.MaxPoints : 600, ct);
    }
}
