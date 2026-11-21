"""Assist handler for SAM 2.1 (cookbook 20/08 §10–11, M05 interactive segmentation).

`assist` deployment from a catalog variant: the shim builds a temporary model directory (20/08 §8):
  weights/sam2.pt (artifacts.best.as), config/variant.json ({"name", "config", ...}).
In: SegmentInput(path, points=[(x, y, label 1|0)], box=(x1, y1, x2, y2)?) — pixel coordinates.
Out: DetectOutput with one Obj carrying a polygon (largest contour) — the M05 adapter normalizes to 0–1.
Images are cached by path so consecutive clicks on the same image do not recompute the embedding.
"""
from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
import torch
from PIL import Image
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor

from mlp_serve.types import DetectOutput, Obj, SegmentInput


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir)
        variant = json.loads((d / "config" / "variant.json").read_text())
        ckpt = d / "weights" / "sam2.pt"
        self.predictor = SAM2ImagePredictor(build_sam2(variant["config"], str(ckpt), device=device))
        self.device = device
        self._current: str | None = None

    def _set_image(self, path: str) -> tuple[int, int]:
        img = np.array(Image.open(path).convert("RGB"))
        if self._current != path:
            self.predictor.set_image(img)
            self._current = path
        return img.shape[1], img.shape[0]

    @torch.inference_mode()
    def predict(self, batch: list[SegmentInput]) -> list[DetectOutput]:
        out = []
        for item in batch:
            w, h = self._set_image(item.path)
            coords = np.array([[p[0], p[1]] for p in item.points], dtype=np.float32) if item.points else None
            labels = np.array([p[2] for p in item.points], dtype=np.int32) if item.points else None
            box = np.array(item.box, dtype=np.float32) if item.box else None
            masks, scores, _ = self.predictor.predict(point_coords=coords, point_labels=labels, box=box,
                                                      multimask_output=False)
            mask = masks[0].astype(np.uint8)
            contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            objs = []
            if contours:
                c = max(contours, key=cv2.contourArea)
                x, y, bw, bh = cv2.boundingRect(c)
                objs.append(Obj(class_id=-1, class_name="", score=float(scores[0]),
                                x1=float(x), y1=float(y), x2=float(x + bw), y2=float(y + bh),
                                polygon=c.reshape(-1, 2).astype(float).tolist()))
            out.append(DetectOutput(width=w, height=h, objects=objs))
        return out
