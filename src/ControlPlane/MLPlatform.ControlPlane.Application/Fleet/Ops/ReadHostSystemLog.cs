using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record ReadHostSystemLogQuery(Guid WorkspaceId, Guid HostId, string Unit, int Lines) : IQuery<HostOpsReply>;

public sealed class ReadHostSystemLogQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IHostOps ops) : IQueryHandler<ReadHostSystemLogQuery, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ReadHostSystemLogQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.SystemLog) { Name = r.Unit, Lines = r.Lines }, ct);
        return reply;
    }
}
