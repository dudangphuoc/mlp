"""Container entry of a code run: fetch the code snapshot, then run the developer's command inside it."""
from __future__ import annotations

import io
import json
import os
import subprocess
import sys
import tarfile
import urllib.request
from pathlib import Path

ROOT = Path("/work")


def main() -> None:
    with urllib.request.urlopen(os.environ["MLP_CODE_URL"], timeout=600) as response:
        archive = response.read()
    ROOT.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as tar:
        for member in tar.getmembers():
            target = (ROOT / member.name).resolve()
            if member.isfile() and str(target).startswith(str(ROOT) + os.sep):
                tar.extract(member, ROOT, **({"filter": "data"} if sys.version_info >= (3, 12) else {}))
    os.chdir(ROOT / os.environ.get("MLP_CODE_WORKDIR", ""))
    command = json.loads(os.environ["MLP_CODE_COMMAND"])
    if os.environ.get("MLP_DEBUG_TOKEN"):
        command = _under_debugger(command)
    sys.stdout.flush()
    os.execvp(command[0], command)


def _under_debugger(command: list) -> list:
    """Start the relay, then run the command under debugpy connected to it (the developer's F5 session)."""
    relay = subprocess.Popen([sys.executable, "-m", "mlplatform.debugrelay"], stdout=subprocess.PIPE, text=True)
    port = relay.stdout.readline().strip()
    return [command[0], "-Xfrozen_modules=off", "-m", "debugpy", "--connect", f"127.0.0.1:{port}",
            "--adapter-access-token", os.environ["MLP_DEBUG_TOKEN"], "--parent-session-pid", os.environ["MLP_DEBUG_PPID"], *command[1:]]


if __name__ == "__main__":
    main()
