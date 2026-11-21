using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

// ---- Agent register (reconnect) ----
public sealed record AgentRegisterCommand(string Credential, string AgentVersion, byte[]? HostInfo, string? WgPublicKey = null) : ICommand<HostRow>;

public sealed class AgentRegisterCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<AgentRegisterCommand, HostRow>
{
    public async ValueTask<HostRow> Handle(AgentRegisterCommand c, CancellationToken ct)
    {
        var h = await db.GetHostByCredentialAsync(tokens.Hash(c.Credential), ct);
        if (h is null || h.DisconnectedAt is not null)
            throw new UnauthenticatedException("FLEET_CREDENTIAL_INVALID", "Invalid machine credential");
        await db.SetHostOnlineAsync(h.Id, "online", c.AgentVersion, c.HostInfo, clock.UtcNow, ct);
        if (!string.IsNullOrEmpty(c.WgPublicKey)) await db.SetHostWgKeyAsync(h.Id, c.WgPublicKey, ct);
        return h with { State = "online" };
    }
}
