using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Agent (restarted or reconnected) reports containers still running (20/10 §4.3, §4.7): `suspect` attempt
/// with a container → `running` (nothing lost).</summary>
[Access(AccessKind.Internal)]
public sealed record ReconcileHostCommand(Guid HostId, IReadOnlyList<Guid> Running) : ICommand<(IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)>, IInternalCommand;

public sealed class ReconcileHostCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ReconcileHostCommand, (IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)>
{
    public async ValueTask<(IReadOnlyList<Guid> Stop, IReadOnlyList<Guid> Lost)> Handle(ReconcileHostCommand c, CancellationToken ct)
    {
        var stop = new List<Guid>();
        var lost = new List<Guid>();
        var running = c.Running.ToHashSet();
        foreach (var a in await db.HostAttemptsAsync(c.HostId, [.. running], ct))
        {
            var alive = running.Contains(a.Id);
            switch (a.State)
            {
                case "suspect" when alive:
                    await db.ReviveAttemptAsync(a.Id, ct);
                    await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, $"agent:{c.HostId}", "attempt", a.Id, "attempt.reattached", "success",
                        "Agent is back and the container is still running — continuing with nothing lost", ct: ct);
                    break;
                case "suspect":
                    lost.Add(a.Id);
                    break;
                case "launching" or "running":
                    break;
                default:
                    if (alive)
                    {
                        stop.Add(a.Id);
                        await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "attempt", a.Id, "attempt.fenced", "warning",
                            "Previous machine is back but the run attempt was replaced — stopping the old container", ct: ct);
                    }
                    break;
            }
        }
        return (stop, lost);
    }
}
