"""Generates training configs for the DETR family sharing one code base (D-FINE, DEIM/DEIMv2, RT-DETRv4).

Why: the repos' original configs all include `../dataset/coco_detection.yml` and tie the training schedule to many
epoch-dependent keys (flat_epoch, no_aug_epoch, stop_epoch, policy.epoch, mixup_epochs, …).
This script creates a new config that:
  - __include__s the original variant config (absolute path),
  - points the dataset at the platform-converted COCO data, num_classes, remap_mscoco_category=False,
  - sets the epoch count and proportionally scales every epoch-dependent schedule key,
  - sets the total batch size and output_dir.
Epoch key: D-FINE uses `epochs`, RT-DETRv4/DEIM/DEIMv2 use `epoches` (--epoch-key).

Examples:
  train: python make_config.py --base /opt/recipe/src/configs/dfine/dfine_hgnetv2_s_coco.yml \
           --data /mnt/data --num-classes 7 --epochs 72 --batch 16 --epoch-key epochs \
           --output-dir /mnt/out --out /mnt/out/mlp_train.yml
  eval:  python make_config.py --base /mnt/model/config/mlp_train.yml --data /mnt/data \
           --train-split '' --val-split test --output-dir /mnt/out --out /mnt/out/mlp_eval.yml
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import yaml

# Keys (dotted paths) tied to the epoch count in the original config; missing keys are skipped.
SCHEDULE_KEYS = [
    "flat_epoch",
    "no_aug_epoch",
    "mixup_epochs",
    "copyblend_epochs",
    "train_dataloader.dataset.transforms.policy.epoch",
    "train_dataloader.collate_fn.stop_epoch",
    "train_dataloader.collate_fn.mixup_epochs",
    "train_dataloader.collate_fn.copyblend_epochs",
    "DEIMCriterion.matcher.matcher_change_epoch",
]


def load_merged(path: Path) -> dict:
    """Reads a config + recursive __include__ (enough to get the original schedule values)."""
    cfg = yaml.safe_load(path.read_text()) or {}
    merged: dict = {}
    for inc in cfg.pop("__include__", []):
        deep_merge(merged, load_merged((path.parent / inc).resolve()))
    deep_merge(merged, cfg)
    return merged


def deep_merge(dst: dict, src: dict) -> None:
    for k, v in src.items():
        if isinstance(v, dict) and isinstance(dst.get(k), dict):
            deep_merge(dst[k], v)
        else:
            dst[k] = v


def get(d: dict, dotted: str):
    for part in dotted.split("."):
        if not isinstance(d, dict) or part not in d:
            return None
        d = d[part]
    return d


def put(d: dict, dotted: str, value) -> None:
    parts = dotted.split(".")
    for part in parts[:-1]:
        d = d.setdefault(part, {})
    d[parts[-1]] = value


def scale(value, ratio: float):
    if isinstance(value, list):
        return [scale(v, ratio) for v in value]
    if isinstance(value, (int, float)):
        return max(1, round(value * ratio)) if value > 0 else value
    return value


TUNE_KEYS = ("lr", "backbone_lr_scale", "freeze", "img_size", "multiscale", "aug_strong", "aug_flip")
STRONG_AUG = ("RandomPhotometricDistort", "RandomZoomOut", "RandomIoUCrop")


def flag(text: str) -> bool:
    return str(text).strip().lower() in ("1", "true", "yes", "on")


def resized(ops: list, size: int) -> list:
    return [{**op, "size": [size, size]} if isinstance(op, dict) and op.get("type") == "Resize" else op for op in ops]


def apply_tune(base: dict, cfg: dict, t: dict) -> None:
    """Shallow tuning (round 1, D-45): each key is one knob of the canvas. Lists are written whole — the upstream loader replaces
    a list instead of merging it — starting from the variant's own merged config. A key that is None is left as the variant has it."""
    if t.get("lr") is not None or t.get("backbone_lr_scale") is not None:
        old = get(base, "optimizer.lr")
        lr = float(t["lr"]) if t.get("lr") is not None else old
        put(cfg, "optimizer.lr", lr)
        groups = [dict(g) for g in (get(base, "optimizer.params") or [])]
        for g in groups:
            if "backbone" in str(g.get("params", "")) and "lr" in g:
                # No explicit scale: keep the ratio the variant ships with, so changing the peak LR moves the backbone with it.
                scale = float(t["backbone_lr_scale"]) if t.get("backbone_lr_scale") is not None else (g["lr"] / old if old else 1.0)
                g["lr"] = lr * scale
        if groups:
            put(cfg, "optimizer.params", groups)
    if t.get("freeze") in ("backbone", "none") and isinstance(base.get("HGNetv2"), dict):
        frozen = t["freeze"] == "backbone"
        put(cfg, "HGNetv2.freeze_at", 3 if frozen else -1)
        put(cfg, "HGNetv2.freeze_stem_only", False)   # upstream default is True: freeze_at alone would lock only the stem
        put(cfg, "HGNetv2.freeze_norm", frozen)
    train_ops = get(base, "train_dataloader.dataset.transforms.ops")
    if isinstance(train_ops, list) and (t.get("img_size") is not None or t.get("aug_strong") is False or t.get("aug_flip") is False):
        ops = list(train_ops)
        if t.get("aug_strong") is False:
            ops = [o for o in ops if not (isinstance(o, dict) and o.get("type") in STRONG_AUG)]
        if t.get("aug_flip") is False:
            ops = [o for o in ops if not (isinstance(o, dict) and o.get("type") == "RandomHorizontalFlip")]
        if t.get("img_size") is not None:
            ops = resized(ops, int(t["img_size"]))
        put(cfg, "train_dataloader.dataset.transforms.ops", ops)
    if t.get("img_size") is not None:
        size = int(t["img_size"])
        val_ops = get(base, "val_dataloader.dataset.transforms.ops")
        if isinstance(val_ops, list):
            put(cfg, "val_dataloader.dataset.transforms.ops", resized(val_ops, size))
        put(cfg, "train_dataloader.collate_fn.base_size", size)
        put(cfg, "eval_spatial_size", [size, size])   # the serve handler reads this key too
    if t.get("multiscale") is False:
        put(cfg, "train_dataloader.collate_fn.base_size_repeat", None)


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--base", type=Path, required=True, help="original variant config, or the generated config for eval")
    a.add_argument("--data", type=Path, help="root of the platform-converted COCO tree: {split}/images, {split}/annotations.json")
    a.add_argument("--no-data", action="store_true", help="inspect only: write the overlay without dataset paths")
    a.add_argument("--lr", type=float)
    a.add_argument("--backbone-lr-scale", type=float)
    a.add_argument("--freeze", choices=["none", "backbone"])
    a.add_argument("--img-size", type=int)
    a.add_argument("--multiscale", type=flag)
    a.add_argument("--aug-strong", type=flag)
    a.add_argument("--aug-flip", type=flag)
    a.add_argument("--params-json", default="{}", help="the same knobs (plus epochs, batch) as one JSON object; flags win")
    a.add_argument("--num-classes", type=int, help="empty = keep the value from --base")
    a.add_argument("--epochs", type=int)
    a.add_argument("--batch", type=int)
    a.add_argument("--epoch-key", choices=["epochs", "epoches"], help="required with --epochs")
    a.add_argument("--train-split", default="train", help="empty = leave the train dataset unchanged (used for eval)")
    a.add_argument("--val-split", default="val", help="split used for val_dataloader; eval passes 'test'")
    a.add_argument("--output-dir", type=Path, required=True)
    a.add_argument("--extra-json", default="{}", help="extra overrides, as {\"a.b\": value}")
    a.add_argument("--out", type=Path, required=True)
    args = a.parse_args()
    given = json.loads(args.params_json or "{}")
    tune = {k: (getattr(args, k) if getattr(args, k) is not None else given.get(k)) for k in TUNE_KEYS}
    args.epochs = args.epochs or given.get("epochs")
    args.batch = args.batch or given.get("batch")
    if args.epochs and not args.epoch_key:
        a.error("--epoch-key is required with --epochs")
    if not args.no_data and args.data is None:
        a.error("--data is required (or --no-data)")

    base = load_merged(args.base)
    cfg: dict = {"__include__": [str(args.base.resolve())]}
    if args.num_classes:
        put(cfg, "num_classes", args.num_classes)
    put(cfg, "remap_mscoco_category", False)
    put(cfg, "output_dir", str(args.output_dir))
    if args.no_data:
        pass
    elif args.train_split:
        put(cfg, "train_dataloader.dataset.img_folder", str(args.data / args.train_split / "images"))
        put(cfg, "train_dataloader.dataset.ann_file", str(args.data / args.train_split / "annotations.json"))
    if not args.no_data:
        val_dir = args.data / args.val_split
        if not (val_dir / "annotations.json").exists():
            _user_error("split_missing", split=args.val_split)
        put(cfg, "val_dataloader.dataset.img_folder", str(val_dir / "images"))
        put(cfg, "val_dataloader.dataset.ann_file", str(val_dir / "annotations.json"))
    if args.batch:
        put(cfg, "train_dataloader.total_batch_size", args.batch)

    if args.epochs:
        old = base.get(args.epoch_key)
        put(cfg, args.epoch_key, args.epochs)
        if isinstance(old, (int, float)) and old > 0:
            ratio = args.epochs / old
            for key in SCHEDULE_KEYS:
                v = get(base, key)
                if v is not None:
                    put(cfg, key, scale(v, ratio))

    apply_tune(base, cfg, tune)

    for key, value in json.loads(args.extra_json).items():
        put(cfg, key, value)
    # Platform overrides (trial passes, denser progress lines) come through the environment, never through the command template.
    env_extra = os.environ.get("MLP_EXTRA_JSON", "").strip()
    if env_extra:
        extra = json.loads(env_extra)
        if not isinstance(extra, dict):
            _user_error("extra_json_invalid", message="MLP_EXTRA_JSON must be a JSON object")
        for key, value in extra.items():
            put(cfg, key, value)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(yaml.safe_dump(cfg, sort_keys=False, allow_unicode=True))
    print(f"wrote {args.out}")


def _user_error(reason: str, **fields) -> None:
    """User error: print MLP_ERROR to stderr and exit with code 2 (20/08 §12)."""
    print("MLP_ERROR " + json.dumps({"reason": reason, **fields}), file=sys.stderr)
    sys.exit(2)


if __name__ == "__main__":
    main()
