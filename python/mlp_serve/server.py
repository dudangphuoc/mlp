"""Serving runtime: downloads the model directory, loads the handler, exposes the gRPC ModelServer (20/08 §10, M12 R1).

Environment variables (set by the api/agent when starting a replica):
  MLP_HANDLER       module:Class (from the recipe's entrypoints.serve.handler)
  MLP_TASK          task key
  MLP_MODEL_DIR     model directory (D-28) inside the container (default /mnt/model)
  MLP_MODEL_FILES   JSON [{"path","url"}] — presigned GETs for the model version files; downloaded before loading
  MLP_PARAMS        parameters as JSON (from recipe.lock.json)
  MLP_SERVE_PORT    gRPC port (default 8500)
gRPC stubs are generated at image build time from proto/mlplatform/v1/model_server.proto (package mlp_serve.gen).
"""
from __future__ import annotations

import importlib
import json
import os
import sys
import tempfile
import threading
import urllib.request
from concurrent import futures
from pathlib import Path

import grpc

from mlp_serve import types as T
from mlp_serve.gen.mlplatform.v1 import model_server_pb2 as pb
from mlp_serve.gen.mlplatform.v1 import model_server_pb2_grpc as pbg


def download_model(model_dir: Path) -> None:
    """Skips files already present from an earlier start (per-version volume); each file lands atomically, then a marker lists the set."""
    files = json.loads(os.environ.get("MLP_MODEL_FILES", "[]"))
    wanted = sorted(f["path"] for f in files)
    marker = model_dir / ".mlp-files.json"
    try:
        if marker.exists() and json.loads(marker.read_text()) == wanted:
            print(f"[mlp-serve] model files already on this host ({len(files)})", flush=True)
            return
    except (OSError, ValueError):
        pass
    for f in files:
        target = (model_dir / f["path"]).resolve()
        if not str(target).startswith(str(model_dir.resolve())):
            raise SystemExit(f"invalid model file path: {f['path']}")
        target.parent.mkdir(parents=True, exist_ok=True)
        part = target.with_name(f"{target.name}.part-{os.getpid()}")
        with urllib.request.urlopen(f["url"], timeout=600) as r, open(part, "wb") as out:
            while chunk := r.read(1 << 20):
                out.write(chunk)
        os.replace(part, target)
    marker.write_text(json.dumps(wanted))
    print(f"[mlp-serve] downloaded {len(files)} model files to {model_dir}", flush=True)


def to_input(msg: pb.TaskInput, tmp: Path):
    kind = msg.WhichOneof("kind")
    if kind == "tabular":
        row = dict(msg.tabular.numbers)
        row.update(msg.tabular.strings)
        return T.TabularInput(row=row)
    if kind == "text":
        return T.TextInput(text=msg.text.text, labels=list(msg.text.labels))
    if kind == "image":
        p = tmp / f"img-{id(msg)}"
        p.write_bytes(msg.image.data)
        return T.ImageInput(path=str(p), conf=msg.image.conf or 0.25, iou=msg.image.iou or 0.7, imgsz=msg.image.imgsz or None,
                            labels=list(msg.image.labels))
    if kind == "audio":
        p = tmp / f"audio-{id(msg)}.{msg.audio.format or 'wav'}"
        p.write_bytes(msg.audio.data)
        return T.AudioInput(path=str(p), language=msg.audio.language or None)
    if kind == "chat":
        return T.ChatInput(messages=[{"role": m.role, "content": m.content} for m in msg.chat.messages],
                           max_tokens=msg.chat.max_tokens or 512, temperature=msg.chat.temperature)
    raise ValueError(f"unsupported input: {kind}")


