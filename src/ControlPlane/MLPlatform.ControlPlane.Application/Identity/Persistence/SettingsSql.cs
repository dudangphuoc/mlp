using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Settings page queries (S-01-2..5).</summary>
public static partial class SettingsSql
{
    [SqlQuery("Identity/UpdateDisplayName.sql")] public static partial Task UpdateDisplayNameAsync(this IWriteSession db, Guid id, string displayName, CancellationToken ct);
    [SqlQuery("Identity/UpdatePassword.sql")] public static partial Task UpdatePasswordAsync(this IWriteSession db, Guid id, string passwordHash, CancellationToken ct);
    [SqlQuery("Identity/RevokeOtherSessions.sql")] public static partial Task RevokeOtherSessionsAsync(this IWriteSession db, Guid userId, string keepHash, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/InsertApiKey.sql")] public static partial Task InsertApiKeyAsync(this IWriteSession db, Guid id, Guid userId, string tokenHash, string name, DateTimeOffset now, Guid? workspaceId, CancellationToken ct);
    [SqlQuery("Identity/ListApiKeys.sql")] public static partial Task<List<ApiKeyRow>> ListApiKeysAsync(this IReadSession db, Guid userId, CancellationToken ct);
    [SqlQuery("Identity/RevokeApiKey.sql")] public static partial Task<long> RevokeApiKeyAsync(this IWriteSession db, Guid id, Guid userId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/RenameWorkspace.sql")] public static partial Task RenameWorkspaceAsync(this IWriteSession db, Guid workspaceId, string name, CancellationToken ct);
    [SqlQuery("Identity/SetMemberRole.sql")] public static partial Task<long> SetMemberRoleAsync(this IWriteSession db, Guid workspaceId, Guid userId, string role, CancellationToken ct);
    [SqlQuery("Identity/DeleteMember.sql")] public static partial Task<long> DeleteMemberAsync(this IWriteSession db, Guid workspaceId, Guid userId, CancellationToken ct);
    [SqlQuery("Identity/ListInvitations.sql")] public static partial Task<List<InvitationListRow>> ListInvitationsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Identity/RevokeInvitation.sql")] public static partial Task RevokeInvitationAsync(this IWriteSession db, Guid id, Guid workspaceId, DateTimeOffset now, CancellationToken ct);
}
