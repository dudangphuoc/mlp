using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record ListHostFilesQuery(Guid WorkspaceId, Guid HostId, string Path) : IQuery<HostOpsReply>;

public sealed class ListHostFilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IHostOps ops) : IQueryHandler<ListHostFilesQuery, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ListHostFilesQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsList) { Path = r.Path }, ct);
        return reply;
    }
}