def to_output(o) -> pb.TaskOutput:
    if isinstance(o, T.TabularOutput):
        return pb.TaskOutput(tabular=pb.TabularOutput(label=o.label or "", probabilities=o.probabilities or {},
                                                      value=o.value or 0.0, has_value=o.value is not None))
    if isinstance(o, T.ClassifyOutput):
        return pb.TaskOutput(classify=pb.ClassifyOutput(labels=[pb.LabelScore(label=l, score=s) for l, s in o.labels]))
    if isinstance(o, T.DetectOutput):
        objs = [pb.DetectObject(class_id=x.class_id, class_name=x.class_name, score=x.score, x1=x.x1, y1=x.y1, x2=x.x2, y2=x.y2,
                                polygon=[c for pt in (x.polygon or []) for c in pt], angle=x.angle or 0.0) for x in o.objects]
        return pb.TaskOutput(detect=pb.DetectOutput(width=o.width, height=o.height, objects=objs))
    if isinstance(o, T.NerOutput):
        return pb.TaskOutput(ner=pb.NerOutput(entities=[pb.Entity(label=a, start=b, end=c, text=d, score=e) for a, b, c, d, e in o.entities]))
    if isinstance(o, T.GenerateOutput):
        return pb.TaskOutput(generate=pb.GenerateOutput(text=o.text, prompt_tokens=o.prompt_tokens, completion_tokens=o.completion_tokens))
    if isinstance(o, T.AsrOutput):
        return pb.TaskOutput(asr=pb.AsrOutput(text=o.text, segments=[pb.Segment(start=s[0] or 0, end=s[1] or 0, text=s[2], speaker=s[3] or "")
                                                                      for s in o.segments]))
    raise ValueError(f"unsupported output: {type(o).__name__}")


class Servicer(pbg.ModelServerServicer):
    def __init__(self, handler, task: str, handler_name: str, slots: int):
        self.handler, self.task, self.handler_name = handler, task, handler_name
        # Predict may use at most `slots` worker threads; the rest stay free so Health always answers under load.
        self._slots = threading.BoundedSemaphore(slots)

    def Predict(self, request, context):
        if not context.is_active():  # caller gave up while queued: skip the work
            context.abort(grpc.StatusCode.CANCELLED, "request cancelled")
        if not self._slots.acquire(timeout=float(os.environ.get("MLP_SERVE_QUEUE_WAIT", "0.5"))):
            context.abort(grpc.StatusCode.RESOURCE_EXHAUSTED, "replica is busy")
        try:
            return self._predict(request, context)
        finally:
            self._slots.release()

    def _predict(self, request, context):
        with tempfile.TemporaryDirectory() as d:
            try:
                inputs = [to_input(i, Path(d)) for i in request.inputs]
            except ValueError as e:
                context.abort(grpc.StatusCode.INVALID_ARGUMENT, str(e))
            outs = self.handler.predict(inputs)
            return pb.PredictResponse(outputs=[to_output(o) for o in outs])

    def Health(self, request, context):
        return pb.HealthResponse(ready=True)

    def Metadata(self, request, context):
        return pb.ServerMetadata(task=self.task, handler=self.handler_name)


def pick_device() -> str:
    """MLP_DEVICE from the scheduler (cuda when a GPU was allocated); otherwise CUDA if torch sees one, else CPU."""
    dev = os.environ.get("MLP_DEVICE")
    if dev:
        return dev
    try:
        import torch  # optional: recipes without torch serve on CPU
        return "cuda" if torch.cuda.is_available() else "cpu"
    except Exception:  # noqa: BLE001 — no torch / broken CUDA → CPU
        return "cpu"


def main() -> int:
    model_dir = Path(os.environ.get("MLP_MODEL_DIR", "/mnt/model"))
    model_dir.mkdir(parents=True, exist_ok=True)
    download_model(model_dir)
    handler_name = os.environ["MLP_HANDLER"]
    mod, cls = handler_name.split(":")
    sys.path.insert(0, os.environ.get("MLP_HANDLER_PATH", "/opt/recipe"))
    handler = getattr(importlib.import_module(mod), cls)()
    task = os.environ.get("MLP_TASK", "")
    device = pick_device()
    print(f"[mlp-serve] device: {device}", flush=True)
    handler.load(str(model_dir), os.environ.get("MLP_BASE_DIR"), device,
                 T.Signature(task=task, params=json.loads(os.environ.get("MLP_PARAMS", "{}"))))
    workers = max(2, int(os.environ.get("MLP_SERVE_WORKERS", "4")))
    # Bounded queue: excess calls are rejected (RESOURCE_EXHAUSTED) instead of piling up behind slow inference.
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=workers), maximum_concurrent_rpcs=workers * 4)
    pbg.add_ModelServerServicer_to_server(Servicer(handler, task, handler_name, workers - 1), server)
    port = int(os.environ.get("MLP_SERVE_PORT", "8500"))
    server.add_insecure_port(f"0.0.0.0:{port}")
    server.start()
    print(f"[mlp-serve] {handler_name} ready on :{port}", flush=True)
    server.wait_for_termination()
    return 0


if __name__ == "__main__":
    sys.exit(main())
