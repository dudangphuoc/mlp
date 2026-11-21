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

/// <summary>Shim heartbeat (30 s) — tier 2 of 20/10 §2.3.</summary>
[Access(AccessKind.WorkloadToken)]
public sealed record WorkloadHeartbeatCommand(string Token) : ICommand<bool>, IInternalCommand;

public sealed class WorkloadHeartbeatCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<WorkloadHeartbeatCommand, bool>
{
    public async ValueTask<bool> Handle(WorkloadHeartbeatCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        await db.AttemptHeartbeatAsync(a.Id, clock.UtcNow, ct);
        await Ops2Sql.ClearSignalLostAsync(db, a.Id, ct);
        return true;
    }
}
