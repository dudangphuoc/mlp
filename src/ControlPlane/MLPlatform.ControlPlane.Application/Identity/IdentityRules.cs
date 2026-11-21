using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

internal static partial class IdentityRules
{
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$")]
    public static partial Regex Slug();

    /// <summary>Turns a free-form name into a slug fragment (lowercase a-z0-9 and single hyphens, at most <paramref name="max"/> chars).</summary>
    public static string SlugPart(string name, int max)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark
                     && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var r = sb.ToString().Trim('-');
        if (r.Length > max) r = r[..max].TrimEnd('-');
        return r.Length == 0 ? "model" : r;
    }

    public static void ValidateAccount(string email, string password, string username)
    {
        var errors = new Dictionary<string, string>();
        if (!email.Contains('@') || email.Length > 320) errors["email"] = "Invalid email";
        if (password.Length < 10) errors["password"] = "Password must be at least 10 characters";
        if (!Slug().IsMatch(username)) errors["username"] = "Lowercase letters, numbers and hyphens only";
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    public static async Task<Guid> CreateUserWithPersonalWorkspace(IWriteSession db, IClock clock, string email,
        string password, string username, string displayName, bool instanceAdmin, CancellationToken ct)
    {
        if (await db.GetUserByEmailAsync(email, ct) is not null)
            throw new ConflictException("IAM_EMAIL_TAKEN", "Email is already in use");
        if (await db.SlugExistsAsync(username, ct))
            throw new ConflictException("IAM_USERNAME_TAKEN", "Username is already in use");
        var now = clock.UtcNow;
        var userId = Ids.New();
        await db.InsertUserAsync(userId, email, username, displayName, PasswordHasher.Hash(password), instanceAdmin, now, ct);
        var wsId = Ids.New();
        await db.InsertWorkspaceAsync(wsId, username, displayName, "personal", userId, now, ct);
        await db.InsertMembershipAsync(wsId, userId, "owner", now, ct);
        return userId;
    }

    public static async Task<MeDto> LoadMe(IReadSession db, Guid userId, CancellationToken ct)
    {
        var u = await db.GetUserByIdAsync(userId, ct)
                ?? throw new UnauthenticatedException("IAM_UNAUTHENTICATED", "Invalid session");
        var ws = await db.ListMembershipsAsync(userId, ct);
        return new MeDto(u.Id, u.Email, u.Username, u.DisplayName, u.IsInstanceAdmin, ws);
    }
}
