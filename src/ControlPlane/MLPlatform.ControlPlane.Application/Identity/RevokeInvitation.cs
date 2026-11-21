using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Revoke a pending invitation.</summary>
[RequiresPermission("member.manage")]
public sealed record RevokeInvitationCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;

/// <summary>Handles <see cref="RevokeInvitationCommand"/>.</summary>
public sealed class RevokeInvitationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RevokeInvitationCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeInvitationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.RevokeInvitationAsync(c.Id, c.WorkspaceId, clock.UtcNow, ct);
        return true;
    }
}
