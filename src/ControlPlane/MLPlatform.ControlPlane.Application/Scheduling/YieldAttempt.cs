using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>The agent refused GPU work because another environment's container holds the GPU (FLEET_GPU_YIELDED): the
/// attempt is cancelled and the job waits again without using up one of its attempts.</summary>
[Access(AccessKind.Internal)]
public sealed record YieldAttemptCommand(Guid AttemptId, string Holders) : ICommand<Guid>, IInternalCommand;

public sealed class YieldAttemptCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<YieldAttemptCommand, Guid>
{
    public const string Reason = "gpu_yielded";

    public async ValueTask<Guid> Handle(YieldAttemptCommand c, CancellationToken ct)
    {
        var a = await db.GetAttemptAsync(c.AttemptId, ct) ?? throw new NotFoundException("SCHED_ATTEMPT_NOT_FOUND", "Attempt not found");
        await db.SetAttemptStateAsync(a.Id, "cancelled", null, Reason, c.Holders, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "attempt", a.Id,
            "attempt." + Reason, "warning", Reason + (string.IsNullOrEmpty(c.Holders) ? "" : " · " + c.Holders), ct: ct);
        await db.RequeueJobAsync(a.JobId, Reason, ct);
        return a.JobId;
    }
}
