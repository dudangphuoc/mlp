"""Serve handler for any ONNX model (cookbook 20/08 §10).

Standard model directory (D-28): weights/model.onnx, classes.json, config/onnx.json:
{
  "task": "classify" | "detect" | "tabular-classify" | "tabular-regress",
  "input": {"name": "images", "size": [640, 640], "layout": "NCHW", "scale": 255.0,
            "mean": [0, 0, 0], "std": [1, 1, 1], "rgb": true},          # image
  "features": ["a", "b"],                                                 # tabular
  "detect_layout": "boxes_scores_labels" | "yolo",                        # detect
  "outputs": {"boxes": "boxes", "scores": "scores", "labels": "labels"}, # output names per layout
  "resample": "bilinear" | "bicubic" | "nearest",                        # resize filter (default bicubic = Pillow default)
  "extra_inputs": {"orig_target_sizes": {"value": "orig_size", "dtype": "int64"}}
}
extra_inputs: further graph inputs fed per image. value "orig_size" = [[w, h]] of the original image (DETR-family exports take it
and return boxes in original pixels, so boxes are not rescaled); "input_size" = [[size_w, size_h]] (boxes are rescaled as usual).

"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

RESAMPLE = {"bilinear": Image.BILINEAR, "bicubic": Image.BICUBIC, "nearest": Image.NEAREST}


def extra_feeds(cfg: dict, w: int, h: int) -> tuple[dict, bool]:
    """Extra graph inputs for one image, and whether boxes still need rescaling to the original size."""
    feeds, rescale = {}, True
    for name, spec in (cfg.get("extra_inputs") or {}).items():
        size = [w, h] if spec.get("value") == "orig_size" else list(cfg["input"]["size"])
        if spec.get("value") == "orig_size":
            rescale = False
        feeds[name] = np.array([size], dtype=np.int64 if spec.get("dtype", "int64") == "int64" else np.float32)
    return feeds, rescale


from mlp_serve.types import (ClassifyOutput, DetectOutput, ImageInput, Obj, TabularInput, TabularOutput)


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir)
        self.cfg = json.loads((d / "config" / "onnx.json").read_text())
        providers = ["CUDAExecutionProvider", "CPUExecutionProvider"] if device.startswith("cuda") else ["CPUExecutionProvider"]
        self.sess = ort.InferenceSession(str(d / "weights" / "model.onnx"), providers=providers)
        self.classes = json.loads((d / "classes.json").read_text()) if (d / "classes.json").exists() else None
        self.task = self.cfg["task"]

    def _name(self, cid: int) -> str:
        return self.classes[cid] if self.classes and 0 <= cid < len(self.classes) else str(cid)

    def _image(self, path: str) -> tuple[np.ndarray, int, int]:
        c = self.cfg["input"]
        img = Image.open(path).convert("RGB")
        w, h = img.size
        x = np.asarray(img.resize(tuple(c["size"]), RESAMPLE[self.cfg.get("resample", "bicubic")]), dtype=np.float32) / float(c.get("scale", 255.0))
        if not c.get("rgb", True):
            x = x[..., ::-1]  # RGB → BGR (Pillow has no BGR mode)
        x = (x - np.array(c.get("mean", [0, 0, 0]), dtype=np.float32)) / np.array(c.get("std", [1, 1, 1]), dtype=np.float32)
        if c.get("layout", "NCHW") == "NCHW":
            x = x.transpose(2, 0, 1)
        return x[None], w, h

    def predict(self, batch):
        if self.task.startswith("tabular"):
            return self._tabular(batch)
        return [self._one_image(item) for item in batch]

    def _tabular(self, batch: list[TabularInput]) -> list[TabularOutput]:
        x = np.array([[float(item.row[f]) for f in self.cfg["features"]] for item in batch], dtype=np.float32)
        outs = self.sess.run(None, {self.sess.get_inputs()[0].name: x})
        if self.task == "tabular-regress":
            return [TabularOutput(value=float(v)) for v in np.ravel(outs[0])]
        probs = outs[1] if len(outs) > 1 else None
        res = []
        for i, lbl in enumerate(np.ravel(outs[0])):
            p = None
            if probs is not None:
                row = probs[i]
                p = {str(k): float(v) for k, v in (row.items() if isinstance(row, dict) else enumerate(row))}
            res.append(TabularOutput(label=str(lbl), probabilities=p))
        return res

    def _one_image(self, item: ImageInput):
        x, w, h = self._image(item.path)
        extra, rescale = extra_feeds(self.cfg, w, h)
        outs = dict(zip([o.name for o in self.sess.get_outputs()], self.sess.run(None, {self.cfg["input"]["name"]: x, **extra})))
        if self.task == "classify":
            logits = next(iter(outs.values()))[0]
            e = np.exp(logits - logits.max())
            p = e / e.sum()
            top = np.argsort(-p)[:5]
            return ClassifyOutput(labels=[(self._name(int(i)), float(p[i])) for i in top])
        sx, sy = (w / self.cfg["input"]["size"][0], h / self.cfg["input"]["size"][1]) if rescale else (1.0, 1.0)
        objs = []
        if self.cfg.get("detect_layout", "boxes_scores_labels") == "yolo":
            pred = next(iter(outs.values()))[0]          # [4+C, N] or [N, 4+C]
            pred = pred.T if pred.shape[0] < pred.shape[1] else pred
            for row in pred:
                cid = int(np.argmax(row[4:]))
                sc = float(row[4 + cid])
                if sc < item.conf:
                    continue
                cx, cy, bw, bh = row[:4]
                objs.append(Obj(class_id=cid, class_name=self._name(cid), score=sc,
                                x1=float((cx - bw / 2) * sx), y1=float((cy - bh / 2) * sy),
                                x2=float((cx + bw / 2) * sx), y2=float((cy + bh / 2) * sy)))
            # NMS is not done here — YOLO model exports should enable nms=True (openItems)
        else:
            names = self.cfg["outputs"]
            boxes, scores, labels = outs[names["boxes"]][0], outs[names["scores"]][0], outs[names["labels"]][0]
            for b, sc, lbl in zip(boxes, scores, labels):
                if float(sc) < item.conf:
                    continue
                objs.append(Obj(class_id=int(lbl), class_name=self._name(int(lbl)), score=float(sc),
                                x1=float(b[0] * sx), y1=float(b[1] * sy), x2=float(b[2] * sx), y2=float(b[3] * sy)))
        return DetectOutput(width=w, height=h, objects=objs)
