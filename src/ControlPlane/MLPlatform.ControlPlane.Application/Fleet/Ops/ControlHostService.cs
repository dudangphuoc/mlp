using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_write")]
public sealed record ControlHostServiceCommand(Guid WorkspaceId, Guid HostId, string Name, string Action) : ICommand<HostOpsReply>;

public sealed class ControlHostServiceCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IHostOps ops, AuditDetails details) : ICommandHandler<ControlHostServiceCommand, HostOpsReply>
{
    public async ValueTask<HostOpsReply> Handle(ControlHostServiceCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_write", ct);
        details.Set("service", r.Name);
        details.Set("action", r.Action);
        var reply = await ops.ExecuteAsync(r.HostId, new(HostOpsKind.ServiceControl) { Name = r.Name, Action = r.Action }, ct);
        return reply;
    }
}
