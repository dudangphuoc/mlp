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

public sealed record ReopenLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Reason) : ICommand<string>;

public sealed class ReopenLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ReopenLabelItemCommand, string>
{
    public async ValueTask<string> Handle(ReopenLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead can reopen items");
        if (string.IsNullOrWhiteSpace(c.Reason)) throw LabelRules.Invalid("reason", "A reason for reopening is required");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.Status is not ("accepted" or "skipped")) throw new PreconditionException("LABEL_PROJECT_STATE", "Only accepted or skipped items can be reopened");
        var now = clock.UtcNow;
        await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Reason.Trim(), now, ct);
        var next = s.AnnotatorId is null ? "unlabeled" : "rejected";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, null, null, false, false, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "reopen", null, null, uid, now, ct);
        if (s.AnnotatorId is { } annotator)
            await Notifier.SendAsync(db, clock, p.WorkspaceId, [annotator], "labeling.rejected", $"Lead reopened an item in \"{p.Name}\"",
                $"#/labeling/{p.Id}/annotate", $"labelproject/{p.Id}", "warning", ct);
        return next;
    }
}
