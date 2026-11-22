using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

public static partial class OpsGpuSql
{
    [SqlQuery("Ops/InsertGpuSample.sql")]
    public static partial Task InsertGpuSampleAsync(this IWriteSession db, Guid hostId, string gpuUuid, DateTimeOffset at, double util, double? memUtil,
        long vramUsed, long vramTotal, double temp, double power, double? powerLimit, double? smClock, string[] throttle, long? ecc, CancellationToken ct);
    [SqlQuery("Ops/ListGpuAlerts.sql")]
    public static partial Task<List<GpuAlertRow>> ListGpuAlertsAsync(this IReadSession db, Guid hostId, CancellationToken ct);
    [SqlQuery("Ops/UpsertGpuAlert.sql")]
    public static partial Task UpsertGpuAlertAsync(this IWriteSession db, Guid hostId, string gpuUuid, string kind, string level, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/DeleteGpuAlert.sql")]
    public static partial Task DeleteGpuAlertAsync(this IWriteSession db, Guid hostId, string gpuUuid, string kind, CancellationToken ct);
    [SqlQuery("Ops/MarkGpuAlertNotified.sql")]
    public static partial Task MarkGpuAlertNotifiedAsync(this IWriteSession db, Guid hostId, string gpuUuid, string kind, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/GpuHistoryRaw.sql")]
    public static partial Task<List<GpuPointRow>> GpuHistoryRawAsync(this IReadSession db, int step, Guid hostId, string gpuUuid, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Ops/GpuHistoryRollup.sql")]
    public static partial Task<List<GpuPointRow>> GpuHistoryRollupAsync(this IReadSession db, int step, Guid hostId, string gpuUuid, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Ops/RollupGpuSamples.sql")]
    public static partial Task RollupGpuSamplesAsync(this IWriteSession db, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    [SqlQuery("Ops/PruneGpuSamples.sql")]
    public static partial Task PruneGpuSamplesAsync(this IWriteSession db, DateTimeOffset rawBefore, DateTimeOffset rollupBefore, CancellationToken ct);
}
