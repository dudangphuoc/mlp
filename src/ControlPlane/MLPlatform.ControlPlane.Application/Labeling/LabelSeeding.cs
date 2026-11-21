using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Which files of a dataset become items: images for image tasks; for other tasks every file except metadata (yaml, json).</summary>
public static class LabelSeeding
{
    private static readonly string[] TextTasks = ["text", "tabular", "generate", "asr", "tts", "audio", "speech"];

    public static bool ImageTask(string task) => !TextTasks.Any(t => task.StartsWith(t, StringComparison.Ordinal) || task.Contains("-" + t, StringComparison.Ordinal));

    public static Task<long> SeedAsync(IWriteSession db, Guid projectId, Guid datasetId, Guid? versionId, string task, CancellationToken ct)
    {
        var images = ImageTask(task);
        var exts = FileKinds.Extensions("image");
        return versionId is { } v ? db.SeedItemsFromVersionAsync(projectId, datasetId, v, images, exts, ct) : db.SeedItemsAsync(projectId, datasetId, images, exts, ct);
    }

    /// <summary>The YOLO label file that may sit next to an image: …/images/x.jpg ↔ …/labels/x.txt (or yololabels), or x.txt in the same folder.</summary>
    public static IReadOnlyList<string> YoloCandidates(string imagePath)
    {
        var stem = imagePath[..^Path.GetExtension(imagePath).Length];
        var list = new List<string>();
        foreach (var dir in new[] { "/labels/", "/yololabels/", "/yolo_labels/" })
        {
            var c = (stem.StartsWith("images/", StringComparison.Ordinal) ? "/" + stem : stem).Replace("/images/", dir).TrimStart('/') + ".txt";
            if (c != stem + ".txt") list.Add(c);
        }
        list.Add(stem + ".txt");
        return list;
    }

    /// <summary>"class cx cy w h" lines (normalized centre) → platform boxes {x, y, w, h} (normalized top-left); malformed lines are skipped.</summary>
    public static List<AnnotationInput> ParseYolo(string text, IReadOnlySet<int>? classes)
    {
        var res = new List<AnnotationInput>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || !int.TryParse(p[0], out var cls) || cls < 0 || (classes is { Count: > 0 } && !classes.Contains(cls))) continue;
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
                || !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) continue;
            var x = Math.Clamp(cx - w / 2, 0, 1); var y = Math.Clamp(cy - h / 2, 0, 1);
            w = Math.Min(w, 1 - x); h = Math.Min(h, 1 - y);
            if (w <= 0 || h <= 0) continue;
            var geom = new JsonObject { ["x"] = Math.Round(x, 6), ["y"] = Math.Round(y, 6), ["w"] = Math.Round(w, 6), ["h"] = Math.Round(h, 6) }.ToJsonString();
            res.Add(new AnnotationInput("bbox", cls, geom, "{}", "import", null));
        }
        return res;
    }
}
