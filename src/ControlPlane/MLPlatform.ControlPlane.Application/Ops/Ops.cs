using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public sealed record EventRow(Guid Id, DateTimeOffset At, string Source, string ResourceType, Guid? ResourceId, string Kind, string Severity, string Message, string Data);
public sealed record WorkloadAttemptRow(Guid Id, Guid JobId, string State, Guid WorkspaceId, Guid[] HostIds);
public sealed record CheckpointRow(Guid Id, Guid AttemptId, Guid HostId, long Step, string Name, string Location, string? S3Key, long SizeBytes, string Sha256, DateTimeOffset CreatedAt);
public sealed record AttemptDetailRow(Guid Id, int Number, Guid[] HostIds, string State, int? ExitCode, string? ExitReason, string? Message, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, DateTimeOffset? LastHeartbeatAt, DateTimeOffset? SuspectUntil, string? ResumedFrom);
public sealed record ProgressRow(string? Progress, DateTimeOffset? ProgressAt);
public sealed record SuspectRow(Guid Id, Guid JobId, Guid WorkspaceId);

public static partial class OpsSql
{
    [SqlQuery("Ops/InsertEvent.sql")]
    public static partial Task InsertEventAsync(this IWriteSession db, Guid id, DateTimeOffset at, DateTimeOffset now, Guid? workspaceId, string source, string resourceType,
        Guid? resourceId, string kind, string severity, string message, string data, CancellationToken ct);
    [SqlQuery("Ops/ListEvents.sql")]
    public static partial Task<List<EventRow>> ListEventsAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/JobWorkspace.sql")]
    public static partial Task<Guid?> JobWorkspaceAsync(this IReadSession db, Guid id, CancellationToken ct);
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

/// <summary>How long to wait for a machine to come back before treating the attempt as `node_lost` (20/10 §4.4, default 5 minutes).</summary>
public sealed record RecoveryOptions(TimeSpan Grace);

/// <summary>Records an operational event row (20/10 §2.1) — in the same transaction as the state change that caused it.</summary>
public static class OpsEvents
{
    public static Task EmitAsync(IWriteSession db, IClock clock, Guid? workspaceId, string source, string resourceType, Guid? resourceId, string kind,
        string severity, string message, JsonObject? data = null, DateTimeOffset? at = null, CancellationToken ct = default) =>
        db.InsertEventAsync(Ids.New(), at ?? clock.UtcNow, clock.UtcNow, workspaceId, source, resourceType, resourceId, kind, severity, message,
            (data ?? []).ToJsonString(), ct);
}

/// <summary>
/// Authenticates the `mlpj_` workload token with fencing (20/10 §4.5): a token of a replaced/finished attempt → `SCHED_ATTEMPT_FENCED`,
/// so two copies never write to the same run.
/// </summary>
public static class Workload
{
    public static async Task<WorkloadAttemptRow> ResolveAsync(IReadSession db, TokenService tokens, string token, CancellationToken ct)
    {
        var a = string.IsNullOrEmpty(token) ? null : await db.AttemptByWorkloadTokenAsync(tokens.Hash(token), ct);
        if (a is null) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Invalid job token");
        if (a.State is not ("launching" or "running" or "suspect"))
            throw new PreconditionException("SCHED_ATTEMPT_FENCED", "This run attempt has been replaced by a newer one");
        return a;
    }
}

// ───────────── Shim → control plane ─────────────

/// <summary>Standard progress (20/10 §2.2): phase change → `progress.phase` event.</summary>
public sealed record ReportProgressCommand(string Token, string Phase, double Current, double Total, string Unit, double Percent, double Rate, double EtaSeconds,
    string Message, int StallAfterMin = 0, string DetailJson = "") : ICommand<bool>, IInternalCommand;

public sealed class ReportProgressCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<ReportProgressCommand, bool>
{
    public async ValueTask<bool> Handle(ReportProgressCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        var old = await db.JobProgressAsync(a.JobId, a.WorkspaceId, ct);
        var oldPhase = old?.Progress is { } p ? JsonNode.Parse(p)?["phase"]?.ToString() : null;
        var progress = new JsonObject
        {
            ["phase"] = c.Phase, ["current"] = c.Current, ["total"] = c.Total, ["unit"] = c.Unit, ["percent"] = Math.Round(c.Percent, 2),
            ["rate"] = c.Rate, ["eta_seconds"] = Math.Round(c.EtaSeconds), ["message"] = c.Message, ["attempt_id"] = a.Id.ToString(),
            ["stall_after_min"] = c.StallAfterMin > 0 ? c.StallAfterMin : 30,
        };
        // R24: training detail from the shim (epoch/step, train|validate, loss, stages); a malformed value is dropped, never stored as text.
        if (c.DetailJson.Length is > 0 and < 8192)
            try { if (JsonNode.Parse(c.DetailJson) is JsonObject d) progress["detail"] = d; } catch (System.Text.Json.JsonException) { }
        if (c.Phase == "preparing_data" && progress["detail"]?["sync"] is JsonObject sync) await DataSyncRecorder.RecordAsync(db, clock, a, sync, ct);
        var oldCurrent = old?.Progress is { } op && JsonNode.Parse(op)?["current"] is JsonValue ov && ov.TryGetValue<double>(out var oc) ? oc : double.NaN;
        await db.SetProgressAsync(a.JobId, progress.ToJsonString(), clock.UtcNow, ct);
        await db.AttemptHeartbeatAsync(a.Id, clock.UtcNow, ct);
        await Ops2Sql.ClearSignalLostAsync(db, a.Id, ct);
        if (oldPhase != c.Phase || oldCurrent != c.Current) await Ops2Sql.ProgressChangedAsync(db, a.JobId, clock.UtcNow, ct);
        if (oldPhase != c.Phase)
            await OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "progress.phase", "info",
                Phases.Label(c.Phase) + (string.IsNullOrEmpty(c.Message) ? "" : ": " + c.Message), new JsonObject { ["phase"] = c.Phase }, ct: ct);
        return true;
    }
}

