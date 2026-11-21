using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Attempt stuck in "reserving" longer than Timeout (Ray has no suitable node, holder lost, machine out of
/// resources…) → mark it failed and requeue the job (not counted as a retry).</summary>
[Access(AccessKind.Internal)]
public sealed record ExpireReservationsCommand(TimeSpan Timeout) : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class ExpireReservationsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ExpireReservationsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(ExpireReservationsCommand c, CancellationToken ct)
    {
        var stale = await db.StaleReservationsAsync(clock.UtcNow - c.Timeout, ct);
        foreach (var a in stale)
        {
            var msg = $"Could not reserve resources after {(int)c.Timeout.TotalMinutes} minutes (machine not ready or out of CPU/GPU/RAM) — retrying";
            await db.SetAttemptStateAsync(a.Id, "failed", null, "reserve_timeout", msg, clock.UtcNow, ct);
            await db.RequeueJobAsync(a.JobId, "Waiting for resources: " + msg, ct);
        }
        return stale.Select(a => a.Id).ToList();
    }
}
