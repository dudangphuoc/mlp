using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public static partial class NbSql
{
    [SqlQuery("Nb/ListProfiles.sql")]
    public static partial Task<List<ProfileRow>> ListProfilesAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Nb/GetProfile.sql")]
    public static partial Task<ProfileRow?> GetProfileAsync(this IReadSession db, string name, CancellationToken ct);
    [SqlQuery("Nb/RestartServer.sql")]
    public static partial Task RestartServerAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid jobId, string image, Guid? hostId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/DeleteServer.sql")]
    public static partial Task DeleteServerAsync(this IWriteSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Nb/InsertServer.sql")]
    public static partial Task InsertServerAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid userId, string name, string profile, string image,
        string ide, double cpus, int memoryGb, int gpus, Guid jobId, int idleTimeoutMin, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/GetServer.sql")]
    public static partial Task<ServerRow?> GetServerAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Nb/GetServerById.sql")]
    public static partial Task<ServerRow?> GetServerByIdAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Nb/GetServerByJob.sql")]
    public static partial Task<ServerRow?> GetServerByJobAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Nb/ListServers.sql")]
    public static partial Task<List<ServerRow>> ListServersAsync(this IReadSession db, Guid workspaceId, Guid userId, bool all, CancellationToken ct);
    [SqlQuery("Nb/ListIdleServers.sql")]
    public static partial Task<List<ServerRow>> ListIdleServersAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/CountActiveServers.sql")]
    public static partial Task<long> CountActiveServersAsync(this IReadSession db, Guid workspaceId, Guid userId, CancellationToken ct);
    [SqlQuery("Nb/SetServerState.sql")]
    public static partial Task SetServerStateAsync(this IWriteSession db, Guid id, string state, string? reason, Guid? hostId, string? endpoint,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/ListStoppingServers.sql")]
    public static partial Task<List<StoppingServerRow>> ListStoppingServersAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Nb/ClaimIdleWarnings.sql")]
    public static partial Task<List<IdleWarnRow>> ClaimIdleWarningsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/TouchServer.sql")]
    public static partial Task TouchServerAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
}
