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

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record SkipLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Reason) : ICommand<string>;

public sealed class SkipLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SkipLabelItemCommand, string>
{
    public async ValueTask<string> Handle(SkipLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (string.IsNullOrWhiteSpace(c.Reason)) throw LabelRules.Invalid("reason", "A reason for skipping is required");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId != uid || s.Status != "in_progress") throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item is not assigned to you");
        access.RequireLock(s, uid);
        await db.UpdateItemStateAsync(p.Id, c.Path, "skipped", null, null, null, c.Reason.Trim(), false, false, clock.UtcNow, ct);
        return "skipped";
    }
}
