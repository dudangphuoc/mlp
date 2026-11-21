using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- Register (M01 §5.1) ----
public sealed record RegisterCommand(string Email, string Password, string Username, string DisplayName, string InvitationToken = "") : ICommand<Guid>;

public sealed class RegisterCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<RegisterCommand, Guid>
{
    public async ValueTask<Guid> Handle(RegisterCommand c, CancellationToken ct)
    {
        IdentityRules.ValidateAccount(c.Email, c.Password, c.Username);
        var invited = !string.IsNullOrEmpty(c.InvitationToken);
        var settings = await db.GetInstanceSettingsAsync(ct);
        if (!invited && (settings is null || !settings.AllowSignup))
            throw new ForbiddenException("IAM_SIGNUP_DISABLED", "Sign-up is by invitation only");
        var email = c.Email.Trim().ToLowerInvariant();
        var inv = invited ? await InvitationRules.ValidateAsync(db, clock, tokens, c.InvitationToken, email, ct) : null;
        var id = await IdentityRules.CreateUserWithPersonalWorkspace(db, clock, email,
            c.Password, c.Username, string.IsNullOrWhiteSpace(c.DisplayName) ? c.Username : c.DisplayName, false, ct);
        if (inv is not null) await InvitationRules.AcceptAsync(db, clock, inv, id, ct);
        return id;
    }
}
