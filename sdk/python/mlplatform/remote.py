"""Send the code to a cluster machine, then mirror the run here: logs, state, Ctrl+C, exit code."""
from __future__ import annotations

import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path
from typing import Dict, List, NoReturn, Optional, Set, Tuple

from . import codeinfo, debugrelay, snapshot, wire
from .errors import MlpError

TRACKING = "mlplatform.v1.TrackingService/"
SCHEDULER = "mlplatform.v1.SchedulerService/"
FINISHED = {"succeeded", "failed", "cancelled"}


def run_remotely(url: str, key: str, machine: str, project: str, name: str, code: codeinfo.CodeInfo, script: Path,
                 debug: Optional[Tuple[str, int, str]] = None) -> NoReturn:
    """Upload the code, start it on `machine`, follow it until it ends, and exit with its exit code."""
    root = code.root or script.parent
    base, deleted = _server_base(url, key, code, root)
    names = None
    if base:
        names, deleted = snapshot.changes_since(root, base)
    archive, sha, count = snapshot.pack(root, names)
    target = wire.call(url, key, TRACKING + "CreateCodeSnapshotUpload",
                       wire.string(1, os.environ.get("MLP_WORKSPACE", "")) + wire.string(2, sha) + wire.integer(3, len(archive)))
    workspace_id = wire.text(target, 1)
    if not wire.number_of(target, 3):
        _put(wire.text(target, 4), archive)
    what = f"{count} changed files on top of {base[:8]}, the server fetches the rest" if base else f"{count} files"
    _say(f"sent {what} ({len(archive) >> 10} KB){' · dirty' if code.dirty else ''}")
    models, datasets = _references(root)
    entry = script.resolve().relative_to(root.resolve()).as_posix() if root.resolve() in script.resolve().parents else ""
    for ref, files in models.items():
        try:
            wire.call(url, key, TRACKING + "ResolveModelRef", wire.string(1, workspace_id) + wire.string(3, ref))
        except MlpError as e:
            if entry in files:
                raise
            _say(f"warning: {', '.join(sorted(files))} uses {ref}: {e.message}")
    data = _upload_local_data(url, key, datasets)
    body = _open(workspace_id, project, name, code, machine, sha, root, script, base, deleted, data)
    if debug:
        body += wire.message(7, wire.string(12, debug[2]) + wire.integer(13, os.getpid()) + wire.string(14, str(root.resolve())))
    reply = wire.call(url, key, TRACKING + "OpenCodeRun", body)
    run = wire.nested(reply, 1)
    run_id, number, job_id = wire.text(run, 1), wire.number_of(run, 14), wire.text(run, 8)
    control = debugrelay.serve_developer(url, key, run_id, (debug[0], debug[1])) if debug else None
    if control:
        _say("debugger attached through the platform: breakpoints stop on the remote machine")
    _say(f"remote · run #{number} on {machine} in {wire.text(reply, 3)}/{wire.text(reply, 4)} · {wire.text(reply, 5)}")
    code = _follow(url, key, workspace_id, run_id, job_id)
    if control:
        control.send(b"done", 1)
        control.close()
    os._exit(code)


def command_of(script: Path, workdir: Path) -> List[str]:
    """The command that started this process, runnable from `workdir` on the remote machine."""
    main = sys.modules.get("__main__")
    spec = getattr(main, "__spec__", None)
    if spec is not None and spec.name and spec.name != "__main__":
        return ["python", "-m", spec.name.removesuffix(".__main__"), *sys.argv[1:]]
    return ["python", os.path.relpath(script, workdir).replace(os.sep, "/"), *sys.argv[1:]]


def _server_base(url: str, key: str, code: codeinfo.CodeInfo, root: Path) -> Tuple[str, List[str]]:
    """The pushed commit the server can fetch itself, when a code source covers this repository; otherwise ""."""
    if not code.git_remote or not code.root:
        return "", []
    match = wire.call(url, key, TRACKING + "MatchCodeSource", wire.string(1, os.environ.get("MLP_WORKSPACE", "")) + wire.string(2, code.git_remote))
    if not wire.text(match, 1):
        return "", []
    upstream = _git(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}")
    base = _git(root, "merge-base", "HEAD", "@{u}") if upstream else (code.commit if _git(root, "branch", "-r", "--contains", "HEAD") else "")
    if not base:
        _say("this commit is not pushed yet: sending every file")
    return base, []


REFERENCE = re.compile(r"""\b(?:mlp|mlplatform)\.(model|dataset)\(\s*(["'])(.+?)\2""")


def _references(root: Path) -> Tuple[Dict[str, Set[str]], List[str]]:
    """Literal mlp.model("…") and mlp.dataset("…") arguments in the code: models (with the files using them) are checked
    before the run is sent, local data folders are sent along."""
    models: Dict[str, Set[str]] = {}
    datasets: Set[str] = set()
    for path in snapshot.files_of(root):
        if path.suffix != ".py":
            continue
        for kind, _, value in REFERENCE.findall((root / path).read_text(encoding="utf-8", errors="replace")):
            if kind == "model" and value.lower().startswith("hf://"):
                models.setdefault(value, set()).add(path.as_posix())
            elif kind == "dataset":
                datasets.add(value)
    return models, sorted(d for d in datasets if Path(d).expanduser().exists())


