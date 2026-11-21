using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>State of a job as the launcher needs it between slow steps (image build → container start).</summary>
public sealed record JobStateQuery(Guid JobId) : IQuery<string?>;

/// <summary>Reads the job's current state; null when the job is gone.</summary>
public sealed class JobStateQueryHandler(IReadSession db) : IQueryHandler<JobStateQuery, string?>
{
    public async ValueTask<string?> Handle(JobStateQuery q, CancellationToken ct) =>
        await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, q.JobId, ct) is { } ws ? (await db.GetJobAsync(q.JobId, ws, ct))?.State : null;
}
