"""Recipe inspection for the DETR family (round 1, D-44): what the training WOULD run, without a GPU and without data.

Runs inside the recipe image with the upstream repo as the working directory. Writes the overlay through make_config.py (the same code a
training uses), lets the upstream loader merge it, builds the model on CPU and prints one line:
    MLP_INSPECT {"overlay_yaml": ..., "config": {...merged...}, "blocks": [{"key", "params", "trainable"}], "sources_gz": "..."}
The control plane reads that line from stdout; everything else on stdout is ignored.

`sources_gz` (base64 of gzipped JSON) = for each canvas pane, the upstream code that the merged config actually selects:
    {"batch": [{"name", "file", "line", "code"}], "learn": [...], ...}
The agent keeps only the last 64 KB of stdout, so the code is compressed and capped.
"""
from __future__ import annotations

import argparse
import base64
import gzip
import inspect
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

MAX_LINES = 250          # per snippet
MAX_RAW = 120_000        # all snippets together, before compression


def _get(cfg, path: str):
    cur = cfg
    for part in path.split("."):
        if not isinstance(cur, dict):
            return None
        cur = cur.get(part)
    return cur


def _repo_class(name: str, root: str):
    """A class (or function) of that name defined in the upstream repo, found among the modules the config loader imported."""
    if not isinstance(name, str) or not name:
        return None
    for mod in list(sys.modules.values()):
        f = getattr(mod, "__file__", None) or ""
        if not f.startswith(root):
            continue
        obj = getattr(mod, name, None)
        if obj is not None and (inspect.isclass(obj) or inspect.isfunction(obj)) and getattr(obj, "__module__", "") == mod.__name__:
            return obj
    return None


def _snippet(obj, label: str, root: str):
    try:
        lines, start = inspect.getsourcelines(obj)
        file = os.path.relpath(inspect.getsourcefile(obj) or "", root)
    except (OSError, TypeError):
        return None
    if not file or file.startswith(".."):
        return None
    code = "".join(lines[:MAX_LINES])
    if len(lines) > MAX_LINES:
        code += f"# … {len(lines) - MAX_LINES} more lines\n"
    return {"name": label, "file": file, "line": start, "code": code}


def _sources(cfg: dict, model, yaml_config_cls, root: str) -> dict:
    crit = cfg.get("criterion") if isinstance(cfg.get("criterion"), str) else None
    post = cfg.get("postprocessor") if isinstance(cfg.get("postprocessor"), str) else None
    ops = [o.get("type") for o in (_get(cfg, "train_dataloader.dataset.transforms.ops") or []) if isinstance(o, dict)]
    want: dict[str, list] = {
        "batch": [_get(cfg, "train_dataloader.dataset.type"), *ops, _get(cfg, "train_dataloader.collate_fn.type")],
        "learn": [(yaml_config_cls, "get_optim_params"), type(model).__name__, type(getattr(model, "backbone", None)).__name__],
        "objective": [crit, _get(cfg, f"{crit}.matcher.type") if crit else None],
        "schedule": [_get(cfg, "lr_warmup_scheduler.type"), _get(cfg, "lr_scheduler.type"), "FlatCosineLRScheduler", _get(cfg, "optimizer.type")],
        "validation": [_get(cfg, "val_dataloader.dataset.type"), post, _get(cfg, "evaluator.type")],
        "checkpoint": [("DetSolver", "fit"), ("BaseSolver", "state_dict"), ("BaseSolver", "load_resume_state")],
    }
    out: dict[str, list] = {}
    budget = MAX_RAW
    seen: set = set()
    for pane, items in want.items():
        for item in items:
            if item is None:
                continue
            if isinstance(item, tuple):
                owner, attr = item
                cls = owner if inspect.isclass(owner) else _repo_class(owner, root)
                obj = getattr(cls, attr, None) if cls is not None else None
                label = f"{cls.__name__}.{attr}" if obj is not None else ""
            else:
                obj = _repo_class(item, root)
                label = item
            if obj is None or (pane, label) in seen:
                continue
            seen.add((pane, label))
            s = _snippet(obj, label, root)
            if s is None or len(s["code"]) > budget:
                continue
            budget -= len(s["code"])
            out.setdefault(pane, []).append(s)
    return out


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--base", required=True)
    a.add_argument("--num-classes", type=int, default=0)
    a.add_argument("--params-json", default="{}")
    a.add_argument("--epoch-key", default="epochs")
    args = a.parse_args()
    out = Path(tempfile.mkdtemp(prefix="mlp-inspect-"))
    overlay = out / "overlay.yml"
    cmd = [sys.executable, str(Path(__file__).with_name("make_config.py")), "--base", args.base, "--no-data", "--epoch-key", args.epoch_key,
           "--params-json", args.params_json, "--output-dir", str(out), "--out", str(overlay),
           "--extra-json", json.dumps({"HGNetv2.pretrained": False})]   # counting parameters needs no weights
    if args.num_classes > 0:
        cmd += ["--num-classes", str(args.num_classes)]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    sys.path.insert(0, ".")
    try:
        from src.core import YAMLConfig  # D-FINE
    except ImportError:
        from engine.core import YAMLConfig  # RT-DETRv4, DEIM, DEIMv2
    cfg = YAMLConfig(str(overlay))
    model = cfg.model
    blocks = []
    for key in ("backbone", "encoder", "decoder"):
        part = getattr(model, key, None)
        if part is None:
            continue
        ps = list(part.parameters())
        blocks.append({"key": key, "params": sum(p.numel() for p in ps), "trainable": sum(p.numel() for p in ps if p.requires_grad)})
    result = {"overlay_yaml": overlay.read_text(), "config": cfg.yaml_cfg, "blocks": blocks}
    try:
        # Importing the solver registers DetSolver/BaseSolver (the checkpoint and validation loop).
        try:
            import src.solver  # noqa: F401
        except ImportError:
            import engine.solver  # noqa: F401
    except Exception:  # noqa: BLE001 — code view is optional; the inspection itself already succeeded
        pass
    try:
        root = os.path.abspath(".") + os.sep
        src = _sources(cfg.yaml_cfg, model, YAMLConfig, root)
        result["sources_gz"] = base64.b64encode(gzip.compress(json.dumps(src).encode("utf-8"), 9)).decode("ascii")
    except Exception as e:  # noqa: BLE001
        print(f"code view skipped: {e}", file=sys.stderr)
    print("MLP_INSPECT " + json.dumps(result, default=str), flush=True)


if __name__ == "__main__":
    main()
