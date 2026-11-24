"""init / params / log / save: the same code runs untracked on the dev machine, or reports a run to the platform."""
from __future__ import annotations

import atexit
import json
import math
import os
import re
import socket
import sys
import threading
import time
import urllib.request
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union

from . import codeinfo, dotenv, fetch, remote, wire
from .errors import ConfigError, MlpError

TRACKING = "mlplatform.v1.TrackingService/"
RUNTIME = "mlplatform.v1.RuntimeService/"
HEARTBEAT_SECONDS = 20.0
FLUSH_SECONDS = 2.0
FLUSH_POINTS = 500


class Params(dict):
    """Parameters readable as attributes (`cfg.lr`) or keys (`cfg["lr"]`)."""

    def __getattr__(self, name: str) -> Any:
        try:
            return self[name]
        except KeyError:
            raise AttributeError(name) from None


class Run:
    """The current run; `tracked` is False when no platform is configured."""

    def __init__(self, base_url: str = "", key: str = "", workspace_id: str = "", run_id: str = "", number: int = 0, web_url: str = "",
                 in_job: bool = False) -> None:
        self.base_url, self.key, self.in_job = base_url, key, in_job
        self.workspace_id, self.id, self.number, self.web_url = workspace_id, run_id, number, web_url
        self.exit_code = 0
        self._points: List[Tuple[str, int, float, float]] = []
        self._steps: Dict[str, int] = {}
        self._lock = threading.Lock()
        self._done = threading.Event()
        self._finished = False

    @property
    def tracked(self) -> bool:
        return bool(self.id)

    def call(self, method: str, body: bytes) -> wire.Fields:
        return wire.call(self.base_url, self.key, method, body)

    def ref(self) -> bytes:
        return wire.string(1, self.workspace_id) + wire.string(2, self.id)


_run: Optional[Run] = None


def init(project: Optional[str] = None, name: Optional[str] = None) -> Run:
    """Start the run: reads .env, opens a tracked run when MLP_API_URL and MLP_API_KEY are set. Call it first."""
    global _run
    if _run is not None:
        return _run
    if os.environ.get("MLP_RUN_ID") and os.environ.get("MLP_JOB_TOKEN"):
        _run = Run(os.environ.get("MLP_API_HTTP", ""), os.environ["MLP_JOB_TOKEN"], "", os.environ["MLP_RUN_ID"], in_job=True)
        _start_background(_run)
        return _run
    script = Path(sys.argv[0]).resolve() if sys.argv and sys.argv[0] else Path.cwd() / "interactive"
    env_file = dotenv.load(script.parent)
    url, key = os.environ.get("MLP_API_URL", "").strip(), os.environ.get("MLP_API_KEY", "").strip()
    machine = os.environ.get("MLP_MACHINE", "local").strip() or "local"
    _check(url, key, machine, env_file)
    if not url:
        _run = Run()
        _say("local · not tracked")
        return _run
    code = codeinfo.collect(script)
    slug = slugify(project or os.environ.get("MLP_PROJECT") or (code.root.name if code.root else script.parent.name))
    if machine != "local":
        remote.run_remotely(url, key, machine, slug, name or script.stem, code, script, _debugger())
    reply = wire.call(url, key, TRACKING + "OpenCodeRun", _open_request(code, slug, name or script.stem))
    run_fields = wire.nested(reply, 1)
    _run = Run(url, key, wire.text(reply, 2), wire.text(run_fields, 1), wire.number_of(run_fields, 14), wire.text(reply, 5))
    _say(f"local · run #{_run.number} in {wire.text(reply, 3)}/{wire.text(reply, 4)}" + (f" · {_run.web_url}" if _run.web_url else ""))
    _start_background(_run)
    return _run


def run() -> Optional[Run]:
    """The current run, or None before init()."""
    return _run


def params(**defaults: Any) -> Params:
    """Declare parameters with defaults; `--name value` or `--name=value` on the command line overrides them."""
    current = _run or init()
    values = Params({k: _override(k, v) for k, v in defaults.items()})
    if current.tracked:
        current.call(TRACKING + "SetCodeRunParams", current.ref() + wire.string(3, json.dumps(values, default=str)))
    else:
        _say("params " + " ".join(f"{k}={v}" for k, v in values.items()))
    return values


