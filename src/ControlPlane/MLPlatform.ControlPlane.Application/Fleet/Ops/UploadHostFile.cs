using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record UploadHostFileCommand(Guid WorkspaceId, Guid HostId, string Path, Stream Content, long Size) : ICommand<HostOpsReply>;

public sealed class UploadHostFileCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details)
    : ICommandHandler<UploadHostFileCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(UploadHostFileCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("path", r.Path);
        details.Set("bytes", r.Size);
        var reply = await ops.UploadAsync(r.HostId, r.Path, r.Content, r.Size, ct);
        details.Set("sha256", reply.Sha256);
        return reply;
    }
}
