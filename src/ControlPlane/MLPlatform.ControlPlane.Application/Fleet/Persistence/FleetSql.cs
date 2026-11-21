using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

public static partial class FleetSql
{
    [SqlQuery("Fleet/InsertEnrollment.sql")]
    public static partial Task InsertEnrollmentAsync(this IWriteSession db, Guid id, Guid workspaceId, string tokenHash,
        string os, string dataPath, string modelsPath, Guid createdBy, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        CancellationToken ct);

    [SqlQuery("Fleet/GetEnrollmentByToken.sql")]
    public static partial Task<EnrollmentRow?> GetEnrollmentByTokenAsync(this IWriteSession db, string tokenHash, CancellationToken ct);

    [SqlQuery("Fleet/UseEnrollment.sql")]
    public static partial Task UseEnrollmentAsync(this IWriteSession db, Guid id, Guid hostId, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/SetHostWgKey.sql")]
    public static partial Task SetHostWgKeyAsync(this IWriteSession db, Guid id, string wgPublicKey, CancellationToken ct);
    [SqlQuery("Fleet/ListOverlayPeers.sql")]
    public static partial Task<List<OverlayPeerRow>> ListOverlayPeersAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Fleet/NextOverlaySeq.sql")]
    public static partial Task<long> NextOverlaySeqAsync(this IWriteSession db, CancellationToken ct);

    [SqlQuery("Fleet/InsertHost.sql")]
    public static partial Task InsertHostAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string overlayIp,
        string? wgPublicKey, string credentialHash, string agentVersion, string dataPath, string modelsPath,
        byte[]? hostInfo, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/ReEnrollHost.sql")]
    public static partial Task ReEnrollHostAsync(this IWriteSession db, Guid id, string credentialHash, string? wgPublicKey,
        string agentVersion, byte[]? hostInfo, string dataPath, string modelsPath, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/GetHostByCredential.sql")]
    public static partial Task<HostRow?> GetHostByCredentialAsync(this IReadSession db, string credentialHash, CancellationToken ct);

    [SqlQuery("Fleet/GetHost.sql")]
    public static partial Task<HostRow?> GetHostAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);

    [SqlQuery("Fleet/ListHosts.sql")]
    public static partial Task<List<HostRow>> ListHostsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);

    [SqlQuery("Fleet/SetHostOnline.sql")]
    public static partial Task SetHostOnlineAsync(this IWriteSession db, Guid id, string state, string agentVersion,
        byte[]? hostInfo, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/SetHostState.sql")]
    public static partial Task SetHostStateAsync(this IWriteSession db, Guid id, string state, string? reason,
        DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/DisconnectHost.sql")]
    public static partial Task DisconnectHostAsync(this IWriteSession db, Guid id, Guid workspaceId, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Fleet/ListOnlineHosts.sql")]
    public static partial Task<List<HostRow>> ListOnlineHostsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}
