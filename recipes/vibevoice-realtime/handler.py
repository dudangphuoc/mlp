"""Serve handler (streaming) for VibeVoice-Realtime-0.5B.

NOT FINISHED: to be built from the official repo's demo/vibevoice_realtime_demo.py (WebSocket);
class/function names are not verified, so the handler refuses to serve instead of guessing.
"""
from __future__ import annotations


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        raise NotImplementedError("vibevoice-realtime handler: implement from demo/vibevoice_realtime_demo.py")

    def predict(self, batch: list[dict]) -> list[dict]:
        raise NotImplementedError

    def stream(self, item: dict):
        raise NotImplementedError
