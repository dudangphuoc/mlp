using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public static partial class SchedSql
{
    [SqlQuery("Sched/JobsByIds.sql")]
    public static partial Task<List<JobInfoRow>> JobsByIdsAsync(this IReadSession db, Guid workspaceId, Guid[] ids, CancellationToken ct);
    [SqlQuery("Sched/InsertJob.sql")]
    public static partial Task InsertJobAsync(this IWriteSession db, Guid id, Guid workspaceId, string kind, string name, Guid ownerId,
        string spec, int priority, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/ClaimNextJob.sql")]
    public static partial Task<ClaimedJobRow?> ClaimNextJobAsync(this IWriteSession db, CancellationToken ct);

    [SqlQuery("Sched/SetJobStarting.sql")]
    public static partial Task SetJobStartingAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Sched/RequeueJob.sql")]
    public static partial Task RequeueJobAsync(this IWriteSession db, Guid id, string reason, CancellationToken ct);

    [SqlQuery("Sched/SetJobState.sql")]
    public static partial Task SetJobStateAsync(this IWriteSession db, Guid id, string state, string? reason, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/InsertAttempt.sql")]
    public static partial Task InsertAttemptAsync(this IWriteSession db, Guid id, Guid jobId, int number, string tokenHash, Guid[] hostIds,
        string bundles, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/GetAttemptByToken.sql")]
    public static partial Task<AttemptRow?> GetAttemptByTokenAsync(this IReadSession db, string tokenHash, CancellationToken ct);

    [SqlQuery("Sched/GetAttempt.sql")]
    public static partial Task<AttemptRow?> GetAttemptAsync(this IReadSession db, Guid id, CancellationToken ct);

    [SqlQuery("Sched/UpsertAllocation.sql")]
    public static partial Task UpsertAllocationAsync(this IWriteSession db, Guid attemptId, Guid hostId, string[] gpuUuids, string nodeIp,
        DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/ListAllocations.sql")]
    public static partial Task<List<AllocationRow>> ListAllocationsAsync(this IReadSession db, Guid attemptId, CancellationToken ct);

    [SqlQuery("Sched/SetAttemptState.sql")]
    public static partial Task SetAttemptStateAsync(this IWriteSession db, Guid id, string state, int? exitCode, string? exitReason,
        string? message, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/ListJobs.sql")]
    public static partial Task<List<JobRow>> ListJobsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);

    [SqlQuery("Sched/GetJob.sql")]
    public static partial Task<JobRow?> GetJobAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Sched/CancelPendingAttempts.sql")]
    public static partial Task CancelPendingAttemptsAsync(this IWriteSession db, Guid jobId, Guid workspaceId, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Sched/StaleReservations.sql")]
    public static partial Task<List<StaleReservationRow>> StaleReservationsAsync(this IReadSession db, DateTimeOffset before, CancellationToken ct);
    [SqlQuery("Sched/ActiveAttempts.sql")]
    public static partial Task<List<AttemptRow>> ActiveAttemptsAsync(this IReadSession db, CancellationToken ct);

    [SqlQuery("Sched/SetWorkloadToken.sql")]
    public static partial Task SetWorkloadTokenAsync(this IWriteSession db, Guid id, string tokenHash, CancellationToken ct);

    [SqlQuery("Sched/GetJobByWorkloadToken.sql")]
    public static partial Task<Guid?> GetJobByWorkloadTokenAsync(this IReadSession db, string tokenHash, CancellationToken ct);

    /// <summary>Retry after failure (M03 R4): keep attempt_count so max_attempts is respected.</summary>
    [SqlQuery("Sched/RetryJob.sql")]
    public static partial Task RetryJobAsync(this IWriteSession db, Guid id, string reason, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/HostAttemptJobs.sql")]
    public static partial Task<List<HostAttemptJobRow>> HostAttemptJobsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Ops/AttemptByWorkloadToken.sql")]
    public static partial Task<WorkloadAttemptRow?> AttemptByWorkloadTokenAsync(this IReadSession db, string tokenHash, CancellationToken ct);
}
