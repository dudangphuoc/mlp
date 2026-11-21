using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Fleet autonomy (D-40): machines keep their containers running while the control plane is down, however
/// long.</summary>
[Access(AccessKind.Internal)]
public sealed record RearmAfterRestartCommand(TimeSpan Grace) : ICommand<bool>, IInternalCommand;

public sealed class RearmAfterRestartCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RearmAfterRestartCommand, bool>
{
    public async ValueTask<bool> Handle(RearmAfterRestartCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.RearmAttemptsAsync(now, now + c.Grace, ct);
        await db.MarkHostsWaitingAsync(ct);
        await db.RearmServersAsync(now, ct);
        await db.RearmDeploymentsAsync(now, ct);
        return true;
    }
}
