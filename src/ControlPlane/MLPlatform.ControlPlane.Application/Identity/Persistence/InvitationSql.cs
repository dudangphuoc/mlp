using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public static partial class InvitationSql
{
    [SqlQuery("Identity/InsertInvitation.sql")]
    public static partial Task InsertInvitationAsync(this IWriteSession db, Guid id, Guid workspaceId, string email, string role, string tokenHash,
        Guid createdBy, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Identity/GetInvitation.sql")]
    public static partial Task<InvitationRow?> GetInvitationAsync(this IWriteSession db, string tokenHash, CancellationToken ct);
    [SqlQuery("Identity/AcceptInvitation.sql")]
    public static partial Task AcceptInvitationAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/ListMembers.sql")]
    public static partial Task<List<MemberRow>> ListMembersAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}
