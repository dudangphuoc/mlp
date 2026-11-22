using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record MakeHostDirCommand(Guid WorkspaceId, Guid HostId, string Path) : ICommand<HostOpsReply>;

public sealed class MakeHostDirCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<MakeHostDirCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(MakeHostDirCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("path", r.Path);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsMkdir) { Path = r.Path }, ct);
        return reply;
    }
}
