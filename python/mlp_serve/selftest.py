"""Trial self-test (round 1 R15–R22): load a packaged model directory through the recipe's serve handler, the way server.py does,
and predict a few images. Prints one line: MLP_SELFTEST {"ok": true, "images": n, "objects": k} (or {"ok": false, "error": "..."}).

  python -m mlp_serve.selftest --model-dir <dir> --images <dir> [--limit 8] [--handler module:Class] [--task detect] [--params-json {...}]

The handler defaults to MLP_HANDLER; the import path to MLP_HANDLER_PATH (default /opt/recipe), as in server.py.
"""
from __future__ import annotations

import argparse
import importlib
import json
import os
import sys
import traceback
from pathlib import Path

from mlp_serve import types as T
from mlp_serve.server import pick_device

IMAGE_EXT = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


def report(**fields) -> None:
    print("MLP_SELFTEST " + json.dumps(fields), flush=True)


def count_objects(out) -> int:
    for name in ("objects", "labels", "entities", "segments"):
        v = getattr(out, name, None)
        if isinstance(v, list):
            return len(v)
    return 0


def main() -> int:
    a = argparse.ArgumentParser()
    a.add_argument("--model-dir", type=Path, required=True)
    a.add_argument("--images", type=Path, required=True)
    a.add_argument("--limit", type=int, default=8)
    a.add_argument("--handler", default=os.environ.get("MLP_HANDLER", ""))
    a.add_argument("--task", default=os.environ.get("MLP_TASK", ""))
    a.add_argument("--params-json", default=os.environ.get("MLP_PARAMS", "{}"))
    args = a.parse_args()
    if ":" not in args.handler:
        report(ok=False, error="no serve handler (module:Class)")
        return 1
    images = sorted(p for p in args.images.rglob("*") if p.is_file() and p.suffix.lower() in IMAGE_EXT)[: max(1, args.limit)]
    if not images:
        report(ok=False, error=f"no images under {args.images}")
        return 1
    try:
        mod, cls = args.handler.split(":")
        sys.path.insert(0, os.environ.get("MLP_HANDLER_PATH", "/opt/recipe"))
        handler = getattr(importlib.import_module(mod), cls)()
        handler.load(str(args.model_dir), os.environ.get("MLP_BASE_DIR"), pick_device(),
                     T.Signature(task=args.task, params=json.loads(args.params_json or "{}")))
        outs = handler.predict([T.ImageInput(path=str(p), conf=0.25, iou=0.7, imgsz=None, labels=[]) for p in images])
        objects = sum(count_objects(o) for o in outs)
    except Exception as e:  # noqa: BLE001 — any failure of the handler is the result of the test
        traceback.print_exc()
        report(ok=False, error=f"{type(e).__name__}: {e}"[:300])
        return 1
    report(ok=True, images=len(images), objects=objects)
    return 0


if __name__ == "__main__":
    sys.exit(main())
