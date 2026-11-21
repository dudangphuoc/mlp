using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

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
