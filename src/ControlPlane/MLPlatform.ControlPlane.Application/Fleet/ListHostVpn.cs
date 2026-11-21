using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>VPN status of the workspace's machines.</summary>
public sealed record ListHostVpnQuery(Guid WorkspaceId) : IQuery<List<HostVpnRow>>;

/// <summary>Handles <see cref="ListHostVpnQuery"/>.</summary>
public sealed class ListHostVpnQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHostVpnQuery, List<HostVpnRow>>
{
    public async ValueTask<List<HostVpnRow>> Handle(ListHostVpnQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListHostVpnAsync(q.WorkspaceId, ct);
    }
}
