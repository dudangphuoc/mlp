using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

// ───────────── Internal use (scheduler, proxy, reaper) — not called from RPC ─────────────

[Access(AccessKind.Internal)]
public sealed record SetNotebookStateByJobCommand(Guid JobId, string State, string? Reason, Guid? HostId, string? Endpoint) : ICommand<bool>;

public sealed class SetNotebookStateByJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetNotebookStateByJobCommand, bool>
{
    public async ValueTask<bool> Handle(SetNotebookStateByJobCommand c, CancellationToken ct)
    {
        if (await db.GetServerByJobAsync(c.JobId, ct) is not { } s) return false;
        var state = s.State is "stopping" or "stopped" && c.State is "failed" or "stopped" ? "stopped" : c.State;
        if (s.State == "stopped" && state != "stopped") return true;
        await db.SetServerStateAsync(s.Id, state, state == "stopped" && s.State == "stopping" ? s.StateReason : c.Reason, c.HostId, c.Endpoint, clock.UtcNow, ct);
        return true;
    }
}
