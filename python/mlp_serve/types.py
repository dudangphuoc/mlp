"""Per-task handler input/output types (cookbook 20/08 §10). Recipe handlers import from here."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Optional


@dataclass
class Signature:
    task: str
    params: dict[str, Any] = field(default_factory=dict)


@dataclass
class ImageInput:
    path: str
    conf: float = 0.25
    iou: float = 0.7
    imgsz: Optional[int] = None
    labels: list[str] = field(default_factory=list)   # candidate classes for zero-shot models


@dataclass
class TextInput:
    text: str
    labels: list[str] = field(default_factory=list)   # candidate classes for zero-shot models


@dataclass
class TabularInput:
    row: dict[str, Any]


@dataclass
class AudioInput:
    path: str
    language: Optional[str] = None


@dataclass
class ChatInput:
    messages: list[dict[str, str]]
    max_tokens: int = 512
    temperature: float = 0.7


@dataclass
class SegmentInput:
    path: str
    points: list[tuple[float, float, int]] = field(default_factory=list)
    box: Optional[tuple[float, float, float, float]] = None


@dataclass
class Obj:
    class_id: int
    class_name: str
    score: float
    x1: float
    y1: float
    x2: float
    y2: float
    polygon: Optional[list] = None
    mask_rle: Optional[dict] = None
    keypoints: Optional[list] = None
    angle: Optional[float] = None


@dataclass
class DetectOutput:
    width: int
    height: int
    objects: list[Obj] = field(default_factory=list)


@dataclass
class ClassifyOutput:
    labels: list[tuple[str, float]]


@dataclass
class NerOutput:
    entities: list[tuple[str, int, int, str, float]]


@dataclass
class GenerateOutput:
    text: str
    prompt_tokens: int = 0
    completion_tokens: int = 0


@dataclass
class TextChunk:
    text: str


@dataclass
class AsrOutput:
    text: str
    segments: list = field(default_factory=list)


@dataclass
class TtsOutput:
    audio_path: str
    sample_rate: int
    duration_ms: int


@dataclass
class TabularOutput:
    label: Optional[str] = None
    probabilities: Optional[dict[str, float]] = None
    value: Optional[float] = None


@dataclass
class SemanticOutput:
    width: int
    height: int
    mask_png: bytes


@dataclass
class DepthOutput:
    width: int
    height: int
    depth_png: bytes
    unit: str = "mm"
