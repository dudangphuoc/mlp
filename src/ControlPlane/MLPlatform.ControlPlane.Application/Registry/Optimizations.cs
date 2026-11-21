using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>One optimization run with its source version; <c>Report</c> is the job's report.json, <c>Promoted</c> candidate → version id.</summary>
public sealed record OptimizationRow(Guid Id, Guid WorkspaceId, Guid SourceVersionId, Guid ExportId, string Device, string Candidates, Guid? HostId,
    Guid? CalibDatasetVersionId, Guid CheckDatasetVersionId, double Conf, string State, string Error, Guid? JobId, string Report, string Promoted,
    DateTimeOffset CreatedAt, string ModelName, int SourceVersion, string Task);

/// <summary>SQL of model optimization.</summary>
public static partial class OptimizationSql
{
    [SqlQuery("Registry/InsertOptimization.sql")]
    public static partial Task InsertOptimizationAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid sourceVersionId, Guid exportId, string device,
        string candidates, Guid? hostId, Guid? calibDatasetVersionId, Guid checkDatasetVersionId, double conf, Guid jobId, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/ListOptimizations.sql")]
    public static partial Task<List<OptimizationRow>> ListOptimizationsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/GetOptimization.sql")]
    public static partial Task<OptimizationRow?> GetOptimizationAsync(this IReadSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/FinishOptimization.sql")]
    public static partial Task FinishOptimizationAsync(this IWriteSession db, Guid workspaceId, Guid id, string state, string error, string report, CancellationToken ct);
    [SqlQuery("Registry/SetOptimizationPromoted.sql")]
    public static partial Task SetOptimizationPromotedAsync(this IWriteSession db, Guid workspaceId, Guid id, string candidate, Guid versionId, CancellationToken ct);
    [SqlQuery("Registry/DeleteOptimization.sql")]
    public static partial Task DeleteOptimizationAsync(this IWriteSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/InsertOptimizedVersion.sql")]
    public static partial Task InsertOptimizedVersionAsync(this IWriteSession db, Guid id, Guid modelId, int version, Guid? runId, string task, string prefix,
        long fileCount, long sizeBytes, Guid createdBy, DateTimeOffset now, string sourceRef, Guid parentVersionId, string optimization, CancellationToken ct);
    [SqlQuery("Registry/GetVersionDevice.sql")]
    public static partial Task<string?> GetVersionDeviceAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/GetModelVersionModelId.sql")]
    public static partial Task<Guid?> GetModelVersionModelIdAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/ListReadyOnnxExports.sql")]
    public static partial Task<List<ExportRow>> ListReadyOnnxExportsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
}

/// <summary>Rules of an optimization run: valid candidates per device, image picking, storage layout, the job's script.</summary>
public static class OptimizationRules
{
    /// <summary>Only pairs that make the model faster: FP16 pays off on a GPU, INT8 (without TensorRT) only on a CPU.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        ["cpu"] = ["fp32", "int8"],
        ["cuda"] = ["fp32", "fp16"],
    };
    public static readonly string[] Tasks = ["detect", "classify"];
    public static string Prefix(Guid ws, Guid id) => $"{ws}/optimizations/{id}/";
    public const int WorstImages = 6;

    /// <summary>The job's script (python/mlp_optimize/pipeline.py, embedded); also stored with every run so it can be run again byte for byte.</summary>
    public static string Script()
    {
        using var s = typeof(OptimizationRules).Assembly.GetManifestResourceStream("optimize/pipeline.py") ?? throw new InvalidOperationException("missing optimize/pipeline.py");
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>Deterministic pick of <paramref name="n"/> items (same seed → same images, so runs can be compared).</summary>
    public static List<T> Pick<T>(IEnumerable<T> items, int n, string seed, Func<T, string> key) =>
        [.. items.OrderBy(i => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + key(i)))), StringComparer.Ordinal).Take(n)];
}

/// <summary>Start an optimization run: picks calibration (train split) and check (val split) images, stores the spec and submits the job.</summary>
public sealed record StartOptimizationCommand(Guid WorkspaceId, Guid SourceVersionId, string Device, IReadOnlyList<string> Candidates, Guid? HostId,
    Guid CheckDatasetVersionId, int CheckImages, Guid? CalibDatasetVersionId, int CalibImages, double Conf, string CalibMethod) : ICommand<OptimizationRow>;

