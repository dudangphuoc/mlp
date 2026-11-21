using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

// ---- Submit job (M03 §5.1) ----
public sealed record SubmitJobCommand(Guid WorkspaceId, string Kind, string Name, JobSpecDto Spec, int Priority = 50) : ICommand<Guid>;

public sealed class SubmitJobCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<SubmitJobCommand, Guid>
{
    public async ValueTask<Guid> Handle(SubmitJobCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(c.Spec.Image)) errors["image"] = "Image is required";
        if (c.Spec.Cpus <= 0) errors["cpus"] = "cpus must be > 0 (R1: a bundle always requests ≥ 1 CPU so it is not placed on the head)";
        if (c.Spec.Hosts.Count > 0 && c.Spec.Hosts.Count != c.Spec.Nnodes) errors["hosts"] = "Number of pinned machines must equal nnodes";
        if (errors.Count > 0) throw new ValidationException(errors);
        var id = Ids.New();
        await db.InsertJobAsync(id, c.WorkspaceId, c.Kind, c.Name, uid, c.Spec.ToJson(), c.Priority, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "job", id, "job.queued", "info",
            $"Queued: needs {c.Spec.Nnodes} machines × {c.Spec.Cpus} CPU{(c.Spec.Gpus > 0 ? $", {c.Spec.Gpus} GPU" : "")}", ct: ct);
        return id;
    }
}

public sealed record ListJobsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<JobRow>>;

public sealed class ListJobsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListJobsQuery, IReadOnlyList<JobRow>>
{
    public async ValueTask<IReadOnlyList<JobRow>> Handle(ListJobsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "job.submit", ct);
        return await db.ListJobsAsync(q.WorkspaceId, ct);
    }
}

public sealed record GetJobQuery(Guid WorkspaceId, Guid JobId) : IQuery<JobRow>;

public sealed class GetJobQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetJobQuery, JobRow>
{
    public async ValueTask<JobRow> Handle(GetJobQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "job.submit", ct);
        return await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
    }
}

// ---- Internal: scheduling loop ----
public sealed record ClaimedJob(ClaimedJobRow Job, JobSpecDto Spec);

public sealed record ClaimNextJobCommand : ICommand<ClaimedJob?>;

public sealed class ClaimNextJobCommandHandler(IWriteSession db) : ICommandHandler<ClaimNextJobCommand, ClaimedJob?>
{
    public async ValueTask<ClaimedJob?> Handle(ClaimNextJobCommand c, CancellationToken ct) =>
        await db.ClaimNextJobAsync(ct) is { } j ? new ClaimedJob(j, JobSpecDto.Parse(j.Spec)) : null;
}

public sealed record RequeueJobCommand(Guid JobId, string Reason) : ICommand<bool>;

public sealed class RequeueJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RequeueJobCommand, bool>
{
    public async ValueTask<bool> Handle(RequeueJobCommand c, CancellationToken ct)
    {
        await db.RequeueJobAsync(c.JobId, c.Reason, ct);
        // Waiting for machines repeats every loop — only record an event when the reason changes (avoid flooding the timeline).
        var last = (await MLPlatform.ControlPlane.Application.Ops.OpsSql.ListEventsAsync(db, c.JobId, ct)).LastOrDefault();
        if (last?.Kind != "job.waiting" || last.Message != c.Reason)
            await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, c.JobId, ct), "scheduler", "job", c.JobId,
                "job.waiting", "warning", c.Reason, ct: ct);
        return true;
    }
}

public sealed record AttemptCreated(Guid AttemptId, string Token);

public sealed record CreateAttemptCommand(Guid JobId, int Number, IReadOnlyList<BundleDto> Bundles) : ICommand<AttemptCreated>;

public sealed class CreateAttemptCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<CreateAttemptCommand, AttemptCreated>
{
    public async ValueTask<AttemptCreated> Handle(CreateAttemptCommand c, CancellationToken ct)
    {
        var id = Ids.New();
        var token = TokenService.New("mlpj_");
        await db.InsertAttemptAsync(id, c.JobId, c.Number, tokens.Hash(token), c.Bundles.Select(b => b.HostId).ToArray(),
            System.Text.Json.JsonSerializer.Serialize(c.Bundles.ToList(), SchedJson.Default.ListBundleDto), clock.UtcNow, ct);
        await db.SetJobStartingAsync(c.JobId, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, c.JobId, ct), "scheduler", "attempt", id,
            "attempt.reserving", "info", $"Attempt {c.Number} reserving resources (placement group) on {c.Bundles.Count} machines", ct: ct);
        return new AttemptCreated(id, token);
    }
}

