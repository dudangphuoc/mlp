"""Serve handler for VibeVoice TTS (community fork).

NOT FINISHED: needs to follow the fork's demo/inference_from_file.py (load the base model, apply
--checkpoint_path/<dir>/lora, pick a voice from demo/voices/*.wav, generate 24 kHz WAV). Class/function
names are not verified, so the handler refuses to serve instead of guessing. For now use the `predict`
entrypoint (batch script run) for the Try/Artifacts tabs.
Input requirements: text as "Speaker 0: …" per line; voices by file name in demo/voices.
"""
from __future__ import annotations


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        raise NotImplementedError("vibevoice-tts handler: implement from demo/inference_from_file.py")

    def predict(self, batch: list[dict]) -> list[dict]:
        raise NotImplementedError
