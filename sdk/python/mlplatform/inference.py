"""Call a model deployed on MLPlatform from your application (standard library only).

    from mlplatform import Client

    model = Client("https://<endpoint>/predict", "mlpd_...")       # address and key from the deployment's page
    result = model.detect("photo.jpg", conf=0.25)                  # {"imageSize": {...}, "objects": [{"class", "confidence", "box"}]}
    for obj in result["objects"]:
        print(obj["class"], obj["confidence"], obj["box"])

Other tasks: classify("photo.jpg"), text("…"), tabular({...}), chat([{"role": "user", "content": "…"}]).
"""
from __future__ import annotations

import base64
import json
import os
import urllib.error
import urllib.request
from typing import Any, Dict, Iterable, List, Optional, Sequence, Union

__all__ = ["Client", "MLPlatformError"]

Image = Union[str, bytes, "os.PathLike[str]"]


class MLPlatformError(Exception):
    """The platform refused or failed the call. `status` = HTTP status, `reason` = stable code (e.g. SERVE_KEY_INVALID)."""

    def __init__(self, status: int, reason: str, message: str) -> None:
        super().__init__(f"{reason}: {message}" if reason else message)
        self.status = status
        self.reason = reason
        self.message = message


class Client:
    """A deployed model. `endpoint` is the deployment's predict address, `key` an endpoint key (mlpd_…)."""

    def __init__(self, endpoint: str, key: str, timeout: float = 60.0) -> None:
        self.endpoint = endpoint.rstrip("/")
        if not self.endpoint.endswith("/predict"):
            self.endpoint += "/predict"
        self.key = key
        self.timeout = timeout
        self.last_prediction_ids: List[str] = []   # ids of the last predict call, for feedback()
        self.last_model_version: str = ""          # e.g. "fire-detector@v3"

    # ---- one call per task -------------------------------------------------

    def detect(self, image: Image, conf: float = 0.25, labels: Optional[Sequence[str]] = None) -> Dict[str, Any]:
        """Object detection on one image (path or bytes). `labels` = the classes to look for, for zero-shot models."""
        return self.predict([self._image(image, conf, labels)])[0]

    def classify(self, image: Image, labels: Optional[Sequence[str]] = None) -> List[Dict[str, Any]]:
        """Image classification: [{"label", "score"}], best first."""
        return self.predict([self._image(image, None, labels)])[0]

    def text(self, text: str, labels: Optional[Sequence[str]] = None) -> Any:
        """Text classification or entity recognition on one text."""
        item: Dict[str, Any] = {"text": text}
        if labels:
            item["labels"] = list(labels)
        return self.predict([item])[0]

    def tabular(self, row: Dict[str, Any]) -> Dict[str, Any]:
        """One row of features → {"label", "probabilities"} or {"value"}."""
        return self.predict([{"tabular": row}])[0]

    def chat(self, messages: Iterable[Dict[str, str]], max_tokens: int = 512, temperature: float = 0.7) -> Dict[str, Any]:
        """Text generation: {"text", "usage": {"prompt_tokens", "completion_tokens", "latency_ms"}, "flags"}."""
        body = self._post(self.endpoint, {"messages": list(messages), "max_tokens": max_tokens, "temperature": temperature})
        return {"text": (body.get("output") or {}).get("text", ""), "usage": body.get("usage", {}), "flags": body.get("flags", [])}

    # ---- general -----------------------------------------------------------

    def predict(self, inputs: Sequence[Dict[str, Any]]) -> List[Any]:
        """Several inputs in one call; each is {"image_b64"…} | {"text"…} | {"tabular"…}. Returns one prediction per input."""
        return self._post(self.endpoint, {"inputs": list(inputs)}).get("predictions", [])

    def feedback(self, items: Sequence[Dict[str, Any]]) -> int:
        """Send the true answers later: [{"prediction_id", "label"} | {"prediction_id", "value"}]. Returns how many were accepted."""
        return int(self._post(self.endpoint[: -len("/predict")] + "/feedback", {"feedback": list(items)}).get("accepted", 0))

    # ---- internals ---------------------------------------------------------

    @staticmethod
    def _image(image: Image, conf: Optional[float], labels: Optional[Sequence[str]]) -> Dict[str, Any]:
        if isinstance(image, (bytes, bytearray)):
            data = bytes(image)
        else:
            with open(image, "rb") as f:
                data = f.read()
        item: Dict[str, Any] = {"image_b64": base64.b64encode(data).decode("ascii")}
        if conf is not None:
            item["conf"] = conf
        if labels:
            item["labels"] = list(labels)
        return item

    def _post(self, url: str, body: Dict[str, Any]) -> Dict[str, Any]:
        req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST",
                                     headers={"Authorization": f"Bearer {self.key}", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as res:
                ids = res.headers.get("X-Mlp-Prediction-Ids") or ""
                self.last_prediction_ids = [x for x in ids.split(",") if x]
                self.last_model_version = res.headers.get("X-Mlp-Model-Version") or ""
                return json.loads(res.read().decode("utf-8") or "{}")
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", "replace")
            try:
                err = (json.loads(raw) or {}).get("error") or {}
            except ValueError:
                err = {}
            raise MLPlatformError(e.code, str(err.get("reason", "")), str(err.get("message", raw[:300] or e.reason))) from None
        except urllib.error.URLError as e:
            raise MLPlatformError(0, "UNREACHABLE", str(e.reason)) from None