/// <summary>Validates the device × candidate pairs, signs every download and upload link and submits a job in the generic-onnx image.</summary>
public sealed class StartOptimizationCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<StartOptimizationCommand, OptimizationRow>
{
    public async ValueTask<OptimizationRow> Handle(StartOptimizationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var v = await db.GetModelVersionAsync(c.SourceVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        if (!OptimizationRules.Tasks.Contains(v.Task)) throw new PreconditionException("OPT_TASK", $"Task {v.Task} cannot be optimized yet (detect, classify)");
        if (!OptimizationRules.Allowed.TryGetValue(c.Device, out var allowed)) throw new ValidationException(new Dictionary<string, string> { ["device"] = "cpu | cuda" });
        var cands = c.Candidates.Distinct().ToList();
        if (cands.Count == 0 || cands.Any(x => !allowed.Contains(x)))
            throw new ValidationException(new Dictionary<string, string> { ["candidates"] = $"{c.Device}: {string.Join(", ", allowed)}" });
        if (c.CheckImages is < 10 or > 1000) throw new ValidationException(new Dictionary<string, string> { ["check_images"] = "10–1000" });
        var export = (await db.ListReadyOnnxExportsAsync(c.WorkspaceId, v.Id, ct)).FirstOrDefault()
                     ?? throw new PreconditionException("OPT_NEEDS_ONNX", "Convert this version to ONNX first");

        var id = Ids.New();
        var seed = id.ToString();
        var check = await Images(c.WorkspaceId, c.CheckDatasetVersionId, ["val", "test"], c.CheckImages, seed, ct);
        if (check.Count == 0) throw new PreconditionException("OPT_NO_IMAGES", "The check dataset version has no images");
        var calib = new List<OptImage>();
        if (cands.Contains("int8"))
        {
            if (c.CalibDatasetVersionId is not { } cv) throw new ValidationException(new Dictionary<string, string> { ["calib_dataset_version_id"] = "INT8 needs calibration images" });
            if (c.CalibImages is < 20 or > 2000) throw new ValidationException(new Dictionary<string, string> { ["calib_images"] = "20–2000" });
            calib = await Images(c.WorkspaceId, cv, ["train"], c.CalibImages, seed, ct);
            if (calib.Count == 0) throw new PreconditionException("OPT_NO_IMAGES", "The calibration dataset version has no train images");
        }

        var prefix = OptimizationRules.Prefix(c.WorkspaceId, id);
        string Put(string rel) => s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + rel, TimeSpan.FromHours(24), Audience.Workload);
        string Get(string bucket, string key) => s3.Presign(HttpMethod.Get, bucket, key, TimeSpan.FromHours(24), Audience.Workload);
        var uploads = new JsonObject { ["report.json"] = Put("report.json"), ["config/onnx.json"] = Put("config/onnx.json"), ["signature.json"] = Put("signature.json") };
        foreach (var x in cands) uploads[$"{x}/model.onnx"] = Put($"{x}/model.onnx");
        for (var i = 0; i < OptimizationRules.WorstImages; i++) uploads[$"worst/{i}.jpg"] = Put($"worst/{i}.jpg");
        var srcPrefix = (await db.GetVersionPrefixAsync(v.Id, c.WorkspaceId, ct))!;
        var hasClasses = (await s3.HeadAsync(Buckets.Artifacts, srcPrefix + "classes.json", ct)) is not null;
        JsonArray Files(IEnumerable<OptImage> l) => new(l.Select(f => (JsonNode)new JsonObject
            { ["path"] = f.Path, ["url"] = f.Url, ["size"] = f.Size, ["id"] = f.Id, ["cache"] = f.Id is null ? null : DataCacheRules.Mount(f.DatasetId) }).ToArray());
        var spec = new JsonObject
        {
            ["task"] = v.Task, ["device"] = c.Device, ["candidates"] = new JsonArray(cands.Select(x => (JsonNode)x).ToArray()),
            ["conf"] = c.Conf <= 0 ? 0.5 : c.Conf, ["threads"] = 4, ["warmup"] = 20, ["iters"] = 200,
            ["calib_method"] = c.CalibMethod == "percentile" ? "percentile" : "minmax", ["seed"] = seed,
            ["model_url"] = Get(Buckets.Artifacts, ExportRules.Prefix(c.WorkspaceId, export.Id) + export.FileName),
            ["classes_url"] = hasClasses ? Get(Buckets.Artifacts, srcPrefix + "classes.json") : null,
            ["calib"] = Files(calib), ["check"] = Files(check), ["uploads"] = uploads,
        };
        // The spec (hundreds of links) and the script live in storage: an environment variable is limited to 128 KB.
        await s3.PutAsync(Buckets.Artifacts, prefix + "spec.json", Encoding.UTF8.GetBytes(spec.ToJsonString()), "application/json", ct);
        await s3.PutAsync(Buckets.Artifacts, prefix + "pipeline.py", Encoding.UTF8.GetBytes(OptimizationRules.Script()), "text/x-python", ct);
        var job = new JobSpecDto
        {
            Image = "recipes/generic-onnx:current",
            Command = ["python3", "-c", "import os,urllib.request;exec(compile(urllib.request.urlopen(os.environ['MLP_OPT_SCRIPT_URL']).read(),'pipeline.py','exec'))"],
            Cpus = 4, Gpus = c.Device == "cuda" ? 1 : 0, GpuMemoryMb = c.Device == "cuda" ? 4096 : 0, TimeoutSeconds = 3 * 3600,
            Hosts = c.HostId is { } h ? [h] : [],
            Env = new Dictionary<string, string>
            {
                ["MLP_OPT_SPEC_URL"] = Get(Buckets.Artifacts, prefix + "spec.json"), ["MLP_OPT_SCRIPT_URL"] = Get(Buckets.Artifacts, prefix + "pipeline.py"),
                ["OMP_NUM_THREADS"] = "4",
                // host dataset caches the images are read from / downloaded into (mounted by the scheduler)
                ["MLP_DATA_CACHES"] = string.Join(',', check.Concat(calib).Where(f => f.Id is not null).Select(f => f.DatasetId.ToString("N")).Distinct()),
            },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "optimize", $"optimize-{v.ModelName}-v{v.Version}-{c.Device}", job), ct);
        await db.InsertOptimizationAsync(id, c.WorkspaceId, v.Id, export.Id, c.Device, string.Join(',', cands), c.HostId,
            cands.Contains("int8") ? c.CalibDatasetVersionId : null, c.CheckDatasetVersionId, c.Conf <= 0 ? 0.5 : c.Conf, jobId, uid, clock.UtcNow, ct);
        return (await db.GetOptimizationAsync(c.WorkspaceId, id, ct))!;
    }

    /// <summary>Images of a dataset version in the wanted splits (folders or the version's mlp-split.json), else any image; deterministic pick.</summary>
    private async Task<List<OptImage>> Images(Guid ws, Guid versionId, string[] splits, int n, string seed, CancellationToken ct)
    {
        await auth.RequireAsync(ws, "dataset.read", ct);
        var v = await db.GetVersionAsync(versionId, ws, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var files = await db.ListVersionFilesAsync(v.Id, ct);
        Dictionary<string, string>? fixedSplit = null;
        if (files.FirstOrDefault(f => f.Path == SplitRules.File) is { } sf)
        {
            byte[]? raw = sf.Url is null ? await s3.GetAsync(Buckets.Datasets, v.Prefix + sf.Path, ct)
                : sf.Url.StartsWith("s3://", StringComparison.Ordinal) && sf.Url[5..].IndexOf('/') is var slash and > 0
                    ? await s3.GetAsync(sf.Url[5..][..slash], sf.Url[5..][(slash + 1)..], ct) : null;
            try { fixedSplit = raw is null ? null : (JsonNode.Parse(raw)?["files"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "", StringComparer.Ordinal); }
            catch (JsonException) { }
        }
        var images = files.Where(f => SplitRules.IsImage(f.Path)).ToList();
        string? SplitOf(string p) => SplitRules.FolderSplit(p) ?? fixedSplit?.GetValueOrDefault(p);
        var wanted = images.Where(f => splits.Contains(SplitOf(f.Path))).ToList();
        if (wanted.Count == 0) wanted = images;
        return [.. OptimizationRules.Pick(wanted, n, seed, f => f.Path).Select(f => new OptImage(f.Path, Evaluations.FileUrl(s3, v, f.Path, f.Url, Audience.Workload),
            f.SizeBytes, DataCacheRules.Id(v, f.Path, f.Url, f.SizeBytes), v.DatasetId))];
    }
}

/// <summary>A check/calibration image of an optimization: link, size and host cache id (null = not cacheable).</summary>
public sealed record OptImage(string Path, string Url, long Size, string? Id, Guid DatasetId);

/// <summary>Optimization runs of the workspace, newest first; running ones are settled from their job first.</summary>
public sealed record ListOptimizationsCommand(Guid WorkspaceId) : ICommand<IReadOnlyList<OptimizationRow>>;

/// <summary>A finished job's report.json becomes the run's report (ready when at least one candidate was built).</summary>
public sealed class ListOptimizationsCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<ListOptimizationsCommand, IReadOnlyList<OptimizationRow>>
{
    public async ValueTask<IReadOnlyList<OptimizationRow>> Handle(ListOptimizationsCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var rows = await db.ListOptimizationsAsync(c.WorkspaceId, ct);
        var changed = false;
        foreach (var o in rows.Where(o => o.State == "running")) changed |= await Settle(db, s3, o, ct);
        return changed ? await db.ListOptimizationsAsync(c.WorkspaceId, ct) : rows;
    }

    /// <summary>Settles one running run from its job; true when the row changed.</summary>
    public static async Task<bool> Settle(IWriteSession db, S3Client s3, OptimizationRow o, CancellationToken ct)
    {
        var job = o.JobId is { } jid ? await db.GetJobAsync(jid, o.WorkspaceId, ct) : null;
        if (job is null) { await db.FinishOptimizationAsync(o.WorkspaceId, o.Id, "failed", "The job is gone", "{}", ct); return true; }
        if (job.State is not ("succeeded" or "failed" or "cancelled")) return false;
        JsonObject? report = null;
        try { report = await s3.GetAsync(Buckets.Artifacts, OptimizationRules.Prefix(o.WorkspaceId, o.Id) + "report.json", ct) is { } raw ? JsonNode.Parse(raw) as JsonObject : null; }
        catch (JsonException) { }
        var ok = report?["state"]?.ToString() == "ready";
        var error = ok ? "" : report?["error"]?.ToString() ?? (job.State == "cancelled" ? "cancelled" : job.StateReason ?? job.LastExitReason ?? "The job failed");
        await db.FinishOptimizationAsync(o.WorkspaceId, o.Id, ok ? "ready" : "failed", error, report?.ToJsonString() ?? "{}", ct);
        return true;
    }
}

/// <summary>One optimization run (settled first).</summary>
public sealed record GetOptimizationCommand(Guid WorkspaceId, Guid Id) : ICommand<OptimizationRow>;

/// <summary>Reads, settles if running, reads again.</summary>
public sealed class GetOptimizationCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<GetOptimizationCommand, OptimizationRow>
{
    public async ValueTask<OptimizationRow> Handle(GetOptimizationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        if (o.State == "running" && await ListOptimizationsCommandHandler.Settle(db, s3, o, ct)) o = (await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct))!;
        return o;
    }
}

