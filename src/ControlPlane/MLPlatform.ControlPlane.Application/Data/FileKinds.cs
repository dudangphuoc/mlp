using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>How the content browser treats a file, by extension.</summary>
public static class FileKinds
{
    private static readonly Dictionary<string, string[]> Map = new()
    {
        ["image"] = ["jpg", "jpeg", "png", "bmp", "webp", "gif", "tif", "tiff"],
        ["text"] = ["txt", "md", "log", "yaml", "yml", "xml", "names"],
        ["table"] = ["csv", "tsv"],
        ["json"] = ["json", "jsonl", "ndjson"],
        ["audio"] = ["wav", "mp3", "flac", "ogg", "m4a", "opus"],
        ["video"] = ["mp4", "mov", "webm", "mkv", "avi"],
    };

    public static string[] Extensions(string kind) => Map.GetValueOrDefault(kind, []);

    public static string Of(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        foreach (var (kind, exts) in Map) if (exts.Contains(ext)) return kind;
        return "other";
    }

    /// <summary>Escapes LIKE wildcards so a folder or search text matches literally.</summary>
    public static string LikeEscape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
