using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Fleet.Gpu;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

[RequiresPermission("fleet.read")]
public sealed record GetHostGpusQuery(Guid WorkspaceId, Guid HostId) : IQuery<HostGpuData>;

public sealed class GetHostGpusQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHostGpusQuery, HostGpuData>
{
    public async ValueTask<HostGpuData> Handle(GetHostGpusQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        var host = await db.GetHostAsync(q.HostId, q.WorkspaceId, ct) ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
        var metrics = await db.HostLastMetricsAsync(q.HostId, q.WorkspaceId, ct);
        var reservations = (await db.HostGpuAllocationsAsync(q.HostId, ct))
            .SelectMany(a => a.GpuUuids.Select(g => new GpuReservation(g, a.AttemptId, a.JobId, a.JobName, GpuNeed.Mb(JobSpecDto.Parse(a.Spec)))))
            .ToList();
        return new HostGpuData(host, metrics?.Metrics, metrics?.MetricsAt, await db.ListGpuAlertsAsync(q.HostId, ct), reservations);
    }
}
