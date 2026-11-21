using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notebooks;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Cancel any job that has not finished — waiting, reserving, preparing or running — from the Jobs
/// page.</summary>
[RequiresPermission("job.submit")]
public sealed record CancelJobByUserCommand(Guid WorkspaceId, Guid JobId) : ICommand<JobRow>;

/// <summary>Routes the cancel to the job's owner so its page shows the same outcome; a job without an owner is just
/// cancelled.</summary>
public sealed class CancelJobByUserCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IMediator mediator, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<CancelJobByUserCommand, JobRow>
{
    public async ValueTask<JobRow> Handle(CancelJobByUserCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var job = await db.GetJobAsync(c.JobId, c.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        if (job.State is "succeeded" or "failed" or "cancelled") throw new PreconditionException("SCHED_JOB_FINISHED", "This job has already finished");
        if (await db.GetRunByJobAsync(job.Id, ct) is { } run && run.WorkspaceId == c.WorkspaceId)
        {
            if (await db.GetTrialByRunAsync(run.Id, c.WorkspaceId, ct) is { } trial)
            {
                if (trial.State is "queued" or "running") await mediator.Send(new CancelTrialCommand(c.WorkspaceId, trial.Id), ct);
            }
            else if (!RunRules.Finished(run.State)) await mediator.Send(new StopRunCommand(c.WorkspaceId, run.Id), ct);
        }
        else if (await db.GetDeploymentByJobAsync(job.Id, ct) is { State: not "stopped" } dep) await mediator.Send(new StopDeploymentCommand(c.WorkspaceId, dep.Id), ct);
        else if (await db.GetServerByJobAsync(job.Id, ct) is { State: not ("stopped" or "stopping") } nb) await mediator.Send(new StopNotebookCommand(c.WorkspaceId, nb.Id), ct);
        afterCommit.StopAfterCommitTolerant(workloads, job.Id);
        return job;
    }
}
