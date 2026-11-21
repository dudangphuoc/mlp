using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record ListJobsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<JobRow>>;

public sealed class ListJobsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListJobsQuery, IReadOnlyList<JobRow>>
{
    public async ValueTask<IReadOnlyList<JobRow>> Handle(ListJobsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "job.submit", ct);
        return await db.ListJobsAsync(q.WorkspaceId, ct);
    }
}
