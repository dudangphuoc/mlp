using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

// ───────────── Item state transitions (§4) ─────────────

public sealed record SubmitLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<string>;

public sealed class SubmitLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SubmitLabelItemCommand, string>
{
    public async ValueTask<string> Handle(SubmitLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId != uid || s.Status != "in_progress") throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item is not assigned to you");
        access.RequireLock(s, uid);
        var next = p.ReviewMode == "none" ? "accepted" : "in_review";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, null, null, true, false, clock.UtcNow, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "submit", null, null, uid, clock.UtcNow, ct);
        return next;
    }
}
