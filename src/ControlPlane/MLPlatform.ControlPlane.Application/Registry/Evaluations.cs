using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public static class Evaluations
{
    public const int MaxSamplesLimit = 5000;

    /// <summary>Class names by id from a label schema (lenient: gaps keep their numeric id).</summary>
    public static List<string> Classes(string? classesJson)
    {
        var map = new SortedDictionary<int, string>();
        foreach (var n in (classesJson is null ? null : JsonNode.Parse(classesJson) as JsonArray) ?? [])
            if (n?["id"] is JsonValue id && id.TryGetValue<int>(out var i)) map[i] = n["name"]?.ToString() ?? i.ToString(CultureInfo.InvariantCulture);
        if (map.Count == 0) return [];
        var list = new List<string>();
        for (var k = 0; k <= map.Keys.Max(); k++) list.Add(map.TryGetValue(k, out var v) ? v : k.ToString(CultureInfo.InvariantCulture));
        return list;
    }

    /// <summary>Where a dataset file is downloaded from: the source URL (reference version) or our storage.</summary>
    public static string FileUrl(S3Client s3, VersionRow v, string path, string? url, Audience audience)
    {
        if (url is null) return s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + path, TimeSpan.FromHours(6), audience);
        if (!url.StartsWith("s3://", StringComparison.Ordinal)) return url;
        var rest = url[5..];
        var slash = rest.IndexOf('/');
        return s3.Presign(HttpMethod.Get, rest[..slash], rest[(slash + 1)..], TimeSpan.FromHours(6), audience);
    }
}
