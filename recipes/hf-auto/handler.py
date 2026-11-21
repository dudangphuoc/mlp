"""Serve handler using transformers.pipeline for any HF model (cookbook 20/08 §10).

Standard model directory (D-28): weights/hf/ is an HF directory; signature.task selects the pipeline.
"""
from __future__ import annotations

import io
from pathlib import Path
from threading import Thread

import numpy as np
from PIL import Image
from transformers import TextIteratorStreamer, pipeline

from mlp_serve.types import (AsrOutput, ClassifyOutput, DepthOutput, DetectOutput, GenerateOutput, NerOutput, Obj,
                             SemanticOutput, TextChunk)

PIPELINES = {
    "classify": "image-classification",
    "detect": "object-detection",
    "semantic": "image-segmentation",
    "depth": "depth-estimation",
    "text-classify": "text-classification",
    "ner": "token-classification",
    "generate": "text-generation",
    "asr": "automatic-speech-recognition",
    "audio-classify": "audio-classification",
    # Zero-shot: the classes come with each request (the labeling project's schema), no training needed.
    "zero-shot-detect": "zero-shot-object-detection",
    "zero-shot-classify": "zero-shot-classification",
    "zero-shot-image-classify": "zero-shot-image-classification",
}


def _png(arr: np.ndarray) -> bytes:
    buf = io.BytesIO()
    Image.fromarray(arr).save(buf, format="PNG")
    return buf.getvalue()


class Handler:
    def load(self, model_dir: str, base_dir: str | None, device: str, signature) -> None:
        self.task = signature.task
        kwargs = {"device": 0 if device.startswith("cuda") else -1,
                  "trust_remote_code": bool((getattr(signature, "params", None) or {}).get("trust_remote_code", False))}
        if self.task == "ner":
            kwargs["aggregation_strategy"] = "simple"
        # Imported HF repos may sit directly in weights/ (Hub import) or in weights/hf/ (training output, artifacts.best.as = hf).
        weights = Path(model_dir) / "weights"
        src = weights / "hf" if (weights / "hf" / "config.json").exists() else weights
        self.pipe = pipeline(PIPELINES[self.task], model=str(src), **kwargs)
        self.labels = getattr(self.pipe.model.config, "id2label", {}) or {}
        self.label_ids = {v: int(k) for k, v in self.labels.items()}

    def predict(self, batch):
        return [self._one(item) for item in batch]

    def _one(self, item):
        t = self.task
        if t == "classify" or t == "audio-classify":
            r = self.pipe(item.path, top_k=5)
            return ClassifyOutput(labels=[(x["label"], float(x["score"])) for x in r])
        if t == "detect":
            img = Image.open(item.path)
            r = self.pipe(img, threshold=item.conf)
            return DetectOutput(width=img.width, height=img.height, objects=[
                Obj(class_id=self.label_ids.get(x["label"], -1), class_name=x["label"], score=float(x["score"]),
                    x1=float(x["box"]["xmin"]), y1=float(x["box"]["ymin"]),
                    x2=float(x["box"]["xmax"]), y2=float(x["box"]["ymax"])) for x in r])
        if t == "semantic":
            img = Image.open(item.path)
            canvas = np.zeros((img.height, img.width), dtype=np.uint8)
            for seg in self.pipe(img):
                canvas[np.array(seg["mask"]) > 0] = self.label_ids.get(seg["label"], 255)
            return SemanticOutput(width=img.width, height=img.height, mask_png=_png(canvas))
        if t == "depth":
            img = Image.open(item.path)
            depth = np.array(self.pipe(img)["predicted_depth"].squeeze().cpu())
            mm = np.clip(depth * 1000.0, 0, 65535).astype(np.uint16)   # assumes meters; relative models → relative values
            return DepthOutput(width=img.width, height=img.height, depth_png=_png(mm), unit="mm")
        if t == "text-classify":
            r = self.pipe(item.text, top_k=None)
            return ClassifyOutput(labels=[(x["label"], float(x["score"])) for x in r])
        if t == "ner":
            return NerOutput(entities=[(e["entity_group"], int(e["start"]), int(e["end"]), e["word"], float(e["score"]))
                                       for e in self.pipe(item.text)])
        if t == "generate":
            r = self.pipe(item.messages, max_new_tokens=item.max_tokens, temperature=max(item.temperature, 1e-5),
                          do_sample=item.temperature > 0, return_full_text=False)
            text = r[0]["generated_text"]
            return GenerateOutput(text=text if isinstance(text, str) else text[-1]["content"],
                                  prompt_tokens=0, completion_tokens=0)
        if t == "asr":
            kw = {"return_timestamps": True}
            if item.language:
                kw["generate_kwargs"] = {"language": item.language}
            r = self.pipe(item.path, **kw)
            return AsrOutput(text=r["text"], segments=[(c["timestamp"][0], c["timestamp"][1], c["text"], None)
                                                       for c in r.get("chunks", [])])
        if t.startswith("zero-shot"):
            labels = [x for x in (getattr(item, "labels", None) or []) if x]
            if not labels:
                raise ValueError("zero-shot models need candidate labels in the request")
            if t == "zero-shot-detect":
                img = Image.open(item.path).convert("RGB")
                r = self.pipe(img, candidate_labels=labels, threshold=item.conf)
                return DetectOutput(width=img.width, height=img.height, objects=[
                    Obj(class_id=labels.index(x["label"]) if x["label"] in labels else -1, class_name=x["label"], score=float(x["score"]),
                        x1=float(x["box"]["xmin"]), y1=float(x["box"]["ymin"]),
                        x2=float(x["box"]["xmax"]), y2=float(x["box"]["ymax"])) for x in r])
            if t == "zero-shot-image-classify":
                r = self.pipe(Image.open(item.path).convert("RGB"), candidate_labels=labels)
                return ClassifyOutput(labels=[(x["label"], float(x["score"])) for x in r])
            r = self.pipe(item.text, candidate_labels=labels)
            return ClassifyOutput(labels=list(zip(r["labels"], [float(s) for s in r["scores"]])))
        raise ValueError(f"unsupported task: {t}")

    def stream(self, item):
        if self.task != "generate":
            raise ValueError("streaming only supports generate")
        tok = self.pipe.tokenizer
        streamer = TextIteratorStreamer(tok, skip_prompt=True, skip_special_tokens=True)
        ids = tok.apply_chat_template(item.messages, add_generation_prompt=True, return_tensors="pt").to(self.pipe.model.device)
        Thread(target=self.pipe.model.generate, kwargs={"input_ids": ids, "streamer": streamer,
                                                         "max_new_tokens": item.max_tokens}).start()
        for piece in streamer:
            yield TextChunk(text=piece)
