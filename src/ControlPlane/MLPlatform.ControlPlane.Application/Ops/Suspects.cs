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

// ───────────── Machine disconnected / reconnected ─────────────

/// <summary>Agent channel dropped → attempts on the machine become `suspect` with a grace deadline (GPUs still count as
/// held).</summary>
public static class Suspects
{
    public static async Task MarkAsync(IWriteSession db, IClock clock, RecoveryOptions opts, Guid hostId, string hostName, CancellationToken ct)
    {
        var until = clock.UtcNow + opts.Grace;
        foreach (var s in await db.MarkSuspectAsync(hostId, until, ct))
            await TimelineEvents.EmitAsync(db, clock, s.WorkspaceId, "scheduler", "attempt", s.Id, "attempt.suspect", "warning",
                $"Machine {hostName} disconnected — waiting for it to return until {until:HH:mm:ss} UTC before moving to another machine", new JsonObject { ["host_id"] = hostId.ToString() }, ct: ct);
    }
}
