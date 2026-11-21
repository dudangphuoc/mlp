using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

internal static class InvitationRules
{
    /// <summary>Checks the invitation before creating the account (token used/expired/wrong email → IAM_INVITATION_INVALID).</summary>
    public static async Task<InvitationRow> ValidateAsync(IWriteSession db, IClock clock, TokenService tokens, string token, string email, CancellationToken ct)
    {
        var inv = await db.GetInvitationAsync(tokens.Hash(token), ct);
        if (inv is null || inv.AcceptedAt is not null || inv.ExpiresAt < clock.UtcNow || inv.Email != email)
            throw new PreconditionException("IAM_INVITATION_INVALID", "Invitation is invalid or has expired");
        return inv;
    }

    public static async Task AcceptAsync(IWriteSession db, IClock clock, InvitationRow inv, Guid userId, CancellationToken ct)
    {
        await db.InsertMembershipAsync(inv.WorkspaceId, userId, inv.Role, clock.UtcNow, ct);
        await db.AcceptInvitationAsync(inv.Id, clock.UtcNow, ct);
    }
}
