using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Fleet.Gpu;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>Stores a GPU sample per card, the yield flag, and updates debounced alerts (docs/agent-upgrade.md
/// §2).</summary>
[Access(AccessKind.Internal)]
public sealed record RecordGpuMetricsCommand(Guid HostId, string GpuState, IReadOnlyList<GpuSampleIn> Gpus, bool Yield) : ICommand<int>, IInternalCommand;

public sealed class RecordGpuMetricsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordGpuMetricsCommand, int>
{
    public async ValueTask<int> Handle(RecordGpuMetricsCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.SetHostGpuYieldAsync(c.HostId, c.Yield, ct);
        foreach (var g in c.Gpus)
            await db.InsertGpuSampleAsync(c.HostId, g.Uuid, now, g.Util, g.MemUtil, g.VramUsed, g.VramTotal, g.Temp, g.Power, g.PowerLimit, g.SmClock, g.Throttle, g.Ecc, ct);
        var allocated = (await db.HostGpuAllocationsAsync(c.HostId, ct)).SelectMany(a => a.GpuUuids).ToHashSet(StringComparer.Ordinal);
        var conditions = GpuAlertRules.Evaluate(c.GpuState, c.Gpus, allocated).ToList();
        await UpdateAlertsAsync(c.HostId, conditions, now, ct);
        return c.Gpus.Count;
    }

    private async Task UpdateAlertsAsync(Guid hostId, List<GpuAlertCondition> conditions, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.ListGpuAlertsAsync(hostId, ct);
        foreach (var gone in existing.Where(e => !conditions.Any(c => c.GpuUuid == e.GpuUuid && c.Kind == e.Kind)))
            await db.DeleteGpuAlertAsync(hostId, gone.GpuUuid, gone.Kind, ct);
        HostAlertTarget? target = null;
        foreach (var cond in conditions)
        {
            var row = existing.FirstOrDefault(e => e.GpuUuid == cond.GpuUuid && e.Kind == cond.Kind);
            if (row is null || row.Level != cond.Level) await db.UpsertGpuAlertAsync(hostId, cond.GpuUuid, cond.Kind, cond.Level, now, ct);
            var since = row?.Since ?? now;
            if (row?.NotifiedAt is not null || now - since < cond.Hold) continue;
            target ??= await db.HostAlertTargetAsync(hostId, ct);
            if (target is null) return;
            await NotifyAsync(target, hostId, cond, now, ct);
        }
    }

    private async Task NotifyAsync(HostAlertTarget target, Guid hostId, GpuAlertCondition cond, DateTimeOffset now, CancellationToken ct)
    {
        var admins = await Notifier.AdminsAsync(db, target.WorkspaceId, ct);
        var severity = cond.Level switch { "crit" => "error", "warn" => "warning", _ => "info" };
        await Notifier.SendAsync(db, clock, target.WorkspaceId, admins, GpuAlertRules.NotificationKind(cond.Kind), $"{target.Name} · {cond.Title}",
            $"#/fleet/{hostId}/gpu", $"host/{hostId}/gpu/{cond.GpuUuid}", severity, ct);
        await db.MarkGpuAlertNotifiedAsync(hostId, cond.GpuUuid, cond.Kind, now, ct);
    }
}