def log(metrics: Dict[str, Union[int, float]], step: Optional[int] = None) -> None:
    """Record metric values; without `step` each key counts up from 0."""
    current = _run or init()
    now = time.time()
    with current._lock:
        points = []
        for k, v in metrics.items():
            value = float(v)
            s = step if step is not None else current._steps.get(k, -1) + 1
            current._steps[k] = max(current._steps.get(k, -1), s)
            if math.isfinite(value):
                points.append((k, s, value, now))
        if current.tracked:
            current._points.extend(points)
            should_flush = len(current._points) >= FLUSH_POINTS
        else:
            should_flush = False
    if not current.tracked:
        _say(" ".join(f"{k}={v:g}" for k, _, v, _ in points) + (f" (step {step})" if step is not None else ""))
    elif should_flush:
        _flush(current)


def model(ref: str) -> Path:
    """Local path of a model (hf://owner/name[@revision][#file=path]), downloaded once and reused."""
    current = _run or init()
    repo, revision, file = fetch.parse_model(ref)
    if current.tracked:
        body = wire.string(1, current.workspace_id) + wire.string(2, current.id if current.in_job else "") + wire.string(3, ref)
        sha, files = fetch.platform_model_files(current.call(TRACKING + "ResolveModelRef", body))
    else:
        sha, files = fetch.hub_files(repo, revision, file)
    dest = fetch.materialize(fetch.cache_root("models", current.in_job) / repo / sha, files, f"model {repo}@{sha[:8]}")
    return dest / file if file else dest


def dataset(ref: str) -> Path:
    """Local path of data: a folder or file (sent along when the code runs remotely), or a platform dataset "name[@N]"."""
    current = _run or init()
    key = os.path.normpath(ref)
    local = json.loads(os.environ.get("MLP_DATA_MAP", "{}"))
    if current.in_job and key in local:
        return fetch.unpack_local_data(local[key], True)
    if Path(ref).exists() or ref.startswith((".", "/", "~")):
        return Path(ref).expanduser()
    if not current.tracked:
        raise ConfigError(f"dataset {ref!r} is on the platform: set MLP_API_URL and MLP_API_KEY")
    reply = current.call(TRACKING + "ResolveDataset", wire.string(1, current.workspace_id) + wire.string(2, current.id if current.in_job else "") + wire.string(3, ref))
    files = [(wire.text(f, 1), wire.number_of(f, 2), wire.text(f, 3), b"") for f in map(wire.decode, reply.get(4, []))]
    root = fetch.cache_root("data", current.in_job) / f"v-{wire.text(reply, 1)}"
    return fetch.materialize(root, files, f"dataset {wire.text(reply, 2)}@{wire.number_of(reply, 3)}")


def save(path: Union[str, os.PathLike]) -> Path:
    """Keep a file or folder as the run's result; the stored names keep the folder's own name."""
    current = _run or init()
    target = Path(path)
    if not target.exists():
        raise FileNotFoundError(target)
    if not current.tracked:
        return target
    files = [target] if target.is_file() else sorted(p for p in target.rglob("*") if p.is_file())
    names = [p.relative_to(target.parent).as_posix() for p in files]
    for start in range(0, len(files), 500):
        chunk, chunk_names = files[start:start + 500], names[start:start + 500]
        urls = wire.texts(current.call(RUNTIME + "CreateArtifactUploads", wire.string(1, current.id) + wire.strings(2, chunk_names)), 1)
        for file, url in zip(chunk, urls):
            _put(url, file)
        done = b"".join(wire.message(2, wire.string(1, n) + wire.integer(2, f.stat().st_size)) for n, f in zip(chunk_names, chunk))
        current.call(RUNTIME + "CompleteArtifacts", wire.string(1, current.id) + done)
    _say(f"saved {len(files)} file(s) from {target}")
    return target


def slugify(text: str) -> str:
    """Project names become lowercase letters, digits and single dashes (max 64)."""
    slug = re.sub(r"-+", "-", re.sub(r"[^a-z0-9]+", "-", text.lower())).strip("-")[:64].strip("-")
    return slug or "default"


def _debugger() -> Optional[Tuple[str, int, str]]:
    """The IDE's debug adapter (host, port, access token) when this process was started with F5 under debugpy."""
    try:
        import debugpy
        import pydevd
    except ImportError:
        return None
    setup = getattr(pydevd.SetupHolder, "setup", None)
    if not setup or not debugpy.is_client_connected() or not setup.get("client-access-token"):
        return None
    return setup["client"], int(setup["port"]), setup["client-access-token"]


