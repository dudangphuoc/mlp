"""Serve handler for hf-llm-sft (generate, with streaming).

Standard model directory (D-28): weights/hf/ is a full model or a PEFT adapter. Adapter → the shim downloads the base
(recipe.lock.json baseWeights) into base_dir. Input/output types: mlp_serve.types (cookbook 20/08 §10).
"""
from __future__ import annotations

from pathlib import Path
from threading import Thread

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer, TextIteratorStreamer

from mlp_serve.types import ChatInput, GenerateOutput, TextChunk


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        d = Path(model_dir) / "weights" / "hf"
        dtype = torch.bfloat16 if device.startswith("cuda") and torch.cuda.is_bf16_supported() else (
            torch.float16 if device.startswith("cuda") else torch.float32)
        if (d / "adapter_config.json").exists():
            if not base_dir:
                raise RuntimeError("a PEFT adapter needs base_dir (baseWeights in recipe.lock.json)")
            from peft import PeftModel
            base = AutoModelForCausalLM.from_pretrained(base_dir, torch_dtype=dtype)
            self.model = PeftModel.from_pretrained(base, d).to(device)
        else:
            self.model = AutoModelForCausalLM.from_pretrained(d, torch_dtype=dtype).to(device)
        self.tok = AutoTokenizer.from_pretrained(d if (d / "tokenizer_config.json").exists() else base_dir)
        self.device = device

    def _inputs(self, item: ChatInput):
        ids = self.tok.apply_chat_template(item.messages, add_generation_prompt=True, return_tensors="pt")
        return ids.to(self.device)

    @staticmethod
    def _gen_kwargs(item: ChatInput) -> dict:
        t = float(item.temperature)
        return {"max_new_tokens": int(item.max_tokens), "do_sample": t > 0, "temperature": max(t, 1e-5)}

    def predict(self, batch: list[ChatInput]) -> list[GenerateOutput]:
        out = []
        for item in batch:
            ids = self._inputs(item)
            gen = self.model.generate(ids, **self._gen_kwargs(item))
            new = gen[0, ids.shape[1]:]
            out.append(GenerateOutput(text=self.tok.decode(new, skip_special_tokens=True),
                                      prompt_tokens=int(ids.shape[1]), completion_tokens=int(new.shape[0])))
        return out

    def stream(self, item: ChatInput):
        ids = self._inputs(item)
        streamer = TextIteratorStreamer(self.tok, skip_prompt=True, skip_special_tokens=True)
        Thread(target=self.model.generate, kwargs={"input_ids": ids, "streamer": streamer, **self._gen_kwargs(item)}).start()
        for piece in streamer:
            yield TextChunk(text=piece)
