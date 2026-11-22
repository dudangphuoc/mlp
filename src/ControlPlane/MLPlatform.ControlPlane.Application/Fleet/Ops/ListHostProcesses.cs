using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record ListHostProcessesQuery(Guid WorkspaceId, Guid HostId, string Sort) : IQuery<HostOpsReply>;

public sealed class ListHostProcessesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IHostOps ops) : IQueryHandler<ListHostProcessesQuery, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ListHostProcessesQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.ProcessList) { Sort = r.Sort }, ct);
        return reply;
    }
}
