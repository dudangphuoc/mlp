using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Stores the machine's mlp-net public key (the control plane obtains it via ContainerRunOnce keygen on the
/// machine — D-38).</summary>
public sealed record SetHostWgKeyCommand(Guid HostId, string WgPublicKey) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="SetHostWgKeyCommand"/>.</summary>
public sealed class SetHostWgKeyCommandHandler(IWriteSession db) : ICommandHandler<SetHostWgKeyCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostWgKeyCommand c, CancellationToken ct)
    {
        await db.SetHostWgKeyAsync(c.HostId, c.WgPublicKey, ct);
        return true;
    }
}
