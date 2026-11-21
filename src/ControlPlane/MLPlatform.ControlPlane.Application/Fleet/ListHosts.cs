using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

// ---- Queries for the UI ----
[RequiresPermission("fleet.read")]
public sealed record ListHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

public sealed class ListHostsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListHostsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListHostsAsync(q.WorkspaceId, ct);
    }
}
