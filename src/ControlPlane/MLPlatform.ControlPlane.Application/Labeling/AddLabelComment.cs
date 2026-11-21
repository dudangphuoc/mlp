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
public sealed record AddLabelCommentCommand(Guid WorkspaceId, Guid ProjectId, string Path, string Body) : ICommand<bool>;

public sealed class AddLabelCommentCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<AddLabelCommentCommand, bool>
{
    public async ValueTask<bool> Handle(AddLabelCommentCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (roles.Count == 0) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not a member of this project");
        if (string.IsNullOrWhiteSpace(c.Body)) throw LabelRules.Invalid("body", "Comment is empty");
        _ = await db.GetItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        await db.InsertCommentAsync(Ids.New(), p.Id, c.Path, uid, c.Body.Trim(), clock.UtcNow, ct);
        return true;
    }
}
