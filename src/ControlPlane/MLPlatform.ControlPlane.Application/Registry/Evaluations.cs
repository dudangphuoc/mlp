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

public sealed record EvaluationRow(Guid Id, Guid WorkspaceId, Guid ModelVersionId, Guid DatasetVersionId, string PathFilter, int MaxSamples,
    double ConfThreshold, Guid? DeploymentId, string State, string? StateReason, int Total, int Done, int Errors, string Metrics, string PerClass,
    string? Confusion, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
public sealed record EvalSampleRow(int Idx, string Path, int Width, int Height, string Gt, string Pred, int Tp, int Fp, int Fn, bool Correct,
    string? Error, double LatencyMs);
public sealed record IdxRow(int Idx);

public static partial class EvaluationSql
{
    [SqlQuery("Registry/InsertEvaluation.sql")]
    public static partial Task InsertEvaluationAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid modelVersionId, Guid datasetVersionId,
        string pathFilter, int maxSamples, double confThreshold, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/GetEvaluation.sql")]
    public static partial Task<EvaluationRow?> GetEvaluationAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Registry/ListEvaluations.sql")]
    public static partial Task<List<EvaluationRow>> ListEvaluationsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
    [SqlQuery("Registry/ListActiveEvaluations.sql")]
    public static partial Task<List<EvaluationRow>> ListActiveEvaluationsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Registry/SetEvaluationState.sql")]
    public static partial Task SetEvaluationStateAsync(this IWriteSession db, Guid id, string state, string? reason, Guid? deploymentId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/SetEvaluationTotal.sql")]
    public static partial Task SetEvaluationTotalAsync(this IWriteSession db, Guid id, int total, CancellationToken ct);
    [SqlQuery("Registry/InsertEvalSample.sql")]
    public static partial Task InsertEvalSampleAsync(this IWriteSession db, Guid evaluationId, int idx, string path, int width, int height, string gt,
        string pred, int tp, int fp, int fn, bool correct, string? error, double latencyMs, CancellationToken ct);
    [SqlQuery("Registry/DoneSampleIndexes.sql")]
    public static partial Task<List<IdxRow>> DoneSampleIndexesAsync(this IReadSession db, Guid evaluationId, CancellationToken ct);
    [SqlQuery("Registry/AllEvalSamples.sql")]
    public static partial Task<List<EvalSampleRow>> AllEvalSamplesAsync(this IReadSession db, Guid evaluationId, CancellationToken ct);
    [SqlQuery("Registry/ListEvalSamples.sql")]
    public static partial Task<List<EvalSampleRow>> ListEvalSamplesAsync(this IReadSession db, Guid evaluationId, bool errorsOnly, int offset, int limit, CancellationToken ct);
    [SqlQuery("Registry/CompleteEvaluation.sql")]
    public static partial Task CompleteEvaluationAsync(this IWriteSession db, Guid id, string metrics, string perClass, string? confusion, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/MergeVersionMetrics.sql")]
    public static partial Task MergeVersionMetricsAsync(this IWriteSession db, Guid id, string metrics, CancellationToken ct);
}

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

// ───────────── User commands / queries ─────────────

public sealed record StartEvaluationCommand(Guid WorkspaceId, Guid ModelVersionId, Guid DatasetVersionId, string PathFilter, int MaxSamples, double ConfThreshold)
    : ICommand<EvaluationRow>;

public sealed class StartEvaluationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StartEvaluationCommand, EvaluationRow>
{
    public async ValueTask<EvaluationRow> Handle(StartEvaluationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        if (!EvalMath.Supports(v.Task))
            throw new PreconditionException("EVAL_TASK_UNSUPPORTED", $"Evaluation does not support task {v.Task} yet (supported: detect, classify, zero-shot image tasks)");
        _ = await db.GetVersionAsync(c.DatasetVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        if (c.MaxSamples is < 1 or > Evaluations.MaxSamplesLimit)
            throw new ValidationException(new Dictionary<string, string> { ["max_samples"] = $"1–{Evaluations.MaxSamplesLimit}" });
        if (c.ConfThreshold is < 0.01 or > 0.99) throw new ValidationException(new Dictionary<string, string> { ["conf_threshold"] = "0.01–0.99" });
        var id = Ids.New();
        await db.InsertEvaluationAsync(id, c.WorkspaceId, v.Id, c.DatasetVersionId, c.PathFilter.Trim(), c.MaxSamples, c.ConfThreshold, uid, clock.UtcNow, ct);
        return (await db.GetEvaluationAsync(id, ct))!;
    }
}

public sealed record ListEvaluationsQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<IReadOnlyList<EvaluationRow>>;

public sealed class ListEvaluationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvaluationsQuery, IReadOnlyList<EvaluationRow>>
{
    public async ValueTask<IReadOnlyList<EvaluationRow>> Handle(ListEvaluationsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvaluationsAsync(q.WorkspaceId, q.ModelVersionId, ct);
    }
}

public sealed record GetEvaluationQuery(Guid WorkspaceId, Guid EvaluationId) : IQuery<EvaluationRow>;

public sealed class GetEvaluationQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetEvaluationQuery, EvaluationRow>
{
    public async ValueTask<EvaluationRow> Handle(GetEvaluationQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct);
        return e is not null && e.WorkspaceId == q.WorkspaceId ? e : throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
    }
}

/// <summary>Per-sample results for the gallery, worst first, with a browser-loadable image URL.</summary>
public sealed record ListEvalSamplesQuery(Guid WorkspaceId, Guid EvaluationId, bool ErrorsOnly, int Offset, int Limit)
    : IQuery<IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>>;

public sealed class ListEvalSamplesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, S3Client s3)
    : IQueryHandler<ListEvalSamplesQuery, IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>>
{
    public async ValueTask<IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>> Handle(ListEvalSamplesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct);
        if (e is null || e.WorkspaceId != q.WorkspaceId) throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var v = (await db.GetVersionAsync(e.DatasetVersionId, q.WorkspaceId, ct))!;
        var urls = (await db.ListVersionFilesAsync(v.Id, ct)).ToDictionary(f => f.Path, f => f.Url, StringComparer.Ordinal);
        var rows = await db.ListEvalSamplesAsync(e.Id, q.ErrorsOnly, Math.Max(0, q.Offset), Math.Clamp(q.Limit, 1, 200), ct);
        return rows.Select(r => (r, Evaluations.FileUrl(s3, v, r.Path, urls.GetValueOrDefault(r.Path), Audience.Public))).ToList();
    }
}

public sealed record CancelEvaluationCommand(Guid WorkspaceId, Guid EvaluationId) : ICommand<bool>;

public sealed class CancelEvaluationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CancelEvaluationCommand, bool>
{
    public async ValueTask<bool> Handle(CancelEvaluationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var e = await db.GetEvaluationAsync(c.EvaluationId, ct);
        if (e is null || e.WorkspaceId != c.WorkspaceId) throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        await db.SetEvaluationStateAsync(e.Id, "cancelled", "Cancelled by user", null, clock.UtcNow, ct);
        return true;
    }
}

// ───────────── Engine (EvaluationRunner in the api) ─────────────

public sealed record ListActiveEvaluationsQuery : IQuery<IReadOnlyList<EvaluationRow>>;

public sealed class ListActiveEvaluationsQueryHandler(IReadSession db) : IQueryHandler<ListActiveEvaluationsQuery, IReadOnlyList<EvaluationRow>>
{
    public async ValueTask<IReadOnlyList<EvaluationRow>> Handle(ListActiveEvaluationsQuery q, CancellationToken ct) => await db.ListActiveEvaluationsAsync(ct);
}

public sealed record EvaluationPlan(EvaluationRow Evaluation, string Task, IReadOnlyList<string> Classes, IReadOnlyList<(int Idx, string Image, string ImageUrl,
    string? Label, string? LabelUrl, string? ClassName)> Samples, string? CocoUrl, ISet<int> Done);

/// <summary>Builds the sample list (deterministic shuffle by evaluation id, then the first max_samples) with internal download URLs.</summary>
public sealed record PlanEvaluationQuery(Guid EvaluationId) : IQuery<EvaluationPlan>;

public sealed class PlanEvaluationQueryHandler(IReadSession db, S3Client s3) : IQueryHandler<PlanEvaluationQuery, EvaluationPlan>
{
    public async ValueTask<EvaluationPlan> Handle(PlanEvaluationQuery q, CancellationToken ct)
    {
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct) ?? throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var mv = await db.GetModelVersionAsync(e.ModelVersionId, e.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var v = await db.GetVersionAsync(e.DatasetVersionId, e.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var files = await db.ListVersionFilesAsync(v.Id, ct);
        var urls = files.ToDictionary(f => f.Path, f => f.Url, StringComparer.Ordinal);
        string Url(string p) => Evaluations.FileUrl(s3, v, p, urls.GetValueOrDefault(p), Audience.Internal);
        var classes = Evaluations.Classes((await db.GetCurrentSchemaAsync(v.DatasetId, ct))?.Classes);
        var specs = EvalMath.FindSamples(mv.Task, urls.Keys, e.PathFilter);
        // COCO ground truth: one annotations json (preferring one whose path matches the filter, e.g. val/annotations.json).
        string? coco = null;
        if (EvalMath.DetectTasks.Contains(mv.Task) && specs.All(s => s.LabelPath is null))
            coco = urls.Keys.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && (p.Contains("annotation", StringComparison.OrdinalIgnoreCase) || p.Contains("instances", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => !string.IsNullOrEmpty(e.PathFilter) && p.Contains(e.PathFilter, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (EvalMath.DetectTasks.Contains(mv.Task) && coco is null && specs.All(s => s.LabelPath is null))
            throw new PreconditionException("EVAL_NO_GROUND_TRUTH", "No labels found next to the images (YOLO txt or COCO json)");
        // Same dataset version, filter and sample count → the same images, so two versions (e.g. a model and its optimized copy) are compared fairly.
        var seed = $"{e.DatasetVersionId}|{e.PathFilter}|{e.MaxSamples}";
        var chosen = specs.OrderBy(s => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed + ":" + s.ImagePath))), StringComparer.Ordinal)
            .Take(e.MaxSamples).ToList();
        var done = (await db.DoneSampleIndexesAsync(e.Id, ct)).Select(r => r.Idx).ToHashSet();
        return new EvaluationPlan(e, mv.Task, classes,
            chosen.Select((s, i) => (i, s.ImagePath, Url(s.ImagePath), s.LabelPath, s.LabelPath is null ? null : Url(s.LabelPath), s.ClassName)).ToList(),
            coco is null ? null : Url(coco), done);
    }
}

public sealed record SetEvaluationStateCommand(Guid EvaluationId, string State, string? Reason, Guid? DeploymentId, int? Total = null) : ICommand<bool>, IInternalCommand;

public sealed class SetEvaluationStateCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetEvaluationStateCommand, bool>
{
    public async ValueTask<bool> Handle(SetEvaluationStateCommand c, CancellationToken ct)
    {
        await db.SetEvaluationStateAsync(c.EvaluationId, c.State, c.Reason, c.DeploymentId, clock.UtcNow, ct);
        if (c.Total is { } t) await db.SetEvaluationTotalAsync(c.EvaluationId, t, ct);
        if (c.State == "failed" && await db.GetEvaluationAsync(c.EvaluationId, ct) is { } e)
            await Notifier.SendAsync(db, clock, e.WorkspaceId, [e.CreatedBy], "model.evaluated", $"Model evaluation failed: {c.Reason}",
                $"#/models?eval={e.Id}", $"evaluation/{e.Id}", "error", ct);
        return true;
    }
}

public sealed record RecordEvalSampleCommand(Guid EvaluationId, int Idx, string Path, int Width, int Height, string Gt, string Pred, int Tp, int Fp, int Fn,
    bool Correct, string? Error, double LatencyMs) : ICommand<bool>, IInternalCommand;

public sealed class RecordEvalSampleCommandHandler(IWriteSession db) : ICommandHandler<RecordEvalSampleCommand, bool>
{
    public async ValueTask<bool> Handle(RecordEvalSampleCommand c, CancellationToken ct)
    {
        await db.InsertEvalSampleAsync(c.EvaluationId, c.Idx, c.Path, c.Width, c.Height, c.Gt, c.Pred, c.Tp, c.Fp, c.Fn, c.Correct, c.Error, c.LatencyMs, ct);
        return true;
    }
}

/// <summary>Computes the metrics from the stored samples, merges test/* into the model version, notifies the requester.</summary>
public sealed record CompleteEvaluationCommand(Guid EvaluationId) : ICommand<bool>, IInternalCommand;

public sealed class CompleteEvaluationCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<CompleteEvaluationCommand, bool>
{
    public async ValueTask<bool> Handle(CompleteEvaluationCommand c, CancellationToken ct)
    {
        var e = await db.GetEvaluationAsync(c.EvaluationId, ct) ?? throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var mv = (await db.GetModelVersionAsync(e.ModelVersionId, e.WorkspaceId, ct))!;
        var rows = (await db.AllEvalSamplesAsync(e.Id, ct)).Where(r => r.Error is null).ToList();
        if (rows.Count == 0) throw new PreconditionException("EVAL_NO_SAMPLES", "No sample could be evaluated");
        JsonObject metrics;
        JsonArray perClass;
        JsonObject? confusion = null;
        if (EvalMath.DetectTasks.Contains(mv.Task))
        {
            var images = rows.Select(r => ((IReadOnlyList<EvalBox>)Boxes(r.Gt), (IReadOnlyList<EvalBox>)Boxes(r.Pred))).ToList();
            var s = EvalMath.Detection(images, e.ConfThreshold);
            var sweep = EvalMath.ThresholdSweep(images);
            var best = sweep.MaxBy(x => x.F1);
            metrics = new JsonObject
            {
                ["test/mAP50-95"] = R(s.Map5095), ["test/mAP50"] = R(s.Map50), ["test/precision"] = R(s.Precision), ["test/recall"] = R(s.Recall),
                ["test/f1"] = R(s.F1), ["test/tp"] = s.Tp, ["test/fp"] = s.Fp, ["test/fn"] = s.Fn,
                ["test/best_conf"] = best.Conf, ["test/best_f1"] = R(best.F1), ["test/best_precision"] = R(best.Precision), ["test/best_recall"] = R(best.Recall),
            };
            confusion = new JsonObject
            {
                ["sweep"] = new JsonArray(sweep.Select(x => (JsonNode)new JsonObject { ["conf"] = x.Conf, ["precision"] = R(x.Precision), ["recall"] = R(x.Recall), ["f1"] = R(x.F1) }).ToArray()),
            };
            perClass = new JsonArray(s.PerClass.Select(p => (JsonNode)new JsonObject
            {
                ["class"] = p.Cls, ["gt"] = p.Gt, ["pred"] = p.Pred, ["ap50"] = R(p.Ap50), ["ap50_95"] = R(p.Ap5095),
                ["precision"] = R(p.Precision), ["recall"] = R(p.Recall),
            }).ToArray());
        }
        else
        {
            var s = EvalMath.Classification(rows.Select(r => (Label(r.Gt), Label(r.Pred))).ToList());
            metrics = new JsonObject { ["test/accuracy"] = R(s.Accuracy), ["test/f1_macro"] = R(s.F1Macro) };
            perClass = new JsonArray(s.PerClass.Select(p => (JsonNode)new JsonObject
            {
                ["class"] = p.Cls, ["gt"] = p.Support, ["precision"] = R(p.Precision), ["recall"] = R(p.Recall), ["f1"] = R(p.F1),
            }).ToArray());
            confusion = new JsonObject
            {
                ["labels"] = new JsonArray(s.Labels.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
                ["matrix"] = new JsonArray(s.Confusion.Select(row => (JsonNode)new JsonArray(row.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())).ToArray()),
            };
        }
        metrics["test/samples"] = rows.Count;
        metrics["test/latency_ms"] = Math.Round(rows.Average(r => r.LatencyMs), 1);
        await db.CompleteEvaluationAsync(e.Id, metrics.ToJsonString(), perClass.ToJsonString(), confusion?.ToJsonString(), clock.UtcNow, ct);
        await db.MergeVersionMetricsAsync(mv.Id, metrics.ToJsonString(), ct);
        var headline = metrics["test/mAP50-95"] is { } m ? $"mAP50-95 {m}" : $"accuracy {metrics["test/accuracy"]}";
        await Notifier.SendAsync(db, clock, e.WorkspaceId, [e.CreatedBy], "model.evaluated", $"Evaluation of {mv.ModelName} v{mv.Version} finished: {headline}",
            $"#/models?eval={e.Id}", $"evaluation/{e.Id}", "success", ct);
        return true;
    }

    private static double R(double v) => Math.Round(v, 4);

    private static List<EvalBox> Boxes(string json) =>
        (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>().Select(o =>
        {
            var b = o["b"]!.AsArray();
            return new EvalBox(o["c"]!.ToString(), b[0]!.GetValue<double>(), b[1]!.GetValue<double>(), b[2]!.GetValue<double>(), b[3]!.GetValue<double>(),
                o["s"]?.GetValue<double>() ?? 1);
        }).ToList();

    private static string Label(string json) => (JsonNode.Parse(json) as JsonObject)?["c"]?.ToString() ?? "";
}

/// <summary>Model versions registered from a run — the run page offers Test / Evaluate on them right after training.</summary>
public sealed record ListVersionsByRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<ModelVersionRow>>;

public sealed class ListVersionsByRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionsByRunQuery, IReadOnlyList<ModelVersionRow>>
{
    public async ValueTask<IReadOnlyList<ModelVersionRow>> Handle(ListVersionsByRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListVersionsByRunAsync(q.RunId, q.WorkspaceId, ct);
    }
}