/// <summary>Holder reports an allocation. Returns the allocation list once every machine has reported (so the api sends StartWorkload), otherwise null.</summary>
public sealed record ReportAllocationCommand(string Token, Guid AttemptId, Guid HostId, string[] GpuUuids, string NodeIp, bool Ok, string Message)
    : ICommand<AllocationComplete?>;

public sealed record AllocationComplete(AttemptRow Attempt, JobSpecDto Spec, IReadOnlyList<AllocationRow> Allocations);

public sealed class ReportAllocationCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<ReportAllocationCommand, AllocationComplete?>
{
    public async ValueTask<AllocationComplete?> Handle(ReportAllocationCommand c, CancellationToken ct)
    {
        var a = await db.GetAttemptByTokenAsync(tokens.Hash(c.Token), ct);
        if (a is null || a.Id != c.AttemptId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Invalid job token");
        if (!a.HostIds.Contains(c.HostId)) throw new ForbiddenException("SCHED_ALLOCATION_MISMATCH", "Machine does not belong to the attempt");
        if (a.State != "reserving") return null;
        if (!c.Ok)
        {
            await db.SetAttemptStateAsync(a.Id, "failed", null, c.Message, "Could not reserve resources", clock.UtcNow, ct);
            // Say what the job is waiting for and who holds it — a silent retry loop looks like a hang to the user.
            var spec = JobSpecDto.Parse(a.Spec);
            var holders = spec.Gpus > 0
                ? (await MLPlatform.ControlPlane.Application.Ops.Ops2Sql.HostAttemptJobsAsync(db, a.WorkspaceId, ct))
                    .Where(h => h.AttemptId != a.Id && h.GpuUuids.Length > 0 && a.HostIds.Contains(h.HostId)).Select(h => h.JobName).Distinct().ToList()
                : [];
            var reason = c.Message != "placement_timeout" ? "Waiting for resources: " + c.Message
                : "Waiting for " + (spec.Gpus > 0 ? "GPU memory" : "free CPU/memory") + (holders.Count > 0 ? " — in use by: " + string.Join(", ", holders) : "");
            var before = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
            await db.RequeueJobAsync(a.JobId, reason, ct);
            if (before?.StateReason != reason)
                await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "attempt", a.Id, "attempt.waiting", "warning", reason, ct: ct);
            if (await MLPlatform.ControlPlane.Application.Serving.ServingSql.GetDeploymentByJobAsync(db, a.JobId, ct) is { State: not "stopped" } dep)
                await MLPlatform.ControlPlane.Application.Serving.ServingSql.SetDeploymentStateAsync(db, dep.Id, dep.State, reason, null, ct);
            if (await MLPlatform.ControlPlane.Application.Notebooks.NbSql.GetServerByJobAsync(db, a.JobId, ct) is { State: "queued" or "starting" } nb)
                await MLPlatform.ControlPlane.Application.Notebooks.NbSql.SetServerStateAsync(db, nb.Id, nb.State, reason, null, null, clock.UtcNow, ct);
            return null;
        }
        await db.UpsertAllocationAsync(a.Id, c.HostId, c.GpuUuids, c.NodeIp, clock.UtcNow, ct);
        var allocs = await db.ListAllocationsAsync(a.Id, ct);
        if (allocs.Count < a.HostIds.Length) return null;
        await db.SetAttemptStateAsync(a.Id, "launching", null, null, null, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "holder", "attempt", a.Id, "attempt.launching", "info",
            "Resources reserved — sending the container run command" + (c.GpuUuids.Length > 0 ? $" (GPU {string.Join(",", c.GpuUuids)})" : ""), ct: ct);
        return new AllocationComplete(a, JobSpecDto.Parse(a.Spec), allocs);
    }
}

public sealed record MarkAttemptRunningCommand(Guid AttemptId, Guid JobId) : ICommand<bool>;

public sealed class MarkAttemptRunningCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MarkAttemptRunningCommand, bool>
{
    public async ValueTask<bool> Handle(MarkAttemptRunningCommand c, CancellationToken ct)
    {
        // The job was cancelled while its container was being prepared: do not bring it back — the caller stops the container.
        if (await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, c.JobId, ct) is { } ws && (await db.GetJobAsync(c.JobId, ws, ct))?.State == "cancelled")
        {
            await db.SetAttemptStateAsync(c.AttemptId, "cancelled", null, "cancelled", null, clock.UtcNow, ct);
            return false;
        }
        await db.SetAttemptStateAsync(c.AttemptId, "running", null, null, null, clock.UtcNow, ct);
        await db.SetJobStateAsync(c.JobId, "running", null, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, c.JobId, ct), "agent", "attempt", c.AttemptId,
            "attempt.running", "info", "Container started", ct: ct);
        return true;
    }
}

