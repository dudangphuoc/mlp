using System.Globalization;
using System.Text.Json.Nodes;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Model-agnostic evaluation math: how ground truth is found in a dataset version (YOLO, COCO, class folders)
/// and the standard metrics (COCO mAP@[.50:.95] with 101-point interpolation.</summary>
public static class EvalMath
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
    public static readonly string[] DetectTasks = ["detect", "zero-shot-detect"];
    public static readonly string[] ClassifyTasks = ["classify", "zero-shot-image-classify"];
    public static bool Supports(string task) => DetectTasks.Contains(task) || ClassifyTasks.Contains(task);

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Pairs images with their ground truth.</summary>
    public static List<EvalSampleSpec> FindSamples(string task, IReadOnlyCollection<string> paths, string filter)
    {
        var set = new HashSet<string>(paths, StringComparer.Ordinal);
        var images = paths.Where(IsImage).Where(p => string.IsNullOrEmpty(filter) || p.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<EvalSampleSpec>(images.Count);
        foreach (var img in images)
        {
            if (ClassifyTasks.Contains(task))
            {
                var dir = Path.GetDirectoryName(img)?.Replace('\\', '/') ?? "";
                var cls = dir.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (cls is not null && !cls.Equals("images", StringComparison.OrdinalIgnoreCase)) result.Add(new EvalSampleSpec(img, null, cls));
                continue;
            }
            result.Add(new EvalSampleSpec(img, YoloLabelFor(img, set), null));
        }
        return result;
    }

    private static string? YoloLabelFor(string img, HashSet<string> set)
    {
        var stem = img[..^Path.GetExtension(img).Length];
        foreach (var dirName in new[] { "/labels/", "/yololabels/", "/yolo_labels/" })
        {
            var c = stem.Replace("/images/", dirName) + ".txt";
            if (c != stem + ".txt" && set.Contains(c)) return c;
        }
        return set.Contains(stem + ".txt") ? stem + ".txt" : null;
    }

    /// <summary>YOLO line: class cx cy w h (normalized). Class ids map to names via the dataset's classes.</summary>
    public static List<EvalBox> ParseYolo(string text, IReadOnlyList<string> classes, int width, int height)
    {
        var boxes = new List<EvalBox>();
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var v = new double[4];
            var ok = true;
            for (var i = 0; i < 4; i++) ok &= double.TryParse(p[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
            if (!ok) continue;
            var name = id >= 0 && id < classes.Count ? classes[id] : id.ToString(CultureInfo.InvariantCulture);
            if (p.Length > 5 && p.Length % 2 == 1)
            {
                var xs = new List<double>(); var ys = new List<double>();
                for (var i = 1; i + 1 < p.Length; i += 2)
                {
                    xs.Add(double.Parse(p[i], CultureInfo.InvariantCulture));
                    ys.Add(double.Parse(p[i + 1], CultureInfo.InvariantCulture));
                }
                boxes.Add(new EvalBox(Norm(name), xs.Min() * width, ys.Min() * height, xs.Max() * width, ys.Max() * height));
                continue;
            }
            boxes.Add(new EvalBox(Norm(name), (v[0] - v[2] / 2) * width, (v[1] - v[3] / 2) * height, (v[0] + v[2] / 2) * width, (v[1] + v[3] / 2) * height));
        }
        return boxes;
    }

    /// <summary>COCO json → file name (basename) → boxes. Categories by name.</summary>
    public static Dictionary<string, List<EvalBox>> ParseCoco(string json)
    {
        var doc = JsonNode.Parse(json)!.AsObject();
        var cats = (doc["categories"] as JsonArray ?? []).OfType<JsonObject>()
            .ToDictionary(c => c["id"]!.GetValue<int>(), c => Norm(c["name"]?.ToString() ?? c["id"]!.ToString()));
        var files = (doc["images"] as JsonArray ?? []).OfType<JsonObject>()
            .ToDictionary(i => i["id"]!.GetValue<long>(), i => Path.GetFileName(i["file_name"]?.ToString() ?? ""));
        var res = files.Values.Distinct().ToDictionary(f => f, _ => new List<EvalBox>(), StringComparer.Ordinal);
        foreach (var a in (doc["annotations"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (!files.TryGetValue(a["image_id"]!.GetValue<long>(), out var f) || a["bbox"] is not JsonArray b || b.Count < 4) continue;
            var x = b[0]!.GetValue<double>(); var y = b[1]!.GetValue<double>();
            var name = cats.TryGetValue(a["category_id"]?.GetValue<int>() ?? -1, out var n) ? n : "?";
            res[f].Add(new EvalBox(name, x, y, x + b[2]!.GetValue<double>(), y + b[3]!.GetValue<double>()));
        }
        return res;
    }

    public static string Norm(string s) => s.Trim().ToLowerInvariant();

    // ───────────── Detection metrics ─────────────

    public sealed record ClassAp(string Cls, int Gt, int Pred, double Ap50, double Ap5095, double Precision, double Recall);

    public sealed record DetectSummary(double Map50, double Map5095, double Precision, double Recall, double F1, int Tp, int Fp, int Fn,
        IReadOnlyList<ClassAp> PerClass);

    /// <summary>Per-image counts at a confidence threshold and IoU 0.5 (greedy by score, same class).</summary>
    public static (int Tp, int Fp, int Fn) CountAt(IReadOnlyList<EvalBox> gt, IReadOnlyList<EvalBox> pred, double conf, double iou = 0.5)
    {
        var used = new bool[gt.Count];
        int tp = 0, fp = 0;
        foreach (var p in pred.Where(p => p.Score >= conf).OrderByDescending(p => p.Score))
        {
            var best = -1; var bestIou = iou;
            for (var g = 0; g < gt.Count; g++)
            {
                if (used[g] || gt[g].Cls != p.Cls) continue;
                var v = EvalBox.Iou(p, gt[g]);
                if (v >= bestIou) { bestIou = v; best = g; }
            }
            if (best >= 0) { used[best] = true; tp++; } else fp++;
        }
        return (tp, fp, gt.Count - tp);
    }

    public static DetectSummary Detection(IReadOnlyList<(IReadOnlyList<EvalBox> Gt, IReadOnlyList<EvalBox> Pred)> images, double conf)
    {
        var classes = images.SelectMany(i => i.Gt.Select(g => g.Cls)).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        var thresholds = Enumerable.Range(0, 10).Select(i => 0.5 + 0.05 * i).ToArray();
        var per = new List<ClassAp>();
        foreach (var cls in classes)
        {
            var nGt = images.Sum(i => i.Gt.Count(g => g.Cls == cls));
            var preds = images.SelectMany((im, idx) => im.Pred.Where(p => p.Cls == cls).Select(p => (Img: idx, Box: p)))
                .OrderByDescending(x => x.Box.Score).ToList();
            var aps = new double[thresholds.Length];
            for (var t = 0; t < thresholds.Length; t++) aps[t] = AveragePrecision(images, cls, preds, nGt, thresholds[t]);
            int tp = 0, fp = 0, fn = 0;
            foreach (var im in images)
            {
                var g = im.Gt.Where(b => b.Cls == cls).ToList();
                var (a, b, c) = CountAt(g, im.Pred.Where(p => p.Cls == cls).ToList(), conf);
                tp += a; fp += b; fn += c;
            }
            per.Add(new ClassAp(cls, nGt, preds.Count(p => p.Box.Score >= conf), aps[0], aps.Average(),
                tp + fp == 0 ? 0 : (double)tp / (tp + fp), tp + fn == 0 ? 0 : (double)tp / (tp + fn)));
        }
        int TP = 0, FP = 0, FN = 0;
        foreach (var im in images) { var (a, b, c) = CountAt(im.Gt, im.Pred, conf); TP += a; FP += b; FN += c; }
        var p = TP + FP == 0 ? 0 : (double)TP / (TP + FP);
        var r = TP + FN == 0 ? 0 : (double)TP / (TP + FN);
        return new DetectSummary(per.Count == 0 ? 0 : per.Average(c => c.Ap50), per.Count == 0 ? 0 : per.Average(c => c.Ap5095),
            p, r, p + r == 0 ? 0 : 2 * p * r / (p + r), TP, FP, FN, per);
    }

    /// <summary>Overall precision/recall/F1 for confidence thresholds 0.05…0.95 — the best F1 suggests the deployment
    /// threshold.</summary>
    public static List<(double Conf, double Precision, double Recall, double F1)> ThresholdSweep(
        IReadOnlyList<(IReadOnlyList<EvalBox> Gt, IReadOnlyList<EvalBox> Pred)> images)
    {
        var res = new List<(double, double, double, double)>();
        for (var i = 1; i <= 19; i++)
        {
            var conf = Math.Round(i * 0.05, 2);
            int tp = 0, fp = 0, fn = 0;
            foreach (var im in images) { var (a, b, c) = CountAt(im.Gt, im.Pred, conf); tp += a; fp += b; fn += c; }
            var p = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            var r = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            res.Add((conf, p, r, p + r == 0 ? 0 : 2 * p * r / (p + r)));
        }
        return res;
    }

    /// <summary>COCO-style AP for one class at one IoU threshold: greedy matching by score, 101-point interpolated
    /// precision.</summary>
    private static double AveragePrecision(IReadOnlyList<(IReadOnlyList<EvalBox> Gt, IReadOnlyList<EvalBox> Pred)> images, string cls,
        List<(int Img, EvalBox Box)> preds, int nGt, double iouT)
    {
        if (nGt == 0) return 0;
        var used = images.Select(i => new bool[i.Gt.Count]).ToArray();
        var tps = new List<bool>(preds.Count);
        foreach (var (img, box) in preds)
        {
            var gt = images[img].Gt;
            var best = -1; var bestIou = iouT;
            for (var g = 0; g < gt.Count; g++)
            {
                if (used[img][g] || gt[g].Cls != cls) continue;
                var v = EvalBox.Iou(box, gt[g]);
                if (v >= bestIou) { bestIou = v; best = g; }
            }
            if (best >= 0) used[img][best] = true;
            tps.Add(best >= 0);
        }
        var recall = new double[tps.Count];
        var precision = new double[tps.Count];
        int ctp = 0;
        for (var k = 0; k < tps.Count; k++)
        {
            if (tps[k]) ctp++;
            recall[k] = (double)ctp / nGt;
            precision[k] = (double)ctp / (k + 1);
        }
        for (var k = precision.Length - 2; k >= 0; k--) precision[k] = Math.Max(precision[k], precision[k + 1]);
        double sum = 0;
        for (var i = 0; i <= 100; i++)
        {
            var rt = i / 100.0;
            var idx = Array.FindIndex(recall, x => x >= rt);
            sum += idx < 0 ? 0 : precision[idx];
        }
        return sum / 101;
    }

    // ───────────── Classification metrics ─────────────

    public sealed record ClassifySummary(double Accuracy, double F1Macro, IReadOnlyList<(string Cls, int Support, double Precision, double Recall, double F1)> PerClass,
        IReadOnlyList<string> Labels, int[][] Confusion);

    public static ClassifySummary Classification(IReadOnlyList<(string Truth, string Pred)> rows)
    {
        var labels = rows.SelectMany(r => new[] { r.Truth, r.Pred }).Where(s => s.Length > 0).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var index = labels.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i);
        var cm = labels.Select(_ => new int[labels.Count]).ToArray();
        foreach (var (t, p) in rows) if (index.TryGetValue(t, out var ti) && index.TryGetValue(p, out var pi)) cm[ti][pi]++;
        var per = new List<(string, int, double, double, double)>();
        foreach (var l in labels.Where(l => rows.Any(r => r.Truth == l)))
        {
            var i = index[l];
            var tp = cm[i][i];
            var support = cm[i].Sum();
            var predicted = cm.Sum(row => row[i]);
            var prec = predicted == 0 ? 0 : (double)tp / predicted;
            var rec = support == 0 ? 0 : (double)tp / support;
            per.Add((l, support, prec, rec, prec + rec == 0 ? 0 : 2 * prec * rec / (prec + rec)));
        }
        var acc = rows.Count == 0 ? 0 : (double)rows.Count(r => r.Truth == r.Pred) / rows.Count;
        return new ClassifySummary(acc, per.Count == 0 ? 0 : per.Average(x => x.Item5), per, labels, cm);
    }

    /// <summary>Image size from the file header (JPEG SOF, PNG IHDR) — used when the model does not report it.</summary>
    public static (int W, int H)? ImageSize(ReadOnlySpan<byte> b)
    {
        if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
            return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                var len = (b[i + 2] << 8) | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + len;
            }
        }
        return null;
    }
}
