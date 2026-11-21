using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

public static partial class DataSql
{
    [SqlQuery("Data/VersionSplits.sql")]
    public static partial Task<List<VersionSplitRow>> VersionSplitsAsync(this IReadSession db, Guid[] ids, CancellationToken ct);
    [SqlQuery("Data/CopyVersionFilesAsReferences.sql")]
    public static partial Task CopyVersionFilesAsReferencesAsync(this IWriteSession db, Guid versionId, Guid sourceVersionId, string baseUrl, string skipPath, CancellationToken ct);
    [SqlQuery("Data/SetVersionSplit.sql")]
    public static partial Task SetVersionSplitAsync(this IWriteSession db, Guid id, string splitState, string splitCounts, string? valSha256, CancellationToken ct);
    [SqlQuery("Data/VersionNames.sql")]
    public static partial Task<List<VersionNameRow>> VersionNamesAsync(this IReadSession db, Guid workspaceId, Guid[] ids, CancellationToken ct);
    [SqlQuery("Data/InsertDataset.sql")]
    public static partial Task InsertDatasetAsync(this IWriteSession db, Guid id, Guid workspaceId, string slug, string name, string task,
        Guid ownerId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Data/ListDatasets.sql")]
    public static partial Task<List<DatasetRow>> ListDatasetsAsync(this IReadSession db, Guid workspaceId, string task, CancellationToken ct);
    [SqlQuery("Data/GetDataset.sql")]
    public static partial Task<DatasetRow?> GetDatasetAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Data/UpsertFile.sql")]
    public static partial Task UpsertFileAsync(this IWriteSession db, Guid datasetId, string path, long sizeBytes, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Data/ListFiles.sql")]
    public static partial Task<List<FileRow>> ListFilesAsync(this IReadSession db, Guid datasetId, CancellationToken ct);
    [SqlQuery("Data/InsertVersion.sql")]
    public static partial Task InsertVersionAsync(this IWriteSession db, Guid id, Guid datasetId, int number, string prefix, long fileCount,
        long sizeBytes, string note, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Data/InsertReferenceVersion.sql")]
    public static partial Task InsertReferenceVersionAsync(this IWriteSession db, Guid id, Guid datasetId, int number, long fileCount, long sizeBytes, string note,
        Guid createdBy, DateTimeOffset now, string sourceRef, CancellationToken ct);
    [SqlQuery("Data/InsertVersionFileUrl.sql")]
    public static partial Task InsertVersionFileUrlAsync(this IWriteSession db, Guid versionId, string path, long sizeBytes, string url, CancellationToken ct);
    [SqlQuery("Data/InsertVersionFile.sql")]
    public static partial Task InsertVersionFileAsync(this IWriteSession db, Guid versionId, string path, long sizeBytes, CancellationToken ct);
    [SqlQuery("Data/NextVersionNumber.sql")]
    public static partial Task<int> NextVersionNumberAsync(this IWriteSession db, Guid datasetId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Data/ListVersions.sql")]
    public static partial Task<List<VersionRow>> ListVersionsAsync(this IReadSession db, Guid datasetId, CancellationToken ct);
    [SqlQuery("Data/GetVersion.sql")]
    public static partial Task<VersionRow?> GetVersionAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Data/DeleteWorkingFile.sql")]
    public static partial Task<long> DeleteWorkingFileAsync(this IWriteSession db, Guid datasetId, string path, CancellationToken ct);
    [SqlQuery("Data/InsertPendingRemoval.sql")]
    public static partial Task InsertPendingRemovalAsync(this IWriteSession db, Guid datasetId, string path, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Data/DeletePendingRemoval.sql")]
    public static partial Task DeletePendingRemovalAsync(this IWriteSession db, Guid datasetId, string path, CancellationToken ct);
    [SqlQuery("Data/ListPendingRemovals.sql")]
    public static partial Task<List<PendingPathRow>> ListPendingRemovalsAsync(this IReadSession db, Guid datasetId, CancellationToken ct);
    [SqlQuery("Data/ClearPendingChanges.sql")]
    public static partial Task ClearPendingChangesAsync(this IWriteSession db, Guid datasetId, CancellationToken ct);
    [SqlQuery("Data/LatestVersion.sql")]
    public static partial Task<VersionRow?> LatestVersionAsync(this IReadSession db, Guid datasetId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Data/InsertVersionFilesFromBase.sql")]
    public static partial Task InsertVersionFilesFromBaseAsync(this IWriteSession db, Guid versionId, Guid baseVersionId, string baseUrl, string[] skipPaths, CancellationToken ct);
    [SqlQuery("Data/SetVersionTotals.sql")]
    public static partial Task SetVersionTotalsAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Data/VersionFilesReferenced.sql")]
    public static partial Task<long> VersionFilesReferencedAsync(this IReadSession db, Guid versionId, string prefixUrl, CancellationToken ct);
    [SqlQuery("Data/DeleteVersion.sql")]
    public static partial Task DeleteVersionAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Data/SetDatasetStatus.sql")]
    public static partial Task SetDatasetStatusAsync(this IWriteSession db, Guid id, Guid workspaceId, string status, CancellationToken ct);
    [SqlQuery("Data/ListVersionFiles.sql")]
    public static partial Task<List<VersionFileRow>> ListVersionFilesAsync(this IReadSession db, Guid versionId, CancellationToken ct);
}
