using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-3: remove a member (not the Owner, not yourself).</summary>
[RequiresPermission("member.manage")]
public sealed record RemoveMemberCommand(Guid WorkspaceId, Guid UserId) : ICommand<bool>;

/// <summary>Handles <see cref="RemoveMemberCommand"/>.</summary>
public sealed class RemoveMemberCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RemoveMemberCommand, bool>
{
    public async ValueTask<bool> Handle(RemoveMemberCommand c, CancellationToken ct)
    {
        var me = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (me == c.UserId) throw new PreconditionException("IAM_SELF_REMOVE", "You cannot remove yourself from the workspace");
        return await db.DeleteMemberAsync(c.WorkspaceId, c.UserId, ct) > 0
            ? true : throw new PreconditionException("IAM_MEMBER_PROTECTED", "Cannot remove the Owner, or the member does not exist");
    }
}
