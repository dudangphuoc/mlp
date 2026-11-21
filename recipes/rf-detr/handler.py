"""Serve/pre-label handler for rf-detr (cookbook 20/08 §10).

Standard model directory (D-28): weights/checkpoint_best_total.pth, classes.json; or variant mode: config/variant.json.
Segment variants: return a polygon (largest contour of the mask) — pixel coordinates.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import supervision as sv
from PIL import Image
from rfdetr import RFDETR

from mlp_serve.types import DetectOutput, ImageInput, Obj


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir)
        ckpt = d / "weights" / "checkpoint_best_total.pth"
        if ckpt.exists():
            self.model = RFDETR.from_checkpoint(str(ckpt))
        else:
            # Catalog variant mode (20/08 §8): no weight file — instantiate the class by variant name,
            # the library downloads the pretrained COCO weights itself.
            import rfdetr
            from run_rfdetr import CLASSES
            variant = json.loads((d / "config" / "variant.json").read_text())
            self.model = getattr(rfdetr, CLASSES[variant["name"]])()
            # Pretrained COCO returns the original COCO category_id (1..90) → mapped by name to the index in classes.json (coco80).
            from rfdetr.util.coco_classes import COCO_CLASSES
            self.coco_names = COCO_CLASSES
        self.classes = json.loads((d / "classes.json").read_text()) if (d / "classes.json").exists() else None
        self.coco_names = getattr(self, "coco_names", None)

    def _name(self, cid: int, det_names, i: int) -> str:
        if self.classes and 0 <= cid < len(self.classes):
            return self.classes[cid]
        return str(det_names[i]) if det_names is not None and det_names[i] is not None else str(cid)

    @staticmethod
    def _polygon(mask: np.ndarray) -> list[list[float]] | None:
        polys = sv.mask_to_polygons(mask.astype(bool))
        if not polys:
            return None
        biggest = max(polys, key=lambda p: len(p))
        return biggest.astype(float).tolist()

    def predict(self, batch: list[ImageInput]) -> list[DetectOutput]:
        out = []
        for item in batch:
            img = Image.open(item.path).convert("RGB")
            det = self.model.predict(img, threshold=item.conf)
            names = det.data.get("class_name") if det.data else None
            objs = []
            for i in range(len(det)):
                x1, y1, x2, y2 = (float(v) for v in det.xyxy[i])
                cid = int(det.class_id[i])
                if self.coco_names is not None:
                    name = self.coco_names.get(cid, str(cid))
                    cid = self.classes.index(name) if self.classes and name in self.classes else cid
                objs.append(Obj(class_id=cid, class_name=self._name(cid, names, i), score=float(det.confidence[i]),
                                x1=x1, y1=y1, x2=x2, y2=y2,
                                polygon=self._polygon(det.mask[i]) if det.mask is not None else None))
            out.append(DetectOutput(width=img.width, height=img.height, objects=objs))
        return out
