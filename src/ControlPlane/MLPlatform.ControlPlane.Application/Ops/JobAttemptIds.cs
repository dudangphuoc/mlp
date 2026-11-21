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

/// <summary>Checks read permission on the job (logs) and returns the attempt list.</summary>
public sealed record JobAttemptIdsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<Guid>>;

public sealed class JobAttemptIdsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<JobAttemptIdsQuery, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(JobAttemptIdsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetJobAsync(q.JobId, q.WorkspaceId, ct) ?? throw new NotFoundException("SCHED_JOB_NOT_FOUND", "Job not found");
        return (await db.JobAttemptsAsync(q.JobId, ct)).Select(a => a.Id).ToList();
    }
}
