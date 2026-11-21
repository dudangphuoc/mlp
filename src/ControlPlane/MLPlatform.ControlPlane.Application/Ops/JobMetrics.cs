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

[RequiresPermission("run.read")]
public sealed record JobMetricsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<AttemptMetricRow>>;

public sealed class JobMetricsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<JobMetricsQuery, IReadOnlyList<AttemptMetricRow>>
{
    public async ValueTask<IReadOnlyList<AttemptMetricRow>> Handle(JobMetricsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.JobMetricsAsync(q.JobId, clock.UtcNow.AddHours(-24), ct);
    }
}
