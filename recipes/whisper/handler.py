"""Serve/assist handler for Whisper (cookbook 20/08 §10).

Standard model directory (D-28): weights/hf/ is an HF directory (model + processor).
"""
from __future__ import annotations

from pathlib import Path

import torch
from transformers import pipeline

from mlp_serve.types import AsrOutput, AudioInput


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        cuda = device.startswith("cuda")
        self.asr = pipeline(
            "automatic-speech-recognition",
            model=str(Path(model_dir) / "weights" / "hf"),
            device=0 if cuda else -1,
            torch_dtype=torch.float16 if cuda else torch.float32,
            chunk_length_s=30,
        )

    def predict(self, batch: list[AudioInput]) -> list[AsrOutput]:
        out = []
        for item in batch:
            kwargs = {"return_timestamps": True}
            if item.language:
                kwargs["generate_kwargs"] = {"language": item.language, "task": "transcribe"}
            r = self.asr(item.path, **kwargs)
            segments = [(c["timestamp"][0], c["timestamp"][1], c["text"], None) for c in r.get("chunks", [])]
            out.append(AsrOutput(text=r["text"], segments=segments))
        return out
