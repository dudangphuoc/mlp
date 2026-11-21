using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Cancel a job (notebook/deployment stopped, user cancelled): mark it before stopping the container so FinishAttempt does not retry.</summary>
public sealed record CancelJobCommand(Guid JobId, string Reason) : ICommand<bool>;

public sealed class CancelJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<CancelJobCommand, bool>
{
    public async ValueTask<bool> Handle(CancelJobCommand c, CancellationToken ct)
    {
        await db.SetJobStateAsync(c.JobId, "cancelled", c.Reason, clock.UtcNow, ct);
        if (await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, c.JobId, ct) is { } ws)
            await db.CancelPendingAttemptsAsync(c.JobId, ws, clock.UtcNow, ct);
        return true;
    }
}
