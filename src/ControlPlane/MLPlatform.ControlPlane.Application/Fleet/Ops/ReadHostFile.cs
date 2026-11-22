using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record ReadHostFileQuery(Guid WorkspaceId, Guid HostId, string Path, ulong Offset, uint Length) : IQuery<HostOpsReply>;

public sealed class ReadHostFileQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IHostOps ops) : IQueryHandler<ReadHostFileQuery, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ReadHostFileQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.FsRead) { Path = r.Path, Offset = r.Offset, Length = r.Length }, ct);
        return reply;
    }
}
