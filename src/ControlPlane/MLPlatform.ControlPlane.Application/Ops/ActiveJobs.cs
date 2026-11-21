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

// ───────────── Reads for WatchWorkspace / cluster map / status ─────────────

public sealed record ActiveJobsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ActiveJobRow>>;

public sealed class ActiveJobsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<ActiveJobsQuery, IReadOnlyList<ActiveJobRow>>
{
    public async ValueTask<IReadOnlyList<ActiveJobRow>> Handle(ActiveJobsQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ActiveJobsForUserAsync(q.WorkspaceId, uid, clock.UtcNow.AddSeconds(-60), ct);
    }
}
