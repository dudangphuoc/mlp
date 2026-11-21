using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>My API keys (token not included — shown only once at creation).</summary>
public sealed record ApiKeyRow(Guid Id, string? Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
/// <summary>Invitation in a workspace.</summary>
public sealed record InvitationListRow(Guid Id, string Email, string Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);

/// <summary>Settings page queries (S-01-2..5).</summary>
public static partial class SettingsSql
{
    [SqlQuery("Identity/UpdateDisplayName.sql")] public static partial Task UpdateDisplayNameAsync(this IWriteSession db, Guid id, string displayName, CancellationToken ct);
    [SqlQuery("Identity/UpdatePassword.sql")] public static partial Task UpdatePasswordAsync(this IWriteSession db, Guid id, string passwordHash, CancellationToken ct);
    [SqlQuery("Identity/RevokeOtherSessions.sql")] public static partial Task RevokeOtherSessionsAsync(this IWriteSession db, Guid userId, string keepHash, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/InsertApiKey.sql")] public static partial Task InsertApiKeyAsync(this IWriteSession db, Guid id, Guid userId, string tokenHash, string name, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/ListApiKeys.sql")] public static partial Task<List<ApiKeyRow>> ListApiKeysAsync(this IReadSession db, Guid userId, CancellationToken ct);
    [SqlQuery("Identity/RevokeApiKey.sql")] public static partial Task<long> RevokeApiKeyAsync(this IWriteSession db, Guid id, Guid userId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/RenameWorkspace.sql")] public static partial Task RenameWorkspaceAsync(this IWriteSession db, Guid workspaceId, string name, CancellationToken ct);
    [SqlQuery("Identity/SetMemberRole.sql")] public static partial Task<long> SetMemberRoleAsync(this IWriteSession db, Guid workspaceId, Guid userId, string role, CancellationToken ct);
    [SqlQuery("Identity/DeleteMember.sql")] public static partial Task<long> DeleteMemberAsync(this IWriteSession db, Guid workspaceId, Guid userId, CancellationToken ct);
    [SqlQuery("Identity/ListInvitations.sql")] public static partial Task<List<InvitationListRow>> ListInvitationsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Identity/RevokeInvitation.sql")] public static partial Task RevokeInvitationAsync(this IWriteSession db, Guid id, Guid workspaceId, DateTimeOffset now, CancellationToken ct);
}

/// <summary>S-01-2: change display name.</summary>
public sealed record UpdateProfileCommand(string DisplayName) : ICommand<MeDto>;
/// <summary>Handles <see cref="UpdateProfileCommand"/>.</summary>
public sealed class UpdateProfileCommandHandler(IWriteSession db, ICurrentUser user) : ICommandHandler<UpdateProfileCommand, MeDto>
{
    public async ValueTask<MeDto> Handle(UpdateProfileCommand c, CancellationToken ct)
    {
        var name = c.DisplayName.Trim();
        if (name.Length is < 1 or > 80) throw new ValidationException(new Dictionary<string, string> { ["display_name"] = "Must be 1–80 characters" });
        var uid = user.RequireUser();
        await db.UpdateDisplayNameAsync(uid, name, ct);
        return await IdentityRules.LoadMe(db, uid, ct);
    }
}

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

/// <summary>S-01-4: my API keys.</summary>
public sealed record ListApiKeysQuery : IQuery<IReadOnlyList<ApiKeyRow>>;
/// <summary>Handles <see cref="ListApiKeysQuery"/>.</summary>
public sealed class ListApiKeysQueryHandler(IReadSession db, ICurrentUser user) : IQueryHandler<ListApiKeysQuery, IReadOnlyList<ApiKeyRow>>
{
    public async ValueTask<IReadOnlyList<ApiKeyRow>> Handle(ListApiKeysQuery q, CancellationToken ct) => await db.ListApiKeysAsync(user.RequireUser(), ct);
}

/// <summary>Create an API key (the mlpk_ token is shown once; same permissions as the creator).</summary>
public sealed record CreateApiKeyCommand(string Name) : ICommand<(Guid Id, string Token)>;
/// <summary>Handles <see cref="CreateApiKeyCommand"/>.</summary>
public sealed class CreateApiKeyCommandHandler(IWriteSession db, IClock clock, ICurrentUser user, TokenService tokens) : ICommandHandler<CreateApiKeyCommand, (Guid Id, string Token)>
{
    public async ValueTask<(Guid Id, string Token)> Handle(CreateApiKeyCommand c, CancellationToken ct)
    {
        var name = c.Name.Trim();
        if (name.Length is < 1 or > 60) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Must be 1–60 characters" });
        var token = TokenService.New("mlpk_");
        var id = Ids.New();
        await db.InsertApiKeyAsync(id, user.RequireUser(), tokens.Hash(token), name, clock.UtcNow, ct);
        return (id, token);
    }
}

/// <summary>Revoke one of my API keys.</summary>
public sealed record RevokeApiKeyCommand(Guid Id) : ICommand<bool>;
/// <summary>Handles <see cref="RevokeApiKeyCommand"/>.</summary>
public sealed class RevokeApiKeyCommandHandler(IWriteSession db, IClock clock, ICurrentUser user) : ICommandHandler<RevokeApiKeyCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeApiKeyCommand c, CancellationToken ct) =>
        await db.RevokeApiKeyAsync(c.Id, user.RequireUser(), clock.UtcNow, ct) > 0
            ? true : throw new NotFoundException("IAM_API_KEY_NOT_FOUND", "API key not found");
}

