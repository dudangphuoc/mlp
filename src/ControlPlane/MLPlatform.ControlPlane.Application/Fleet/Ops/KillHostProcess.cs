using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record KillHostProcessCommand(Guid WorkspaceId, Guid HostId, int Pid, bool Force, bool Tree) : ICommand<HostOpsReply>;

public sealed class KillHostProcessCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<KillHostProcessCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(KillHostProcessCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("pid", r.Pid);
        details.Set("force", r.Force);
        details.Set("tree", r.Tree);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.ProcessKill) { Pid = r.Pid, Force = r.Force, Tree = r.Tree }, ct);
        return reply;
    }
}
