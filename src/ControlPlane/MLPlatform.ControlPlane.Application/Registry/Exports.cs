using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>One conversion of a model version to another format: <c>running</c>, <c>ready</c> (file in storage) or <c>failed</c>.</summary>
public sealed record ExportRow(Guid Id, Guid WorkspaceId, Guid ModelVersionId, string Format, string State, string Error, Guid? JobId,
    string FileName, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>SQL of the model conversions.</summary>
public static partial class ExportSql
{
    [SqlQuery("Registry/InsertExport.sql")]
    public static partial Task InsertExportAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid modelVersionId, string format, Guid jobId,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/ListExports.sql")]
    public static partial Task<List<ExportRow>> ListExportsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
    [SqlQuery("Registry/GetExport.sql")]
    public static partial Task<ExportRow?> GetExportAsync(this IReadSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/FinishExport.sql")]
    public static partial Task FinishExportAsync(this IWriteSession db, Guid workspaceId, Guid id, string state, string error, string fileName,
        long sizeBytes, CancellationToken ct);
    [SqlQuery("Registry/DeleteExport.sql")]
    public static partial Task DeleteExportAsync(this IWriteSession db, Guid workspaceId, Guid id, CancellationToken ct);
}

/// <summary>Where a conversion keeps its files, and the script its job runs.</summary>
public static class ExportRules
{
    public static string Prefix(Guid ws, Guid id) => $"{ws}/exports/{id}/";
    /// <summary>Uploaded by the job under a fixed name; renamed to the real file name once the job reported it.</summary>
    public const string Staged = "artifact";
    public const string Meta = "meta.json";

    /// <summary>Formats the recipe's <c>entrypoints.export</c> declares (empty = the recipe cannot convert).</summary>
    public static IReadOnlyList<string> Formats(JsonObject manifest) =>
        (manifest["entrypoints"]?["export"]?["formats"] as JsonArray)?.Select(f => f?.ToString() ?? "").Where(f => f.Length > 0).ToList() ?? [];

    /// <summary>
    /// Formats built from the version's ONNX file (any recipe that exports ONNX) by <see cref="DerivedScript"/> in the generic-onnx image:
    /// FP16 ONNX for GPU runtimes, OpenVINO IR for Intel CPUs/iGPUs, TensorRT engines built on the target GPU (engines only run on that GPU model + driver).
    /// </summary>
    public static readonly string[] Derived = ["onnx-fp16", "openvino-fp32", "openvino-fp16", "tensorrt-fp16", "tensorrt-fp32"];

    public static IReadOnlyList<string> AllFormats(JsonObject manifest)
    {
        var native = Formats(manifest);
        return native.Contains("onnx") ? [.. native, .. Derived.Where(d => !native.Contains(d))] : native;
    }

    /// <summary>Converts the ONNX file at MLP_ONNX_URL to MLP_EXPORT_FORMAT, uploads one file (or a zip) and meta.json like <see cref="Script"/>.</summary>
    public const string DerivedScript = """
import json, os, pathlib, subprocess, sys, time, urllib.request, zipfile
fmt, base = os.environ["MLP_EXPORT_FORMAT"], os.environ["MLP_EXPORT_NAME"]
work = pathlib.Path("/tmp/mlp-convert"); work.mkdir(parents=True, exist_ok=True)
src = work / "model.onnx"
with urllib.request.urlopen(os.environ["MLP_ONNX_URL"], timeout=600) as r, open(src, "wb") as w:
    while chunk := r.read(1 << 20):
        w.write(chunk)
print(f"[mlp-export] source ONNX {src.stat().st_size} bytes → {fmt}", flush=True)
t0 = time.time()
out = work / "out"; out.mkdir(exist_ok=True)
if fmt == "onnx-fp16":
    import onnx
    from onnxruntime.transformers.float16 import convert_float_to_float16
    m = onnx.load(str(src))
    m16 = convert_float_to_float16(m, keep_io_types=True, op_block_list=["Resize", "Range", "TopK", "NonMaxSuppression", "Softmax", "ReduceMean", "Pow", "Sqrt", "Exp", "Log"])
    onnx.save(m16, str(out / "model-fp16.onnx"))
elif fmt.startswith("openvino-"):
    import openvino as ov
    m = ov.convert_model(str(src))
    ov.save_model(m, str(out / "model.xml"), compress_to_fp16=fmt.endswith("fp16"))
elif fmt.startswith("tensorrt-"):
    import tensorrt as trt
    logger = trt.Logger(trt.Logger.WARNING)
    builder = trt.Builder(logger)
    net = builder.create_network(0)
    parser = trt.OnnxParser(net, logger)
    if not parser.parse(src.read_bytes()):
        sys.exit("TensorRT could not parse the ONNX file: " + "; ".join(str(parser.get_error(i)) for i in range(parser.num_errors)))
    cfg = builder.create_builder_config()
    cfg.set_memory_pool_limit(trt.MemoryPoolType.WORKSPACE, 4 << 30)
    # dynamic dimensions are fixed for the engine: batch 1, image side from the model (640 when unknown), other dims 1
    profile, fixed = builder.create_optimization_profile(), []
    for i in range(net.num_inputs):
        inp = net.get_input(i)
        dims = list(inp.shape)
        if all(d >= 0 for d in dims):
            continue
        shape = [1 if j == 0 and d < 0 else (640 if d < 0 and len(dims) == 4 and j >= 2 else (1 if d < 0 else d)) for j, d in enumerate(dims)]
        profile.set_shape(inp.name, shape, shape, shape)
        fixed.append(f"{inp.name}={shape}")
    if fixed:
        cfg.add_optimization_profile(profile)
        print("[mlp-export] fixed input shapes: " + ", ".join(fixed), flush=True)
    if fmt.endswith("fp16"):
        cfg.set_flag(trt.BuilderFlag.FP16)
    engine = builder.build_serialized_network(net, cfg)
    if engine is None:
        sys.exit("TensorRT engine build failed")
    (out / "model.engine").write_bytes(bytes(engine))
    gpu = subprocess.run(["nvidia-smi", "--query-gpu=name,driver_version", "--format=csv,noheader"], capture_output=True, text=True).stdout.strip()
    (out / "engine.json").write_text(json.dumps({"tensorrt": trt.__version__, "gpu": gpu, "precision": fmt.split("-")[1], "inputs": fixed}, indent=2))
else:
    sys.exit("unknown format " + fmt)
paths = sorted(p for p in out.rglob("*") if p.is_file())
print(f"[mlp-export] converted in {time.time() - t0:.1f} s: " + ", ".join(f"{p.name} {p.stat().st_size} B" for p in paths), flush=True)
if len(paths) == 1:
    artifact, name = paths[0], base + "-" + fmt + "".join(paths[0].suffixes[-1:])
else:
    artifact, name = work / "export.zip", f"{base}-{fmt}.zip"
    with zipfile.ZipFile(artifact, "w", zipfile.ZIP_DEFLATED) as z:
        for p in paths:
            z.write(p, p.relative_to(out))
size = artifact.stat().st_size
def put(url, body, length):
    req = urllib.request.Request(url, data=body, method="PUT", headers={"Content-Length": str(length), "Content-Type": "application/octet-stream"})
    urllib.request.urlopen(req, timeout=3600).read()
with open(artifact, "rb") as body:
    put(os.environ["MLP_EXPORT_PUT"], body, size)
meta = json.dumps({"file": name, "size": size}).encode()
put(os.environ["MLP_EXPORT_META_PUT"], meta, len(meta))
print(f"[mlp-export] uploaded {name} ({size} bytes)", flush=True)
""";

    /// <summary>
    /// Runs inside the recipe image (python + mlp-shim are there): downloads the model version to /mnt/model, lets the shim run the recipe's
    /// export command, then uploads the result — the single output file as it is, several files as one zip — and a small meta.json naming it.
    /// Passed in an environment variable so that changing it never rebuilds a recipe image.
    /// </summary>
    public const string Script = """
import json, os, pathlib, subprocess, sys, urllib.request, zipfile
model, out = pathlib.Path("/mnt/model"), pathlib.Path("/mnt/output")
model.mkdir(parents=True, exist_ok=True); out.mkdir(parents=True, exist_ok=True)
files = json.loads(os.environ["MLP_MODEL_FILES"])
for f in files:
    target = (model / f["path"]).resolve()
    if not str(target).startswith(str(model.resolve())):
        sys.exit("invalid model file path: " + f["path"])
    target.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(f["url"], timeout=600) as r, open(target, "wb") as w:
        while chunk := r.read(1 << 20):
            w.write(chunk)
print(f"[mlp-export] downloaded {len(files)} model files", flush=True)
code = subprocess.call(["mlp-shim", "run", "--spec-env", "MLP_JOB_SPEC"])
if code != 0:
    sys.exit(code)
listed = json.loads((out / ".mlp" / "export-files.json").read_text())
paths = []
for rel in listed:
    p = out / rel
    paths += [x for x in sorted(p.rglob("*")) if x.is_file()] if p.is_dir() else [p]
base, fmt = os.environ["MLP_EXPORT_NAME"], os.environ["MLP_EXPORT_FORMAT"]
if len(paths) == 1:
    artifact, name = paths[0], base + "".join(paths[0].suffixes[-1:])
else:
    artifact, name = out / ".mlp" / "export.zip", f"{base}-{fmt}.zip"
    with zipfile.ZipFile(artifact, "w", zipfile.ZIP_DEFLATED) as z:
        for p in paths:
            z.write(p, p.relative_to(out))
size = artifact.stat().st_size
def put(url, body, length):
    req = urllib.request.Request(url, data=body, method="PUT", headers={"Content-Length": str(length), "Content-Type": "application/octet-stream"})
    urllib.request.urlopen(req, timeout=3600).read()
with open(artifact, "rb") as body:
    put(os.environ["MLP_EXPORT_PUT"], body, size)
meta = json.dumps({"file": name, "size": size}).encode()
put(os.environ["MLP_EXPORT_META_PUT"], meta, len(meta))
print(f"[mlp-export] uploaded {name} ({size} bytes)", flush=True)
""";
}

/// <summary>Formats a model version can be converted to, from its recipe.</summary>
public sealed record ListExportFormatsQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<IReadOnlyList<string>>;

/// <summary>Reads <c>entrypoints.export.formats</c> of the version's recipe.</summary>
public sealed class ListExportFormatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator) : IQueryHandler<ListExportFormatsQuery, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(ListExportFormatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var v = await db.GetModelVersionAsync(q.ModelVersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        try { return ExportRules.AllFormats(JsonNode.Parse((await mediator.Query(new GetRecipeQuery(q.WorkspaceId, v.Recipe), ct)).Manifest)!.AsObject()); }
        catch (NotFoundException) { return []; }
    }
}

/// <summary>Convert a model version to <paramref name="Format"/>: submits a job that runs the recipe's export command.</summary>
public sealed record StartExportCommand(Guid WorkspaceId, Guid ModelVersionId, string Format, Guid? HostId = null) : ICommand<ExportRow>;

/// <summary>Builds the shim spec (entrypoint <c>export</c>), signs the upload links and submits the job.</summary>
public sealed class StartExportCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartExportCommand, ExportRow>
{
    public async ValueTask<ExportRow> Handle(StartExportCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, v.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        if (ExportRules.Derived.Contains(c.Format) && !ExportRules.Formats(manifest).Contains(c.Format))
            return await StartDerivedAsync(c, v, uid, ct);
        if (!ExportRules.Formats(manifest).Contains(c.Format))
            throw new PreconditionException("REGISTRY_EXPORT_FORMAT", $"Recipe {recipe.Name} cannot convert to {c.Format}");
        if (recipe.State == "invalid") throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");
        var variant = (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => x["name"]?.ToString() == v.Variant);
        var id = Ids.New();
        var prefix = ExportRules.Prefix(c.WorkspaceId, id);
        var needsGpu = c.Format is "engine";   // TensorRT engines are built on a GPU; every other format converts on CPU
        var shimSpec = new JsonObject
        {
            ["manifest"] = manifest.DeepClone(), ["entrypoint"] = "export", ["task"] = v.Task, ["variant"] = variant?.DeepClone() ?? new JsonObject { ["name"] = v.Variant },
            ["params"] = new JsonObject(), ["output"] = "/mnt/output", ["model"] = "/mnt/model", ["format"] = c.Format, ["recipeSrc"] = "/opt/recipe/src",
            ["hardware"] = new JsonObject { ["device"] = needsGpu ? "cuda" : "cpu" },
        };
        var name = $"{v.ModelName}-v{v.Version}";
        var spec = new JobSpecDto
        {
            Image = $"recipes/{recipe.Name}:current", Command = ["python", "-c", "import os;exec(os.environ['MLP_EXPORT_SCRIPT'])"],
            Cpus = 2, Gpus = needsGpu ? 1 : 0, TimeoutSeconds = 3600,
            Env = new Dictionary<string, string>
            {
                ["MLP_EXPORT_SCRIPT"] = ExportRules.Script, ["MLP_JOB_SPEC"] = shimSpec.ToJsonString(),
                ["MLP_MODEL_FILES"] = await ModelFiles.BuildAsync(db, s3, v.Id, c.WorkspaceId, ct),
                ["MLP_EXPORT_NAME"] = name, ["MLP_EXPORT_FORMAT"] = c.Format,
                ["MLP_EXPORT_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Staged, TimeSpan.FromHours(24), Audience.Workload),
                ["MLP_EXPORT_META_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Meta, TimeSpan.FromHours(24), Audience.Workload),
            },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "export", $"export-{name}-{c.Format}", spec), ct);
        await db.InsertExportAsync(id, c.WorkspaceId, v.Id, c.Format, jobId, uid, clock.UtcNow, ct);
        return (await db.GetExportAsync(c.WorkspaceId, id, ct))!;
    }

    /// <summary>Format built from the version's ready ONNX export in the generic-onnx image (TensorRT on a GPU of the chosen machine).</summary>
    private async Task<ExportRow> StartDerivedAsync(StartExportCommand c, ModelVersionRow v, Guid uid, CancellationToken ct)
    {
        var onnx = (await db.ListReadyOnnxExportsAsync(c.WorkspaceId, v.Id, ct)).FirstOrDefault()
                   ?? throw new PreconditionException("REGISTRY_EXPORT_NEEDS_ONNX", "Convert this version to ONNX first");
        var gpu = c.Format.StartsWith("tensorrt-", StringComparison.Ordinal);
        var id = Ids.New();
        var prefix = ExportRules.Prefix(c.WorkspaceId, id);
        var name = $"{v.ModelName}-v{v.Version}";
        var spec = new JobSpecDto
        {
            Image = "recipes/generic-onnx:current", Command = ["python3", "-c", "import os;exec(os.environ['MLP_EXPORT_SCRIPT'])"],
            Cpus = 2, Gpus = gpu ? 1 : 0, GpuMemoryMb = gpu ? 4096 : 0, TimeoutSeconds = 3600, Hosts = c.HostId is { } h ? [h] : [],
            Env = new Dictionary<string, string>
            {
                ["MLP_EXPORT_SCRIPT"] = ExportRules.DerivedScript, ["MLP_EXPORT_NAME"] = name, ["MLP_EXPORT_FORMAT"] = c.Format,
                ["MLP_ONNX_URL"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, ExportRules.Prefix(c.WorkspaceId, onnx.Id) + onnx.FileName, TimeSpan.FromHours(6), Audience.Workload),
                ["MLP_EXPORT_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Staged, TimeSpan.FromHours(24), Audience.Workload),
                ["MLP_EXPORT_META_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Meta, TimeSpan.FromHours(24), Audience.Workload),
            },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "export", $"export-{name}-{c.Format}", spec), ct);
        await db.InsertExportAsync(id, c.WorkspaceId, v.Id, c.Format, jobId, uid, clock.UtcNow, ct);
        return (await db.GetExportAsync(c.WorkspaceId, id, ct))!;
    }
}

/// <summary>The conversions of a model version, newest first; running ones are settled from their job first.</summary>
public sealed record ListExportsCommand(Guid WorkspaceId, Guid ModelVersionId) : ICommand<IReadOnlyList<ExportRow>>;

/// <summary>
/// A running conversion whose job succeeded becomes <c>ready</c> (the uploaded file takes its real name), one whose job failed or was
/// cancelled becomes <c>failed</c> with the job's reason. Settled here, on read, so no scheduler hook is needed.
/// </summary>
public sealed class ListExportsCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<ListExportsCommand, IReadOnlyList<ExportRow>>
{
    public async ValueTask<IReadOnlyList<ExportRow>> Handle(ListExportsCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var rows = await db.ListExportsAsync(c.WorkspaceId, c.ModelVersionId, ct);
        var changed = false;
        foreach (var e in rows.Where(e => e.State == "running"))
        {
            var job = e.JobId is { } jid ? await db.GetJobAsync(jid, c.WorkspaceId, ct) : null;
            if (job is null) { await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", "The job is gone", "", 0, ct); changed = true; continue; }
            if (job.State is "failed" or "cancelled")
            {
                await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", job.StateReason ?? job.LastExitReason ?? job.State, "", 0, ct);
                changed = true;
            }
            else if (job.State == "succeeded")
            {
                var prefix = ExportRules.Prefix(c.WorkspaceId, e.Id);
                JsonObject? meta = null;
                try { meta = await s3.GetAsync(Buckets.Artifacts, prefix + ExportRules.Meta, ct) is { } raw ? JsonNode.Parse(raw) as JsonObject : null; }
                catch (JsonException) { }
                var file = Path.GetFileName(meta?["file"]?.ToString() ?? "");
                if (file.Length == 0) await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", "The job finished without a result file", "", 0, ct);
                else
                {
                    await s3.CopyAsync(Buckets.Artifacts, prefix + ExportRules.Staged, Buckets.Artifacts, prefix + file, ct);
                    await s3.DeleteAsync(Buckets.Artifacts, prefix + ExportRules.Staged, ct);
                    await db.FinishExportAsync(c.WorkspaceId, e.Id, "ready", "", file, InspectionRules.Long(meta?["size"]), ct);
                }
                changed = true;
            }
        }
        return changed ? await db.ListExportsAsync(c.WorkspaceId, c.ModelVersionId, ct) : rows;
    }
}

/// <summary>A one-hour download link of a finished conversion.</summary>
public sealed record GetExportDownloadQuery(Guid WorkspaceId, Guid ExportId) : IQuery<string>;

/// <summary>Signs the stored file for the browser.</summary>
public sealed class GetExportDownloadQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth) : IQueryHandler<GetExportDownloadQuery, string>
{
    public async ValueTask<string> Handle(GetExportDownloadQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetExportAsync(q.WorkspaceId, q.ExportId, ct) ?? throw new NotFoundException("REGISTRY_EXPORT_NOT_FOUND", "Conversion not found");
        if (e.State != "ready") throw new PreconditionException("REGISTRY_EXPORT_NOT_READY", "The conversion has no file yet");
        return s3.Presign(HttpMethod.Get, Buckets.Artifacts, ExportRules.Prefix(q.WorkspaceId, e.Id) + e.FileName, TimeSpan.FromHours(1));
    }
}

/// <summary>Remove a conversion and its file (a running one: its job keeps running; cancel it on the Jobs page).</summary>
public sealed record DeleteExportCommand(Guid WorkspaceId, Guid ExportId) : ICommand<bool>;

/// <summary>Deletes the stored objects, then the row.</summary>
public sealed class DeleteExportCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteExportCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteExportCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var e = await db.GetExportAsync(c.WorkspaceId, c.ExportId, ct) ?? throw new NotFoundException("REGISTRY_EXPORT_NOT_FOUND", "Conversion not found");
        foreach (var o in await s3.ListAsync(Buckets.Artifacts, ExportRules.Prefix(c.WorkspaceId, e.Id), ct)) await s3.DeleteAsync(Buckets.Artifacts, o.Key, ct);
        await db.DeleteExportAsync(c.WorkspaceId, e.Id, ct);
        return true;
    }
}
