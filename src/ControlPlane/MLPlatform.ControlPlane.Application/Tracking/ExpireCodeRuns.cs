using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Background: code runs that stopped sending heartbeats are marked failed.</summary>
[Access(AccessKind.Internal)]
public sealed record ExpireCodeRunsCommand : ICommand<long>, IInternalCommand;

public sealed class ExpireCodeRunsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ExpireCodeRunsCommand, long>
{
    public async ValueTask<long> Handle(ExpireCodeRunsCommand c, CancellationToken ct) =>
        await db.ExpireCodeRunsAsync(clock.UtcNow, clock.UtcNow - CodeRunRules.HeartbeatTimeout, ct);
}
