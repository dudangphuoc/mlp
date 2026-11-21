using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Container exited / failed to start → finalize the attempt + job by exit code (20/08 §12, M03 R4).</summary>
[Access(AccessKind.Internal)]
public sealed record FinishAttemptCommand(Guid AttemptId, int ExitCode, string? Reason, string? Message) : ICommand<FinishResult>;

public sealed class FinishAttemptCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FinishAttemptCommand, FinishResult>
{
    public async ValueTask<FinishResult> Handle(FinishAttemptCommand c, CancellationToken ct)
    {
        var a = await db.GetAttemptAsync(c.AttemptId, ct) ?? throw new NotFoundException("SCHED_ATTEMPT_NOT_FOUND", "Attempt not found");
        var now = clock.UtcNow;
        if (a.State is "succeeded" or "failed" or "cancelled" or "stopped")
        {
            var cur = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
            return new FinishResult(a.JobId, cur?.State ?? "failed", false, Stale: true);
        }
        var (state, reason) = c.ExitCode switch
        {
            0 => ("succeeded", "succeeded"),
            2 => ("failed", c.Reason ?? "user_error"),
            3 => ("failed", "recipe_error"),
            137 => ("failed", "oom"),
            _ => ("failed", c.Reason ?? "failed"),
        };
        var job0 = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
        if (job0?.State == "cancelled")
        {
            await db.SetAttemptStateAsync(a.Id, "cancelled", c.ExitCode, "cancelled", c.Message, now, ct);
            return new FinishResult(a.JobId, "cancelled", false);
        }
        await db.SetAttemptStateAsync(a.Id, state, c.ExitCode, reason, c.Message, now, ct);
        await EmitAttemptFinishedAsync(a, c, state, reason, ct);
        var retryable = state == "failed" && reason is "node_lost" or "container_lost" or "signal_lost" or "head_lost" or "timeout" or "failed";
        var job = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
        if (retryable && job is not null && job.AttemptCount < 3)
        {
            await db.RetryJobAsync(a.JobId, $"Retrying after error {reason} (attempt {job.AttemptCount}/3)", now, ct);
            await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "job", a.JobId, "job.recovering", "warning",
                $"Retrying from the latest checkpoint (attempt {job.AttemptCount + 1}/3)", ct: ct);
            return new FinishResult(a.JobId, "queued", true);
        }
        return await FinishJobAsync(a, c, state, reason, now, ct);
    }

    private async Task EmitAttemptFinishedAsync(AttemptRow a, FinishAttemptCommand c, string state, string reason, CancellationToken ct)
    {
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, reason is "node_lost" or "container_lost" ? "scheduler" : "agent", "attempt", a.Id,
            state == "succeeded" ? "attempt.succeeded" : "attempt." + reason, state == "succeeded" ? "success" : reason is "node_lost" or "container_lost" ? "warning" : "error",
            state == "succeeded" ? "Attempt finished" : $"Attempt failed: {reason} (code {c.ExitCode}){(string.IsNullOrEmpty(c.Message) ? "" : " — " + c.Message)}",
            new System.Text.Json.Nodes.JsonObject { ["exit_code"] = c.ExitCode }, ct: ct);
    }

    private async Task<FinishResult> FinishJobAsync(AttemptRow a, FinishAttemptCommand c, string state, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var jobState = state == "succeeded" ? "succeeded" : "failed";
        var detail = string.IsNullOrWhiteSpace(c.Message) ? $"The process exited with code {c.ExitCode} — open the job log for details" : c.Message;
        await db.SetJobStateAsync(a.JobId, jobState, state == "succeeded" ? null : reason is "failed" ? detail : $"{reason}: {detail}", now, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "job", a.JobId, "job." + jobState, jobState == "succeeded" ? "success" : "error",
            jobState == "succeeded" ? "Job finished" : $"Job failed: {reason}", ct: ct);
        await MLPlatform.ControlPlane.Application.Notifications.Notifier.JobFinishedAsync(db, clock, a.JobId, jobState, state == "succeeded" ? null : reason, ct);
        return new FinishResult(a.JobId, jobState, false);
    }
}
