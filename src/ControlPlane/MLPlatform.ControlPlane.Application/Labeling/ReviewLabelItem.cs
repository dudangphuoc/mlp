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

public sealed record ReviewLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path, bool Accept, string Comment) : ICommand<string>;

public sealed class ReviewLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ReviewLabelItemCommand, string>
{
    public async ValueTask<string> Handle(ReviewLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a Reviewer of this project");
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (s.AnnotatorId == uid) throw new ForbiddenException("LABEL_SELF_REVIEW", "You cannot review an item you labeled");
        if (s.Status != "in_review") throw new PreconditionException("LABEL_NOT_IN_REVIEW", "Item is not awaiting review");
        if (!c.Accept && string.IsNullOrWhiteSpace(c.Comment))
            throw new InvalidArgumentException("LABEL_REJECT_COMMENT_REQUIRED", "A reason is required when rejecting");
        var now = clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(c.Comment)) await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Comment.Trim(), now, ct);
        var next = c.Accept ? "accepted" : "rejected";
        await db.UpdateItemStateAsync(p.Id, c.Path, next, null, null, uid, null, false, true, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, c.Accept ? "accept" : "reject", null, null, uid, now, ct);
        if (!c.Accept && s.AnnotatorId is { } annotator)
            await Notifier.SendAsync(db, clock, p.WorkspaceId, [annotator], "labeling.rejected", $"Item rejected in \"{p.Name}\"",
                $"#/labeling/{p.Id}/annotate", $"labelproject/{p.Id}", "warning", ct);
        return next;
    }
}
