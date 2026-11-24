"""Download models and datasets once into a cache and reuse them: on cluster machines a volume shared by the
workspace's runs, on the dev machine ~/.cache/mlplatform."""
from __future__ import annotations

import hashlib
import io
import json
import os
import re
import shutil
import tarfile
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from . import wire
from .errors import ConfigError, MlpError

HF = os.environ.get("HF_ENDPOINT", "https://huggingface.co").rstrip("/")


def cache_root(kind: str, in_job: bool) -> Path:
    """Where `kind` ("models" or "data") is cached."""
    mounted = Path("/mnt/mlp-models" if kind == "models" else "/mnt/mlp-data")
    if in_job and mounted.is_dir():
        return mounted
    return Path(os.environ.get("MLP_CACHE", Path.home() / ".cache" / "mlplatform")) / kind


def parse_model(ref: str) -> Tuple[str, str, Optional[str]]:
    """hf://owner/name[@revision][#file=path] → (repo, revision, file)."""
    r = ref[5:] if ref.lower().startswith("hf://") else ref
    file = None
    if "#file=" in r:
        r, file = r.split("#file=", 1)
    revision = "main"
    if "@" in r:
        r, revision = r.split("@", 1)
    if r.count("/") != 1 or not all(r.split("/")):
        raise ConfigError(f"model reference {ref!r}: use hf://owner/name[@revision][#file=path]")
    return r, revision, file


def hub_files(repo: str, revision: str, file: Optional[str]) -> Tuple[str, List[Tuple[str, int, str, bytes]]]:
    """Resolve straight against Hugging Face (untracked runs), with the developer's own HF_TOKEN if set."""
    info = _hub_json(f"{HF}/api/models/{repo}/revision/{urllib.parse.quote(revision, safe='')}")
    sha = info["sha"]
    if info.get("gated") not in (None, False) and not (os.environ.get("HF_TOKEN") or os.environ.get("HUGGING_FACE_HUB_TOKEN")):
        raise MlpError("PERMISSION_DENIED", f"{repo} is gated: set HF_TOKEN for an account that accepted its terms")
    files, url = [], f"{HF}/api/models/{repo}/tree/{sha}?recursive=true"
    while url:
        request = _hub_request(url)
        with urllib.request.urlopen(request, timeout=60) as response:
            files.extend(e for e in json.load(response) if e.get("type") == "file")
            link = response.headers.get("Link", "")
        match = re.search(r'<([^>]+)>;\s*rel="next"', link)
        url = match.group(1) if match else ""
    chosen = [f for f in files if file is None or f["path"] == file]
    if not chosen:
        raise MlpError("NOT_FOUND", f"{repo} has no file {file}")
    return sha, [(f["path"], int((f.get("lfs") or {}).get("size") or f.get("size") or 0), f"{HF}/{repo}/resolve/{sha}/{urllib.parse.quote(f['path'])}", b"")
                 for f in chosen]


def platform_model_files(reply: wire.Fields) -> Tuple[str, List[Tuple[str, int, str, bytes]]]:
    files = []
    for raw in reply.get(3, []):
        f = wire.decode(raw)
        content = f.get(4, [b""])[-1]
        files.append((wire.text(f, 1), wire.number_of(f, 2), wire.text(f, 3), content if isinstance(content, bytes) else b""))
    return wire.text(reply, 2), files


def materialize(dest: Path, files: List[Tuple[str, int, str, bytes]], label: str) -> Path:
    """Download every file into `dest` once (a `.complete` marker per file set); concurrent runs never see half a set."""
    marker = dest.parent / f".{dest.name}.complete-{hashlib.sha256(json.dumps(sorted(p for p, *_ in files)).encode()).hexdigest()[:12]}"
    if marker.exists():
        _say(f"{label} · cached")
        return dest
    total = sum(size for _, size, *_ in files)
    _say(f"{label} · downloading {len(files)} files ({total / 1e6:.1f} MB)")
    staging = dest.parent / f".{dest.name}.{os.getpid()}"
    shutil.rmtree(staging, ignore_errors=True)
    for path, _, url, content in files:
        target = staging / path
        target.parent.mkdir(parents=True, exist_ok=True)
        if content:
            target.write_bytes(content)
        else:
            _download(url, target)
    dest.mkdir(parents=True, exist_ok=True)
    for item in sorted(staging.rglob("*")):
        if item.is_file():
            final = dest / item.relative_to(staging)
            final.parent.mkdir(parents=True, exist_ok=True)
            os.replace(item, final)
    shutil.rmtree(staging, ignore_errors=True)
    marker.touch()
    return dest


def unpack_local_data(entry: Dict[str, str], in_job: bool) -> Path:
    """A local data folder uploaded from the dev machine: extract it once into the cache, keyed by its SHA-256."""
    dest = cache_root("data", in_job) / entry["sha256"]
    if not (dest / ".complete").exists():
        with urllib.request.urlopen(entry["url"], timeout=600) as response:
            archive = response.read()
        staging = dest.parent / f".{dest.name}.{os.getpid()}"
        shutil.rmtree(staging, ignore_errors=True)
        staging.mkdir(parents=True)
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as tar:
            for member in tar.getmembers():
                if member.isfile() and not member.name.startswith(("/", "..")) and ".." not in Path(member.name).parts:
                    tar.extract(member, staging)
        if not dest.exists():
            os.replace(staging, dest)
        else:
            shutil.rmtree(staging, ignore_errors=True)
        (dest / ".complete").touch()
    return dest / entry["name"]


def _download(url: str, target: Path) -> None:
    request = _hub_request(url) if url.startswith(HF + "/") else urllib.request.Request(url, headers={"user-agent": "mlplatform-python"})
    with urllib.request.urlopen(request, timeout=600) as response, target.open("wb") as out:
        shutil.copyfileobj(response, out, 1 << 20)


def _hub_request(url: str) -> urllib.request.Request:
    headers = {"user-agent": "mlplatform-python"}
    token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGING_FACE_HUB_TOKEN")
    if token:
        headers["authorization"] = f"Bearer {token}"
    return urllib.request.Request(url, headers=headers)


def _hub_json(url: str) -> dict:
    try:
        with urllib.request.urlopen(_hub_request(url), timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as e:
        if e.code in (401, 403):
            raise MlpError("PERMISSION_DENIED", "gated or private model: set HF_TOKEN for an account that accepted its terms") from None
        if e.code == 404:
            raise MlpError("NOT_FOUND", f"not found on Hugging Face: {url}") from None
        raise


def _say(message: str) -> None:
    import sys
    print(f"mlp: {message}", file=sys.stderr, flush=True)
