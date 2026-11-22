using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record WriteHostFileCommand(Guid WorkspaceId, Guid HostId, string Path, ulong Offset, ReadOnlyMemory<byte> Data, bool Truncate) : ICommand<HostOpsReply>;

public sealed class WriteHostFileCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<WriteHostFileCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(WriteHostFileCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("path", r.Path);
        details.Set("offset", r.Offset);
        details.Set("bytes", r.Data.Length);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsWrite) { Path = r.Path, Offset = r.Offset, Data = r.Data, Truncate = r.Truncate }, ct);
        details.Set("sha256", reply.Sha256);
        return reply;
    }
}
