using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

/// <summary>
/// Drift statistics (M13 §2) — pure C#, no external library dependencies (AOT):
/// numeric → 10 bins by baseline deciles, PSI + two-sample KS; categorical → PSI over frequencies (including an "other" bucket).
/// Default PSI thresholds 0.1 (warning) / 0.25 (alert) per common practice.
/// </summary>
public static class Drift
{
    private const double Eps = 1e-4;
    private const int MaxSample = 5000;

    /// <summary>Baseline from a table (header + rows): each numeric column stores bin edges + proportions + a sorted sample; text columns store frequencies; the label column → label distribution.</summary>
    public static JsonObject BuildBaseline(IReadOnlyList<string> header, IReadOnlyList<string[]> rows, string labelColumn)
    {
        var features = new JsonObject();
        JsonObject? labels = null;
        for (var c = 0; c < header.Count; c++)
        {
            var name = header[c];
            var col = rows.Select(r => c < r.Length ? r[c] : "").ToList();
            if (name == labelColumn) { labels = Frequencies(col); continue; }
            var nums = col.Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToList();
            if (nums.All(d => !double.IsNaN(d)) && nums.Count > 0)
            {
                var sorted = nums.Order().ToArray();
                var edges = Enumerable.Range(1, 9).Select(i => Quantile(sorted, i / 10.0)).Distinct().ToArray();
                var props = Histogram(sorted, edges);
                var step = Math.Max(1, sorted.Length / MaxSample);
                features[name] = new JsonObject
                {
                    ["kind"] = "numeric", ["edges"] = Arr(edges), ["props"] = Arr(props),
                    ["sample"] = Arr(sorted.Where((_, i) => i % step == 0).ToArray()),
                    ["mean"] = sorted.Average(),
                };
            }
            else features[name] = new JsonObject { ["kind"] = "categorical", ["freq"] = Frequencies(col) };
        }
        return new JsonObject { ["features"] = features, ["labels"] = labels };
    }

