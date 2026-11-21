using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- Login ----
public sealed record LoginCommand(string Email, string Password) : ICommand<SessionDto>;

public sealed class LoginCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<LoginCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(LoginCommand c, CancellationToken ct)
    {
        var u = await db.GetUserByEmailAsync(c.Email.Trim().ToLowerInvariant(), ct);
        if (u is null || u.Status != UserStatus.Active || !PasswordHasher.Verify(c.Password, u.PasswordHash))
            throw new UnauthenticatedException("IAM_INVALID_CREDENTIALS", "Incorrect email or password");
        var token = TokenService.New("mlp_s_");
        await db.InsertSessionAsync(Ids.New(), u.Id, tokens.Hash(token), clock.UtcNow, ct);
        return new SessionDto(token, await IdentityRules.LoadMe(db, u.Id, ct));
    }
}