def _check(url: str, key: str, machine: str, env_file: Optional[Path]) -> None:
    where = f" (from {env_file})" if env_file else ""
    if bool(url) != bool(key):
        missing = "MLP_API_KEY" if url else "MLP_API_URL"
        raise ConfigError(f"{missing} is missing{where}: set both MLP_API_URL and MLP_API_KEY, or neither")
    if machine != "local" and not url:
        raise ConfigError(f"MLP_MACHINE={machine}{where} needs MLP_API_URL and MLP_API_KEY")


def _open_request(code: codeinfo.CodeInfo, project: str, name: str) -> bytes:
    run_code = (wire.string(1, code.git_remote) + wire.string(2, code.commit) + wire.boolean(3, code.dirty) + wire.string(4, code.script)
                + wire.strings(5, code.argv) + wire.string(6, code.python) + wire.string(7, code.branch))
    return (wire.string(1, os.environ.get("MLP_WORKSPACE", "")) + wire.string(2, project) + wire.string(3, name)
            + wire.string(5, socket.gethostname()) + wire.message(6, run_code))


def _override(key: str, default: Any) -> Any:
    raw = _argv_value(key)
    if raw is None:
        return default
    if isinstance(default, bool):
        return raw.lower() in ("1", "true", "yes", "on", "")
    if isinstance(default, int):
        return int(raw)
    if isinstance(default, float):
        return float(raw)
    return raw


def _argv_value(key: str) -> Optional[str]:
    flags = {f"--{key}", f"--{key.replace('_', '-')}"}
    args = sys.argv[1:]
    for i, arg in enumerate(args):
        name, eq, value = arg.partition("=")
        if name in flags:
            if eq:
                return value
            return args[i + 1] if i + 1 < len(args) and not args[i + 1].startswith("--") else ""
    return None


def _start_background(current: Run) -> None:
    threading.Thread(target=_background, args=(current,), name="mlp-heartbeat", daemon=True).start()
    if current.in_job:
        atexit.register(_flush, current)
        return
    previous_hook = sys.excepthook

    def on_uncaught(kind, value, tb):
        current.exit_code = 130 if issubclass(kind, KeyboardInterrupt) else 1
        previous_hook(kind, value, tb)

    sys.excepthook = on_uncaught
    real_exit = sys.exit

    def on_exit(status: Any = None) -> None:
        current.exit_code = status if isinstance(status, int) else (0 if status is None else 1)
        real_exit(status)

    sys.exit = on_exit
    atexit.register(_finish, current)


def _background(current: Run) -> None:
    last_beat = 0.0
    while not current._done.wait(FLUSH_SECONDS):
        _flush(current)
        if not current.in_job and time.monotonic() - last_beat >= HEARTBEAT_SECONDS:
            last_beat = time.monotonic()
            _quietly(lambda: current.call(TRACKING + "HeartbeatCodeRun", current.ref()))


def _flush(current: Run) -> None:
    with current._lock:
        points, current._points = current._points, []
    for start in range(0, len(points), 5000):
        body = wire.string(1, current.id) + b"".join(
            wire.message(2, wire.string(1, k) + wire.integer(2, s) + wire.double(3, v)
                         + wire.message(4, wire.integer(1, int(at)) + wire.integer(2, int((at % 1) * 1e9))))
            for k, s, v, at in points[start:start + 5000])
        _quietly(lambda: current.call(TRACKING + "LogMetrics", body))


def _finish(current: Run) -> None:
    if current._finished:
        return
    current._finished = True
    current._done.set()
    _flush(current)
    _quietly(lambda: current.call(TRACKING + "FinishCodeRun", current.ref() + wire.integer(3, current.exit_code)))


def _put(url: str, file: Path) -> None:
    with file.open("rb") as body:
        request = urllib.request.Request(url, data=body, method="PUT", headers={"content-length": str(file.stat().st_size)})
        with urllib.request.urlopen(request, timeout=600) as response:
            response.read()


def _quietly(action) -> None:
    try:
        action()
    except MlpError as e:
        _say(f"warning: {e}")


def _say(message: str) -> None:
    print(f"mlp: {message}", file=sys.stderr, flush=True)
