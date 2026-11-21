using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Cache ids and sizes of a version's files.</summary>
public sealed record VersionFileIdsQuery(Guid WorkspaceId, Guid VersionId) : IQuery<(Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)>;

/// <summary>A version's files as cache ids and sizes (no links) — for transfer estimates.</summary>
public sealed class VersionFileIdsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<VersionFileIdsQuery, (Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)>
{
    public async ValueTask<(Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)> Handle(VersionFileIdsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(q.VersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        return (v.DatasetId, (await db.ListVersionFilesAsync(v.Id, ct)).Select(f => (DataCacheRules.Id(v, f.Path, f.Url, f.SizeBytes), f.SizeBytes)).ToList());
    }
}

/// <summary>
/// Host dataset cache: one docker volume per dataset per machine (<c>mlp-data-{dataset}</c>) holding immutable objects; each run gets a view of
/// hard links to them. A file is cached by an id derived from an address whose bytes can never change: dataset bucket keys are written once
/// (version numbers are never reused — <c>data.datasets.version_seq</c>) and Hugging Face links pinned to a commit. Other references (plain
/// URLs, external S3) can be overwritten by their owner, so they get no id and are downloaded for every run.
/// </summary>
public static partial class DataCacheRules
{
    public const string MountRoot = "/mnt/datasets";

    public static string Volume(Guid datasetId) => $"mlp-data-{datasetId:N}";

    public static string Mount(Guid datasetId) => $"{MountRoot}/{datasetId:N}";

    public static string? Id(VersionRow v, string path, string? url, long size)
    {
        var address = url is null ? $"s3://{Buckets.Datasets}/{v.Prefix}{path}"
            : url.StartsWith($"s3://{Buckets.Datasets}/", StringComparison.Ordinal) || PinnedHf().IsMatch(url) ? url
            : null;
        return address is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{address}|{size}")))[..32];
    }

    [GeneratedRegex(@"^https://huggingface\.co/.+/resolve/[0-9a-f]{40}/")] private static partial Regex PinnedHf();
}