public static class Phases
{
    public static string Label(string phase) => phase switch
    {
        "preparing_data" => "Preparing data",
        "restoring_checkpoint" => "Restoring checkpoint",
        "running" => "Running",
        "uploading" => "Uploading results",
        "finishing" => "Finishing",
        _ => phase,
    };
}

/// <summary>Shim heartbeat (30 s) — tier 2 of 20/10 §2.3.</summary>
public sealed record WorkloadHeartbeatCommand(string Token) : ICommand<bool>, IInternalCommand;

public sealed class WorkloadHeartbeatCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<WorkloadHeartbeatCommand, bool>
{
    public async ValueTask<bool> Handle(WorkloadHeartbeatCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        await db.AttemptHeartbeatAsync(a.Id, clock.UtcNow, ct);
        await Ops2Sql.ClearSignalLostAsync(db, a.Id, ct);
        return true;
    }
}

/// <summary>Durable checkpoint (20/10 §4.2): presigned PUT to `mlp-artifacts/{ws}/jobs/{job}/checkpoints/{step}/{name}`.</summary>
public sealed record CreateCheckpointUploadCommand(string Token, string Name, long Step) : ICommand<(string Url, string Key)>, IInternalCommand;

public sealed class CreateCheckpointUploadCommandHandler(IReadSession db, TokenService tokens, S3Client s3) : ICommandHandler<CreateCheckpointUploadCommand, (string Url, string Key)>
{
    public async ValueTask<(string Url, string Key)> Handle(CreateCheckpointUploadCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        var key = $"{a.WorkspaceId}/jobs/{a.JobId}/checkpoints/{c.Step}/{Buckets.SafePath(c.Name)}";
        return (s3.Presign(HttpMethod.Put, Buckets.Artifacts, key, TimeSpan.FromHours(6), Audience.Workload), key);
    }
}

public sealed record RegisterCheckpointCommand(string Token, string Name, long Step, string Location, string? Key, long SizeBytes, string Sha256)
    : ICommand<bool>, IInternalCommand;

