"""Where the running code comes from: git remote, commit, branch, uncommitted changes, script path."""
from __future__ import annotations

import platform
import subprocess
import sys
import urllib.parse
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Optional


@dataclass
class CodeInfo:
    git_remote: str = ""
    commit: str = ""
    branch: str = ""
    dirty: bool = False
    script: str = ""
    argv: List[str] = field(default_factory=list)
    python: str = ""
    root: Optional[Path] = None


def collect(script: Path) -> CodeInfo:
    """Describe the script and its git repository; missing git is not an error."""
    info = CodeInfo(script=script.name, argv=list(sys.argv[1:]), python=platform.python_version())
    root = _git(script.parent, "rev-parse", "--show-toplevel")
    if not root:
        return info
    info.root = Path(root)
    info.script = script.resolve().relative_to(info.root.resolve()).as_posix()
    info.git_remote = without_credentials(_git(info.root, "remote", "get-url", "origin"))
    info.commit = _git(info.root, "rev-parse", "HEAD")
    branch = _git(info.root, "rev-parse", "--abbrev-ref", "HEAD")
    info.branch = "" if branch == "HEAD" else branch
    info.dirty = bool(_git(info.root, "status", "--porcelain"))
    return info


def without_credentials(url: str) -> str:
    """https://user:token@host/repo → https://host/repo; scp-style ssh remotes are kept as they are."""
    parts = urllib.parse.urlsplit(url)
    if not parts.scheme or "@" not in parts.netloc:
        return url
    return urllib.parse.urlunsplit(parts._replace(netloc=parts.netloc.rsplit("@", 1)[1]))


def _git(cwd: Path, *args: str) -> str:
    try:
        done = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.TimeoutExpired):
        return ""
    return done.stdout.strip() if done.returncode == 0 else ""
