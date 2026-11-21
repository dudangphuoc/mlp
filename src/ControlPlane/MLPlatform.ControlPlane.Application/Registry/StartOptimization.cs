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
