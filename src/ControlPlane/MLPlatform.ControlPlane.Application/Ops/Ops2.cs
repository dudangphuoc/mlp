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

public sealed record AttemptGpuRow(Guid AttemptId, string[] GpuUuids);
public sealed record AttemptMetricRow(Guid AttemptId, DateTimeOffset At, double CpuPercent, long MemoryBytes, double? GpuUtil, long? VramBytes);
public sealed record SignalLostRow(Guid Id, Guid JobId, Guid WorkspaceId, Guid OwnerId, string Name, DateTimeOffset? SignalLostAt);
public sealed record StalledRow(Guid Id, Guid WorkspaceId, Guid OwnerId, string Name, double? Current);
public sealed record ActiveJobRow(Guid Id, string Name, string Kind, string State, string? StateReason, string? Progress, DateTimeOffset? StalledAt,
    string? AttemptState, DateTimeOffset? SignalLostAt, DateTimeOffset? SuspectUntil);
public sealed record HostMapRow(Guid Id, string Name, string State, DateTimeOffset LastSeenAt, DateTimeOffset? MetricsAt, string? LastMetrics, byte[]? HostInfo, long Recovering);
public sealed record HostAttemptJobRow(Guid AttemptId, Guid JobId, string JobName, Guid HostId, string[] GpuUuids, string Spec);

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

/// <summary>Detection thresholds (20/10 §10): shim silent → signal lost; silent much longer → stop and retry.</summary>
public sealed record SignalOptions(TimeSpan SignalLostAfter, TimeSpan SignalKillAfter);

/// <summary>Latest machine metrics + per-attempt metrics (container `mlp-w-{attempt}-{rank}`; GPUs by the attempt's `gpu_uuids`) — 20/10 §2.5.</summary>
public sealed record RecordHostMetricsCommand(Guid HostId, string MetricsJson, IReadOnlyList<(string Container, double Cpu, long Memory)> Containers,
    IReadOnlyList<(string Uuid, double Util, long Vram)> Gpus) : ICommand<int>, IInternalCommand;

public sealed class RecordHostMetricsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordHostMetricsCommand, int>
{
    public async ValueTask<int> Handle(RecordHostMetricsCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.SetHostMetricsAsync(c.HostId, c.MetricsJson, now, ct);
        var gpusOf = (await db.AttemptGpusAsync(c.HostId, ct)).ToDictionary(a => a.AttemptId, a => a.GpuUuids);
        var n = 0;
        foreach (var (name, cpu, mem) in c.Containers)
        {
            if (!name.StartsWith("mlp-w-", StringComparison.Ordinal) || name.Length < 6 + 36 || !Guid.TryParse(name.AsSpan(6, 36), out var attempt)) continue;
            double? util = null; long? vram = null;
            if (gpusOf.TryGetValue(attempt, out var uuids) && uuids.Length > 0)
            {
                var mine = c.Gpus.Where(g => uuids.Contains(g.Uuid)).ToList();
                if (mine.Count > 0) { util = mine.Average(g => g.Util); vram = mine.Sum(g => g.Vram); }
            }
            await db.InsertAttemptMetricAsync(attempt, now, cpu, mem, util, vram, ct);
            n++;
        }
        return n;
    }
}

