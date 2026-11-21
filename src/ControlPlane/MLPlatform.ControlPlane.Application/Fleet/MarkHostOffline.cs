using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record MarkHostOfflineCommand(Guid HostId, string Reason) : ICommand<bool>;

public sealed class MarkHostOfflineCommandHandler(IWriteSession db, IClock clock, MLPlatform.ControlPlane.Application.Ops.RecoveryOptions recovery) : ICommandHandler<MarkHostOfflineCommand, bool>
{
    public async ValueTask<bool> Handle(MarkHostOfflineCommand c, CancellationToken ct)
    {
        var before = await MLPlatform.ControlPlane.Application.Notifications.NotifySql.HostInfoForNotifyAsync(db, c.HostId, ct);
        await db.SetHostStateAsync(c.HostId, "offline", c.Reason, clock.UtcNow, ct);
        if (before is not null)
            await MLPlatform.ControlPlane.Application.Ops.Suspects.MarkAsync(db, clock, recovery, c.HostId, before.Name, ct);
        if (before is { State: not "offline" })
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, before.WorkspaceId,
                await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, before.WorkspaceId, ct), "fleet.host_offline",
                $"Machine {before.Name} is offline", "#/fleet", $"host/{before.Id}", "warning", ct);
        return true;
    }
}
