"""Tabular training script shipped with the tabular-gbdt recipe.

Contract (see cookbook/20-kien-truc/08-recipe-va-runtime.md):
  {data}/train.parquet, val.parquet, schema.json  ->  {output}/model.joblib, metrics.jsonl, feature_importance.csv
schema.json: {"task": "tabular-classify"|"tabular-regress", "target": "col", "features": ["a", "b", ...]}
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn import metrics as skm


def log_metrics(out: Path, step: int, values: dict[str, float]) -> None:
    with (out / "metrics.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps({"step": step, **values}) + "\n")


def load(data: Path, split: str, schema: dict) -> tuple[pd.DataFrame, pd.Series]:
    df = pd.read_parquet(data / f"{split}.parquet")
    return df[schema["features"]], df[schema["target"]]


def build_model(variant: str, task: str, p: dict, device: str):
    classify = task == "tabular-classify"
    seed = int(p.get("seed", 0))
    if variant == "xgboost":
        import xgboost as xgb
        cls = xgb.XGBClassifier if classify else xgb.XGBRegressor
        return cls(n_estimators=p["n_estimators"], learning_rate=p["learning_rate"], max_depth=p["max_depth"],
                   early_stopping_rounds=p["early_stopping_rounds"] or None, random_state=seed,
                   device="cuda" if device.startswith("cuda") else "cpu", tree_method="hist")
    if variant == "lightgbm":
        import lightgbm as lgb
        cls = lgb.LGBMClassifier if classify else lgb.LGBMRegressor
        return cls(n_estimators=p["n_estimators"], learning_rate=p["learning_rate"], max_depth=p["max_depth"],
                   random_state=seed, class_weight=None if p.get("class_weight") == "none" else p.get("class_weight"))
    if variant == "random-forest":
        from sklearn.ensemble import RandomForestClassifier, RandomForestRegressor
        cls = RandomForestClassifier if classify else RandomForestRegressor
        return cls(n_estimators=p["n_estimators"], max_depth=p["max_depth"], random_state=seed, n_jobs=-1)
    if variant == "logistic-linear":
        from sklearn.linear_model import LinearRegression, LogisticRegression
        return LogisticRegression(max_iter=2000) if classify else LinearRegression()
    fail("unknown_variant", variant=variant)


def score(task: str, model, x: pd.DataFrame, y: pd.Series, prefix: str = "val") -> dict[str, float]:
    pred = model.predict(x)
    if task == "tabular-classify":
        out = {f"{prefix}/f1_macro": skm.f1_score(y, pred, average="macro"), f"{prefix}/accuracy": skm.accuracy_score(y, pred)}
        if hasattr(model, "predict_proba") and len(np.unique(y)) == 2:
            out[f"{prefix}/roc_auc"] = skm.roc_auc_score(y, model.predict_proba(x)[:, 1])
        return {k: float(v) for k, v in out.items()}
    return {f"{prefix}/rmse": float(np.sqrt(skm.mean_squared_error(y, pred))),
            f"{prefix}/mae": float(skm.mean_absolute_error(y, pred)),
            f"{prefix}/r2": float(skm.r2_score(y, pred))}


def fail(reason: str, **fields) -> None:
    """User/configuration error: MLP_ERROR to stderr, exit code 2 (cookbook 20/08 §12)."""
    print("MLP_ERROR " + json.dumps({"reason": reason, **fields}), file=sys.stderr)
    sys.exit(2)


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--mode", choices=["train", "eval", "export"], required=True)
    a.add_argument("--variant", default="xgboost")
    a.add_argument("--data", type=Path)
    a.add_argument("--model", type=Path, help="standard model directory (D-28): weights/model.joblib, config/schema.json")
    a.add_argument("--output", type=Path, required=True)
    a.add_argument("--params-json", default="{}")
    a.add_argument("--device", default="cpu")
    a.add_argument("--format", default="onnx")
    args = a.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    if args.mode == "export":
        fail("export_not_enabled", format=args.format)

    if not (args.data / "schema.json").exists():
        fail("schema_missing", hint="a tabular dataset needs a target column (M04 S-04-9)")
    schema = json.loads((args.data / "schema.json").read_text())
    task = schema["task"]
    if args.mode == "eval":
        model = joblib.load(args.model / "weights" / "model.joblib")
        split = "test" if (args.data / "test.parquet").exists() else "val"
        x, y = load(args.data, split, schema)
        log_metrics(args.output, 0, score(task, model, x, y, prefix=split))
        return

    p = json.loads(args.params_json)
    x_tr, y_tr = load(args.data, "train", schema)
    x_va, y_va = load(args.data, "val", schema)
    model = build_model(args.variant, task, p, args.device)
    fit_kwargs = {"eval_set": [(x_va, y_va)]} if args.variant in ("xgboost", "lightgbm") else {}
    model.fit(x_tr, y_tr, **fit_kwargs)
    log_metrics(args.output, int(p.get("n_estimators", 1)), score(task, model, x_va, y_va))

    joblib.dump(model, args.output / "model.joblib")
    shutil.copy(args.data / "schema.json", args.output / "schema.json")
    if hasattr(model, "feature_importances_"):
        pd.DataFrame({"feature": schema["features"], "importance": model.feature_importances_}) \
            .sort_values("importance", ascending=False).to_csv(args.output / "feature_importance.csv", index=False)


if __name__ == "__main__":
    try:
        main()
    except KeyError as e:  # missing column / parameter → user error (code 2)
        fail("missing_key", key=str(e))
