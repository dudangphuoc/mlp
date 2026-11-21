using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>api startup: unfinished jobs → failed.</summary>
[Access(AccessKind.Internal)]
public sealed record FailStaleHubImportsCommand : ICommand<int>, IInternalCommand;

/// <summary>Handles <see cref="FailStaleHubImportsCommand"/>.</summary>
public sealed class FailStaleHubImportsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FailStaleHubImportsCommand, int>
{
    public async ValueTask<int> Handle(FailStaleHubImportsCommand c, CancellationToken ct) => (int)await db.FailStaleImportsAsync(clock.UtcNow, ct);
}
