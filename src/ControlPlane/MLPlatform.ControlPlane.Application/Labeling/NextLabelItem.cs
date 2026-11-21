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

// ───────────── Queue ─────────────

public sealed record NextLabelItemCommand(Guid WorkspaceId, Guid ProjectId, bool Review) : ICommand<ItemView?>;

public sealed class NextLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock, S3Client s3) : ICommandHandler<NextLabelItemCommand, ItemView?>
{
    public async ValueTask<ItemView?> Handle(NextLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var now = clock.UtcNow;
        var expires = now.AddMinutes(p.LockTtlMin);
        PathRow? row;
        if (c.Review)
        {
            if (!roles.Contains("reviewer") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a Reviewer of this project");
            row = await db.ClaimNextReviewAsync(p.Id, uid, now, expires, ct);
        }
        else
        {
            if (!roles.Contains("annotator") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not an Annotator of this project");
            var max = (await db.ListProjectMembersAsync(p.Id, ct)).Where(m => m.UserId == uid && m.Role == "annotator").Select(m => m.MaxOpenItems).DefaultIfEmpty(50).Max();
            if (await db.CountOpenAsync(p.Id, uid, now, ct) >= max)
                throw new ResourceExhaustedException("LABEL_MAX_OPEN_REACHED", "Maximum number of held items reached");
            row = await db.ClaimNextItemAsync(p.Id, uid, now, expires, ct);
        }
        return row is null ? null : await access.ViewAsync(p, row.Path, s3, ct, db);
    }
}
