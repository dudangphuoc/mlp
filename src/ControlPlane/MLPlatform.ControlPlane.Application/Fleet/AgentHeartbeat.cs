using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record AgentHeartbeatCommand(Guid HostId, byte[]? HostInfo, string AgentVersion) : ICommand<bool>;

public sealed class AgentHeartbeatCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<AgentHeartbeatCommand, bool>
{
    public async ValueTask<bool> Handle(AgentHeartbeatCommand c, CancellationToken ct)
    {
        await db.SetHostOnlineAsync(c.HostId, "online", c.AgentVersion, c.HostInfo, clock.UtcNow, ct);
        return true;
    }
}
