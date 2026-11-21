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

[RequiresPermission("dataset.read")]
public sealed record SystemIncidentsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EventRow>>;

public sealed class SystemIncidentsQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<SystemIncidentsQuery, IReadOnlyList<EventRow>>
{
    public async ValueTask<IReadOnlyList<EventRow>> Handle(SystemIncidentsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.SystemIncidentsAsync(clock.UtcNow.AddDays(-30), ct);
    }
}
