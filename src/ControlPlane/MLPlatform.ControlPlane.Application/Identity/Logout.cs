using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- Logout ----
[Access(AccessKind.Authenticated)]
public sealed record LogoutCommand : ICommand<bool>;

public sealed class LogoutCommandHandler(IWriteSession db, IClock clock, ICurrentUser user) : ICommandHandler<LogoutCommand, bool>
{
    public async ValueTask<bool> Handle(LogoutCommand c, CancellationToken ct)
    {
        if (user.SessionTokenHash is { } h) await db.RevokeSessionAsync(h, clock.UtcNow, ct);
        return true;
    }
}
