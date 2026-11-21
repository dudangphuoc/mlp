using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Where the api reads one dataset file from (internal URL) — the gRPC layer downloads a bounded prefix for
/// previews.</summary>
[RequiresPermission("dataset.read")]
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
            ? DatasetFileUrls.For(s3, version, row.Path, row.Url, Audience.Internal)
            : s3.Presign(HttpMethod.Get, Buckets.Datasets, Buckets.Raw(q.WorkspaceId, q.DatasetId) + row.Path, TimeSpan.FromMinutes(10), Audience.Internal);
        return (url, row.SizeBytes);
    }
}
