using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record MarkAttemptRunningCommand(Guid AttemptId, Guid JobId) : ICommand<bool>;

public sealed class MarkAttemptRunningCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MarkAttemptRunningCommand, bool>
{
    public async ValueTask<bool> Handle(MarkAttemptRunningCommand c, CancellationToken ct)
    {
        // The job was cancelled while its container was being prepared: do not bring it back — the caller stops the container.
        if (await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, c.JobId, ct) is { } ws && (await db.GetJobAsync(c.JobId, ws, ct))?.State == "cancelled")
        {
            await db.SetAttemptStateAsync(c.AttemptId, "cancelled", null, "cancelled", null, clock.UtcNow, ct);
            return false;
        }
        await db.SetAttemptStateAsync(c.AttemptId, "running", null, null, null, clock.UtcNow, ct);
        await db.SetJobStateAsync(c.JobId, "running", null, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, c.JobId, ct), "agent", "attempt", c.AttemptId,
            "attempt.running", "info", "Container started", ct: ct);
        return true;
    }
}
