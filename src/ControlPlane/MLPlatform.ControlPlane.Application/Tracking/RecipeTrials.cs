using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Rules shared by the trial handlers (round 1 R15–R22): the data subset, reading the shim's lines, building the view.</summary>
public static class RecipeTrials
{
    public const string Marker = "[mlp-trial] ";
    public const int MinTrainImages = 160;
    public const int ValImages = 128;

    /// <summary>Stages (last state per key, in first-seen order) and the measurement from the shim's <c>[mlp-trial]</c> lines.</summary>
    public static (List<TrialStageView> Stages, TrialMeasure? Measure) Parse(IEnumerable<string> lines)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, TrialStageView>();
        TrialMeasure? measure = null;
        foreach (var line in lines)
        {
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at < 0) continue;
            JsonObject? o;
            try { o = JsonNode.Parse(line[(at + Marker.Length)..]) as JsonObject; }
            catch (JsonException) { continue; }
            if (o?["key"]?.ToString() is not { Length: > 0 } key) continue;
            if (key == "measure")
            {
                measure = new TrialMeasure(D(o["steps_per_s"]), D(o["peak_vram_mb"]) is { } p ? (long)p : null, o["vram_estimated"]?.GetValueKind() == JsonValueKind.True,
                    D(o["val_s_per_batch"]), D(o["train_images"]) is { } ti ? (int)ti : null, D(o["val_images"]) is { } vi ? (int)vi : null);
                continue;
            }
            if (!byKey.ContainsKey(key)) order.Add(key);
            byKey[key] = new TrialStageView(key, o["state"]?.ToString() ?? "", D(o["seconds"]) ?? 0, o["detail"]?.ToString() ?? "");
        }
        return (order.Select(k => byKey[k]).ToList(), measure);
    }

    private static double? D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    public static string StagesJson(IEnumerable<TrialStageView> stages) =>
        new JsonArray(stages.Select(s => (JsonNode)new JsonObject { ["key"] = s.Key, ["state"] = s.State, ["seconds"] = s.Seconds, ["detail"] = s.Detail }).ToArray())
            .ToJsonString();

    public static List<TrialStageView> StagesFrom(string json) =>
        (JsonNode.Parse(string.IsNullOrEmpty(json) ? "[]" : json) as JsonArray ?? []).OfType<JsonObject>()
        .Select(o => new TrialStageView(o["key"]?.ToString() ?? "", o["state"]?.ToString() ?? "", D(o["seconds"]) ?? 0, o["detail"]?.ToString() ?? "")).ToList();

    /// <summary>
    /// The view of a trial. While it waits or runs, <paramref name="liveLines"/> (the attempt's <c>[mlp-trial]</c> lines from the log) give the stages,
    /// and a running job shows the trial as running.
    /// </summary>
    public static async Task<TrialView> ViewAsync(IReadSession db, TrialRow t, IReadOnlyList<string>? liveLines, CancellationToken ct)
    {
        var job = t.JobId is { } j ? (await db.JobsByIdsAsync(t.WorkspaceId, [j], ct)).FirstOrDefault() : null;
        var host = t.HostId is { } h ? (await db.GetHostAsync(h, t.WorkspaceId, ct))?.Name : null;
        var active = t.State is "queued" or "running";
        var stages = StagesFrom(t.Stages);
        var (steps, peak, estimated, train, val) = (t.StepsPerS, t.PeakVramMb, t.VramEstimated, t.TrainImages, t.ValImages);
        if (active && liveLines is { Count: > 0 })
        {
            var (live, m) = Parse(liveLines);
            if (live.Count > 0) stages = live;
            if (m is not null) (steps, peak, estimated) = (m.StepsPerS, m.PeakVramMb, m.VramEstimated);
        }
        var state = active && (job?.State == "running" || stages.Any(s => s.State != "pending")) ? "running" : t.State;
        return new TrialView(t, state, stages, steps, peak, estimated, train, val, job, host);
    }

    /// <summary>
    /// The subset a trial trains on: per split, images in the order of their hash with seed "trial" — train = max(160, 10 × batch), val = 128
    /// (fewer when the split is smaller). The split comes from the version's <c>mlp-split.json</c>, else from train/val folders.
    /// Non-image files are kept when their stem matches a chosen image (labels) or no image at all (classes.txt, data.yaml, the split file).
    /// </summary>
    public static async Task<(HashSet<string> Files, int Train, int Val)> SubsetAsync(IReadSession db, S3Client s3, VersionRow version, string splitState, int batch,
        CancellationToken ct)
    {
        var files = await db.ListVersionFilesAsync(version.Id, ct);
        Dictionary<string, string>? fixedSplit = null;
        if (splitState == "file" && files.FirstOrDefault(f => f.Path == SplitRules.File) is { Url: null }
            && await s3.GetAsync(Buckets.Datasets, version.Prefix + SplitRules.File, ct) is { } body
            && JsonNode.Parse(Encoding.UTF8.GetString(body))?["files"] is JsonObject map)
            fixedSplit = map.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.ToString());
        // Same rule as the converter: an image missing from the split file, or outside any split folder, counts as train.
        string SplitOf(string p) => fixedSplit is not null ? fixedSplit.GetValueOrDefault(p, "train") : SplitRules.FolderSplit(p) ?? "train";
        var images = files.Where(f => SplitRules.IsImage(f.Path)).Select(f => f.Path).ToList();
        List<string> Pick(string split, int n) =>
            images.Where(p => SplitOf(p) == split).OrderBy(p => SplitRules.Bucket("trial", p)).ThenBy(p => p, StringComparer.Ordinal).Take(n).ToList();
        var train = Pick("train", Math.Max(MinTrainImages, 10 * batch));
        var val = Pick("val", ValImages);
        var chosen = train.Concat(val).ToHashSet(StringComparer.Ordinal);
        static string Stem(string p) => Path.GetFileNameWithoutExtension(p);
        var allStems = images.Select(Stem).ToHashSet(StringComparer.Ordinal);
        var chosenStems = chosen.Select(Stem).ToHashSet(StringComparer.Ordinal);
        foreach (var f in files)
            if (!SplitRules.IsImage(f.Path) && (chosenStems.Contains(Stem(f.Path)) || !allStems.Contains(Stem(f.Path)))) chosen.Add(f.Path);
        return (chosen, train.Count, val.Count);
    }
}
