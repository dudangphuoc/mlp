using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>
/// Anomaly detector (20/10 §2.3): shim signal lost (warning; too long → return the attempt so the host stops and retries),
/// progress unchanged for longer than `stall_after_min` → "may be stalled" (warning only, no automatic stop).
/// </summary>
public sealed record DetectAnomaliesCommand : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class DetectAnomaliesCommandHandler(IWriteSession db, IClock clock, SignalOptions opts) : ICommandHandler<DetectAnomaliesCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(DetectAnomaliesCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var kill = new List<Guid>();
        foreach (var a in await db.SignalLostAsync(now - opts.SignalLostAfter, ct))
        {
            if (a.SignalLostAt is null)
            {
                await db.MarkSignalLostAsync(a.Id, now, ct);
                await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "system:recovery", "attempt", a.Id, "job.signal_lost", "warning",
                    $"No signal from the training process for over {opts.SignalLostAfter.TotalMinutes:0.#} minutes (machine still online)", ct: ct);
                await Notifier.SendAsync(db, clock, a.WorkspaceId, [a.OwnerId], "job.signal_lost", $"Job \"{a.Name}\" lost signal", $"#/jobs/{a.JobId}", $"job/{a.JobId}", "warning", ct);
            }
            else if (now - a.SignalLostAt.Value >= opts.SignalKillAfter - opts.SignalLostAfter) kill.Add(a.Id);
        }
        foreach (var j in await db.StalledAsync(now, ct))
        {
            await db.MarkStalledAsync(j.Id, now, ct);
            await TimelineEvents.EmitAsync(db, clock, j.WorkspaceId, "system:recovery", "job", j.Id, "progress.stalled", "warning",
                $"Progress stuck at {j.Current:0.#} — may be stalled (not stopped automatically: some valid steps stay silent for a long time)", ct: ct);
            await Notifier.SendAsync(db, clock, j.WorkspaceId, [j.OwnerId], "job.stalled", $"Job \"{j.Name}\" may be stalled", $"#/jobs/{j.Id}", $"job/{j.Id}", "warning", ct);
        }
        return kill;
    }
}
