"""Platform converter: a downloaded detection dataset → the COCO tree recipes with `data.input: coco` read.

Output (what recipes/_shared/detr_family/make_config.py expects):
    {dst}/train/images/*, {dst}/train/annotations.json
    {dst}/val/images/*,   {dst}/val/annotations.json
Category ids are 0..N-1 in the dataset's class order (the label schema), so remap_mscoco_category stays off.
Images are symlinked, not copied.

Accepted inputs (detected automatically):
  - COCO tree: {split}/annotations.json (+ images next to it or in {split}/images) → linked as is; a missing val split is cut from train.
  - YOLO: image files with a matching .txt label file (`class cx cy w h`, normalized), in a sibling folder named
    labels/ or yololabels/ (…/images/x.jpg ↔ …/labels/x.txt) or next to the image.
Splits (D-49 — fixed per dataset version, never drawn per training run):
  1. `mlp-split.json` at the dataset root ({"files": {"<image path>": "train|val|test"}}), written by the platform's SplitVersion;
  2. otherwise train/val/valid/test folders in the path;
  3. otherwise (older versions, pipelines) a hash of the image path: sha256("0:<path>") mod 10000 >= 8500 → val. Same rule as
     SplitRules.Bucket in the control plane; it does not depend on the run's seed, so every run of a version sees the same val set.
The test split is never linked into the training tree.
Class names: --classes (JSON list, index = class id) wins; otherwise data.yaml / notes.json / classes.txt from the dataset.

User errors print `MLP_ERROR {json}` and exit 2 (20/08 §12).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
import re
import sys
from pathlib import Path

IMAGE_EXT = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
LABEL_DIRS = ("labels", "yololabels", "yolo_labels")
SPLIT_ALIASES = {"train": "train", "training": "train", "val": "val", "valid": "val", "validation": "val", "test": "test"}


def fail(reason: str, message: str) -> None:
    print("MLP_ERROR " + json.dumps({"reason": reason, "message": message}), file=sys.stderr)
    sys.exit(2)


def image_size(path: Path) -> tuple[int, int]:
    try:
        from PIL import Image  # header only; the pixels are not decoded
        with Image.open(path) as im:
            return im.size
    except ImportError:
        return _header_size(path)


def _header_size(path: Path) -> tuple[int, int]:
    """JPEG/PNG size without PIL."""
    data = path.read_bytes()[:256 * 1024]
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")
    i = 2
    while i + 9 < len(data):
        if data[i] != 0xFF:
            i += 1
            continue
        marker = data[i + 1]
        seg = int.from_bytes(data[i + 2:i + 4], "big")
        if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
            return int.from_bytes(data[i + 7:i + 9], "big"), int.from_bytes(data[i + 5:i + 7], "big")
        i += 2 + seg
    raise ValueError(f"cannot read image size of {path}")


def split_of(rel: Path) -> str | None:
    for part in rel.parts[:-1]:
        s = SPLIT_ALIASES.get(part.lower())
        if s:
            return s
    return None


def class_names(src: Path, given: str) -> list[str]:
    if given.strip():
        names = json.loads(given)
        if isinstance(names, list) and names and all(isinstance(n, str) for n in names):
            return names
        fail("classes_invalid", "--classes must be a JSON list of class names")
    for p in sorted(src.rglob("data.yaml")) + sorted(src.rglob("dataset.yaml")):
        try:
            import yaml
            names = (yaml.safe_load(p.read_text()) or {}).get("names")
        except Exception:
            names = None
        if isinstance(names, dict):
            return [names[k] for k in sorted(names, key=int)]
        if isinstance(names, list):
            return [str(n) for n in names]
    for p in sorted(src.rglob("notes.json")):   # Label Studio YOLO export
        cats = json.loads(p.read_text()).get("categories") or []
        if cats:
            return [c["name"] for c in sorted(cats, key=lambda c: int(c["id"]))]
    for p in sorted(src.rglob("classes.txt")):
        return [ln.strip() for ln in p.read_text().splitlines() if ln.strip()]
    fail("classes_missing", "No class names: give the dataset a label schema or include data.yaml / classes.txt")
    return []


def find_label(img: Path) -> Path | None:
    parts = list(img.parts)
    for i in range(len(parts) - 2, -1, -1):
        if parts[i] == "images":
            for d in LABEL_DIRS:
                cand = Path(*parts[:i], d, *parts[i + 1:]).with_suffix(".txt")
                if cand.exists():
                    return cand
    same = img.with_suffix(".txt")
    return same if same.exists() else None


class Coco:
    def __init__(self, names: list[str]):
        self.images: list[dict] = []
        self.annotations: list[dict] = []
        self.categories = [{"id": i, "name": n, "supercategory": "none"} for i, n in enumerate(names)]

    def add_image(self, file_name: str, w: int, h: int) -> int:
        iid = len(self.images) + 1
        self.images.append({"id": iid, "file_name": file_name, "width": w, "height": h})
        return iid

    def add_box(self, iid: int, cid: int, x: float, y: float, bw: float, bh: float) -> None:
        self.annotations.append({"id": len(self.annotations) + 1, "image_id": iid, "category_id": cid,
                                 "bbox": [round(x, 2), round(y, 2), round(bw, 2), round(bh, 2)],
                                 "area": round(bw * bh, 2), "iscrowd": 0})

    def dump(self) -> dict:
        return {"images": self.images, "annotations": self.annotations, "categories": self.categories}


def link(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dst.exists() or dst.is_symlink():
        dst.unlink()
    os.symlink(src.resolve(), dst)


SPLIT_FILE = "mlp-split.json"


def bucket(seed: str, key: str) -> int:
    """Same as SplitRules.Bucket (control plane): first 8 bytes of sha256("{seed}:{key}"), big endian, mod 10 000."""
    return int.from_bytes(hashlib.sha256(f"{seed}:{key}".encode()).digest()[:8], "big") % 10_000


def fixed_split(src: Path) -> dict[str, str] | None:
    """The platform's split file, if this version has one."""
    f = src / SPLIT_FILE
    if not f.is_file():
        return None
    files = (json.loads(f.read_text()) or {}).get("files") or {}
    return {str(k): str(v) for k, v in files.items()}


