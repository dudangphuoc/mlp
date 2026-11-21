using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed class RegisteredRunGuard(IReadSession db) : IRunDeletionGuard
{
    public async Task EnsureDeletableAsync(Guid runId, CancellationToken ct)
    {
        if (await db.CountVersionsByRunAsync(runId, ct) > 0)
            throw new PreconditionException("TRACK_RUN_HAS_MODEL", "A registered model version was created from this run — delete that version first");
    }
}
