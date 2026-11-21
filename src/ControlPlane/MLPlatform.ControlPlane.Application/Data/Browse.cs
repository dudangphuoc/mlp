using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record BrowseFileRow(string Path, long SizeBytes, string? Url);
public sealed record BrowseFolderRow(string Name, long Files, long SizeBytes);
public sealed record ExtensionStatRow(string Ext, long Files, long SizeBytes);
public sealed record PathAnnotationRow(string Path, string Kind, int ClassId, string Geometry, string Source);

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

/// <summary>How the content browser treats a file, by extension.</summary>
public static class FileKinds
{
    private static readonly Dictionary<string, string[]> Map = new()
    {
        ["image"] = ["jpg", "jpeg", "png", "bmp", "webp", "gif", "tif", "tiff"],
        ["text"] = ["txt", "md", "log", "yaml", "yml", "xml", "names"],
        ["table"] = ["csv", "tsv"],
        ["json"] = ["json", "jsonl", "ndjson"],
        ["audio"] = ["wav", "mp3", "flac", "ogg", "m4a", "opus"],
        ["video"] = ["mp4", "mov", "webm", "mkv", "avi"],
    };

    public static string[] Extensions(string kind) => Map.GetValueOrDefault(kind, []);

    public static string Of(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        foreach (var (kind, exts) in Map) if (exts.Contains(ext)) return kind;
        return "other";
    }

