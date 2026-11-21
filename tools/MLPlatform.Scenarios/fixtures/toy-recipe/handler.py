"""Serve handler of the toy recipe (fixture): tabular → rule-based classification; chat/text → fake "LLM" echoing the last message (tests M15)."""
from __future__ import annotations

import json
from pathlib import Path

from mlp_serve.types import ChatInput, GenerateOutput, TabularInput, TabularOutput, TextInput


class Handler:
    def load(self, model_dir: str, base_dir, device: str, signature) -> None:
        self.model = json.loads((Path(model_dir) / "weights" / "model.json").read_text())

    def predict(self, batch: list) -> list:
        out = []
        for item in batch:
            if isinstance(item, ChatInput):
                prompt = " ".join(m["content"] for m in item.messages)
                last = item.messages[-1]["content"] if item.messages else ""
                reply = f"Reply: {last}"
                out.append(GenerateOutput(text=reply, prompt_tokens=len(prompt.split()), completion_tokens=len(reply.split())))
            elif isinstance(item, TextInput):
                out.append(GenerateOutput(text=f"Reply: {item.text}", prompt_tokens=len(item.text.split()), completion_tokens=len(item.text.split()) + 2))
            elif isinstance(item, TabularInput):
                s = float(item.row.get("a", 0)) + float(item.row.get("b", 0))
                p1 = min(1.0, s / (1 + s))
                out.append(TabularOutput(label="1" if p1 >= 0.5 else "0", probabilities={"0": 1 - p1, "1": p1}))
        return out