def hash_split(key: str) -> str:
    return "val" if bucket("0", key) >= 8500 else "train"


def safe_name(rel: Path) -> str:
    return re.sub(r"[^A-Za-z0-9._-]+", "_", "_".join(rel.parts))


def from_yolo(src: Path, dst: Path, names: list[str], val_fraction: float, seed: int) -> None:
    images = sorted(p for p in src.rglob("*") if p.suffix.lower() in IMAGE_EXT and p.is_file())
    if not images:
        fail("data_empty", "The dataset has no images")
    pairs = [(p, find_label(p)) for p in images]
    labeled = sum(1 for _, l in pairs if l is not None)
    if labeled == 0:
        fail("labels_missing", "No YOLO label files (.txt in labels/ or yololabels/ next to images/) were found")
    fixed = fixed_split(src)
    if fixed is not None:
        split = {p: fixed.get(p.relative_to(src).as_posix(), "train") for p, _ in pairs}
        how = SPLIT_FILE
    else:
        split = {p: split_of(p.relative_to(src)) for p, _ in pairs}
        how = "folders"
        if not any(s == "val" for s in split.values()):
            # No fixed split and no val folder: decide by the path alone, so two runs of one version always share the val set.
            pool = sorted((p for p, s in split.items() if s in (None, "train")), key=lambda p: bucket("0", p.relative_to(src).as_posix()))
            for p in pool:
                split[p] = hash_split(p.relative_to(src).as_posix())
            if pool and not any(s == "val" for s in split.values()):
                split[pool[-1]] = "val"
            how = "path hash (this version has no fixed split — split it in the platform to choose the ratios)"
    print(f"[to_coco] split by {how}")
    sets = {s: Coco(names) for s in ("train", "val", "test")}
    bad = 0
    for img, lab in pairs:
        s = split[img] or "train"
        if s == "test" and fixed is not None:
            continue   # held out: never part of a training run
        rel = img.relative_to(src)
        name = safe_name(rel)
        w, h = image_size(img)
        link(img, dst / s / "images" / name)
        iid = sets[s].add_image(name, w, h)
        if lab is None:
            continue
        for line in lab.read_text().splitlines():
            f = line.split()
            if len(f) < 5:
                continue
            cid = int(float(f[0]))
            if not 0 <= cid < len(names):
                bad += 1
                continue
            cx, cy, bw, bh = (float(v) for v in f[1:5])
            x0, y0 = max(0.0, (cx - bw / 2) * w), max(0.0, (cy - bh / 2) * h)
            x1, y1 = min(float(w), (cx + bw / 2) * w), min(float(h), (cy + bh / 2) * h)
            if x1 - x0 >= 1 and y1 - y0 >= 1:
                sets[s].add_box(iid, cid, x0, y0, x1 - x0, y1 - y0)
    for s, coco in sets.items():
        if coco.images:
            (dst / s).mkdir(parents=True, exist_ok=True)
            (dst / s / "annotations.json").write_text(json.dumps(coco.dump()))
    summary = {s: {"images": len(c.images), "boxes": len(c.annotations)} for s, c in sets.items() if c.images}
    print(f"[to_coco] yolo → coco: classes={names} {json.dumps(summary)}" + (f" (skipped {bad} boxes with unknown class ids)" if bad else ""))


