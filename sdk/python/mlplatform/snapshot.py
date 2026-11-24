"""Pack the code to run remotely: the repository's files (tracked + untracked, minus ignored), never .env."""
from __future__ import annotations

import gzip
import hashlib
import io
import os
import subprocess
import tarfile
from pathlib import Path
from typing import List, Optional, Tuple

from .errors import ConfigError

MAX_BYTES = 100 << 20
SKIP_DIRS = {".git", "__pycache__", ".venv", "venv", "env", "node_modules", ".mypy_cache", ".pytest_cache", ".ipynb_checkpoints", ".idea", ".vscode"}


def files_of(root: Path) -> List[Path]:
    """Relative paths: `git ls-files` when the folder is a repository, otherwise a walk without caches and virtualenvs."""
    try:
        done = subprocess.run(["git", "ls-files", "-z", "-co", "--exclude-standard"], cwd=root, capture_output=True, timeout=30)
    except (OSError, subprocess.TimeoutExpired):
        done = None
    if done is not None and done.returncode == 0:
        names = [Path(n) for n in done.stdout.decode("utf-8", "replace").split("\0") if n]
    else:
        names = _walk(root)
    return sorted({n for n in names if n.name != ".env" and (root / n).is_file() and not (root / n).is_symlink()})


def _walk(root: Path) -> List[Path]:
    found: List[Path] = []
    for folder, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        found.extend(Path(folder, n).relative_to(root) for n in names)
    return found


def changes_since(root: Path, base: str) -> Tuple[List[Path], List[str]]:
    """Files changed or added since `base` (committed, staged, unstaged, untracked) and files deleted since then."""
    changed = _git_names(root, "diff", "--name-only", "-z", "--no-renames", "--diff-filter=d", base)
    added = _git_names(root, "ls-files", "-z", "-o", "--exclude-standard")
    deleted = _git_names(root, "diff", "--name-only", "-z", "--no-renames", "--diff-filter=D", base)
    names = sorted({Path(n) for n in changed + added if Path(n).name != ".env" and (root / n).is_file() and not (root / n).is_symlink()})
    return names, deleted


def _git_names(root: Path, *args: str) -> List[str]:
    done = subprocess.run(["git", *args], cwd=root, capture_output=True, timeout=30)
    return [n for n in done.stdout.decode("utf-8", "replace").split("\0") if n] if done.returncode == 0 else []


def pack(root: Path, names: Optional[List[Path]] = None) -> Tuple[bytes, str, int]:
    """Deterministic tar.gz of the code (every file, or only `names`): (archive, sha256, file count)."""
    names = files_of(root) if names is None else names
    sizes = [(n, (root / n).stat().st_size) for n in names]
    total = sum(s for _, s in sizes)
    if total > MAX_BYTES:
        largest = ", ".join(f"{n.as_posix()} ({s >> 20} MB)" for n, s in sorted(sizes, key=lambda x: -x[1])[:5])
        raise ConfigError(f"code to send is {total >> 20} MB (limit {MAX_BYTES >> 20} MB); largest: {largest}. Add them to .gitignore")
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode="wb", mtime=0) as gz, tarfile.open(fileobj=gz, mode="w") as tar:
        for name, size in sizes:
            path = root / name
            info = tarfile.TarInfo(name.as_posix())
            info.size, info.mtime, info.mode = size, 0, 0o755 if os.access(path, os.X_OK) else 0o644
            with path.open("rb") as body:
                tar.addfile(info, body)
    data = buffer.getvalue()
    return data, hashlib.sha256(data).hexdigest(), len(names)


def pack_path(path: Path) -> Tuple[bytes, str, int]:
    """Deterministic tar.gz of a local data folder or file, ignoring .gitignore; stored under its own name."""
    root = path.parent
    names = [path.relative_to(root)] if path.is_file() else [path.relative_to(root) / n for n in _walk(path)]
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode="wb", mtime=0, compresslevel=1) as gz, tarfile.open(fileobj=gz, mode="w") as tar:
        for name in sorted(names):
            info = tarfile.TarInfo(name.as_posix())
            info.size, info.mtime, info.mode = (root / name).stat().st_size, 0, 0o644
            with (root / name).open("rb") as body:
                tar.addfile(info, body)
    data = buffer.getvalue()
    return data, hashlib.sha256(data).hexdigest(), len(names)