/// <summary>
/// Anomaly detector (20/10 §2.3): shim signal lost (warning; too long → return the attempt so the host stops and retries),
/// progress unchanged for longer than `stall_after_min` → "may be stalled" (warning only, no automatic stop).
/// </summary>
public sealed record DetectAnomaliesCommand : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class DetectAnomaliesCommandHandler(IWriteSession db, IClock clock, SignalOptions opts) : ICommandHandler<DetectAnomaliesCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(DetectAnomaliesCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var kill = new List<Guid>();
        foreach (var a in await db.SignalLostAsync(now - opts.SignalLostAfter, ct))
        {
            if (a.SignalLostAt is null)
            {
                await db.MarkSignalLostAsync(a.Id, now, ct);
                await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "system:recovery", "attempt", a.Id, "job.signal_lost", "warning",
                    $"No signal from the training process for over {opts.SignalLostAfter.TotalMinutes:0.#} minutes (machine still online)", ct: ct);
                await Notifier.SendAsync(db, clock, a.WorkspaceId, [a.OwnerId], "job.signal_lost", $"Job \"{a.Name}\" lost signal", $"#/jobs/{a.JobId}", $"job/{a.JobId}", "warning", ct);
            }
            else if (now - a.SignalLostAt.Value >= opts.SignalKillAfter - opts.SignalLostAfter) kill.Add(a.Id);
        }
        foreach (var j in await db.StalledAsync(now, ct))
        {
            await db.MarkStalledAsync(j.Id, now, ct);
            await TimelineEvents.EmitAsync(db, clock, j.WorkspaceId, "system:recovery", "job", j.Id, "progress.stalled", "warning",
                $"Progress stuck at {j.Current:0.#} — may be stalled (not stopped automatically: some valid steps stay silent for a long time)", ct: ct);
            await Notifier.SendAsync(db, clock, j.WorkspaceId, [j.OwnerId], "job.stalled", $"Job \"{j.Name}\" may be stalled", $"#/jobs/{j.Id}", $"job/{j.Id}", "warning", ct);
        }
        return kill;
    }
}

/// <summary>Move machines now (without waiting for the suspect period to expire) — RecoveryLoop finalizes node_lost within seconds.</summary>
public sealed record MoveAttemptNowCommand(Guid WorkspaceId, Guid JobId) : ICommand<bool>;

public sealed class MoveAttemptNowCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<MoveAttemptNowCommand, bool>
{
    public async ValueTask<bool> Handle(MoveAttemptNowCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var s = await db.SuspectOfJobAsync(c.JobId, c.WorkspaceId, ct) ?? throw new PreconditionException("SCHED_NOT_SUSPECT", "Job is not waiting for its machine to come back");
        await db.SetSuspectUntilAsync(s.Id, clock.UtcNow, ct);
        await TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "attempt", s.Id, "attempt.move_requested", "info", "User requested an immediate machine move", ct: ct);
        return true;
    }
}

public sealed record ExtendGraceCommand(Guid WorkspaceId, Guid JobId, int Minutes) : ICommand<bool>;

public sealed class ExtendGraceCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ExtendGraceCommand, bool>
{
    public async ValueTask<bool> Handle(ExtendGraceCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var s = await db.SuspectOfJobAsync(c.JobId, c.WorkspaceId, ct) ?? throw new PreconditionException("SCHED_NOT_SUSPECT", "Job is not waiting for its machine to come back");
        var mins = c.Minutes is > 0 and <= 24 * 60 ? c.Minutes : 15;
        var until = (await db.JobAttemptsAsync(c.JobId, ct)).First(a => a.Id == s.Id).SuspectUntil.GetValueOrDefault(clock.UtcNow).AddMinutes(mins);
        await db.SetSuspectUntilAsync(s.Id, until, ct);
        await TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "attempt", s.Id, "attempt.grace_extended", "info", $"Waiting {mins} more minutes for the machine to come back", ct: ct);
        return true;
    }
}

/// <summary>Keep the job's 3 latest durable checkpoints (20/10 §4.2); older ones are deleted from storage.</summary>
internal static class CheckpointRetention
{
    public static async Task PruneAsync(IWriteSession db, S3Client s3, Guid jobId, CancellationToken ct)
    {
        foreach (var old in (await db.ListCheckpointsAsync(jobId, ct)).Where(c => c.Location == "durable").OrderByDescending(c => c.Step).Skip(3))
        {
            if (old.S3Key is { } k) await s3.DeleteAsync(Data.Buckets.Artifacts, k, ct);
            await db.DeleteCheckpointAsync(old.Id, ct);
        }
    }
}

// ───────────── Reads for WatchWorkspace / cluster map / status ─────────────

public sealed record ActiveJobsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ActiveJobRow>>;

