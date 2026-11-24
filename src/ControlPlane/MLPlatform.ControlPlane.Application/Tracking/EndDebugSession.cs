using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>The developer closed the F5 debug session of a code run: stop it, or count a failure caused by the stopped
/// debugger in the last minute as cancelled.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record EndDebugSessionCommand(Guid WorkspaceId, Guid RunId) : ICommand<bool>;

public sealed class EndDebugSessionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads)
    : ICommandHandler<EndDebugSessionCommand, bool>
{
    public async ValueTask<bool> Handle(EndDebugSessionCommand c, CancellationToken ct)
    {
        var run = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        if (!RunRules.Finished(run.State))
        {
            await db.SetRunStateAsync(run.Id, "cancelled", clock.UtcNow, ct);
            if (run.JobId is { } job) afterCommit.StopAfterCommit(workloads, job);
            return true;
        }
        await db.MarkDebugRunCancelledAsync(run.Id, run.WorkspaceId, clock.UtcNow - TimeSpan.FromMinutes(1), ct);
        return true;
    }
}
