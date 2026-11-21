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

/// <summary>Manually resume a failed job (e.g. out of retries, OOM) — 20/10 §5 `ResumeJob`.</summary>
[RequiresPermission("job.submit")]
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
        await TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "job", job.Id, "job.resume_requested", "info", "User requested resume from checkpoint", ct: ct);
        return true;
    }
}
