using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record DeleteHostPathCommand(Guid WorkspaceId, Guid HostId, string Path, bool Recursive) : ICommand<HostOpsReply>;

public sealed class DeleteHostPathCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<DeleteHostPathCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(DeleteHostPathCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("path", r.Path);
        details.Set("recursive", r.Recursive);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsDelete) { Path = r.Path, Recursive = r.Recursive }, ct);
        return reply;
    }
}