/// <summary>A one-hour link to a file of a run: a candidate model, a disagreement image, the report, the spec or the script.</summary>
public sealed record GetOptimizationFileQuery(Guid WorkspaceId, Guid Id, string Path) : IQuery<string>;

/// <summary>Only the files a run writes; no path escapes the run's folder.</summary>
public sealed class GetOptimizationFileQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth) : IQueryHandler<GetOptimizationFileQuery, string>
{
    private static readonly System.Text.RegularExpressions.Regex Allowed = new(@"^(?:(?:fp32|fp16|int8)/model\.onnx|worst/\d\.jpg|report\.json|spec\.json|pipeline\.py|config/onnx\.json|signature\.json)$");

    public async ValueTask<string> Handle(GetOptimizationFileQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        if (!Allowed.IsMatch(q.Path)) throw new ValidationException(new Dictionary<string, string> { ["path"] = "Not a file of an optimization" });
        _ = await db.GetOptimizationAsync(q.WorkspaceId, q.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        return s3.Presign(HttpMethod.Get, Buckets.Artifacts, OptimizationRules.Prefix(q.WorkspaceId, q.Id) + q.Path, TimeSpan.FromHours(1));
    }
}

/// <summary>Save a candidate as a new version of the source's model (recipe generic-onnx), with lineage, report and checksums.</summary>
public sealed record PromoteOptimizationCommand(Guid WorkspaceId, Guid Id, string Candidate) : ICommand<ModelVersionRow>;

/// <summary>
/// Copies into <c>models/{model}/v{n}/</c>: weights/model.onnx, config/onnx.json, classes.json (from the source), signature.json, report.json,
/// pipeline.py and SHA256SUMS. The version keeps the source's run so it shows in the project, and its device decides GPU or CPU when deployed.
/// </summary>
public sealed class PromoteOptimizationCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<PromoteOptimizationCommand, ModelVersionRow>
{
    public async ValueTask<ModelVersionRow> Handle(PromoteOptimizationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        if (o.State != "ready") throw new PreconditionException("OPT_NOT_READY", "The optimization has not finished");
        if ((JsonNode.Parse(o.Promoted) as JsonObject)?[c.Candidate]?.ToString() is { } existing && Guid.TryParse(existing, out var vid)
            && await db.GetModelVersionAsync(vid, c.WorkspaceId, ct) is { } already) return already;
        var report = JsonNode.Parse(o.Report) as JsonObject ?? [];
        if (report["candidates"]?[c.Candidate] is not JsonObject cand) throw new PreconditionException("OPT_NO_CANDIDATE", $"Candidate {c.Candidate} was not built");
        var src = await db.GetModelVersionAsync(o.SourceVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var modelId = await db.GetModelVersionModelIdAsync(src.Id, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var srcPrefix = (await db.GetVersionPrefixAsync(src.Id, c.WorkspaceId, ct))!;
        var run = OptimizationRules.Prefix(c.WorkspaceId, o.Id);
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var copies = new List<(string Bucket, string From, string To)>
        {
            (Buckets.Artifacts, run + $"{c.Candidate}/model.onnx", "weights/model.onnx"), (Buckets.Artifacts, run + "config/onnx.json", "config/onnx.json"),
            (Buckets.Artifacts, run + "signature.json", "signature.json"), (Buckets.Artifacts, run + "report.json", "report.json"),
            (Buckets.Artifacts, run + "pipeline.py", "pipeline.py"),
        };
        if (await s3.HeadAsync(Buckets.Artifacts, srcPrefix + "classes.json", ct) is not null) copies.Add((Buckets.Artifacts, srcPrefix + "classes.json", "classes.json"));
        var sums = new StringBuilder();
        long size = 0;
        foreach (var (bucket, from, to) in copies)
        {
            await s3.CopyAsync(bucket, from, Buckets.Artifacts, prefix + to, ct);
            var head = await s3.HeadAsync(Buckets.Artifacts, prefix + to, ct);
            size += head?.Size ?? 0;
            var sha = to == "weights/model.onnx" ? cand["sha256"]?.ToString()
                : await s3.GetAsync(Buckets.Artifacts, prefix + to, ct) is { } bytes ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : null;
            if (sha is not null) sums.Append(sha).Append("  ").Append(to).Append('\n');
        }
        var sumsBytes = Encoding.UTF8.GetBytes(sums.ToString());
        await s3.PutAsync(Buckets.Artifacts, prefix + "SHA256SUMS", sumsBytes, "text/plain", ct);
        var lineage = new JsonObject
        {
            ["optimization_id"] = o.Id.ToString(), ["device"] = o.Device, ["precision"] = c.Candidate, ["host_id"] = o.HostId?.ToString(),
            ["source_version"] = src.Version, ["latency"] = cand["latency"]?.DeepClone(), ["fidelity"] = cand["fidelity"]?.DeepClone(),
            ["size_bytes"] = cand["size_bytes"]?.DeepClone(), ["sha256"] = cand["sha256"]?.DeepClone(),
        };
        var id = Ids.New();
        await db.InsertOptimizedVersionAsync(id, modelId, n, src.RunId, src.Task, prefix, copies.Count + 1, size + sumsBytes.Length, uid, clock.UtcNow,
            $"optimization:{o.Id}#{c.Candidate}", src.Id, lineage.ToJsonString(), ct);
        await db.SetOptimizationPromotedAsync(c.WorkspaceId, o.Id, c.Candidate, id, ct);
        return (await db.GetModelVersionAsync(id, c.WorkspaceId, ct))!;
    }
}

/// <summary>Delete a run and its files (versions already saved from it stay).</summary>
public sealed record DeleteOptimizationCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;

/// <summary>Removes every object under the run's folder, then the row. A running job keeps running; stop it on the Jobs page.</summary>
public sealed class DeleteOptimizationCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteOptimizationCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteOptimizationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        foreach (var f in await s3.ListAsync(Buckets.Artifacts, OptimizationRules.Prefix(c.WorkspaceId, o.Id), ct)) await s3.DeleteAsync(Buckets.Artifacts, f.Key, ct);
        await db.DeleteOptimizationAsync(c.WorkspaceId, o.Id, ct);
        return true;
    }
}