public sealed class ActiveJobsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<ActiveJobsQuery, IReadOnlyList<ActiveJobRow>>
{
    public async ValueTask<IReadOnlyList<ActiveJobRow>> Handle(ActiveJobsQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ActiveJobsForUserAsync(q.WorkspaceId, uid, clock.UtcNow.AddSeconds(-60), ct);
    }
}

public sealed record JobEventsAfterQuery(Guid WorkspaceId, Guid JobId, DateTimeOffset After) : IQuery<IReadOnlyList<EventRow>>;

public sealed class JobEventsAfterQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<JobEventsAfterQuery, IReadOnlyList<EventRow>>
{
    public async ValueTask<IReadOnlyList<EventRow>> Handle(JobEventsAfterQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return await db.EventsAfterAsync(q.JobId, q.After, ct);
    }
}

public sealed record ClusterMapQuery(Guid WorkspaceId) : IQuery<(IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)>;

public sealed class ClusterMapQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ClusterMapQuery, (IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)>
{
    public async ValueTask<(IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)> Handle(ClusterMapQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return (await db.HostMapAsync(q.WorkspaceId, ct), await db.HostAttemptJobsAsync(q.WorkspaceId, ct));
    }
}

public sealed record JobMetricsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<AttemptMetricRow>>;

public sealed class JobMetricsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<JobMetricsQuery, IReadOnlyList<AttemptMetricRow>>
{
    public async ValueTask<IReadOnlyList<AttemptMetricRow>> Handle(JobMetricsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.JobMetricsAsync(q.JobId, clock.UtcNow.AddHours(-24), ct);
    }
}

public sealed record SystemIncidentsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EventRow>>;

public sealed class SystemIncidentsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<SystemIncidentsQuery, IReadOnlyList<EventRow>>
{
    public async ValueTask<IReadOnlyList<EventRow>> Handle(SystemIncidentsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.SystemIncidentsAsync(clock.UtcNow.AddDays(-30), ct);
    }
}

/// <summary>Instance-wide events (background loop stalled, component disconnected…) — 20/10 §2.6.</summary>
public sealed record EmitSystemEventCommand(string Source, string Kind, string Severity, string Message) : ICommand<bool>, IInternalCommand;

public sealed class EmitSystemEventCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<EmitSystemEventCommand, bool>
{
    public async ValueTask<bool> Handle(EmitSystemEventCommand c, CancellationToken ct)
    {
        await TimelineEvents.EmitAsync(db, clock, null, c.Source, "system", null, c.Kind, c.Severity, c.Message, ct: ct);
        return true;
    }
}

public sealed record DbPingQuery : IQuery<bool>;

public sealed class DbPingQueryHandler(IReadSession db) : IQueryHandler<DbPingQuery, bool>
{
    public async ValueTask<bool> Handle(DbPingQuery q, CancellationToken ct) => await db.JobWorkspaceAsync(Guid.Empty, ct) is null || true;
}

public sealed record AttemptOwnerRow(Guid JobId, Guid WorkspaceId);

/// <summary>Attempt → (job, workspace) for the log store (path in S3 storage).</summary>
public sealed record AttemptOwnerQuery(Guid AttemptId) : IQuery<AttemptOwnerRow?>;

public sealed class AttemptOwnerQueryHandler(IReadSession db) : IQueryHandler<AttemptOwnerQuery, AttemptOwnerRow?>
{
    public async ValueTask<AttemptOwnerRow?> Handle(AttemptOwnerQuery q, CancellationToken ct) =>
        await Scheduling.SchedSql.GetAttemptAsync(db, q.AttemptId, ct) is { } a ? new AttemptOwnerRow(a.JobId, a.WorkspaceId) : null;
}

/// <summary>Checks read permission on the job (logs) and returns the attempt list.</summary>
public sealed record JobAttemptIdsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<Guid>>;

public sealed class JobAttemptIdsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<JobAttemptIdsQuery, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(JobAttemptIdsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return (await db.JobAttemptsAsync(q.JobId, ct)).Select(a => a.Id).ToList();
    }
}
