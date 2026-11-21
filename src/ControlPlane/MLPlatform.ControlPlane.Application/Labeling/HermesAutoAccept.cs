using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>
/// Auto-accept after a passing check (internal). Applies only if the item is still waiting for review at the checked revision —
/// otherwise nothing changes and the caller records why.
/// </summary>
public sealed record HermesAutoAcceptCommand(Guid ProjectId, Guid DatasetId, string Path, int Revision, Guid ActorId) : ICommand<string?>, IInternalCommand;

public sealed class HermesAutoAcceptCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<HermesAutoAcceptCommand, string?>
{
    /// <returns>null when accepted; otherwise the reason it was not.</returns>
    public async ValueTask<string?> Handle(HermesAutoAcceptCommand c, CancellationToken ct)
    {
        var s = await db.LockItemStateAsync(c.ProjectId, c.Path, ct);
        if (s is null) return "Item not found";
        if (s.Status != "in_review") return $"Item is {s.Status}, not waiting for review";
        if (s.Revision != c.Revision) return "Labels changed after the check";
        var now = clock.UtcNow;
        await db.UpdateItemStateAsync(c.ProjectId, c.Path, "accepted", null, null, null, null, false, true, now, ct);
        await db.InsertHistoryAsync(Ids.New(), c.DatasetId, c.Path, "hermes.accept", null, null, c.ActorId, now, ct);
        return null;
    }
}
