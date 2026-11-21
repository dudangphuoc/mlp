using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Undo an automatic accept: the item goes back to review. Lead or Reviewer.</summary>
public sealed record UndoHermesAcceptCommand(Guid WorkspaceId, Guid ProjectId, Guid CheckId) : ICommand<HermesCheckRow>;

public sealed class UndoHermesAcceptCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<UndoHermesAcceptCommand, HermesCheckRow>
{
    public async ValueTask<HermesCheckRow> Handle(UndoHermesAcceptCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead or Reviewer can undo an automatic accept");
        var check = await db.GetHermesCheckAsync(c.CheckId, p.WorkspaceId, ct);
        if (check is null || check.ProjectId != p.Id) throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        if (check.Action != "auto_accepted") throw new PreconditionException("HERMES_NOT_AUTO_ACCEPTED", "This check did not accept the item");
        var s = await db.LockItemStateAsync(p.Id, check.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.Status != "accepted") throw new PreconditionException("LABEL_PROJECT_STATE", $"Item is {s.Status} now; nothing to undo");
        var now = clock.UtcNow;
        await db.UpdateItemStateAsync(p.Id, check.Path, "in_review", null, null, null, null, false, false, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, check.Path, "hermes.undo", null, null, uid, now, ct);
        await db.MarkHermesUndoneAsync(check.Id, p.WorkspaceId, uid, now, ct);
        return (await db.GetHermesCheckAsync(check.Id, p.WorkspaceId, ct))!;
    }
}
