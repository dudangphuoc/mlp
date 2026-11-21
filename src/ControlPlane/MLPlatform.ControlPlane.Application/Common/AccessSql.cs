using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Workspace membership lookups every module needs for authorization.</summary>
public static partial class AccessSql
{
    [SqlQuery("Identity/GetMemberRole.sql")]
    public static partial Task<string?> GetMemberRoleAsync(this IReadSession db, Guid workspaceId, Guid userId, CancellationToken ct);
}
