using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Files of "dataset@N" (or the latest version) with links for a run's container (<c>InJob</c>) or the
/// developer's machine; the caller has already checked access.</summary>
[Access(AccessKind.Internal)]
public sealed record ResolveDatasetFilesQuery(Guid WorkspaceId, bool InJob, string Reference) : IQuery<DatasetFiles>;

public sealed class ResolveDatasetFilesQueryHandler(IReadSession db, S3Client s3) : IQueryHandler<ResolveDatasetFilesQuery, DatasetFiles>
{
    public async ValueTask<DatasetFiles> Handle(ResolveDatasetFilesQuery q, CancellationToken ct)
    {
        var (name, wanted) = Parse(q.Reference);
        var dataset = (await db.ListDatasetsAsync(q.WorkspaceId, "", ct)).FirstOrDefault(d => d.Slug == name || d.Name == name)
            ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", $"Dataset '{name}' not found");
        var versions = await db.ListVersionsAsync(dataset.Id, ct);
        var version = (wanted is { } n ? versions.FirstOrDefault(v => v.Number == n) : versions.MaxBy(v => v.Number))
            ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", wanted is { } m ? $"{name} has no version {m}" : $"{name} has no version yet");
        var audience = q.InJob ? Audience.Workload : Audience.Public;
        var files = (await db.ListVersionFilesAsync(version.Id, ct)).Select(f => new VersionFileLink(f.Path,
            f.Url is { } u ? SourceUrls.Resolve(s3, u) : s3.Presign(HttpMethod.Get, Buckets.Datasets, version.Prefix + f.Path, TimeSpan.FromHours(24), audience),
            f.SizeBytes, DataCacheRules.Id(version, f.Path, f.Url, f.SizeBytes))).ToList();
        return new DatasetFiles(version.Id, dataset.Name, version.Number, files);
    }

    private static (string Name, int? Version) Parse(string reference)
    {
        var at = reference.LastIndexOf('@');
        if (at < 0) return (reference.Trim(), null);
        var v = reference[(at + 1)..].TrimStart('v', 'V');
        return int.TryParse(v, out var n) ? (reference[..at].Trim(), n)
            : throw new ValidationException(new Dictionary<string, string> { ["ref"] = "Use dataset or dataset@N" });
    }
}
