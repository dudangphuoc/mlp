using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

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
