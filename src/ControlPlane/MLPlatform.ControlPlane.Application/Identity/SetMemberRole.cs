using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-3: change a member's role (not the Owner; not yourself).</summary>
public sealed record SetMemberRoleCommand(Guid WorkspaceId, Guid UserId, string Role) : ICommand<bool>;

/// <summary>Handles <see cref="SetMemberRoleCommand"/>.</summary>
public sealed class SetMemberRoleCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<SetMemberRoleCommand, bool>
{
    public async ValueTask<bool> Handle(SetMemberRoleCommand c, CancellationToken ct)
    {
        var me = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (!CreateInvitationCommandHandler.Roles.Contains(c.Role)) throw new ValidationException(new Dictionary<string, string> { ["role"] = string.Join(" | ", CreateInvitationCommandHandler.Roles) });
        if (me == c.UserId) throw new PreconditionException("IAM_SELF_ROLE", "You cannot change your own role");
        return await db.SetMemberRoleAsync(c.WorkspaceId, c.UserId, c.Role, ct) > 0
            ? true : throw new PreconditionException("IAM_MEMBER_PROTECTED", "Cannot change the Owner's role, or the member does not exist");
    }
}
