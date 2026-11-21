using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Turns a stored source address into a URL a workload can download: s3://bucket/key is presigned, https:// is used as is.</summary>
public static class SourceUrls
{
    public static string Resolve(S3Client s3, string url)
    {
        if (!url.StartsWith("s3://", StringComparison.Ordinal)) return url;
        var rest = url[5..];
        var slash = rest.IndexOf('/');
        if (slash <= 0) throw new InvalidArgumentException("SOURCE_INVALID", $"Invalid S3 address: {url}");
        return s3.Presign(HttpMethod.Get, rest[..slash], rest[(slash + 1)..], TimeSpan.FromHours(24), Audience.Workload);
    }
}
