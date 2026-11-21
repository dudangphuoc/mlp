using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Identity;

public enum UserStatus { Active, Disabled }

public sealed record UserRow(Guid Id, string Email, string Username, string DisplayName, string PasswordHash,
    UserStatus Status, bool IsInstanceAdmin);

public sealed record MembershipRow(Guid Id, string Slug, string Name, string Kind, DateTimeOffset CreatedAt, string Role);

public sealed record InstanceSettingsRow(string Name, bool AllowSignup, string? BootstrapTokenHash,
    DateTimeOffset? BootstrapExpiresAt, DateTimeOffset? BootstrappedAt);

/// <summary>Display name of a user, for modules that only hold the user id.</summary>
public sealed record UserNameRow(Guid Id, string DisplayName);

/// <summary>Queries for the <c>identity</c> schema — generated from Sql/Identity/*.sql.</summary>
public static partial class IdentitySql
{
    [SqlQuery("Identity/UserNames.sql")]
    public static partial Task<List<UserNameRow>> UserNamesAsync(this IReadSession db, Guid[] ids, CancellationToken ct);
    [SqlQuery("Identity/InsertUser.sql")]
    public static partial Task InsertUserAsync(this IWriteSession db, Guid id, string email, string username,
        string displayName, string passwordHash, bool isInstanceAdmin, DateTimeOffset createdAt, CancellationToken ct);

    [SqlQuery("Identity/GetUserByEmail.sql")]
    public static partial Task<UserRow?> GetUserByEmailAsync(this IReadSession db, string email, CancellationToken ct);

    [SqlQuery("Identity/GetUserById.sql")]
    public static partial Task<UserRow?> GetUserByIdAsync(this IReadSession db, Guid id, CancellationToken ct);

    [SqlQuery("Identity/CountUsers.sql")]
    public static partial Task<long> CountUsersAsync(this IReadSession db, CancellationToken ct);

    [SqlQuery("Identity/InsertSession.sql")]
    public static partial Task InsertSessionAsync(this IWriteSession db, Guid id, Guid userId, string tokenHash,
        DateTimeOffset createdAt, CancellationToken ct);

    [SqlQuery("Identity/GetSessionUser.sql")]
    public static partial Task<Guid?> GetSessionUserAsync(this IReadSession db, string tokenHash, CancellationToken ct);

    [SqlQuery("Identity/RevokeSession.sql")]
    public static partial Task RevokeSessionAsync(this IWriteSession db, string tokenHash, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Identity/InsertWorkspace.sql")]
    public static partial Task InsertWorkspaceAsync(this IWriteSession db, Guid id, string slug, string name, string kind,
        Guid ownerId, DateTimeOffset createdAt, CancellationToken ct);

    [SqlQuery("Identity/InsertMembership.sql")]
    public static partial Task InsertMembershipAsync(this IWriteSession db, Guid workspaceId, Guid userId, string role,
        DateTimeOffset joinedAt, CancellationToken ct);

    [SqlQuery("Identity/ListMemberships.sql")]
    public static partial Task<List<MembershipRow>> ListMembershipsAsync(this IReadSession db, Guid userId, CancellationToken ct);

    [SqlQuery("Identity/SlugExists.sql")]
    public static partial Task<bool> SlugExistsAsync(this IReadSession db, string slug, CancellationToken ct);

    [SqlQuery("Identity/GetInstanceSettings.sql")]
    public static partial Task<InstanceSettingsRow?> GetInstanceSettingsAsync(this IReadSession db, CancellationToken ct);

    [SqlQuery("Identity/SetBootstrapToken.sql")]
    public static partial Task SetBootstrapTokenAsync(this IWriteSession db, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct);

    [SqlQuery("Identity/CompleteBootstrap.sql")]
    public static partial Task CompleteBootstrapAsync(this IWriteSession db, string name, bool allowSignup,
        DateTimeOffset now, CancellationToken ct);

}
