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

public sealed record JobEventsAfterQuery(Guid WorkspaceId, Guid JobId, DateTimeOffset After) : IQuery<IReadOnlyList<EventRow>>;

public sealed class JobEventsAfterQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<JobEventsAfterQuery, IReadOnlyList<EventRow>>
{
    public async ValueTask<IReadOnlyList<EventRow>> Handle(JobEventsAfterQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return await db.EventsAfterAsync(q.JobId, q.After, ct);
    }
}
