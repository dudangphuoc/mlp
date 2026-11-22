using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record MoveHostPathCommand(Guid WorkspaceId, Guid HostId, string From, string To) : ICommand<HostOpsReply>;

public sealed class MoveHostPathCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<MoveHostPathCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(MoveHostPathCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("from", r.From);
        details.Set("to", r.To);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsMove) { Path = r.From, To = r.To }, ct);
        return reply;
    }
}
