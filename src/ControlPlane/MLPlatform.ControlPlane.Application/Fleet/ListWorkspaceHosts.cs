using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Internal (catalog environments, after the RPC checked the permission): every connected-or-not machine of
/// the workspace that was not removed.</summary>
[Access(AccessKind.Internal)]
public sealed record ListWorkspaceHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

/// <summary>Same list as the Machines page, without the fleet permission.</summary>
public sealed class ListWorkspaceHostsQueryHandler(IReadSession db) : IQueryHandler<ListWorkspaceHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListWorkspaceHostsQuery q, CancellationToken ct) => await db.ListHostsAsync(q.WorkspaceId, ct);
}