def _upload_local_data(url: str, key: str, refs: List[str]) -> Dict[str, Tuple[str, str]]:
    data: Dict[str, Tuple[str, str]] = {}
    for ref in refs:
        path = Path(ref).expanduser().resolve()
        archive, sha, count = snapshot.pack_path(path)
        target = wire.call(url, key, TRACKING + "CreateCodeSnapshotUpload",
                           wire.string(1, os.environ.get("MLP_WORKSPACE", "")) + wire.string(2, sha) + wire.integer(3, len(archive)) + wire.boolean(4, True))
        if not wire.number_of(target, 3):
            _put(wire.text(target, 4), archive)
        _say(f"data {ref} · {count} files ({len(archive) >> 20} MB){' · already uploaded' if wire.number_of(target, 3) else ''}")
        data[os.path.normpath(ref)] = (sha, path.name)
    return data


def _git(root: Path, *args: str) -> str:
    try:
        done = subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired):
        return ""
    return done.stdout.strip() if done.returncode == 0 else ""


def _open(workspace_id: str, project: str, name: str, code: codeinfo.CodeInfo, machine: str, sha: str, root: Path, script: Path,
          base: str, deleted: List[str], data: Dict[str, Tuple[str, str]]) -> bytes:
    cwd = Path.cwd().resolve()
    workdir = cwd if cwd == root.resolve() or root.resolve() in cwd.parents else script.parent
    requirements = root / "requirements.txt"
    forwarded: Dict[str, str] = {k: os.environ[k] for k in os.environ.get("MLP_FORWARD_ENV", "").replace(" ", "").split(",") if k and k in os.environ}
    remote = (wire.string(1, machine) + wire.integer(2, int(os.environ.get("MLP_GPU", "1") or 0)) + wire.string(3, os.environ.get("MLP_IMAGE", ""))
              + wire.string(4, sha) + wire.string(5, workdir.relative_to(root.resolve()).as_posix() if workdir != root.resolve() else "")
              + wire.strings(6, command_of(script, workdir))
              + b"".join(wire.message(7, wire.string(1, k) + wire.string(2, v)) for k, v in forwarded.items())
              + wire.string(8, requirements.read_text(encoding="utf-8") if requirements.is_file() else "")
              + wire.string(9, base) + wire.strings(10, deleted)
              + b"".join(wire.message(11, wire.string(1, k) + wire.string(2, f"{sha}:{n}")) for k, (sha, n) in data.items()))
    run_code = (wire.string(1, code.git_remote) + wire.string(2, code.commit) + wire.boolean(3, code.dirty) + wire.string(4, code.script)
                + wire.strings(5, code.argv) + wire.string(6, code.python) + wire.string(7, code.branch))
    return (wire.string(1, workspace_id) + wire.string(2, project) + wire.string(3, name) + wire.string(5, socket.gethostname())
            + wire.message(6, run_code) + wire.message(7, remote))


def _follow(url: str, key: str, workspace_id: str, run_id: str, job_id: str) -> int:
    ref = wire.string(1, workspace_id) + wire.string(2, run_id)
    last_ts, seen, phase, stopping = 0, set(), "", False
    while True:
        try:
            last_ts, seen = _print_logs(url, key, workspace_id, job_id, last_ts, seen)
            run = wire.call(url, key, TRACKING + "GetRun", ref)
            state, job_state = wire.text(run, 4), wire.text(run, 22)
            if job_state and job_state != phase and state not in FINISHED:
                phase = job_state
                _say(phase + (f": {wire.text(run, 23)}" if wire.text(run, 23) else ""))
            if state in FINISHED:
                _print_logs(url, key, workspace_id, job_id, last_ts, seen)
                code = 130 if state == "cancelled" else 0 if state == "succeeded" else _exit_code(url, key, workspace_id, job_id)
                _say(f"remote run {state} · exit {code}")
                return code
            time.sleep(1.0)
        except KeyboardInterrupt:
            if stopping:
                _say("left the run; it keeps its own state on the platform")
                return 130
            stopping = True
            _say("stopping the remote run… (Ctrl+C again to leave it)")
            wire.call(url, key, TRACKING + "StopRun", ref)
        except MlpError as e:
            _say(f"warning: {e}")
            time.sleep(3.0)


def _print_logs(url: str, key: str, workspace_id: str, job_id: str, last_ts: int, seen: Set[Tuple[int, str]]) -> Tuple[int, Set[Tuple[int, str]]]:
    if not job_id:
        return last_ts, seen
    reply = wire.call(url, key, SCHEDULER + "GetJobLogs",
                      wire.string(1, workspace_id) + wire.string(2, job_id) + wire.integer(4, 2000) + wire.integer(5, max(0, last_ts - 1)))
    for raw in reply.get(1, []):
        line = wire.decode(raw)
        ts, text = wire.number_of(line, 1), wire.text(line, 2)
        if (ts, text) in seen or ts < last_ts:
            continue
        print(text, flush=True)
        if ts > last_ts:
            seen = {(t, x) for t, x in seen if t == ts}
            last_ts = ts
        seen.add((ts, text))
    return last_ts, seen


def _exit_code(url: str, key: str, workspace_id: str, job_id: str) -> int:
    ops = wire.call(url, key, SCHEDULER + "GetJobOps", wire.string(1, workspace_id) + wire.string(2, job_id))
    attempts = [wire.decode(a) for a in ops.get(2, [])]
    code = wire.number_of(attempts[-1], 5) if attempts else 0
    return code if 0 < code < 256 else 1


def _put(url: str, data: bytes) -> None:
    request = urllib.request.Request(url, data=data, method="PUT", headers={"content-length": str(len(data))})
    with urllib.request.urlopen(request, timeout=600) as response:
        response.read()


def _say(message: str) -> None:
    print(f"mlp: {message}", file=sys.stderr, flush=True)
