using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>SQL of the host dataset cache records (ops.data_syncs).</summary>
public static partial class DataSyncSql
{
    [SqlQuery("Ops/InsertDataSync.sql")]
    public static partial Task InsertDataSyncAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid jobId, Guid attemptId, Guid hostId, Guid datasetId,
        int files, int reusedFiles, long reusedBytes, int fetchedFiles, long fetchedBytes, int uncachedFiles, int retries, double seconds,
        long cacheFiles, long cacheBytes, long freeBytes, DateTimeOffset at, Guid? versionId, bool subset, CancellationToken ct);
    [SqlQuery("Ops/SyncedVersionsOnHosts.sql")]
    public static partial Task<List<SyncedVersionRow>> SyncedVersionsOnHostsAsync(this IReadSession db, Guid workspaceId, Guid datasetId, CancellationToken ct);
    [SqlQuery("Ops/ListJobDataSyncs.sql")]
    public static partial Task<List<DataSyncRow>> ListJobDataSyncsAsync(this IReadSession db, Guid workspaceId, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/ListHostDataCaches.sql")]
    public static partial Task<List<DataCacheRow>> ListHostDataCachesAsync(this IReadSession db, Guid workspaceId, Guid? hostId, CancellationToken ct);
    [SqlQuery("Ops/DatasetCacheByHost.sql")]
    public static partial Task<List<HostCacheRow>> DatasetCacheByHostAsync(this IReadSession db, Guid workspaceId, Guid datasetId, CancellationToken ct);
    [SqlQuery("Ops/ListAllDataCaches.sql")]
    public static partial Task<List<JanitorCacheRow>> ListAllDataCachesAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Ops/MarkDataCacheRemoved.sql")]
    public static partial Task MarkDataCacheRemovedAsync(this IWriteSession db, Guid workspaceId, Guid hostId, Guid datasetId, DateTimeOffset now, CancellationToken ct);
}
