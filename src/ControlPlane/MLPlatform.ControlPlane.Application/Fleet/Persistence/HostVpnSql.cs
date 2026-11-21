using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>VPN status queries.</summary>
public static partial class HostVpnSql
{
    [SqlQuery("Fleet/SetHostVpn.sql")]
    public static partial Task SetHostVpnAsync(this IWriteSession db, Guid id, string status, string? message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/ListHostVpn.sql")]
    public static partial Task<List<HostVpnRow>> ListHostVpnAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}
