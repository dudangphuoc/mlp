using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

public sealed class SuspectAttemptsOnHostLoss(IWriteSession db, IClock clock, RecoveryOptions recovery) : IHostLossListener
{
    public Task OnHostOfflineAsync(Guid hostId, string hostName, CancellationToken ct) => Suspects.MarkAsync(db, clock, recovery, hostId, hostName, ct);
}
