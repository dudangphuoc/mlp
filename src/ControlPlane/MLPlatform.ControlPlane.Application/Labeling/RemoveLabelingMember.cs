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
public sealed record RemoveLabelingMemberCommand(Guid WorkspaceId, Guid ProjectId, Guid UserId, string Role) : ICommand<bool>;

public sealed class RemoveLabelingMemberCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<RemoveLabelingMemberCommand, bool>
{
    public async ValueTask<bool> Handle(RemoveLabelingMemberCommand c, CancellationToken ct)
    {
        _ = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        await db.RemoveMemberAsync(c.ProjectId, c.UserId, c.Role, ct);
        return true;
    }
}
