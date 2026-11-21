using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;

namespace MLPlatform.Shim;

/// <summary>Find new checkpoints that have finished writing (size + mtime unchanged ≥ 10 s) using the recipe's
/// `checkpoints` glob.</summary>
public sealed class CheckpointWatcher(string output, IReadOnlyList<string> patterns, string statePath)
{
    private readonly Dictionary<string, (long Size, DateTime Mtime, DateTimeOffset Since)> _seen = [];
    private readonly HashSet<string> _done = Load(statePath);

    public IReadOnlyList<string> Ready()
    {
        var now = TimeProvider.System.GetUtcNow();
        var ready = new List<string>();
        foreach (var f in patterns.SelectMany(p => Runner.Glob(output, p)).Where(File.Exists).Distinct())
        {
            var fi = new FileInfo(f);
            var key = $"{Path.GetRelativePath(output, f)}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            if (_done.Contains(key)) continue;
            if (!_seen.TryGetValue(f, out var s) || s.Size != fi.Length || s.Mtime != fi.LastWriteTimeUtc)
                _seen[f] = (fi.Length, fi.LastWriteTimeUtc, now);
            else if (now - s.Since >= TimeSpan.FromSeconds(10)) ready.Add(f);
        }
        return ready;
    }

    public void MarkDone(string file)
    {
        var fi = new FileInfo(file);
        _done.Add($"{Path.GetRelativePath(output, file)}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
        File.WriteAllText(statePath, new JsonObject { ["uploaded"] = new JsonArray(_done.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) }.ToJsonString());
    }

    private static HashSet<string> Load(string path)
    {
        try { return (JsonNode.Parse(File.ReadAllText(path))?["uploaded"] as JsonArray)?.Select(x => x!.ToString()).ToHashSet() ?? []; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return []; }
    }
}