/// <summary>Container exited / failed to start → finalize the attempt + job by exit code (20/08 §12, M03 R4).</summary>
public sealed record FinishAttemptCommand(Guid AttemptId, int ExitCode, string? Reason, string? Message) : ICommand<FinishResult>;

/// <param name="Stale">The attempt was already finalized (replaced, node-lost, fenced): its late exit must not change the job, run, deployment or notebook.</param>
public sealed record FinishResult(Guid JobId, string JobState, bool Retry, bool Stale = false);

public sealed class FinishAttemptCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FinishAttemptCommand, FinishResult>
{
    public async ValueTask<FinishResult> Handle(FinishAttemptCommand c, CancellationToken ct)
    {
        var a = await db.GetAttemptAsync(c.AttemptId, ct) ?? throw new NotFoundException("SCHED_ATTEMPT_NOT_FOUND", "Attempt not found");
        var now = clock.UtcNow;
        // Already finalized (e.g. the agent reports exit after the attempt was deemed node-lost) → skip, do not retry twice.
        if (a.State is "succeeded" or "failed" or "cancelled" or "stopped")
        {
            var cur = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
            return new FinishResult(a.JobId, cur?.State ?? "failed", false, Stale: true);
        }
        var (state, reason) = c.ExitCode switch
        {
            0 => ("succeeded", "succeeded"),
            2 => ("failed", c.Reason ?? "user_error"),   // R26: a recipe-declared failure (loss_invalid …) names itself; never retried
            3 => ("failed", "recipe_error"),
            137 => ("failed", "oom"),
            _ => ("failed", c.Reason ?? "failed"),
        };
        var job0 = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
        if (job0?.State == "cancelled")
        {
            // Deliberate stop (job cancelled, notebook/deployment stopped): the container exiting is not a failure, no retry.
            await db.SetAttemptStateAsync(a.Id, "cancelled", c.ExitCode, "cancelled", c.Message, now, ct);
            return new FinishResult(a.JobId, "cancelled", false);
        }
        await db.SetAttemptStateAsync(a.Id, state, c.ExitCode, reason, c.Message, now, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, a.WorkspaceId, reason is "node_lost" or "container_lost" ? "scheduler" : "agent", "attempt", a.Id,
            state == "succeeded" ? "attempt.succeeded" : "attempt." + reason, state == "succeeded" ? "success" : reason is "node_lost" or "container_lost" ? "warning" : "error",
            state == "succeeded" ? "Attempt finished" : $"Attempt failed: {reason} (code {c.ExitCode}){(string.IsNullOrEmpty(c.Message) ? "" : " — " + c.Message)}",
            new System.Text.Json.Nodes.JsonObject { ["exit_code"] = c.ExitCode }, ct: ct);
        var retryable = state == "failed" && reason is "node_lost" or "container_lost" or "signal_lost" or "head_lost" or "timeout" or "failed";
        var job = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
        if (retryable && job is not null && job.AttemptCount < 3)
        {
            await db.RetryJobAsync(a.JobId, $"Retrying after error {reason} (attempt {job.AttemptCount}/3)", now, ct);
            await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "job", a.JobId, "job.recovering", "warning",
                $"Retrying from the latest checkpoint (attempt {job.AttemptCount + 1}/3)", ct: ct);
            return new FinishResult(a.JobId, "queued", true);
        }
        var jobState = state == "succeeded" ? "succeeded" : "failed";
        var detail = string.IsNullOrWhiteSpace(c.Message) ? $"The process exited with code {c.ExitCode} — open the job log for details" : c.Message;
        await db.SetJobStateAsync(a.JobId, jobState, state == "succeeded" ? null : reason is "failed" ? detail : $"{reason}: {detail}", now, ct);
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "job", a.JobId, "job." + jobState, jobState == "succeeded" ? "success" : "error",
            jobState == "succeeded" ? "Job finished" : $"Job failed: {reason}", ct: ct);
        await MLPlatform.ControlPlane.Application.Notifications.Notifier.JobFinishedAsync(db, clock, a.JobId, jobState, state == "succeeded" ? null : reason, ct);
        return new FinishResult(a.JobId, jobState, false);
    }
}

