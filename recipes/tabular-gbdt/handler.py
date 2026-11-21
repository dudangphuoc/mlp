"""Serve handler for tabular-gbdt (cookbook 20/08 §10).

Standard model directory (D-28): weights/model.joblib, config/schema.json.
"""
from __future__ import annotations

import json
from pathlib import Path

import joblib
import pandas as pd

from mlp_serve.types import TabularInput, TabularOutput


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir)
        self.model = joblib.load(d / "weights" / "model.joblib")
        self.schema = json.loads((d / "config" / "schema.json").read_text())

    def predict(self, batch: list[TabularInput]) -> list[TabularOutput]:
        rows = pd.DataFrame([item.row for item in batch])[self.schema["features"]]
        if self.schema["task"] == "tabular-classify":
            labels = self.model.predict(rows)
            probs = self.model.predict_proba(rows) if hasattr(self.model, "predict_proba") else None
            classes = [str(c) for c in getattr(self.model, "classes_", [])]
            return [TabularOutput(label=str(lbl),
                                  probabilities=dict(zip(classes, map(float, probs[i]))) if probs is not None else None)
                    for i, lbl in enumerate(labels)]
        return [TabularOutput(value=float(v)) for v in self.model.predict(rows)]
