using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public static partial class SchedGpuSql
{
    [SqlQuery("Sched/HostGpuAllocations.sql")]
    public static partial Task<List<GpuAllocationRow>> HostGpuAllocationsAsync(this IReadSession db, Guid hostId, CancellationToken ct);
    [SqlQuery("Sched/GpuJobMarks.sql")]
    public static partial Task<List<GpuJobMarkRow>> GpuJobMarksAsync(this IReadSession db, Guid hostId, string gpuUuid, DateTimeOffset since, CancellationToken ct);
}
