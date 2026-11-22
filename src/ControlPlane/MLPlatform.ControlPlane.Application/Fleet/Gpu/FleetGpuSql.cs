using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Gpu;

public static partial class FleetGpuSql
{
    [SqlQuery("Fleet/SetHostGpuYield.sql")]
    public static partial Task SetHostGpuYieldAsync(this IWriteSession db, Guid id, bool yield, CancellationToken ct);
    [SqlQuery("Fleet/GpuYieldingHosts.sql")]
    public static partial Task<List<YieldingHostRow>> GpuYieldingHostsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Fleet/HostAlertTarget.sql")]
    public static partial Task<HostAlertTarget?> HostAlertTargetAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Fleet/HostLastMetrics.sql")]
    public static partial Task<HostLastMetricsRow?> HostLastMetricsAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
}
