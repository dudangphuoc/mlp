using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Job finished → run state (called from the api after FinishAttempt; to be replaced by a domain event once there is an outbox).</summary>
public sealed record SyncRunStateCommand(Guid JobId, string JobState) : ICommand<bool>;

public sealed class SyncRunStateCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SyncRunStateCommand, bool>
{
    public async ValueTask<bool> Handle(SyncRunStateCommand c, CancellationToken ct)
    {
        if (await db.GetRunByJobAsync(c.JobId, ct) is not { } r) return false;
        await db.SetRunStateAsync(r.Id, c.JobState == "queued" ? "queued" : c.JobState, clock.UtcNow, ct);
        return true;
    }
}
