"""Entry script for the rf-detr recipe (rfdetr==1.12.1 Python API).

train : RFDETR<Size>(pretrain_weights=...).train(dataset_dir=..., output_dir=..., devices="auto", ...)
eval  : RFDETR.from_checkpoint(...).evaluate(dataset_dir=..., split=...) -> metrics.jsonl
export: RFDETR.from_checkpoint(...).export(format=..., output_dir=..., output_name="model")
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import rfdetr

CLASSES = {
    "rfdetr-nano": "RFDETRNano", "rfdetr-small": "RFDETRSmall", "rfdetr-medium": "RFDETRMedium",
    "rfdetr-large": "RFDETRLarge", "rfdetr-xlarge": "RFDETRXLarge", "rfdetr-2xlarge": "RFDETR2XLarge",
    "rfdetr-seg-nano": "RFDETRSegNano", "rfdetr-seg-small": "RFDETRSegSmall", "rfdetr-seg-medium": "RFDETRSegMedium",
    "rfdetr-seg-large": "RFDETRSegLarge", "rfdetr-seg-xlarge": "RFDETRSegXLarge", "rfdetr-seg-2xlarge": "RFDETRSeg2XLarge",
}


def user_error(reason: str, **extra) -> None:
    print("MLP_ERROR " + json.dumps({"reason": reason, **extra}), file=sys.stderr)
    sys.exit(2)


def train(a) -> None:
    cls_name = CLASSES.get(a.variant) or user_error("unknown variant", variant=a.variant)
    p = json.loads(a.params_json)
    kwargs = {"pretrain_weights": a.weights} if a.weights and a.weights != "none" else {}
    model = getattr(rfdetr, cls_name)(**kwargs)
    amp = p.pop("amp_dtype", "auto")
    model.train(
        dataset_dir=str(a.data), output_dir=str(a.output), devices="auto",
        amp_dtype=None if amp == "none" else amp,
        resume=a.resume, tensorboard=True, **p,
    )


def evaluate(a) -> None:
    model = rfdetr.RFDETR.from_checkpoint(str(a.model))
    metrics = model.evaluate(dataset_dir=str(a.data), split=a.split)
    a.output.mkdir(parents=True, exist_ok=True)
    with (a.output / "metrics.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps({"step": 0, **{k: float(v) for k, v in metrics.items() if isinstance(v, (int, float))}}) + "\n")


def export(a) -> None:
    model = rfdetr.RFDETR.from_checkpoint(str(a.model))
    model.export(format=a.format, output_dir=str(a.output), output_name="model")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["train", "eval", "export"])
    ap.add_argument("--variant")
    ap.add_argument("--data", type=Path)
    ap.add_argument("--output", type=Path, required=True)
    ap.add_argument("--weights")
    ap.add_argument("--model", type=Path)
    ap.add_argument("--split", default="test")
    ap.add_argument("--format", default="onnx")
    ap.add_argument("--resume")
    ap.add_argument("--params-json", default="{}")
    a = ap.parse_args()
    {"train": train, "eval": evaluate, "export": export}[a.mode](a)


if __name__ == "__main__":
    main()