/// <summary>S-01-5: rename the workspace (Owner).</summary>
public sealed record RenameWorkspaceCommand(Guid WorkspaceId, string Name) : ICommand<bool>;
/// <summary>Handles <see cref="RenameWorkspaceCommand"/>.</summary>
public sealed class RenameWorkspaceCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RenameWorkspaceCommand, bool>
{
    public async ValueTask<bool> Handle(RenameWorkspaceCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "workspace.manage", ct);
        var name = c.Name.Trim();
        if (name.Length is < 1 or > 80) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Must be 1–80 characters" });
        await db.RenameWorkspaceAsync(c.WorkspaceId, name, ct);
        return true;
    }
}

/// <summary>S-01-3: change a member's role (not the Owner; not yourself).</summary>
public sealed record SetMemberRoleCommand(Guid WorkspaceId, Guid UserId, string Role) : ICommand<bool>;
/// <summary>Handles <see cref="SetMemberRoleCommand"/>.</summary>
public sealed class SetMemberRoleCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<SetMemberRoleCommand, bool>
{
    public async ValueTask<bool> Handle(SetMemberRoleCommand c, CancellationToken ct)
    {
        var me = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (!CreateInvitationCommandHandler.Roles.Contains(c.Role)) throw new ValidationException(new Dictionary<string, string> { ["role"] = string.Join(" | ", CreateInvitationCommandHandler.Roles) });
        if (me == c.UserId) throw new PreconditionException("IAM_SELF_ROLE", "You cannot change your own role");
        return await db.SetMemberRoleAsync(c.WorkspaceId, c.UserId, c.Role, ct) > 0
            ? true : throw new PreconditionException("IAM_MEMBER_PROTECTED", "Cannot change the Owner's role, or the member does not exist");
    }
}

/// <summary>S-01-3: remove a member (not the Owner, not yourself).</summary>
public sealed record RemoveMemberCommand(Guid WorkspaceId, Guid UserId) : ICommand<bool>;
/// <summary>Handles <see cref="RemoveMemberCommand"/>.</summary>
public sealed class RemoveMemberCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RemoveMemberCommand, bool>
{
    public async ValueTask<bool> Handle(RemoveMemberCommand c, CancellationToken ct)
    {
        var me = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (me == c.UserId) throw new PreconditionException("IAM_SELF_REMOVE", "You cannot remove yourself from the workspace");
        return await db.DeleteMemberAsync(c.WorkspaceId, c.UserId, ct) > 0
            ? true : throw new PreconditionException("IAM_MEMBER_PROTECTED", "Cannot remove the Owner, or the member does not exist");
    }
}

/// <summary>Invitations of the workspace.</summary>
public sealed record ListInvitationsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<InvitationListRow>>;
/// <summary>Handles <see cref="ListInvitationsQuery"/>.</summary>
public sealed class ListInvitationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationListRow>>
{
    public async ValueTask<IReadOnlyList<InvitationListRow>> Handle(ListInvitationsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "member.manage", ct);
        return await db.ListInvitationsAsync(q.WorkspaceId, ct);
    }
}

/// <summary>Revoke a pending invitation.</summary>
public sealed record RevokeInvitationCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;
/// <summary>Handles <see cref="RevokeInvitationCommand"/>.</summary>
public sealed class RevokeInvitationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RevokeInvitationCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeInvitationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.RevokeInvitationAsync(c.Id, c.WorkspaceId, clock.UtcNow, ct);
        return true;
    }
}