public sealed class RegisterCheckpointCommandHandler(IWriteSession db, IClock clock, TokenService tokens, S3Client s3) : ICommandHandler<RegisterCheckpointCommand, bool>
{
    public async ValueTask<bool> Handle(RegisterCheckpointCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        if (c.Location is not ("local" or "durable")) throw new ValidationException(new Dictionary<string, string> { ["location"] = "local | durable" });
        if (c.Location == "durable" && (c.Key is null || !c.Key.StartsWith($"{a.WorkspaceId}/jobs/{a.JobId}/checkpoints/", StringComparison.Ordinal)))
            throw new ForbiddenException("SCHED_CHECKPOINT_KEY_INVALID", "Checkpoint key does not belong to this job");
        // One row per (name, location): a newer upload of last.pth replaces the row, so the object it pointed at is deleted (R28, no orphans).
        var replaced = c.Location == "durable"
            ? (await db.ListCheckpointsAsync(a.JobId, ct)).FirstOrDefault(x => x.Location == "durable" && x.Name == Buckets.SafePath(c.Name) && x.S3Key != c.Key)
            : null;
        await db.InsertCheckpointAsync(Ids.New(), a.JobId, a.Id, a.HostIds[0], c.Step, Buckets.SafePath(c.Name), c.Location, c.Key, c.SizeBytes, c.Sha256, clock.UtcNow, ct);
        if (replaced?.S3Key is { } oldKey) await s3.DeleteAsync(Buckets.Artifacts, oldKey, ct);
        if (c.Location == "durable")
            await OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "checkpoint.durable", "success",
                $"Checkpoint at step {c.Step} saved durably ({c.SizeBytes / 1024.0:0.#} KB)", new JsonObject { ["step"] = c.Step, ["name"] = c.Name }, ct: ct);
        if (c.Location == "durable") await CheckpointRetention.PruneAsync(db, s3, a.JobId, ct);
        return true;
    }
}

// ───────────── Machine disconnected / reconnected ─────────────

/// <summary>Agent channel dropped → attempts on the machine become `suspect` with a grace deadline (GPUs still count as held).</summary>
public static class Suspects
{
    public static async Task MarkAsync(IWriteSession db, IClock clock, RecoveryOptions opts, Guid hostId, string hostName, CancellationToken ct)
    {
        var until = clock.UtcNow + opts.Grace;
        foreach (var s in await db.MarkSuspectAsync(hostId, until, ct))
            await OpsEvents.EmitAsync(db, clock, s.WorkspaceId, "scheduler", "attempt", s.Id, "attempt.suspect", "warning",
                $"Machine {hostName} disconnected — waiting for it to return until {until:HH:mm:ss} UTC before moving to another machine", new JsonObject { ["host_id"] = hostId.ToString() }, ct: ct);
    }
}

public sealed record ListExpiredSuspectsQuery : IQuery<IReadOnlyList<SuspectRow>>;

public sealed class ListExpiredSuspectsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListExpiredSuspectsQuery, IReadOnlyList<SuspectRow>>
{
    public async ValueTask<IReadOnlyList<SuspectRow>> Handle(ListExpiredSuspectsQuery q, CancellationToken ct) => await db.ExpiredSuspectsAsync(clock.UtcNow, ct);
}

/// <summary>
/// Agent (restarted or reconnected) reports containers still running (20/10 §4.3, §4.7): `suspect` attempt with a container → `running` (nothing lost);
/// `suspect` attempt with no container → returned so the host finalizes `container_lost` (retry from checkpoint); container of a replaced attempt → stop.
/// </summary>
public sealed record ReconcileHostCommand(Guid HostId, IReadOnlyList<Guid> Running) : ICommand<(IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)>, IInternalCommand;

public sealed class ReconcileHostCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ReconcileHostCommand, (IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)>
{
    public async ValueTask<(IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)> Handle(ReconcileHostCommand c, CancellationToken ct)
    {
        var stop = new List<Guid>();
        var lost = new List<Guid>();
        var running = c.Running.ToHashSet();
        foreach (var a in await db.HostAttemptsAsync(c.HostId, [.. running], ct))
        {
            var alive = running.Contains(a.Id);
            switch (a.State)
            {
                case "suspect" when alive:
                    await db.ReviveAttemptAsync(a.Id, ct);
                    await OpsEvents.EmitAsync(db, clock, a.WorkspaceId, $"agent:{c.HostId}", "attempt", a.Id, "attempt.reattached", "success",
                        "Agent is back and the container is still running — continuing with nothing lost", ct: ct);
                    break;
                case "suspect":
                    lost.Add(a.Id);
                    break;
                case "launching" or "running":
                    break;
                default:
                    if (alive)
                    {
                        stop.Add(a.Id);
                        await OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "attempt", a.Id, "attempt.fenced", "warning",
                            "Previous machine is back but the run attempt was replaced — stopping the old container", ct: ct);
                    }
                    break;
            }
        }
        return (stop, lost);
    }
}

// ───────────── Resume from checkpoint ─────────────

