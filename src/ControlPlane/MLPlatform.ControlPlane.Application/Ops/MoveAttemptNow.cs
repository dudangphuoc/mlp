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

/// <summary>Move machines now (without waiting for the suspect period to expire) — RecoveryLoop finalizes node_lost
/// within seconds.</summary>
[RequiresPermission("job.submit")]
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
