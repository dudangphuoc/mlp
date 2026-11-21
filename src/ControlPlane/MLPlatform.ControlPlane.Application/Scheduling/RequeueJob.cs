using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record RequeueJobCommand(Guid JobId, string Reason) : ICommand<bool>;

public sealed class RequeueJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RequeueJobCommand, bool>
{
    public async ValueTask<bool> Handle(RequeueJobCommand c, CancellationToken ct)
    {
        await db.RequeueJobAsync(c.JobId, c.Reason, ct);
        var last = (await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.ListTimelineEventsAsync(db, c.JobId, ct)).LastOrDefault();
        if (last?.Kind != "job.waiting" || last.Message != c.Reason)
            await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, c.JobId, ct), "scheduler", "job", c.JobId,
                "job.waiting", "warning", c.Reason, ct: ct);
        return true;
    }
}
