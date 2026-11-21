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

public sealed record PredictionRow(Guid Id, DateTimeOffset At, double LatencyMs, string Features, string Prediction, string? GroundTruth);
public sealed record MonitorConfigRow(Guid DeploymentId, Guid WorkspaceId, Guid? DatasetVersionId, string LabelColumn, string? Baseline, long BaselineRows,
    double PsiWarn, double PsiAlert, int WindowSize, int MinSamples, bool Enabled, DateTimeOffset UpdatedAt);
public sealed record SnapshotRow(Guid Id, DateTimeOffset At, int Samples, string Status, string Drift, string Quality);
public sealed record DailyQualityRow(DateTimeOffset Day, long Labeled, double? Accuracy, double? Mae);

public static partial class MonitorSql
{
    [SqlQuery("Monitor/DeleteDeploymentData.sql")]
    public static partial Task DeleteDeploymentDataAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Monitor/InsertPrediction.sql")]
    public static partial Task InsertPredictionAsync(this IWriteSession db, Guid id, Guid deploymentId, int modelVersion, DateTimeOffset now, double latencyMs,
        string features, string prediction, CancellationToken ct);
    [SqlQuery("Monitor/SetGroundTruth.sql")]
    public static partial Task<long> SetGroundTruthAsync(this IWriteSession db, Guid id, Guid deploymentId, string groundTruth, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Monitor/RecentPredictions.sql")]
    public static partial Task<List<PredictionRow>> RecentPredictionsAsync(this IReadSession db, Guid deploymentId, int limit, CancellationToken ct);
    [SqlQuery("Monitor/CountSince.sql")]
    public static partial Task<long> CountSinceAsync(this IReadSession db, Guid deploymentId, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Monitor/UpsertConfig.sql")]
    public static partial Task UpsertConfigAsync(this IWriteSession db, Guid deploymentId, Guid workspaceId, Guid? datasetVersionId, string labelColumn,
        string? baseline, long baselineRows, double psiWarn, double psiAlert, int windowSize, int minSamples, bool enabled, Guid updatedBy,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Monitor/GetConfig.sql")]
    public static partial Task<MonitorConfigRow?> GetConfigAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Monitor/ListEnabledConfigs.sql")]
    public static partial Task<List<MonitorConfigRow>> ListEnabledConfigsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Monitor/InsertSnapshot.sql")]
    public static partial Task InsertSnapshotAsync(this IWriteSession db, Guid id, Guid deploymentId, DateTimeOffset now, int samples, string status,
        string drift, string quality, CancellationToken ct);
    [SqlQuery("Monitor/ListSnapshots.sql")]
    public static partial Task<List<SnapshotRow>> ListSnapshotsAsync(this IReadSession db, Guid deploymentId, int limit, CancellationToken ct);
    [SqlQuery("Monitor/DailyQuality.sql")]
    public static partial Task<List<DailyQualityRow>> DailyQualityAsync(this IReadSession db, Guid deploymentId, DateTimeOffset since, CancellationToken ct);
}

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

// ───────────── Gateway: prediction log + ground-truth labels ─────────────

/// <summary>Gateway logs each prediction (no user, no audit). Returns an id so the client can send the ground-truth label later.</summary>
public sealed record LogPredictionsCommand(Guid DeploymentId, int ModelVersion, double LatencyMs, IReadOnlyList<(string Features, string Prediction)> Items)
    : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class LogPredictionsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<LogPredictionsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(LogPredictionsCommand c, CancellationToken ct)
    {
        var ids = new List<Guid>(c.Items.Count);
        var now = clock.UtcNow;
        var each = c.LatencyMs / Math.Max(1, c.Items.Count);
        foreach (var (features, prediction) in c.Items)
        {
            var id = Ids.New();
            await db.InsertPredictionAsync(id, c.DeploymentId, c.ModelVersion, now, each, features, prediction, ct);
            ids.Add(id);
        }
        return ids;
    }
}

/// <summary>POST /feedback: attaches a late-arriving ground-truth label to a prediction (only predictions of that same deployment).</summary>
public sealed record RecordFeedbackCommand(Guid DeploymentId, IReadOnlyList<(Guid PredictionId, string GroundTruth)> Items) : ICommand<long>, IInternalCommand;

public sealed class RecordFeedbackCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordFeedbackCommand, long>
{
    public async ValueTask<long> Handle(RecordFeedbackCommand c, CancellationToken ct)
    {
        long n = 0;
        foreach (var (id, gt) in c.Items) n += await db.SetGroundTruthAsync(id, c.DeploymentId, gt, clock.UtcNow, ct);
        return n;
    }
}

// ───────────── Config + baseline ─────────────

public sealed record MonitorView(MonitorConfigRow? Config, SnapshotRow? Latest);

public sealed record GetMonitorQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<MonitorView>;

public sealed class GetMonitorQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMonitorQuery, MonitorView>
{
    public async ValueTask<MonitorView> Handle(GetMonitorQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return new MonitorView(await db.GetConfigAsync(q.DeploymentId, ct), (await db.ListSnapshotsAsync(q.DeploymentId, 1, ct)).FirstOrDefault());
    }
}

/// <summary>Sets the baseline = a tabular dataset version (reads the version's CSV from S3 storage, ≤ 50 MB) + PSI thresholds.</summary>
public sealed record ConfigureMonitorCommand(Guid WorkspaceId, Guid DeploymentId, Guid DatasetVersionId, string LabelColumn, double PsiWarn, double PsiAlert,
    int WindowSize, int MinSamples, bool Enabled) : ICommand<MonitorConfigRow>;

public sealed class ConfigureMonitorCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<ConfigureMonitorCommand, MonitorConfigRow>
{
    public async ValueTask<MonitorConfigRow> Handle(ConfigureMonitorCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var v = await db.GetVersionAsync(c.DatasetVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        string[] header = [];
        var rows = new List<string[]>();
        long total = 0;
        foreach (var f in (await db.ListVersionFilesAsync(v.Id, ct)).Where(f => f.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            total += f.SizeBytes;
            if (total > 50L * 1024 * 1024) throw new ResourceExhaustedException("MONITOR_BASELINE_TOO_LARGE", "Baseline CSV > 50 MB (sampling comes later)");
            var bytes = await s3.GetAsync(Buckets.Datasets, v.Prefix + f.Path, ct) ?? [];
            var (h, r) = Drift.ParseCsv(Encoding.UTF8.GetString(bytes));
            if (header.Length == 0) header = h;
            if (h.SequenceEqual(header)) rows.AddRange(r);
        }
        if (header.Length == 0 || rows.Count == 0)
            throw new PreconditionException("MONITOR_BASELINE_UNSUPPORTED", "Dataset version has no tabular CSV to use as a baseline");
        var label = string.IsNullOrWhiteSpace(c.LabelColumn) ? "label" : c.LabelColumn.Trim();
        var baseline = Drift.BuildBaseline(header, rows, label);
        var warn = c.PsiWarn > 0 ? c.PsiWarn : 0.1;
        var alert = c.PsiAlert > warn ? c.PsiAlert : Math.Max(0.25, warn * 2);
        await db.UpsertConfigAsync(c.DeploymentId, c.WorkspaceId, v.Id, label, baseline.ToJsonString(), rows.Count, warn, alert,
            c.WindowSize is >= 20 and <= 100_000 ? c.WindowSize : 500, c.MinSamples is >= 1 and <= 10_000 ? c.MinSamples : 20, c.Enabled, uid, clock.UtcNow, ct);
        return (await db.GetConfigAsync(c.DeploymentId, ct))!;
    }
}

// ───────────── Drift + quality ─────────────

/// <summary>Computes a drift/quality snapshot over the `last_n` most recent predictions (0 = window_size); records the snapshot; transition to alert → M16.</summary>
public sealed record ComputeDriftCommand(Guid WorkspaceId, Guid DeploymentId, int LastN) : ICommand<SnapshotRow>;

public sealed class ComputeDriftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ComputeDriftCommand, SnapshotRow>
{
    public async ValueTask<SnapshotRow> Handle(ComputeDriftCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        return await DriftJob.RunAsync(db, clock, c.DeploymentId, c.LastN, ct);
    }
}

/// <summary>Called by the monitoring loop: runs only when there are enough new predictions since the previous snapshot.</summary>
public sealed record MonitorTickCommand : ICommand<int>, IInternalCommand;

public sealed class MonitorTickCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MonitorTickCommand, int>
{
    public async ValueTask<int> Handle(MonitorTickCommand c, CancellationToken ct)
    {
        var n = 0;
        foreach (var cfg in await db.ListEnabledConfigsAsync(ct))
        {
            var last = (await db.ListSnapshotsAsync(cfg.DeploymentId, 1, ct)).FirstOrDefault();
            if (await db.CountSinceAsync(cfg.DeploymentId, last?.At ?? DateTimeOffset.MinValue, ct) < cfg.MinSamples) continue;
            await DriftJob.RunAsync(db, clock, cfg.DeploymentId, 0, ct);
            n++;
        }
        return n;
    }
}

internal static class DriftJob
{
    public static async Task<SnapshotRow> RunAsync(IWriteSession db, IClock clock, Guid deploymentId, int lastN, CancellationToken ct)
    {
        var cfg = await db.GetConfigAsync(deploymentId, ct);
        if (cfg?.Baseline is null) throw new PreconditionException("MONITOR_NO_BASELINE", "No baseline set for the deployment");
        var preds = await db.RecentPredictionsAsync(deploymentId, lastN > 0 ? Math.Min(lastN, 100_000) : cfg.WindowSize, ct);
        if (preds.Count < Math.Min(cfg.MinSamples, lastN > 0 ? lastN : cfg.MinSamples))
            throw new PreconditionException("MONITOR_NOT_ENOUGH_SAMPLES", $"At least {cfg.MinSamples} predictions are required (have {preds.Count})");
        var features = preds.Select(p => JsonNode.Parse(p.Features) as JsonObject ?? []).ToList();
        var predObjs = preds.Select(p => JsonNode.Parse(p.Prediction) as JsonObject ?? []).ToList();
        var (drift, status) = Drift.Compare(JsonNode.Parse(cfg.Baseline)!.AsObject(), features, predObjs.Select(p => p["label"]?.ToString()).ToList(),
            cfg.PsiWarn, cfg.PsiAlert);

        var labeled = preds.Zip(predObjs).Where(x => x.First.GroundTruth is not null)
            .Select(x => (Pred: x.Second, Gt: JsonNode.Parse(x.First.GroundTruth!) as JsonObject ?? [])).ToList();
        var quality = new JsonObject { ["labeled"] = labeled.Count };
        var cls = labeled.Where(x => x.Gt["label"] is not null).ToList();
        if (cls.Count > 0) quality["accuracy"] = Math.Round(cls.Count(x => x.Pred["label"]?.ToString() == x.Gt["label"]!.ToString()) / (double)cls.Count, 4);
        var reg = labeled.Where(x => x.Gt["value"] is not null && x.Pred["value"] is not null).ToList();
        if (reg.Count > 0) quality["mae"] = Math.Round(reg.Average(x => Math.Abs(x.Pred["value"]!.GetValue<double>() - x.Gt["value"]!.GetValue<double>())), 4);

        var previous = (await db.ListSnapshotsAsync(deploymentId, 1, ct)).FirstOrDefault();
        var id = Ids.New();
        var now = clock.UtcNow;
        await db.InsertSnapshotAsync(id, deploymentId, now, preds.Count, status, drift.ToJsonString(), quality.ToJsonString(), ct);
        if (status == "alert" && previous?.Status != "alert" && await db.DeploymentInfoAsync(deploymentId, ct) is { } d)
        {
            var worst = drift.OfType<JsonObject>().Where(r => r["psi"] is not null).OrderByDescending(r => r["psi"]!.GetValue<double>()).First();
            await Notifier.SendAsync(db, clock, d.WorkspaceId, (await Notifier.AdminsAsync(db, d.WorkspaceId, ct)).Append(d.CreatedBy), "deployment.drift",
                $"Deployment {d.Slug} data drift: {worst["feature"]} PSI {worst["psi"]}", "#/deployments/" + d.Id, $"deployment/{d.Id}", "warning", ct);
            // Continuous training (M14): pipelines in the workspace with a deployment.drift event trigger run with the deployment as a parameter.
            await MLPlatform.ControlPlane.Application.Pipelines.PipelineTriggers.FireAsync(db, clock, "deployment.drift", d.WorkspaceId,
                new JsonObject { ["deployment_id"] = d.Id.ToString(), ["deployment"] = d.Slug }, ct);
        }
        return new SnapshotRow(id, now, preds.Count, status, drift.ToJsonString(), quality.ToJsonString());
    }
}

public sealed record ListSnapshotsQuery(Guid WorkspaceId, Guid DeploymentId, int Limit) : IQuery<IReadOnlyList<SnapshotRow>>;

public sealed class ListSnapshotsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListSnapshotsQuery, IReadOnlyList<SnapshotRow>>
{
    public async ValueTask<IReadOnlyList<SnapshotRow>> Handle(ListSnapshotsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.ListSnapshotsAsync(q.DeploymentId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}

public sealed record GetQualityQuery(Guid WorkspaceId, Guid DeploymentId, int Days) : IQuery<IReadOnlyList<DailyQualityRow>>;

public sealed class GetQualityQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetQualityQuery, IReadOnlyList<DailyQualityRow>>
{
    public async ValueTask<IReadOnlyList<DailyQualityRow>> Handle(GetQualityQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.DailyQualityAsync(q.DeploymentId, clock.UtcNow.AddDays(-(q.Days is > 0 and <= 365 ? q.Days : 30)), ct);
    }
}

public sealed record ListPredictionsQuery(Guid WorkspaceId, Guid DeploymentId, int Limit) : IQuery<IReadOnlyList<PredictionRow>>;

public sealed class ListPredictionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPredictionsQuery, IReadOnlyList<PredictionRow>>
{
    public async ValueTask<IReadOnlyList<PredictionRow>> Handle(ListPredictionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.RecentPredictionsAsync(q.DeploymentId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}
