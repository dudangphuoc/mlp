using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>
/// Fleet autonomy (D-40): machines keep their containers running while the control plane is down, however long. When the control
/// plane starts again it must not judge them by clocks that kept ticking during the outage: every watchdog (waiting for a machine,
/// shim signal, stalled progress, idle notebook, idle test replica) restarts from now, and running attempts wait
/// <paramref name="Grace"/> for their agent to reconnect and report what is actually running.
/// </summary>
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
