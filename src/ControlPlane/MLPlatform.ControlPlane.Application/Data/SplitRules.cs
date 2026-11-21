using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>
/// Fixed dataset splits (D-49). The same rule lives in <c>recipes/_shared/converters/to_coco.py</c>: an image's split is
/// <c>sha256("{seed}:{key}")</c> (first 8 bytes, big endian) mod 10 000, compared with the ratios — so it depends on the path only.
/// </summary>
public static class SplitRules
{
    public const string File = "mlp-split.json";
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>train | val | test from a directory name in the path (same aliases as the converter), or null.</summary>
    public static string? FolderSplit(string path)
    {
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
            switch (parts[i].ToLowerInvariant())
            {
                case "train" or "training": return "train";
                case "val" or "valid" or "validation": return "val";
                case "test": return "test";
            }
        return null;
    }

    public static int Bucket(string seed, string key) =>
        (int)(BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + key))) % 10_000UL);

    public static string CountsJson(IReadOnlyDictionary<string, long> counts) =>
        new JsonObject { ["train"] = counts.GetValueOrDefault("train"), ["val"] = counts.GetValueOrDefault("val"), ["test"] = counts.GetValueOrDefault("test") }.ToJsonString();

    public static Dictionary<string, long> ParseCounts(string? json)
    {
        var res = new Dictionary<string, long>();
        if (string.IsNullOrEmpty(json) || JsonNode.Parse(json) is not JsonObject o) return res;
        foreach (var (k, v) in o) if (v is JsonValue jv && jv.TryGetValue<long>(out var n)) res[k] = n;
        return res;
    }
}
