using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Admin account declared in config (MLP__Admin__Email/Password/Name — compose `.env`): api ensures it exists
/// on every startup.</summary>
public sealed record EnsureConfiguredAdminCommand(string Email, string Password, string DisplayName) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="EnsureConfiguredAdminCommand"/>.</summary>
public sealed class EnsureConfiguredAdminCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<EnsureConfiguredAdminCommand, bool>
{
    public async ValueTask<bool> Handle(EnsureConfiguredAdminCommand c, CancellationToken ct)
    {
        var email = c.Email.Trim().ToLowerInvariant();
        if (await db.GetUserByEmailAsync(email, ct) is not null) return false;
        var username = new string(email.Split('@')[0].Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').ToArray()) is { Length: >= 3 } u ? u : "admin";
        IdentityRules.ValidateAccount(email, c.Password, username);
        await IdentityRules.CreateUserWithPersonalWorkspace(db, clock, email, c.Password, username,
            string.IsNullOrWhiteSpace(c.DisplayName) ? username : c.DisplayName, true, ct);
        if (await db.GetInstanceSettingsAsync(ct) is { BootstrappedAt: null })
            await db.CompleteBootstrapAsync("MLPlatform", false, clock.UtcNow, ct);
        return true;
    }
}
