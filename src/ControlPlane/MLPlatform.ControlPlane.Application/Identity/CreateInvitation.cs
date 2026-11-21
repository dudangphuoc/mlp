using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>M01 §5.3: invite to a workspace with a role; one-time `mlpi_` token valid for 7 days (the invitation email
/// is queued in identity.email_outbox.</summary>
public sealed record CreateInvitationCommand(Guid WorkspaceId, string Email, string Role) : ICommand<(string Token, DateTimeOffset ExpiresAt)>;

public sealed class CreateInvitationCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateInvitationCommand, (string Token, DateTimeOffset ExpiresAt)>
{
    public static readonly string[] Roles = ["admin", "editor", "reviewer", "annotator", "viewer"];

    public async ValueTask<(string Token, DateTimeOffset ExpiresAt)> Handle(CreateInvitationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (!Roles.Contains(c.Role)) throw new ValidationException(new Dictionary<string, string> { ["role"] = string.Join(" | ", Roles) });
        if (!c.Email.Contains('@')) throw new ValidationException(new Dictionary<string, string> { ["email"] = "Invalid email" });
        var token = TokenService.New("mlpi_");
        var now = clock.UtcNow;
        var exp = now.AddDays(7);
        var email = c.Email.Trim().ToLowerInvariant();
        await db.InsertInvitationAsync(Ids.New(), c.WorkspaceId, email, c.Role, tokens.Hash(token), uid, now, exp, ct);
        await db.QueueEmailAsync(Ids.New(), email, "Invitation to join MLPlatform",
            $"You have been invited to a workspace on MLPlatform with the role {c.Role}.\n\nOpen {{public_url}}/#/signup?invite={token} to create your account (expires {exp:yyyy-MM-dd}).\n",
            "invitation", now, ct);
        return (token, exp);
    }
}
