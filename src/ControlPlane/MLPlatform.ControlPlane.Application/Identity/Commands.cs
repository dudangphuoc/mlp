using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record MeDto(Guid UserId, string Email, string Username, string DisplayName, bool IsInstanceAdmin,
    IReadOnlyList<MembershipRow> Workspaces);

public sealed record SessionDto(string Token, MeDto Me);

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

// ---- Logout ----
public sealed record LogoutCommand : ICommand<bool>;

public sealed class LogoutCommandHandler(IWriteSession db, IClock clock, ICurrentUser user) : ICommandHandler<LogoutCommand, bool>
{
    public async ValueTask<bool> Handle(LogoutCommand c, CancellationToken ct)
    {
        if (user.SessionTokenHash is { } h) await db.RevokeSessionAsync(h, clock.UtcNow, ct);
        return true;
    }
}

// ---- GetMe ----
public sealed record GetMeQuery : IQuery<MeDto>;

public sealed class GetMeQueryHandler(IReadSession db, ICurrentUser user) : IQueryHandler<GetMeQuery, MeDto>
{
    public async ValueTask<MeDto> Handle(GetMeQuery q, CancellationToken ct) =>
        await IdentityRules.LoadMe(db, user.RequireUser(), ct);
}

// ---- Workspace ----
public sealed record CreateWorkspaceCommand(string Slug, string Name) : ICommand<MembershipRow>;

public sealed class CreateWorkspaceCommandHandler(IWriteSession db, IClock clock, ICurrentUser user)
    : ICommandHandler<CreateWorkspaceCommand, MembershipRow>
{
    public async ValueTask<MembershipRow> Handle(CreateWorkspaceCommand c, CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        if (await db.SlugExistsAsync(c.Slug, ct))
            throw new ConflictException("IAM_SLUG_TAKEN", "URL is already in use");
        var now = clock.UtcNow;
        var id = Ids.New();
        await db.InsertWorkspaceAsync(id, c.Slug, c.Name, "team", uid, now, ct);
        await db.InsertMembershipAsync(id, uid, "owner", now, ct);
        return new MembershipRow(id, c.Slug, c.Name, "team", now, "owner");
    }
}

public sealed record ListWorkspacesQuery : IQuery<IReadOnlyList<MembershipRow>>;

public sealed class ListWorkspacesQueryHandler(IReadSession db, ICurrentUser user)
    : IQueryHandler<ListWorkspacesQuery, IReadOnlyList<MembershipRow>>
{
    public async ValueTask<IReadOnlyList<MembershipRow>> Handle(ListWorkspacesQuery q, CancellationToken ct) =>
        await db.ListMembershipsAsync(user.RequireUser(), ct);
}
