using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Buckets per 20/05 §3. `mlp-datasets/{ws}/datasets/{ds}/raw/…` (live) and `…/v{n}/…` (immutable).</summary>
public static class Buckets
{
    /// <summary>Bucket names — configurable via MLP__S3__DatasetsBucket / ArtifactsBucket (set by the api at startup).</summary>
    public static string Datasets { get; set; } = "mlp-datasets";
    public static string Artifacts { get; set; } = "mlp-artifacts";
    public static string Raw(Guid ws, Guid ds) => $"{ws}/datasets/{ds}/raw/";
    public static string Version(Guid ws, Guid ds, int n) => $"{ws}/datasets/{ds}/v{n}/";

    /// <summary>Safe relative path: not empty, not absolute, no "..", no control characters.</summary>
    public static string SafePath(string p)
    {
        var x = p.Replace('\\', '/').Trim('/');
        if (x.Length == 0 || x.Length > 1024 || x.Split('/').Any(s => s is "" or "." or "..") || x.Any(char.IsControl))
            throw new ValidationException(new Dictionary<string, string> { ["path"] = "Invalid file path" });
        return x;
    }
}
