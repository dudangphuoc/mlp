"""Serve/pre-label handler for ultralytics-yolo (cookbook 20/08 §10).

Standard model directory (D-28): weights/best.pt. Supports detect, segment, pose, obb, classify.
Pixel coordinates; obb returns 4 corners (polygon) + angle (radians, per ultralytics xywhr).
"""
from __future__ import annotations

from pathlib import Path

from ultralytics import YOLO

from mlp_serve.types import ClassifyOutput, DetectOutput, ImageInput, Obj


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        self.model = YOLO(str(Path(model_dir) / "weights" / "best.pt"))
        self.device = device

    def predict(self, batch: list[ImageInput]) -> list[DetectOutput | ClassifyOutput]:
        first = batch[0]
        kwargs = {"conf": first.conf, "iou": first.iou, "device": self.device, "verbose": False}
        if first.imgsz:
            kwargs["imgsz"] = first.imgsz
        results = self.model.predict([b.path for b in batch], **kwargs)
        return [self._convert(r) for r in results]

    @staticmethod
    def _convert(r) -> DetectOutput | ClassifyOutput:
        h, w = r.orig_shape
        if r.probs is not None:
            return ClassifyOutput(labels=[(r.names[int(c)], float(r.probs.data[int(c)])) for c in r.probs.top5])
        objs: list[Obj] = []
        if r.obb is not None:
            for i in range(len(r.obb)):
                x1, y1, x2, y2 = (float(v) for v in r.obb.xyxy[i])
                cid = int(r.obb.cls[i])
                objs.append(Obj(class_id=cid, class_name=r.names[cid], score=float(r.obb.conf[i]),
                                x1=x1, y1=y1, x2=x2, y2=y2,
                                polygon=r.obb.xyxyxyxy[i].tolist(), angle=float(r.obb.xywhr[i][4])))
        elif r.boxes is not None:
            for i in range(len(r.boxes)):
                x1, y1, x2, y2 = (float(v) for v in r.boxes.xyxy[i])
                cid = int(r.boxes.cls[i])
                objs.append(Obj(class_id=cid, class_name=r.names[cid], score=float(r.boxes.conf[i]),
                                x1=x1, y1=y1, x2=x2, y2=y2,
                                polygon=r.masks.xy[i].tolist() if r.masks is not None else None,
                                keypoints=r.keypoints.xy[i].tolist() if r.keypoints is not None else None))
        return DetectOutput(width=w, height=h, objects=objs)
