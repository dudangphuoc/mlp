"""Debug relay of a code run: on the developer's machine it answers the api's announcements and joins each remote
debugger connection to the IDE's debug adapter (renumbering process ids so containers never clash); in the container
it turns the debugger's TCP connections into WebSockets to the api."""
from __future__ import annotations

import json
import socket
import sys
import threading
from typing import Callable, Dict

from .ws import WebSocket

FIRST_REMOTE_PID = 900000


def ws_base(http_url: str) -> str:
    return http_url.rstrip("/")


def serve_developer(base_url: str, key: str, run_id: str, adapter: tuple) -> WebSocket:
    """Keep the control socket of `run_id` open and bridge every announced connection to the IDE's adapter; send
    "done" on the returned socket when the run ended by itself."""
    renumber = _Renumber()
    control = WebSocket.connect(f"{ws_base(base_url)}/v1/debug/{run_id}/control", key)

    def listen() -> None:
        while (message := _quiet_recv(control)) is not None:
            text = message[1].decode()
            if text.startswith("open "):
                _daemon(lambda c=text[5:].strip(): _bridge_developer(base_url, key, run_id, c, adapter, renumber))

    _daemon(listen)
    return control


def _bridge_developer(base_url: str, key: str, run_id: str, connection: str, adapter: tuple, renumber: "_Renumber") -> None:
    remote = WebSocket.connect(f"{ws_base(base_url)}/v1/debug/{run_id}/dev/{connection}", key)
    local = socket.create_connection(adapter)
    buffer = bytearray()

    def to_adapter() -> None:
        while (message := _quiet_recv(remote)) is not None:
            buffer.extend(message[1])
            local.sendall(renumber.frames(buffer))
        _shutdown(local)

    def to_remote() -> None:
        try:
            while data := local.recv(65536):
                remote.send(data)
        except OSError:
            pass
        remote.close()

    _daemon(to_adapter)
    _daemon(to_remote)


def serve_container(base_url: str, token: str, run_id: str) -> int:
    """Listen on a loopback port inside the container; each debugger connection becomes a WebSocket to the api."""
    server = socket.socket()
    server.bind(("127.0.0.1", 0))
    server.listen(16)

    def accept() -> None:
        while True:
            inbound, _ = server.accept()
            _daemon(lambda s=inbound: _bridge_container(base_url, token, run_id, s))

    _daemon(accept)
    return server.getsockname()[1]


def _bridge_container(base_url: str, token: str, run_id: str, inbound: socket.socket) -> None:
    remote = WebSocket.connect(f"{ws_base(base_url)}/v1/debug/{run_id}/remote", token)

    def up() -> None:
        try:
            while data := inbound.recv(65536):
                remote.send(data)
        except OSError:
            pass
        remote.close()

    def down() -> None:
        while (message := _quiet_recv(remote)) is not None:
            inbound.sendall(message[1])
        _shutdown(inbound)

    _daemon(up)
    _daemon(down)


class _Renumber:
    """Rewrites the process ids in `pydevdSystemInfo` answers to unique numbers, keeping parent links."""

    def __init__(self) -> None:
        self.next, self.known, self.lock = FIRST_REMOTE_PID, {}, threading.Lock()

    def frames(self, buffer: bytearray) -> bytes:
        out = b""
        while (end := buffer.find(b"\r\n\r\n")) >= 0:
            length = int(bytes(buffer[:end]).split(b":")[1])
            if len(buffer) < end + 4 + length:
                break
            body = bytes(buffer[end + 4:end + 4 + length])
            del buffer[:end + 4 + length]
            message = json.loads(body)
            if message.get("type") == "response" and message.get("command") == "pydevdSystemInfo":
                process = message["body"]["process"]
                with self.lock:
                    self.next += 1
                    self.known[process["pid"]] = self.next
                    process["ppid"] = self.known.get(process.get("ppid"), process.get("ppid"))
                    process["pid"] = self.next
                body = json.dumps(message).encode()
            out += b"Content-Length: %d\r\n\r\n" % len(body) + body
        return out


def _quiet_recv(ws: WebSocket):
    try:
        return ws.recv()
    except (OSError, ConnectionError):
        return None


def _shutdown(sock: socket.socket) -> None:
    try:
        sock.shutdown(socket.SHUT_RDWR)
    except OSError:
        pass


def _daemon(target: Callable[[], None]) -> None:
    thread = threading.Thread(target=target, daemon=True)
    thread.is_pydev_daemon_thread = True
    thread.start()


if __name__ == "__main__":
    import os
    import time
    print(serve_container(os.environ["MLP_API_HTTP"], os.environ["MLP_JOB_TOKEN"], os.environ["MLP_RUN_ID"]), flush=True)
    while True:
        time.sleep(3600)
