using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public static partial class OpsSql
{
    [SqlQuery("Ops/AttemptByWorkloadToken.sql")]
    public static partial Task<WorkloadAttemptRow?> AttemptByWorkloadTokenAsync(this IReadSession db, string tokenHash, CancellationToken ct);
    [SqlQuery("Ops/SetProgress.sql")]
    public static partial Task SetProgressAsync(this IWriteSession db, Guid id, string progress, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/Heartbeat.sql")]
    public static partial Task AttemptHeartbeatAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/InsertCheckpoint.sql")]
    public static partial Task InsertCheckpointAsync(this IWriteSession db, Guid id, Guid jobId, Guid attemptId, Guid hostId, long step, string name, string location,
        string? s3Key, long sizeBytes, string sha256, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/ListCheckpoints.sql")]
    public static partial Task<List<CheckpointRow>> ListCheckpointsAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/JobAttempts.sql")]
    public static partial Task<List<AttemptDetailRow>> JobAttemptsAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/JobProgress.sql")]
    public static partial Task<ProgressRow?> JobProgressAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Ops/MarkSuspect.sql")]
    public static partial Task<List<SuspectRow>> MarkSuspectAsync(this IWriteSession db, Guid hostId, DateTimeOffset until, CancellationToken ct);
    [SqlQuery("Ops/ExpiredSuspects.sql")]
    public static partial Task<List<SuspectRow>> ExpiredSuspectsAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ops/HostAttempts.sql")]
    public static partial Task<List<WorkloadAttemptRow>> HostAttemptsAsync(this IReadSession db, Guid hostId, Guid[] ids, CancellationToken ct);
    [SqlQuery("Ops/SetResumedFrom.sql")]
    public static partial Task SetResumedFromAsync(this IWriteSession db, Guid id, string resumedFrom, CancellationToken ct);
    [SqlQuery("Ops/ReviveAttempt.sql")]
    public static partial Task ReviveAttemptAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ops/ResumeJob.sql")]
    public static partial Task ResumeJobAsync(this IWriteSession db, Guid id, Guid workspaceId, string reason, DateTimeOffset now, CancellationToken ct);
}
