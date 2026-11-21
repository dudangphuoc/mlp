using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Presigned GETs (24 hours) for a version's files: stored files from the dataset bucket, reference files from
/// their source.</summary>
public static class VersionFileUrls
{
    public static async Task<IReadOnlyList<VersionFileLink>> SignAsync(IReadSession db, S3Client s3, VersionRow v, CancellationToken ct) =>
        (await db.ListVersionFilesAsync(v.Id, ct)).Select(f => new VersionFileLink(f.Path, f.Url is { } u ? SourceUrls.Resolve(s3, u)
            : s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + f.Path, TimeSpan.FromHours(24), Audience.Workload),
            f.SizeBytes, DataCacheRules.Id(v, f.Path, f.Url, f.SizeBytes))).ToList();
}
