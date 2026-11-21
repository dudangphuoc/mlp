using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.Recipes;

namespace MLPlatform.Shim;

/// <summary>Reads what the trial needs from the training's own output lines (D-FINE logger format,
/// src/misc/logger.py).</summary>
public static partial class TrialLog
{
    /// <summary>Speed and memory of stage 1 from the progress lines, and the validation time per batch when it can be
    /// read.</summary>
    public sealed record Measurement(double? StepsPerSecond, double? PeakMb, double? ValSecondsPerBatch);

    // "Epoch: [0]  [ 3/40]  eta: 0:00:12  lr: …  loss: …  time: 0.3120  data: 0.0100  max mem: 5123"
    [GeneratedRegex(@"\[\s*(\d+)/(\d+)\]\s.*?\btime:\s*([0-9.]+)\s+data:\s*([0-9.]+)(?:\s+max mem:\s*([0-9.]+))?")]
    private static partial Regex Progress();

    // "Test:  Total time: 0:00:04 (0.1234 s / it)"
    [GeneratedRegex(@"^Test:\s+Total time:\s*\S+\s+\(([0-9.]+) s / it\)")]
    private static partial Regex TestTotal();

    public static Measurement Measure(IEnumerable<string> lines)
    {
        var times = new List<double>();
        double? peak = null, val = null;
        foreach (var l in lines)
        {
            if (TestTotal().Match(l) is { Success: true } t && double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tv)) val = tv;
            if (!l.Contains("Epoch:", StringComparison.Ordinal)) continue;
            var m = Progress().Match(l);
            if (!m.Success) continue;
            if (double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) times.Add(s);
            if (m.Groups[5].Success && double.TryParse(m.Groups[5].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mem))
                peak = Math.Max(peak ?? 0, mem);
        }
        double? sps = null;
        var kept = times.Skip(3).Order().ToList();
        if (kept.Count == 0) kept = times.Order().ToList();
        if (kept.Count > 0)
        {
            var median = kept.Count % 2 == 1 ? kept[kept.Count / 2] : (kept[kept.Count / 2 - 1] + kept[kept.Count / 2]) / 2;
            if (median > 0) sps = Math.Round(1 / median, 4);
        }
        return new Measurement(sps, peak, val);
    }

    /// <summary>"Load model.state_dict, {'missed': [...], 'unmatched': [...]}" → layers loaded (unknown: the line does
    /// not list them) and layers created new (missed + unmatched).</summary>
    public static (int? Loaded, int? Created) WeightsLoad(IEnumerable<string> lines)
    {
        var line = lines.LastOrDefault(l => l.Contains("Load model.state_dict, ", StringComparison.Ordinal));
        if (line is null) return (null, null);
        return (null, Count(line, "'missed'") + Count(line, "'unmatched'"));
    }

    private static int Count(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return 0;
        var open = line.IndexOf('[', at);
        var close = open < 0 ? -1 : line.IndexOf(']', open);
        if (open < 0 || close < 0) return 0;
        var inner = line[(open + 1)..close].Trim();
        return inner.Length == 0 ? 0 : inner.Split(',').Count(x => x.Trim().Length > 0);
    }
}
