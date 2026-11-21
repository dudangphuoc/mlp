"""Shared serve/pre-label handler for D-FINE, DEIM/DEIMv2, RT-DETRv4.

Built after the repos' tools/inference/torch_inf.py (they only write result images, no JSON output):
YAMLConfig(cfg, resume=ckpt) → load state 'ema'.'module' or 'model' → model.deploy() + postprocessor.deploy()
→ forward(images, orig_target_sizes) → (labels, boxes, scores).
Repo Python package: D-FINE `src`, RT-DETRv4/DEIMv2 `engine` — both are tried.

Standard model directory (D-28, cookbook 20/08 §8), two modes:
  - trained model:    weights/model.pth, config/mlp_train.yml (generated config), classes.json
  - catalog variant:  weights/model.pth (pretrained), config/variant.json, classes.json (coco80)
Input/output types: mlp_serve.types (20/08 §10) — pixel coordinates.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import torch
import torchvision.transforms as T
from PIL import Image

from mlp_serve.types import DetectOutput, ImageInput, Obj

sys.path.insert(0, "/opt/recipe/src")
try:
    from engine.core import YAMLConfig  # RT-DETRv4, DEIM, DEIMv2
except ImportError:  # pragma: no cover
    from src.core import YAMLConfig  # D-FINE


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir)
        weights = d / "weights" / "model.pth"
        cfg_path = d / "config" / "mlp_train.yml"
        overrides = {}
        if not cfg_path.exists():
            # Catalog variant mode (20/08 §8): pretrained COCO weights + the variant's original config.
            # Disable remapping so output labels are contiguous indices 0..79 matching classes.json (coco80).
            variant = json.loads((d / "config" / "variant.json").read_text())
            cfg_path = Path("/opt/recipe/src") / variant["config"]
            overrides["remap_mscoco_category"] = False
        cfg = YAMLConfig(str(cfg_path), resume=str(weights), **overrides)
        ckpt = torch.load(weights, map_location="cpu", weights_only=False)
        state = ckpt["ema"]["module"] if "ema" in ckpt else ckpt["model"]
        cfg.model.load_state_dict(state)
        self.model = cfg.model.deploy().to(device).eval()
        self.post = cfg.postprocessor.deploy()
        self.device = device
        size = cfg.yaml_cfg.get("eval_spatial_size", [640, 640])
        steps = [T.Resize(tuple(size)), T.ToTensor()]
        if "dinov3" in cfg_path.read_text().lower():  # DEIMv2's torch_inf.py: ImageNet normalization for the DINOv3 backbone
            steps.append(T.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]))
        self.tf = T.Compose(steps)
        self.classes = json.loads((d / "classes.json").read_text()) if (d / "classes.json").exists() else None

    @torch.no_grad()
    def predict(self, batch: list[ImageInput]) -> list[DetectOutput]:
        out = []
        for item in batch:
            img = Image.open(item.path).convert("RGB")
            w, h = img.size
            x = self.tf(img).unsqueeze(0).to(self.device)
            sizes = torch.tensor([[w, h]], device=self.device)
            labels, boxes, scores = self.post(self.model(x), sizes)
            objs = []
            for lbl, box, sc in zip(labels[0].tolist(), boxes[0].tolist(), scores[0].tolist()):
                if sc < item.conf:
                    continue
                cid = int(lbl)
                objs.append(Obj(class_id=cid, class_name=self.classes[cid] if self.classes else str(cid),
                                score=float(sc), x1=box[0], y1=box[1], x2=box[2], y2=box[3]))
            out.append(DetectOutput(width=w, height=h, objects=objs))
        return out
