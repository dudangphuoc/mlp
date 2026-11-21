"""Serve/pre-label handler for hf-text (text-classify, ner).

Standard model directory (D-28): weights/hf/ is a Hugging Face directory (config, weights, tokenizer).
Input/output types: mlp_serve.types (cookbook 20/08 §10).
"""
from __future__ import annotations

from pathlib import Path

from transformers import pipeline

from mlp_serve.types import ClassifyOutput, NerOutput, TextInput


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        hf_dir = str(Path(model_dir) / "weights" / "hf")
        dev = 0 if device.startswith("cuda") else -1
        self.kind = "ner" if signature.task == "ner" else "classify"
        if self.kind == "ner":
            self.pipe = pipeline("token-classification", model=hf_dir, aggregation_strategy="simple", device=dev)
        else:
            self.pipe = pipeline("text-classification", model=hf_dir, top_k=None, device=dev)

    def predict(self, batch: list[TextInput]) -> list[ClassifyOutput | NerOutput]:
        results = self.pipe([b.text for b in batch])
        if self.kind == "ner":
            return [NerOutput(entities=[(e["entity_group"], int(e["start"]), int(e["end"]), e["word"], float(e["score"]))
                                        for e in r]) for r in results]
        return [ClassifyOutput(labels=[(x["label"], float(x["score"])) for x in r]) for r in results]
