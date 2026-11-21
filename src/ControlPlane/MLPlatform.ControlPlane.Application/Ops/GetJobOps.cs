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

public sealed record GetJobOpsQuery(Guid WorkspaceId, Guid JobId) : IQuery<JobOpsView>;

public sealed class GetJobOpsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetJobOpsQuery, JobOpsView>
{
    public async ValueTask<JobOpsView> Handle(GetJobOpsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return new JobOpsView(await db.JobProgressAsync(q.JobId, q.WorkspaceId, ct), await db.JobAttemptsAsync(q.JobId, ct),
            await db.ListCheckpointsAsync(q.JobId, ct), await db.ListTimelineEventsAsync(q.JobId, ct));
    }
}
