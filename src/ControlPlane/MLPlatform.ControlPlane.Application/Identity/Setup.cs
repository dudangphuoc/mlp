using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- First-time setup (D-27, M01 R7) ----
public sealed record GetSetupStateQuery : IQuery<bool>;

public sealed class GetSetupStateQueryHandler(IReadSession db) : IQueryHandler<GetSetupStateQuery, bool>
{
    public async ValueTask<bool> Handle(GetSetupStateQuery q, CancellationToken ct) =>
        (await db.GetInstanceSettingsAsync(ct))?.BootstrappedAt is null;
}

/// <summary>When the DB has no users: generate a one-time bootstrap token (24 hours) and return the raw token to print to the log.</summary>
public sealed record IssueBootstrapTokenCommand : ICommand<string?>;

public sealed class IssueBootstrapTokenCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<IssueBootstrapTokenCommand, string?>
{
    public async ValueTask<string?> Handle(IssueBootstrapTokenCommand c, CancellationToken ct)
    {
        var s = await db.GetInstanceSettingsAsync(ct);
        if (s?.BootstrappedAt is not null || await db.CountUsersAsync(ct) > 0) return null;
        var token = TokenService.New("mlpb_");
        await db.SetBootstrapTokenAsync(tokens.Hash(token), clock.UtcNow.AddHours(24), ct);
        return token;
    }
}

public sealed record CompleteSetupCommand(string BootstrapToken, string Email, string Password, string Username,
    string DisplayName, string InstanceName, bool AllowSignup) : ICommand<SessionDto>;

public sealed class CompleteSetupCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<CompleteSetupCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(CompleteSetupCommand c, CancellationToken ct)
    {
        var s = await db.GetInstanceSettingsAsync(ct);
        if (s is null || s.BootstrappedAt is not null)
            throw new PreconditionException("IAM_SETUP_DONE", "Instance is already set up");
        if (s.BootstrapTokenHash is null || s.BootstrapExpiresAt < clock.UtcNow
            || s.BootstrapTokenHash != tokens.Hash(c.BootstrapToken))
            throw new UnauthenticatedException("IAM_BOOTSTRAP_INVALID", "Setup token is invalid or has expired");
        IdentityRules.ValidateAccount(c.Email, c.Password, c.Username);
        var email = c.Email.Trim().ToLowerInvariant();
        var uid = await IdentityRules.CreateUserWithPersonalWorkspace(db, clock, email, c.Password, c.Username,
            string.IsNullOrWhiteSpace(c.DisplayName) ? c.Username : c.DisplayName, true, ct);
        await db.CompleteBootstrapAsync(string.IsNullOrWhiteSpace(c.InstanceName) ? "MLPlatform" : c.InstanceName,
            c.AllowSignup, clock.UtcNow, ct);
        var token = TokenService.New("mlp_s_");
        await db.InsertSessionAsync(Ids.New(), uid, tokens.Hash(token), clock.UtcNow, ct);
        return new SessionDto(token, await IdentityRules.LoadMe(db, uid, ct));
    }
}

/// <summary>
/// Admin account declared in config (MLP__Admin__Email/Password/Name — compose `.env`): api ensures it exists on every startup.
/// Instance not set up → complete setup with this account (skipping /setup); set up but the account is missing → create an Instance Admin + personal workspace.
/// If it already exists, leave it unchanged (do not overwrite a password the user changed). Returns true when just created.
/// </summary>
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
