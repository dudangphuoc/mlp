using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public static partial class BrowseSql
{
    [SqlQuery("Data/BrowseFiles.sql")]
    public static partial Task<List<BrowseFileRow>> BrowseFilesAsync(this IReadSession db, Guid? versionId, Guid datasetId, string prefixLike, int prefixLen,
        bool recursive, string qLike, bool useExts, string[] exts, int offset, int limit, CancellationToken ct);
    [SqlQuery("Data/CountBrowseFiles.sql")]
    public static partial Task<long> CountBrowseFilesAsync(this IReadSession db, Guid? versionId, Guid datasetId, string prefixLike, int prefixLen,
        bool recursive, string qLike, bool useExts, string[] exts, CancellationToken ct);
    [SqlQuery("Data/BrowseFolders.sql")]
    public static partial Task<List<BrowseFolderRow>> BrowseFoldersAsync(this IReadSession db, Guid? versionId, Guid datasetId, string prefixLike, int prefixLen, CancellationToken ct);
    [SqlQuery("Data/FileExtensionStats.sql")]
    public static partial Task<List<ExtensionStatRow>> FileExtensionStatsAsync(this IReadSession db, Guid? versionId, Guid datasetId, CancellationToken ct);
    [SqlQuery("Data/FindFilesByPath.sql")]
    public static partial Task<List<BrowseFileRow>> FindFilesByPathAsync(this IReadSession db, Guid? versionId, Guid datasetId, string[] paths, CancellationToken ct);
    [SqlQuery("Label/ListAnnotationsForPaths.sql")]
    public static partial Task<List<PathAnnotationRow>> ListAnnotationsForPathsAsync(this IReadSession db, Guid datasetId, string[] paths, CancellationToken ct);
}
