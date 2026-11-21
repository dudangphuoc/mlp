using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record GetJobQuery(Guid WorkspaceId, Guid JobId) : IQuery<JobRow>;

public sealed class GetJobQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetJobQuery, JobRow>
{
    public async ValueTask<JobRow> Handle(GetJobQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "job.submit", ct);
        return await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
    }
}
