using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public static partial class Ops2Sql
{
    [SqlQuery("Ops/AttemptsByContainerHost.sql")]
    public static partial Task<List<AttemptGpuRow>> AttemptGpusAsync(this IReadSession db, Guid hostId, CancellationToken ct);
    [SqlQuery("Ops/InsertAttemptMetric.sql")]
    public static partial Task InsertAttemptMetricAsync(this IWriteSession db, Guid attemptId, DateTimeOffset now, double cpuPercent, long memoryBytes, double? gpuUtil, long? vramBytes, CancellationToken ct);
    [SqlQuery("Ops/SetHostMetrics.sql")]
    public static partial Task SetHostMetricsAsync(this IWriteSession db, Guid id, string metrics, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/JobMetrics.sql")]
    public static partial Task<List<AttemptMetricRow>> JobMetricsAsync(this IReadSession db, Guid jobId, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Ops/SignalLost.sql")]
    public static partial Task<List<SignalLostRow>> SignalLostAsync(this IReadSession db, DateTimeOffset before, CancellationToken ct);
    [SqlQuery("Ops/MarkSignalLost.sql")]
    public static partial Task MarkSignalLostAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/ClearSignalLost.sql")]
    public static partial Task ClearSignalLostAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ops/Stalled.sql")]
    public static partial Task<List<StalledRow>> StalledAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/MarkStalled.sql")]
    public static partial Task MarkStalledAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/ProgressChanged.sql")]
    public static partial Task ProgressChangedAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/SuspectOfJob.sql")]
    public static partial Task<SuspectRow?> SuspectOfJobAsync(this IReadSession db, Guid jobId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Ops/SetSuspectUntil.sql")]
    public static partial Task SetSuspectUntilAsync(this IWriteSession db, Guid id, DateTimeOffset until, CancellationToken ct);
    [SqlQuery("Ops/DeleteCheckpoint.sql")]
    public static partial Task DeleteCheckpointAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ops/ActiveJobsForUser.sql")]
    public static partial Task<List<ActiveJobRow>> ActiveJobsForUserAsync(this IReadSession db, Guid workspaceId, Guid ownerId, DateTimeOffset recent, CancellationToken ct);
    [SqlQuery("Ops/EventsAfter.sql")]
    public static partial Task<List<EventRow>> EventsAfterAsync(this IReadSession db, Guid jobId, DateTimeOffset after, CancellationToken ct);
    [SqlQuery("Ops/HostMap.sql")]
    public static partial Task<List<HostMapRow>> HostMapAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Ops/HostAttemptJobs.sql")]
    public static partial Task<List<HostAttemptJobRow>> HostAttemptJobsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Ops/SystemIncidents.sql")]
    public static partial Task<List<EventRow>> SystemIncidentsAsync(this IReadSession db, DateTimeOffset since, CancellationToken ct);
}
