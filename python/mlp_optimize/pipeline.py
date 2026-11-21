"""MLPlatform model optimization job (v1).

Runs in the generic-onnx image (onnxruntime-gpu, onnx, numpy, pillow, psutil). Reads its spec from MLP_OPT_SPEC_URL:
  {"task": "detect"|"classify", "device": "cpu"|"cuda", "candidates": ["fp32", "fp16"|"int8"], "conf": 0.5, "threads": 4,
   "warmup": 20, "iters": 200, "calib_method": "minmax"|"percentile", "model_url": "...", "classes_url": "..."|null,
   "calib": [{"path", "url"}], "check": [{"path", "url"}], "uploads": {"<relative path>": "<presigned PUT>"}}
Produces, per candidate, <cand>/model.onnx; once: config/onnx.json, signature.json, worst/<i>.jpg, report.json (uploaded last).

Candidates:
  fp32  ONNX Runtime basic-level offline optimization (constant folding, redundant node removal) — portable across machines.
  fp16  float16 weights/activations, float32 inputs/outputs kept, numerically risky ops left in float32. Only for CUDA.
  int8  static QDQ quantization, per-channel weights, Conv/MatMul/Gemm only, calibrated on train images; decoder and
        post-processing of DETR-style detectors stay float32. Only for CPU.
The source model is the baseline for every check; all are run with the same provider and the same images.
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import platform
import re
import sys
import tempfile
import time
import traceback
import urllib.error
import urllib.request
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import psutil
from PIL import Image, ImageDraw

WORK = Path(tempfile.mkdtemp(prefix="mlp-opt-"))
LOG = lambda *a: print("[mlp-optimize]", *a, flush=True)  # noqa: E731


# ───────────── io ─────────────

def fetch(url: str, dest: Path | None = None) -> bytes | Path:
    with urllib.request.urlopen(url, timeout=600) as r:
        if dest is None:
            return r.read()
        dest.parent.mkdir(parents=True, exist_ok=True)
        with open(dest, "wb") as w:
            while chunk := r.read(1 << 20):
                w.write(chunk)
        return dest


OBJECT_STAMP = 978307200  # 2001-01-01 UTC: same marker as the shim's dataset cache (an object rewritten in place loses it)


def fetch_images(files: list[dict], folder: Path) -> tuple[list[Path], dict]:
    """Images from the host dataset cache when present (no transfer); missing ones downloaded into it, 8 at a time."""
    from concurrent.futures import ThreadPoolExecutor
    stats = {"files": len(files), "reused": 0, "reused_bytes": 0, "downloaded": 0, "downloaded_bytes": 0}
    t0 = time.time()

    def one(i_f):
        i, f = i_f
        cache, oid, size = f.get("cache"), f.get("id"), f.get("size", -1)
        if cache and oid and Path(cache).is_dir():
            obj = Path(cache) / "objects" / oid[:2] / oid
            try:
                st = obj.stat()
                if (size < 0 or st.st_size == size) and int(st.st_mtime) == OBJECT_STAMP:
                    return obj, "reused", st.st_size
            except FileNotFoundError:
                pass
            part = obj.with_name(f"{oid}.part-{os.getpid()}-{i}")
            fetch(f["url"], part)
            if size >= 0 and part.stat().st_size != size:
                part.unlink(missing_ok=True)
                raise IOError(f"{f['path']}: size mismatch")
            os.utime(part, (OBJECT_STAMP, OBJECT_STAMP))
            os.replace(part, obj)
            return obj, "downloaded", size
        dst = folder / f"{i}{Path(f['path']).suffix}"
        fetch(f["url"], dst)
        return dst, "downloaded", dst.stat().st_size

    with ThreadPoolExecutor(8) as ex:
        out = list(ex.map(one, enumerate(files)))
    for _, kind, n in out:
        stats[kind] += 1
        stats[kind + "_bytes"] += max(0, n)
    stats["seconds"] = round(time.time() - t0, 1)
    return [p for p, _, _ in out], stats


def put(url: str, data: bytes) -> None:
    req = urllib.request.Request(url, data=data, method="PUT", headers={"Content-Length": str(len(data)), "Content-Type": "application/octet-stream"})
    urllib.request.urlopen(req, timeout=3600).read()


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(1 << 20):
            h.update(chunk)
    return h.hexdigest()


# ───────────── model contract ─────────────

def contract(model_path: Path, task: str) -> dict:
    """config/onnx.json for generic-onnx: input name/size, extra inputs (DETR orig size), output names, bilinear resize."""
    m = onnx.load(str(model_path), load_external_data=False)
    inits = {i.name for i in m.graph.initializer}
    inputs = [i for i in m.graph.input if i.name not in inits]
    img = inputs[0]
    dims = [d.dim_value if d.dim_value > 0 else -1 for d in img.type.tensor_type.shape.dim]
    nchw = len(dims) == 4 and dims[1] in (1, 3)
    size = [dims[3], dims[2]] if nchw else [dims[2], dims[1]]
    size = [s if s > 0 else 640 for s in size]
    cfg: dict = {"task": task, "input": {"name": img.name, "size": size, "layout": "NCHW" if nchw else "NHWC",
                                         "scale": 255.0, "mean": [0, 0, 0], "std": [1, 1, 1], "rgb": True}, "resample": "bilinear"}
    extra = {}
    for i in inputs[1:]:
        elem = i.type.tensor_type.elem_type
        extra[i.name] = {"value": "orig_size", "dtype": "int64" if elem == onnx.TensorProto.INT64 else "float32"}
    if extra:
        cfg["extra_inputs"] = extra
    outs = [o.name for o in m.graph.output]
    if task == "detect":
        low = {n.lower(): n for n in outs}
        if {"boxes", "scores", "labels"} <= set(low):
            cfg["detect_layout"] = "boxes_scores_labels"
            cfg["outputs"] = {k: low[k] for k in ("boxes", "scores", "labels")}
        else:
            cfg["detect_layout"] = "yolo"
    return cfg


def signature(model_path: Path) -> dict:
    m = onnx.load(str(model_path), load_external_data=False)
    inits = {i.name for i in m.graph.initializer}

    def io(v):
        t = v.type.tensor_type
        return {"name": v.name, "dtype": onnx.TensorProto.DataType.Name(t.elem_type).lower(),
                "shape": [d.dim_value if d.dim_value > 0 else (d.dim_param or -1) for d in t.shape.dim]}
    return {"inputs": [io(i) for i in m.graph.input if i.name not in inits], "outputs": [io(o) for o in m.graph.output],
            "opset": max((o.version for o in m.opset_import if o.domain in ("", "ai.onnx")), default=0), "ir_version": m.ir_version}


def preprocess(path: Path, cfg: dict) -> tuple[dict, int, int, Image.Image]:
    c = cfg["input"]
    img = Image.open(path).convert("RGB")
    w, h = img.size
    x = np.asarray(img.resize(tuple(c["size"]), Image.BILINEAR), dtype=np.float32) / float(c["scale"])
    x = (x - np.array(c["mean"], dtype=np.float32)) / np.array(c["std"], dtype=np.float32)
    if c["layout"] == "NCHW":
        x = x.transpose(2, 0, 1)
    feeds = {c["name"]: x[None]}
    for name, spec in (cfg.get("extra_inputs") or {}).items():
        feeds[name] = np.array([[w, h]], dtype=np.int64 if spec["dtype"] == "int64" else np.float32)
    return feeds, w, h, img


# ───────────── sessions ─────────────

@contextlib.contextmanager
def capture_stderr():
    """ONNX Runtime writes node placement to fd 2 at verbose level; capture it while a session is created."""
    buf = tempfile.TemporaryFile(mode="w+b")
    saved = os.dup(2)
    os.dup2(buf.fileno(), 2)
    try:
        yield buf
    finally:
        os.dup2(saved, 2)
        os.close(saved)


def session(path: Path, device: str, threads: int) -> tuple[ort.InferenceSession, dict]:
    so = ort.SessionOptions()
    so.intra_op_num_threads = threads
    so.inter_op_num_threads = 1
    so.log_severity_level = 0
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    providers = ["CUDAExecutionProvider", "CPUExecutionProvider"] if device == "cuda" else ["CPUExecutionProvider"]
    rss0 = psutil.Process().memory_info().rss
    with capture_stderr() as buf:
        s = ort.InferenceSession(str(path), so, providers=providers)
        sys.stderr.flush()
    buf.seek(0)
    log = buf.read().decode("utf-8", "replace")
    if device == "cuda" and "CUDAExecutionProvider" not in s.get_providers():
        # never report CPU numbers as a GPU run: fail with the loader's own reason
        why = [ln.strip() for ln in log.splitlines() if re.search(r"cuda|cudnn|cublas|\.so|error|fail", ln, re.I) and "placed on" not in ln]
        raise RuntimeError("CUDA execution provider not loaded (available: " + ", ".join(ort.get_available_providers()) + "): " + " | ".join(why[-6:])[:1500])
    placement: dict[str, int] = {}
    for ep, n in re.findall(r"placed on \[(\w+)\]\. Number of nodes: (\d+)", log):
        placement[ep] = placement.get(ep, 0) + int(n)
    memcpy = len(re.findall(r"\bMemcpy(?:FromHost|ToHost)?\b", log))
    return s, {"providers": s.get_providers(), "nodes_per_ep": placement, "memcpy_nodes": memcpy, "load_rss_mb": round((psutil.Process().memory_info().rss - rss0) / 2**20, 1)}


def run(s: ort.InferenceSession, feeds: dict) -> dict:
    names = [o.name for o in s.get_outputs()]
    return dict(zip(names, s.run(None, feeds)))


def benchmark(s: ort.InferenceSession, feeds: dict, warmup: int, iters: int) -> dict:
    for _ in range(warmup):
        s.run(None, feeds)
    times = []
    peak = psutil.Process().memory_info().rss
    for _ in range(iters):
        t = time.perf_counter()
        s.run(None, feeds)
        times.append((time.perf_counter() - t) * 1000)
        peak = max(peak, psutil.Process().memory_info().rss)
    a = np.array(times)
    return {"p50_ms": round(float(np.percentile(a, 50)), 2), "p95_ms": round(float(np.percentile(a, 95)), 2),
            "p99_ms": round(float(np.percentile(a, 99)), 2), "mean_ms": round(float(a.mean()), 2),
            "throughput_ips": round(1000.0 / float(a.mean()), 1), "peak_rss_mb": round(peak / 2**20, 1),
            "warmup": warmup, "iters": iters, "batch": 1}


# ───────────── fidelity ─────────────

def boxes_of(out: dict, cfg: dict, conf: float) -> list[tuple[int, float, list[float]]]:
    if cfg.get("detect_layout") != "boxes_scores_labels":
        pred = next(iter(out.values()))[0]
        pred = pred.T if pred.shape[0] < pred.shape[1] else pred
        res = []
        for row in pred:
            cid = int(np.argmax(row[4:]))
            sc = float(row[4 + cid])
            if sc >= conf:
                cx, cy, bw, bh = row[:4]
                res.append((cid, sc, [float(cx - bw / 2), float(cy - bh / 2), float(cx + bw / 2), float(cy + bh / 2)]))
        return res
    n = cfg["outputs"]
    return [(int(l), float(s), [float(v) for v in b]) for b, s, l in zip(out[n["boxes"]][0], out[n["scores"]][0], out[n["labels"]][0]) if float(s) >= conf]


def iou(a: list[float], b: list[float]) -> float:
    ix = max(0.0, min(a[2], b[2]) - max(a[0], b[0]))
    iy = max(0.0, min(a[3], b[3]) - max(a[1], b[1]))
    inter = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


def match(base: list, cand: list) -> tuple[int, int, int, list[float]]:
    """Greedy same-class matching at IoU ≥ 0.5, highest scores first → matched, missed, extra, |score drift| of matched."""
    used, drift, matched = set(), [], 0
    for cid, sc, bx in sorted(base, key=lambda r: -r[1]):
        best, bi = 0.5, -1
        for j, (c2, s2, b2) in enumerate(cand):
            if j in used or c2 != cid:
                continue
            v = iou(bx, b2)
            if v >= best:
                best, bi = v, j
        if bi >= 0:
            used.add(bi)
            matched += 1
            drift.append(abs(sc - cand[bi][1]))
    return matched, len(base) - matched, len(cand) - len(used), drift


# ───────────── candidates ─────────────

def make_fp32(src: Path, dst: Path) -> dict:
    so = ort.SessionOptions()
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_BASIC
    so.optimized_model_filepath = str(dst)
    ort.InferenceSession(str(src), so, providers=["CPUExecutionProvider"])
    return {"graph": "onnxruntime basic (constant folding, redundant nodes)"}


FP16_BLOCK = ["TopK", "GridSample", "Softmax", "LayerNormalization", "ReduceMean", "ReduceSum", "CumSum", "Range", "NonZero",
              "Mod", "ArgMax", "Exp", "Log", "Pow", "Sqrt", "Reciprocal"]


def make_fp16(src: Path, dst: Path) -> dict:
    from onnxruntime.transformers.float16 import convert_float_to_float16
    m = onnx.load(str(src))
    post = [n.name for n in m.graph.node if "postprocessor" in n.name.lower()]
    m16 = convert_float_to_float16(m, keep_io_types=True, op_block_list=FP16_BLOCK, node_block_list=post)
    onnx.save(m16, str(dst))
    return {"keep_io_types": True, "op_block_list": FP16_BLOCK, "nodes_kept_fp32": len(post)}


def int8_excluded(m: onnx.ModelProto) -> list[str]:
    """DETR-style models: decoder (deformable attention sampling offsets / attention weights, box distribution) and
    post-processing stay float32; everything before the decoder (backbone, encoder) is quantized."""
    keys = ("decoder", "postprocessor", "dec_", "enc_score", "enc_bbox", "integral")
    return [n.name for n in m.graph.node if any(k in n.name.lower() for k in keys)]


def make_int8(src: Path, dst: Path, cfg: dict, calib: list[Path], method: str) -> dict:
    from onnxruntime.quantization import CalibrationDataReader, CalibrationMethod, QuantFormat, QuantType, quantize_static
    prepared = WORK / "int8-prep.onnx"
    try:
        from onnxruntime.quantization.shape_inference import quant_pre_process
        quant_pre_process(str(src), str(prepared), skip_symbolic_shape=True)
    except Exception as e:  # noqa: BLE001 — pre-processing only improves quantization coverage
        LOG("pre-process skipped:", e)
        prepared = src
    m = onnx.load(str(prepared), load_external_data=False)
    excluded = int8_excluded(m)

    class Reader(CalibrationDataReader):
        def __init__(self):
            self.it = iter(calib)

        def get_next(self):
            p = next(self.it, None)
            return None if p is None else preprocess(p, cfg)[0]

    flags = open("/proc/cpuinfo").read() if Path("/proc/cpuinfo").exists() else ""
    vnni = any(f in flags for f in ("avx512_vnni", "avx_vnni", "amx_int8"))
    quantize_static(str(prepared), str(dst), Reader(), quant_format=QuantFormat.QDQ, per_channel=True,
                    activation_type=QuantType.QUInt8, weight_type=QuantType.QInt8, reduce_range=not vnni,
                    op_types_to_quantize=["Conv", "MatMul", "Gemm"], nodes_to_exclude=excluded,
                    calibrate_method=CalibrationMethod.Percentile if method == "percentile" else CalibrationMethod.MinMax,
                    extra_options={"ActivationSymmetric": False, "WeightSymmetric": True})
    return {"format": "QDQ", "per_channel": True, "op_types": ["Conv", "MatMul", "Gemm"], "nodes_excluded": len(excluded),
            "calibration_images": len(calib), "calibration_method": method, "cpu_vnni": vnni, "reduce_range": not vnni}


def op_counts(path: Path) -> dict:
    m = onnx.load(str(path), load_external_data=False)
    counts: dict[str, int] = {}
    for n in m.graph.node:
        counts[n.op_type] = counts.get(n.op_type, 0) + 1
    return dict(sorted(counts.items(), key=lambda kv: -kv[1]))


def draw(img: Image.Image, base: list, cand: list, classes: list | None) -> bytes:
    im = img.copy()
    im.thumbnail((480, 480))
    sx, sy = im.width / img.width, im.height / img.height
    d = ImageDraw.Draw(im)
    for color, rows in (("#22c55e", base), ("#f97316", cand)):
        for cid, sc, b in rows:
            d.rectangle([b[0] * sx, b[1] * sy, b[2] * sx, b[3] * sy], outline=color, width=2)
            name = classes[cid] if classes and 0 <= cid < len(classes) else str(cid)
            d.text((b[0] * sx + 2, b[1] * sy + 1), f"{name} {sc:.2f}", fill=color)
    out = io.BytesIO()
    im.save(out, format="JPEG", quality=85)
    return out.getvalue()


# ───────────── main ─────────────

def main() -> None:
    spec = json.loads(fetch(os.environ["MLP_OPT_SPEC_URL"]))
    up = spec["uploads"]
    device, task, conf, threads = spec["device"], spec["task"], float(spec["conf"]), int(spec["threads"])
    report: dict = {"spec": {k: v for k, v in spec.items() if k not in ("uploads", "calib", "check", "model_url", "classes_url")},
                    "candidates": {}, "errors": {}}
    try:
        src = fetch(spec["model_url"], WORK / "source.onnx")
        classes = json.loads(fetch(spec["classes_url"])) if spec.get("classes_url") else None
        cfg = contract(src, task)
        sig = signature(src)
        LOG(f"source {src.stat().st_size} bytes, opset {sig['opset']}, inputs {[i['name'] for i in sig['inputs']]}")
        calib, cs = fetch_images(spec["calib"], WORK / "calib")
        check, ks = fetch_images(spec["check"], WORK / "check")
        report["data"] = {"calib": cs, "check": ks}
        for name, s in (("calibration", cs), ("check", ks)):
            if s["files"]:
                LOG(f"{s['files']} {name} images: {s['reused']} from the machine cache ({s['reused_bytes'] / 2**20:.1f} MB), "
                    f"{s['downloaded']} downloaded ({s['downloaded_bytes'] / 2**20:.1f} MB) in {s['seconds']} s")
        report["host"] = {"cpu": platform.processor() or platform.machine(), "cpu_count": psutil.cpu_count(), "threads": threads,
                          "device": device, "gpu": os.environ.get("NVIDIA_VISIBLE_DEVICES", ""), "python": platform.python_version()}
        report["versions"] = {"onnxruntime": ort.__version__, "onnx": onnx.__version__, "numpy": np.__version__}
        report["source"] = {"size_bytes": src.stat().st_size, "sha256": sha256(src), "ops": op_counts(src), **sig}
        report["contract"] = cfg

        if device == "cuda" and hasattr(ort, "preload_dlls"):
            try:
                ort.preload_dlls()   # CUDA/cuDNN from the image or from nvidia-* wheels
            except Exception as e:  # noqa: BLE001 — the session check below reports the real failure
                LOG("preload_dlls:", e)
        LOG("providers available:", ort.get_available_providers())
        base_sess, base_place = session(src, device, threads)
        feeds = [preprocess(p, cfg)[:3] for p in check]   # (feeds, w, h); images are reopened only for the drawings
        base_out = [run(base_sess, f[0]) for f in feeds]
        report["source"]["placement"] = base_place
        report["source"]["latency"] = benchmark(base_sess, feeds[0][0], spec["warmup"], spec["iters"])
        del base_sess
        LOG("baseline", report["source"]["latency"])

        worst: list[tuple[float, int, str]] = []
        for cand in spec["candidates"]:
            dst = WORK / cand / "model.onnx"
            dst.parent.mkdir(parents=True, exist_ok=True)
            t0 = time.time()
            try:
                if cand == "fp32":
                    info = make_fp32(src, dst)
                elif cand == "fp16":
                    info = make_fp16(src, dst)
                elif cand == "int8":
                    info = make_int8(src, dst, cfg, calib, spec.get("calib_method", "minmax"))
                else:
                    raise ValueError(f"unknown candidate {cand}")
                build_s = round(time.time() - t0, 1)
                sess, place = session(dst, device, threads)
                outs = [run(sess, f[0]) for f in feeds]
                lat = benchmark(sess, feeds[0][0], spec["warmup"], spec["iters"])
                del sess
                fid: dict
                if task == "classify":
                    agree = sum(int(np.argmax(next(iter(b.values()))) == np.argmax(next(iter(o.values())))) for b, o in zip(base_out, outs))
                    fid = {"top1_agreement": round(agree / max(1, len(outs)), 4), "images": len(outs)}
                else:
                    tot = dict(matched=0, missed=0, extra=0, base=0)
                    drift: list[float] = []
                    for i, (b, o) in enumerate(zip(base_out, outs)):
                        bb, cb = boxes_of(b, cfg, conf), boxes_of(o, cfg, conf)
                        m, mi, ex, dr = match(bb, cb)
                        tot["matched"] += m; tot["missed"] += mi; tot["extra"] += ex; tot["base"] += len(bb)
                        drift += dr
                        worst.append((mi + ex + sum(dr), i, cand))
                    fid = {"match_rate": round(tot["matched"] / tot["base"], 4) if tot["base"] else 1.0, "boxes_source": tot["base"],
                           "matched": tot["matched"], "missed": tot["missed"], "extra": tot["extra"],
                           "score_drift_mean": round(float(np.mean(drift)), 4) if drift else 0.0,
                           "score_drift_max": round(float(np.max(drift)), 4) if drift else 0.0, "images": len(outs), "conf": conf}
                report["candidates"][cand] = {"size_bytes": dst.stat().st_size, "sha256": sha256(dst), "build_seconds": build_s,
                                              "settings": info, "placement": place, "latency": lat, "fidelity": fid, "ops": op_counts(dst)}
                put(up[f"{cand}/model.onnx"], dst.read_bytes())
                LOG(cand, "size", dst.stat().st_size, "latency", lat["p50_ms"], "fidelity", fid)
            except Exception as e:  # noqa: BLE001 — one failed candidate does not lose the others
                traceback.print_exc()
                report["errors"][cand] = f"{type(e).__name__}: {e}"[:600]

        if task == "detect" and worst:
            shown = []
            for score, i, cand in sorted(worst, key=lambda r: -r[0]):
                if score <= 0 or i in [s["image"] for s in shown] or len(shown) >= 6:
                    continue
                sess, _ = session(WORK / cand / "model.onnx", device, threads)
                o = run(sess, feeds[i][0])
                del sess
                bb, cb = boxes_of(base_out[i], cfg, conf), boxes_of(o, cfg, conf)
                key = f"worst/{len(shown)}.jpg"
                put(up[key], draw(Image.open(check[i]).convert("RGB"), bb, cb, classes))
                shown.append({"file": key, "image": i, "path": spec["check"][i]["path"], "candidate": cand, "source_boxes": len(bb), "candidate_boxes": len(cb)})
            report["worst"] = shown

        put(up["config/onnx.json"], json.dumps(cfg, indent=2).encode())
        put(up["signature.json"], json.dumps(sig, indent=2).encode())
        report["state"] = "ready" if report["candidates"] else "failed"
    except Exception as e:  # noqa: BLE001
        traceback.print_exc()
        report["state"] = "failed"
        report["error"] = f"{type(e).__name__}: {e}"[:800]
        if isinstance(e, (urllib.error.URLError, TimeoutError, ConnectionError)):
            report["retry"] = True   # network: another attempt may succeed
    put(up["report.json"], json.dumps(report, indent=2, default=str).encode())
    LOG("report uploaded:", report["state"])
    if report["state"] == "ready":
        sys.exit(0)
    if report.get("retry"):
        sys.exit(1)
    # deterministic failure (no CUDA, every candidate failed…): a retry cannot fix it — declared failure, not retried
    msg = report.get("error") or "; ".join(f"{k}: {v}" for k, v in report["errors"].items()) or "no candidate was built"
    print("MLP_FAIL " + json.dumps({"reason": "optimize_failed", "message": msg[:500]}), flush=True)
    sys.exit(2)


if __name__ == "__main__":
    main()
