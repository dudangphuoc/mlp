using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record ListHostServicesQuery(Guid WorkspaceId, Guid HostId) : IQuery<HostOpsReply>;

public sealed class ListHostServicesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IHostOps ops) : IQueryHandler<ListHostServicesQuery, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ListHostServicesQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.ServiceList), ct);
        return reply;
    }
}
