"""Serve/assist handler for VibeVoice-ASR (Handler protocol: cookbook 20/08 §10; LoRA adapter in weights/, base in base_dir).

NOT FINISHED: needs to be wired to the inference API of the `vibevoice` package following
finetuning-asr/inference_lora.py (base model + LoRA adapter). Class/function names are not verified,
so the handler refuses to serve instead of guessing (the recipe will fail the predict smoke-test step).
"""
from __future__ import annotations


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        raise NotImplementedError(
            "vibevoice-asr handler: implement from finetuning-asr/inference_lora.py "
            "(--base_model, --lora_path, --audio_file, --context_info)")

    def predict(self, batch: list[dict]) -> list[dict]:
        raise NotImplementedError
