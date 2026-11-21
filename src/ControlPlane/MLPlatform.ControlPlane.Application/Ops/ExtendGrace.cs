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
