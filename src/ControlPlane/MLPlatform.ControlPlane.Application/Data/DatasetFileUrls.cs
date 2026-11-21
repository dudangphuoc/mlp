using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public static class DatasetFileUrls
{
    /// <summary>Where a dataset file is downloaded from: the source URL (reference version) or our storage.</summary>
    public static string For(S3Client s3, VersionRow v, string path, string? url, Audience audience)
    {
        if (url is null) return s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + path, TimeSpan.FromHours(6), audience);
        if (!url.StartsWith("s3://", StringComparison.Ordinal)) return url;
        var rest = url[5..];
        var slash = rest.IndexOf('/');
        return s3.Presign(HttpMethod.Get, rest[..slash], rest[(slash + 1)..], TimeSpan.FromHours(6), audience);
    }
}
