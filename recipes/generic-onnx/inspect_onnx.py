"""Proposes config/onnx.json for the generic-onnx recipe (cookbook 20/08 §5.5).

Reads the ONNX graph: first input name/shape, output names; guesses the image layout and detect output type.
Writes {out}/config/onnx.json and {out}/signature.json (a proposal — the user confirms in M11 S-11-4).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import onnxruntime as ort


def fail(reason: str, **kw) -> None:
    print("MLP_ERROR " + json.dumps({"reason": reason, **kw}), file=sys.stderr)
    sys.exit(2)


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--model", type=Path, required=True)
    a.add_argument("--task", required=True)
    a.add_argument("--out", type=Path, required=True)
    args = a.parse_args()
    if not args.model.exists():
        fail("model_missing", path=str(args.model))
    sess = ort.InferenceSession(str(args.model), providers=["CPUExecutionProvider"])
    inp = sess.get_inputs()[0]
    outs = [o.name for o in sess.get_outputs()]
    cfg: dict = {"task": args.task}
    if args.task.startswith("tabular"):
        n = inp.shape[-1] if isinstance(inp.shape[-1], int) else 0
        cfg["features"] = [f"f{i}" for i in range(n)]   # the user renames these to the real column names
        cfg["input"] = {"name": inp.name}
    else:
        shape = [d if isinstance(d, int) else -1 for d in inp.shape]
        nchw = len(shape) == 4 and shape[1] in (1, 3)
        size = [shape[3], shape[2]] if nchw else [shape[2], shape[1]]
        size = [s if s > 0 else 640 for s in size]
        cfg["input"] = {"name": inp.name, "size": size, "layout": "NCHW" if nchw else "NHWC",
                        "scale": 255.0, "mean": [0, 0, 0], "std": [1, 1, 1], "rgb": True}
        if args.task == "detect":
            names = {n.lower(): n for n in outs}
            if {"boxes", "scores", "labels"} <= set(names):
                cfg["detect_layout"] = "boxes_scores_labels"
                cfg["outputs"] = {k: names[k] for k in ("boxes", "scores", "labels")}
            else:
                cfg["detect_layout"] = "yolo"
        # DETR-family exports take the original image size as a second input and return boxes in original pixels.
        extra = {i.name: {"value": "orig_size", "dtype": "int64" if "int64" in i.type else "float32"}
                 for i in sess.get_inputs()[1:] if "size" in i.name.lower()}
        if extra:
            cfg["extra_inputs"] = extra
            cfg["resample"] = "bilinear"
    (args.out / "config").mkdir(parents=True, exist_ok=True)
    (args.out / "config" / "onnx.json").write_text(json.dumps(cfg, indent=2))
    (args.out / "signature.json").write_text(json.dumps({
        "task": args.task,
        "inputs": [{"name": i.name, "dtype": i.type, "shape": [d if isinstance(d, int) else -1 for d in i.shape]} for i in sess.get_inputs()],
        "outputs": [{"name": o.name, "dtype": o.type, "shape": [d if isinstance(d, int) else -1 for d in o.shape]} for o in sess.get_outputs()]}, indent=2))
    print(json.dumps(cfg))


if __name__ == "__main__":
    main()
