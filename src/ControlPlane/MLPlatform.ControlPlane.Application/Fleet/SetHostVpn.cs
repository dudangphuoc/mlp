using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Records the tunnel check result (internal, from the agent session).</summary>
[Access(AccessKind.Internal)]
public sealed record SetHostVpnCommand(Guid HostId, string Status, string? Message) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="SetHostVpnCommand"/>.</summary>
public sealed class SetHostVpnCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetHostVpnCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostVpnCommand c, CancellationToken ct)
    {
        await db.SetHostVpnAsync(c.HostId, c.Status, c.Message, clock.UtcNow, ct);
        return true;
    }
}