    /// <summary>Escapes LIKE wildcards so a folder or search text matches literally.</summary>
    public static string LikeEscape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public sealed record BrowsedFile(string Path, long SizeBytes, string Kind, string Url, string? LabelUrl, string? LabelPath, string? AnnotationsJson);
public sealed record BrowseResult(IReadOnlyList<BrowseFolderRow> Folders, IReadOnlyList<BrowsedFile> Files, long Total, string ClassesJson);

/// <summary>
/// Content of a dataset version (or of the working copy when <c>VersionId</c> is null): folders and files under a prefix with
/// browser-loadable URLs, the YOLO label file paired with each image, and the labels made in the platform (M05).
/// </summary>
public sealed record BrowseDatasetQuery(Guid WorkspaceId, Guid DatasetId, Guid? VersionId, string Prefix, string Search, string Kind, bool Recursive,
    int Offset, int Limit) : IQuery<BrowseResult>;

public sealed class BrowseDatasetQueryHandler(IReadSession db, WorkspaceAuthorizer auth, S3Client s3) : IQueryHandler<BrowseDatasetQuery, BrowseResult>
{
    public async ValueTask<BrowseResult> Handle(BrowseDatasetQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var version = q.VersionId is { } vid
            ? await db.GetVersionAsync(vid, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found")
            : null;
        if (version is not null && version.DatasetId != q.DatasetId) throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");

        var prefix = q.Prefix.Replace('\\', '/').TrimStart('/');
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        var prefixLike = FileKinds.LikeEscape(prefix) + "%";
        var qLike = string.IsNullOrWhiteSpace(q.Search) ? "" : "%" + FileKinds.LikeEscape(q.Search.Trim()) + "%";
        var exts = FileKinds.Extensions(q.Kind);
        var recursive = q.Recursive || qLike.Length > 0 || exts.Length > 0;   // a search or a type filter looks below the folder too
        var limit = Math.Clamp(q.Limit, 1, 200);
        var rows = await db.BrowseFilesAsync(q.VersionId, q.DatasetId, prefixLike, prefix.Length, recursive, qLike, exts.Length > 0, exts, Math.Max(0, q.Offset), limit, ct);
        var total = await db.CountBrowseFilesAsync(q.VersionId, q.DatasetId, prefixLike, prefix.Length, recursive, qLike, exts.Length > 0, exts, ct);
        var folders = recursive ? [] : await db.BrowseFoldersAsync(q.VersionId, q.DatasetId, prefixLike, prefix.Length, ct);

        string Url(string path, string? url) => version is not null
            ? Registry.Evaluations.FileUrl(s3, version, path, url, Audience.Public)
            : s3.Presign(HttpMethod.Get, Buckets.Datasets, Buckets.Raw(q.WorkspaceId, q.DatasetId) + path, TimeSpan.FromHours(6), Audience.Public);

        // YOLO labels: the txt next to each image (…/images/x.jpg ↔ …/labels|yololabels/x.txt, or the same folder).
        var images = rows.Where(r => FileKinds.Of(r.Path) == "image").ToList();
        var candidates = new Dictionary<string, List<string>>();
        foreach (var img in images) candidates[img.Path] = [.. Labeling.LabelSeeding.YoloCandidates(img.Path)];
        var labelFiles = candidates.Count == 0 ? []
            : (await db.FindFilesByPathAsync(q.VersionId, q.DatasetId, candidates.Values.SelectMany(x => x).Distinct().ToArray(), ct))
                .ToDictionary(r => r.Path, r => r.Url, StringComparer.Ordinal);
        var annotations = rows.Count == 0 ? new Dictionary<string, List<PathAnnotationRow>>()
            : (await db.ListAnnotationsForPathsAsync(q.DatasetId, rows.Select(r => r.Path).ToArray(), ct))
                .GroupBy(a => a.Path).ToDictionary(g => g.Key, g => g.ToList());

        var files = rows.Select(r =>
        {
            string? labelUrl = null, labelPath = null;
            if (candidates.TryGetValue(r.Path, out var cs) && cs.FirstOrDefault(labelFiles.ContainsKey) is { } lp) { labelPath = lp; labelUrl = Url(lp, labelFiles[lp]); }
            string? ann = null;
            if (annotations.TryGetValue(r.Path, out var list))
                ann = new JsonArray(list.Select(a => (JsonNode)new JsonObject
                {
                    ["kind"] = a.Kind, ["class_id"] = a.ClassId, ["geometry"] = JsonNode.Parse(a.Geometry), ["source"] = a.Source,
                }).ToArray()).ToJsonString();
            return new BrowsedFile(r.Path, r.SizeBytes, FileKinds.Of(r.Path), Url(r.Path, r.Url), labelUrl, labelPath, ann);
        }).ToList();
        var classes = (await Labeling.LabelSql.GetCurrentSchemaAsync(db, q.DatasetId, ct))?.Classes ?? "[]";
        return new BrowseResult(folders, files, total, classes);
    }
}

public sealed record DatasetContentStats(IReadOnlyList<ExtensionStatRow> Extensions, long Files, long SizeBytes);

public sealed record GetDatasetContentStatsQuery(Guid WorkspaceId, Guid DatasetId, Guid? VersionId) : IQuery<DatasetContentStats>;

public sealed class GetDatasetContentStatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDatasetContentStatsQuery, DatasetContentStats>
{
    public async ValueTask<DatasetContentStats> Handle(GetDatasetContentStatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var rows = await db.FileExtensionStatsAsync(q.VersionId, q.DatasetId, ct);
        return new DatasetContentStats(rows, rows.Sum(r => r.Files), rows.Sum(r => r.SizeBytes));
    }
}

/// <summary>Where the api reads one dataset file from (internal URL) — the gRPC layer downloads a bounded prefix for previews.</summary>
public sealed record ResolveDatasetFileQuery(Guid WorkspaceId, Guid DatasetId, Guid? VersionId, string Path) : IQuery<(string Url, long SizeBytes)>;

public sealed class ResolveDatasetFileQueryHandler(IReadSession db, WorkspaceAuthorizer auth, S3Client s3) : IQueryHandler<ResolveDatasetFileQuery, (string Url, long SizeBytes)>
{
    public async ValueTask<(string Url, long SizeBytes)> Handle(ResolveDatasetFileQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var version = q.VersionId is { } vid ? await db.GetVersionAsync(vid, q.WorkspaceId, ct) : null;
        if (q.VersionId is not null && (version is null || version.DatasetId != q.DatasetId))
            throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var row = (await db.FindFilesByPathAsync(q.VersionId, q.DatasetId, [q.Path], ct)).FirstOrDefault()
                  ?? throw new NotFoundException("DATA_FILE_NOT_FOUND", "File not found in this dataset");
        var url = version is not null
            ? Registry.Evaluations.FileUrl(s3, version, row.Path, row.Url, Audience.Internal)
            : s3.Presign(HttpMethod.Get, Buckets.Datasets, Buckets.Raw(q.WorkspaceId, q.DatasetId) + row.Path, TimeSpan.FromMinutes(10), Audience.Internal);
        return (url, row.SizeBytes);
    }
}
