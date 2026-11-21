using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-2: change password (checks the current password), signs out other sessions.</summary>
public sealed record ChangePasswordCommand(string Current, string New) : ICommand<bool>;

/// <summary>Handles <see cref="ChangePasswordCommand"/>.</summary>
public sealed class ChangePasswordCommandHandler(IWriteSession db, IClock clock, ICurrentUser user) : ICommandHandler<ChangePasswordCommand, bool>
{
    public async ValueTask<bool> Handle(ChangePasswordCommand c, CancellationToken ct)
    {
        var uid = user.RequireUser();
        var u = await db.GetUserByIdAsync(uid, ct) ?? throw new UnauthenticatedException("IAM_UNAUTHENTICATED", "Not signed in");
        if (!PasswordHasher.Verify(c.Current, u.PasswordHash))
            throw new ValidationException(new Dictionary<string, string> { ["current"] = "Current password is incorrect" });
        if (c.New.Length < 12) throw new ValidationException(new Dictionary<string, string> { ["new"] = "Must be at least 12 characters" });
        await db.UpdatePasswordAsync(uid, PasswordHasher.Hash(c.New), ct);
        await db.RevokeOtherSessionsAsync(uid, user.SessionTokenHash ?? "", clock.UtcNow, ct);
        return true;
    }
}
