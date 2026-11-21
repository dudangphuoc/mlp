using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Agent reports whether Docker is running on the machine (every 15 s).</summary>
public sealed record SetHostDockerCommand(Guid HostId, bool Ok, string Error) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="SetHostDockerCommand"/>.</summary>
public sealed class SetHostDockerCommandHandler(IWriteSession db) : ICommandHandler<SetHostDockerCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostDockerCommand c, CancellationToken ct) => await db.SetHostDockerAsync(c.HostId, c.Ok, c.Error, ct) ?? false;
}