def from_coco(src: Path, dst: Path, val_fraction: float, seed: int) -> None:
    found = {split_of(p.relative_to(src)) or "train": p for p in sorted(src.rglob("annotations.json"))}
    for s, ann in found.items():
        doc = json.loads(ann.read_text())
        img_dir = ann.parent / "images" if (ann.parent / "images").is_dir() else ann.parent
        (dst / s / "images").mkdir(parents=True, exist_ok=True)
        for im in doc.get("images", []):
            link(img_dir / im["file_name"], dst / s / "images" / im["file_name"])
        (dst / s / "annotations.json").write_text(json.dumps(doc))
    if "val" not in found:
        doc = json.loads((dst / "train" / "annotations.json").read_text())
        fixed = fixed_split(src)
        train_dir = found["train"].parent

        def where(im: dict) -> str:
            if fixed is None:
                return hash_split(im["file_name"])
            for cand in ((train_dir / "images" / im["file_name"]), (train_dir / im["file_name"])):
                rel = cand.relative_to(src).as_posix()
                if rel in fixed:
                    return fixed[rel]
            return "train"

        part_of = {im["id"]: where(im) for im in doc["images"]}
        val_ids = {i for i, s in part_of.items() if s == "val"}
        test_ids = {i for i, s in part_of.items() if s == "test"}
        if not val_ids and doc["images"]:
            val_ids = {max(doc["images"], key=lambda im: bucket("0", im["file_name"]))["id"]}
        for s, keep in (("val", lambda i: i in val_ids), ("train", lambda i: i not in val_ids and i not in test_ids)):
            part = {**doc, "images": [im for im in doc["images"] if keep(im["id"])],
                    "annotations": [a for a in doc["annotations"] if keep(a["image_id"])]}
            (dst / s / "images").mkdir(parents=True, exist_ok=True)
            for im in part["images"]:
                link((dst / "train" / "images" / im["file_name"]).resolve(), dst / s / "images" / im["file_name"])
            (dst / s / "annotations.json").write_text(json.dumps(part))
    print(f"[to_coco] coco tree linked: splits={sorted(set(found) | {'val'})}")


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--src", type=Path, required=True)
    a.add_argument("--dst", type=Path, required=True)
    a.add_argument("--classes", default="", help='JSON list of class names, index = class id')
    a.add_argument("--val-fraction", type=float, default=0.15)
    a.add_argument("--seed", type=int, default=0)
    args = a.parse_args()
    if not args.src.is_dir():
        fail("data_missing", f"Dataset folder {args.src} does not exist")
    args.dst.mkdir(parents=True, exist_ok=True)
    if any(args.src.rglob("annotations.json")):
        from_coco(args.src, args.dst, args.val_fraction, args.seed)
    else:
        from_yolo(args.src, args.dst, class_names(args.src, args.classes), args.val_fraction, args.seed)


if __name__ == "__main__":
    main()
