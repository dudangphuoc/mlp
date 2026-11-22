using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>History of one GPU: raw samples for 1h/24h, 1-minute rollups for 7d/30d, about 300 points per
/// range.</summary>
[RequiresPermission("fleet.read")]
public sealed record GetGpuHistoryQuery(Guid WorkspaceId, Guid HostId, string GpuUuid, string Range) : IQuery<GpuHistoryData>;

public sealed class GetGpuHistoryQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IClock clock) : IQueryHandler<GetGpuHistoryQuery, GpuHistoryData>
{
    public async ValueTask<GpuHistoryData> Handle(GetGpuHistoryQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        _ = await db.GetHostAsync(q.HostId, q.WorkspaceId, ct) ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
        var (span, step, raw) = q.Range switch
        {
            "24h" => (TimeSpan.FromHours(24), 300, true),
            "7d" => (TimeSpan.FromDays(7), 1800, false),
            "30d" => (TimeSpan.FromDays(30), 7200, false),
            _ => (TimeSpan.FromHours(1), 15, true),
        };
        var since = clock.UtcNow - span;
        var points = raw ? await db.GpuHistoryRawAsync(step, q.HostId, q.GpuUuid, since, ct) : await db.GpuHistoryRollupAsync(step, q.HostId, q.GpuUuid, since, ct);
        return new GpuHistoryData(points, await db.GpuJobMarksAsync(q.HostId, q.GpuUuid, since, ct), step);
    }
}
