using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

[RequiresPermission("fleet.manage")]
public sealed record DisconnectHostCommand(Guid WorkspaceId, Guid HostId) : ICommand<bool>;

public sealed class DisconnectHostCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<DisconnectHostCommand, bool>
{
    public async ValueTask<bool> Handle(DisconnectHostCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        await db.DisconnectHostAsync(c.HostId, c.WorkspaceId, clock.UtcNow, ct);
        return true;
    }
}
