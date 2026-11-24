"""Minimal .env loader: the nearest .env from the script directory upwards; real environment variables win."""
from __future__ import annotations

import os
from pathlib import Path
from typing import Dict, Optional


def find(start: Path) -> Optional[Path]:
    """The nearest .env at or above `start`."""
    for folder in [start, *start.parents]:
        candidate = folder / ".env"
        if candidate.is_file():
            return candidate
    return None


def parse(text: str) -> Dict[str, str]:
    """KEY=VALUE lines; `export`, comments, blank lines and matching quotes are handled."""
    values: Dict[str, str] = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.removeprefix("export ").partition("=")
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        elif " #" in value:
            value = value.split(" #", 1)[0].rstrip()
        values[key.strip()] = value
    return values


def load(start: Path) -> Optional[Path]:
    """Put the nearest .env into os.environ without overriding variables that are already set."""
    path = find(start)
    if path is not None:
        for key, value in parse(path.read_text(encoding="utf-8")).items():
            os.environ.setdefault(key, value)
    return path
