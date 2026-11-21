using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Host dataset cache: one docker volume per dataset per machine ( mlp-data-{dataset} ) holding immutable
/// objects; each run gets a view of hard links to them.</summary>
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