/// <summary>
/// Picks the restore point for a new attempt (20/10 §4.6): latest checkpoint; same machine with a local copy → use the volume; different machine → durable copy (presigned GET).
/// Returns the value for `MLP_RESUME_FROM` (`local:&lt;name&gt;` | `url:&lt;presigned&gt;|&lt;name&gt;`) or null.
/// </summary>
public sealed record ResolveResumeCommand(Guid AttemptId, Guid JobId, Guid HostId) : ICommand<string?>, IInternalCommand;

public sealed class ResolveResumeCommandHandler(IWriteSession db, IClock clock, S3Client s3) : ICommandHandler<ResolveResumeCommand, string?>
{
    public async ValueTask<string?> Handle(ResolveResumeCommand c, CancellationToken ct)
    {
        var cps = await db.ListCheckpointsAsync(c.JobId, ct);
        if (cps.Count == 0) return null;
        var ws = await db.JobWorkspaceAsync(c.JobId, ct);
        // R28: every checkpoint name is restored together (D-FINE stage 2 reloads best_stg1.pth); the shim resumes from the recipe's first name.
        var local = cps.Where(x => x.Location == "local" && x.HostId == c.HostId).GroupBy(x => x.Name).Select(g => g.MaxBy(x => x.Step)!).ToList();
        var durable = cps.Where(x => x.Location == "durable" && x.S3Key is not null).GroupBy(x => x.Name).Select(g => g.MaxBy(x => x.Step)!).ToList();
        var localTop = local.Count > 0 ? local.Max(x => x.Step) : -1;
        var durableTop = durable.Count > 0 ? durable.Max(x => x.Step) : -1;
        string value, label;
        if (local.Count > 0 && localTop >= durableTop)
            (value, label) = (string.Join("\n", local.Select(x => $"local:{x.Name}")), $"step {localTop} (local copy on the same machine)");
        else if (durable.Count > 0)
            (value, label) = (string.Join("\n", durable.Select(x =>
                    $"url:{s3.Presign(HttpMethod.Get, Buckets.Artifacts, x.S3Key!, TimeSpan.FromHours(6), Audience.Workload)}|{x.Name}|{x.Sha256}")),
                $"step {durableTop} (durable copy in storage: {string.Join(", ", durable.Select(x => x.Name))})");
        else return null;
        await db.SetResumedFromAsync(c.AttemptId, label, ct);
        await OpsEvents.EmitAsync(db, clock, ws, "scheduler", "attempt", c.AttemptId, "attempt.resumed", "info", "Resuming from checkpoint " + label, ct: ct);
        return value;
    }
}

/// <summary>Manually resume a failed job (e.g. out of retries, OOM) — 20/10 §5 `ResumeJob`.</summary>
public sealed record ResumeJobCommand(Guid WorkspaceId, Guid JobId) : ICommand<bool>;

public sealed class ResumeJobCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ResumeJobCommand, bool>
{
    public async ValueTask<bool> Handle(ResumeJobCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var job = await db.GetJobAsync(c.JobId, c.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        if (job.State != "failed") throw new PreconditionException("SCHED_NOT_FAILED", "Only failed jobs can be resumed");
        if ((await db.ListCheckpointsAsync(job.Id, ct)).Count == 0) throw new NotFoundException("SCHED_CHECKPOINT_NOT_FOUND", "No checkpoint to resume from");
        await db.ResumeJobAsync(job.Id, c.WorkspaceId, "Manually resumed from checkpoint", clock.UtcNow, ct);
        await OpsEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "job", job.Id, "job.resume_requested", "info", "User requested resume from checkpoint", ct: ct);
        return true;
    }
}

// ───────────── Reads for the UI ─────────────

public sealed record JobOpsView(ProgressRow? Progress, IReadOnlyList<AttemptDetailRow> Attempts, IReadOnlyList<CheckpointRow> Checkpoints, IReadOnlyList<EventRow> Events);

public sealed record GetJobOpsQuery(Guid WorkspaceId, Guid JobId) : IQuery<JobOpsView>;

public sealed class GetJobOpsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetJobOpsQuery, JobOpsView>
{
    public async ValueTask<JobOpsView> Handle(GetJobOpsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return new JobOpsView(await db.JobProgressAsync(q.JobId, q.WorkspaceId, ct), await db.JobAttemptsAsync(q.JobId, ct),
            await db.ListCheckpointsAsync(q.JobId, ct), await db.ListEventsAsync(q.JobId, ct));
    }
}
