using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Stop a run that is waiting or training: marks it cancelled and returns its job so the host stops the container (WorkloadLauncher.StopJobAsync).</summary>
public sealed record StopRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<Guid?>;

/// <summary>Marks the run cancelled; the host then stops the job.</summary>
public sealed class StopRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<StopRunCommand, Guid?>
{
    public async ValueTask<Guid?> Handle(StopRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        if (RunRules.Finished(r.State)) throw new PreconditionException("TRACK_RUN_FINISHED", "This run has already finished");
        await db.SetRunStateAsync(r.Id, "cancelled", clock.UtcNow, ct);
        if (r.JobId is { } job) afterCommit.StopAfterCommit(workloads, job);
        return r.JobId;
    }
}