/// <summary>Cancel a job (notebook/deployment stopped, user cancelled): mark it before stopping the container so FinishAttempt does not retry.</summary>
public sealed record CancelJobCommand(Guid JobId, string Reason) : ICommand<bool>;

public sealed class CancelJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<CancelJobCommand, bool>
{
    public async ValueTask<bool> Handle(CancelJobCommand c, CancellationToken ct)
    {
        await db.SetJobStateAsync(c.JobId, "cancelled", c.Reason, clock.UtcNow, ct);
        if (await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, c.JobId, ct) is { } ws)
            await db.CancelPendingAttemptsAsync(c.JobId, ws, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>State of a job as the launcher needs it between slow steps (image build → container start).</summary>
public sealed record JobStateQuery(Guid JobId) : IQuery<string?>;

/// <summary>Reads the job's current state; null when the job is gone.</summary>
public sealed class JobStateQueryHandler(IReadSession db) : IQueryHandler<JobStateQuery, string?>
{
    public async ValueTask<string?> Handle(JobStateQuery q, CancellationToken ct) =>
        await MLPlatform.ControlPlane.Application.Ops.OpsSql.JobWorkspaceAsync(db, q.JobId, ct) is { } ws ? (await db.GetJobAsync(q.JobId, ws, ct))?.State : null;
}

public sealed record ListActiveAttemptsQuery : IQuery<IReadOnlyList<AttemptRow>>;

public sealed class ListActiveAttemptsQueryHandler(IReadSession db) : IQueryHandler<ListActiveAttemptsQuery, IReadOnlyList<AttemptRow>>
{
    public async ValueTask<IReadOnlyList<AttemptRow>> Handle(ListActiveAttemptsQuery q, CancellationToken ct) => await db.ActiveAttemptsAsync(ct);
}

/// <summary>Machines and GPUs an attempt was given (to reserve them again when the Ray head lost them).</summary>
public sealed record ListAttemptAllocationsQuery(Guid AttemptId) : IQuery<IReadOnlyList<AllocationRow>>;

/// <summary>Reads the allocation rows of the attempt.</summary>
public sealed class ListAttemptAllocationsQueryHandler(IReadSession db) : IQueryHandler<ListAttemptAllocationsQuery, IReadOnlyList<AllocationRow>>
{
    public async ValueTask<IReadOnlyList<AllocationRow>> Handle(ListAttemptAllocationsQuery q, CancellationToken ct) => await db.ListAllocationsAsync(q.AttemptId, ct);
}

/// <summary>
/// Attempt stuck in "reserving" longer than <paramref name="Timeout"/> (Ray has no suitable node, holder lost, machine out of resources…) → mark it failed
/// and requeue the job (not counted as a retry). Returns the attempt ids so the api cancels the Ray holder job.
/// </summary>
public sealed record ExpireReservationsCommand(TimeSpan Timeout) : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class ExpireReservationsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ExpireReservationsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(ExpireReservationsCommand c, CancellationToken ct)
    {
        var stale = await db.StaleReservationsAsync(clock.UtcNow - c.Timeout, ct);
        foreach (var a in stale)
        {
            var msg = $"Could not reserve resources after {(int)c.Timeout.TotalMinutes} minutes (machine not ready or out of CPU/GPU/RAM) — retrying";
            await db.SetAttemptStateAsync(a.Id, "failed", null, "reserve_timeout", msg, clock.UtcNow, ct);
            await db.RequeueJobAsync(a.JobId, "Waiting for resources: " + msg, ct);
        }
        return stale.Select(a => a.Id).ToList();
    }
}

/// <summary>A visible step of an attempt on its machine (e.g. building the recipe image) — shown in the job timeline.</summary>
public sealed record EmitAttemptEventCommand(Guid WorkspaceId, Guid AttemptId, string Code, string Level, string Message) : ICommand<bool>, IInternalCommand;

public sealed class EmitAttemptEventCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<EmitAttemptEventCommand, bool>
{
    public async ValueTask<bool> Handle(EmitAttemptEventCommand c, CancellationToken ct)
    {
        await MLPlatform.ControlPlane.Application.Ops.OpsEvents.EmitAsync(db, clock, c.WorkspaceId, "scheduler", "attempt", c.AttemptId, c.Code, c.Level, c.Message, ct: ct);
        return true;
    }
}