    /// <summary>Compares current data with the baseline → [{feature, kind, psi, ks?, status}]; overall status = the worst.</summary>
    public static (JsonArray Results, string Status) Compare(JsonObject baseline, IReadOnlyList<JsonObject> features, IReadOnlyList<string?> predictedLabels,
        double warn, double alert)
    {
        var results = new JsonArray();
        var worst = 0;
        string Grade(double psi) { var g = psi >= alert ? 2 : psi >= warn ? 1 : 0; worst = Math.Max(worst, g); return g switch { 2 => "alert", 1 => "warn", _ => "ok" }; }
        foreach (var (name, node) in baseline["features"]!.AsObject())
        {
            var b = node!.AsObject();
            var values = features.Select(f => f[name]).Where(v => v is not null).ToList();
            if (values.Count == 0) { results.Add((JsonNode)new JsonObject { ["feature"] = name, ["kind"] = b["kind"]!.ToString(), ["status"] = "missing", ["samples"] = 0 }); continue; }
            if (b["kind"]!.ToString() == "numeric")
            {
                var cur = values.Select(v => v is JsonValue jv && jv.TryGetValue<double>(out var d) ? d : double.NaN).Where(d => !double.IsNaN(d)).Order().ToArray();
                if (cur.Length == 0) { results.Add((JsonNode)new JsonObject { ["feature"] = name, ["kind"] = "numeric", ["status"] = "missing", ["samples"] = 0 }); continue; }
                var edges = b["edges"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
                var baseProps = b["props"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
                var psi = Psi(baseProps, Histogram(cur, edges));
                var ks = Ks(b["sample"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray(), cur);
                results.Add((JsonNode)new JsonObject
                {
                    ["feature"] = name, ["kind"] = "numeric", ["psi"] = Math.Round(psi, 4), ["ks"] = Math.Round(ks, 4), ["samples"] = cur.Length,
                    ["baseline_mean"] = Math.Round(b["mean"]!.GetValue<double>(), 4), ["current_mean"] = Math.Round(cur.Average(), 4), ["status"] = Grade(psi),
                });
            }
            else
            {
                var psi = PsiCategorical(b["freq"]!.AsObject(), values.Select(v => v!.ToString()).ToList());
                results.Add((JsonNode)new JsonObject { ["feature"] = name, ["kind"] = "categorical", ["psi"] = Math.Round(psi, 4), ["samples"] = values.Count, ["status"] = Grade(psi) });
            }
        }
        if (baseline["labels"] is JsonObject labelFreq && predictedLabels.Any(l => l is not null))
        {
            var psi = PsiCategorical(labelFreq, predictedLabels.Where(l => l is not null).Select(l => l!).ToList());
            results.Add((JsonNode)new JsonObject { ["feature"] = "(prediction)", ["kind"] = "prediction", ["psi"] = Math.Round(psi, 4), ["samples"] = predictedLabels.Count, ["status"] = Grade(psi) });
        }
        return (results, worst switch { 2 => "alert", 1 => "warn", _ => "ok" });
    }

    private static double Quantile(double[] sorted, double q)
    {
        var pos = q * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(sorted.Length - 1, lo + 1);
        return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    /// <summary>Proportion falling into each bin (bin i: (edge[i-1], edge[i]]; both ends open).</summary>
    private static double[] Histogram(double[] values, double[] edges)
    {
        var counts = new double[edges.Length + 1];
        foreach (var v in values)
        {
            var i = 0;
            while (i < edges.Length && v > edges[i]) i++;
            counts[i]++;
        }
        return counts.Select(c => c / Math.Max(1, values.Length)).ToArray();
    }

    private static double Psi(double[] expected, double[] actual) =>
        expected.Zip(actual, (e, a) => (e: Math.Max(e, Eps), a: Math.Max(a, Eps))).Sum(x => (x.a - x.e) * Math.Log(x.a / x.e));

    private static double PsiCategorical(JsonObject baseFreq, List<string> current)
    {
        var keys = baseFreq.Select(kv => kv.Key).ToList();
        var exp = keys.Select(k => baseFreq[k]!.GetValue<double>()).Append(Math.Max(0, 1 - keys.Sum(k => baseFreq[k]!.GetValue<double>()))).ToArray();
        var n = Math.Max(1, current.Count);
        var act = keys.Select(k => current.Count(v => v == k) / (double)n).Append(current.Count(v => !keys.Contains(v)) / (double)n).ToArray();
        return Psi(exp, act);
    }

    /// <summary>Two-sample Kolmogorov–Smirnov statistic (largest distance between the two empirical CDFs).</summary>
    private static double Ks(double[] a, double[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        int i = 0, j = 0;
        double d = 0;
        while (i < a.Length && j < b.Length)
        {
            var x = Math.Min(a[i], b[j]);
            while (i < a.Length && a[i] <= x) i++;
            while (j < b.Length && b[j] <= x) j++;
            d = Math.Max(d, Math.Abs(i / (double)a.Length - j / (double)b.Length));
        }
        return d;
    }

    private static JsonObject Frequencies(List<string> values)
    {
        var n = Math.Max(1, values.Count);
        var o = new JsonObject();
        foreach (var g in values.GroupBy(v => v).OrderByDescending(g => g.Count()).Take(50)) o[g.Key] = Math.Round(g.Count() / (double)n, 6);
        return o;
    }

    private static JsonArray Arr(IEnumerable<double> xs) => new(xs.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());

    /// <summary>Simple CSV (commas, no nested quotes) — enough for tabular dataset baselines in the first slice.</summary>
    public static (string[] Header, List<string[]> Rows) ParseCsv(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return ([], []);
        return (lines[0].Split(',').Select(h => h.Trim()).ToArray(), lines.Skip(1).Select(l => l.Split(',').Select(v => v.Trim()).ToArray()).ToList());
    }
}
