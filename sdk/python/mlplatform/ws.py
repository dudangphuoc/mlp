"""Minimal WebSocket client (RFC 6455, binary messages) on the standard library, for the debug relay."""
from __future__ import annotations

import base64
import os
import socket
import ssl
import struct
import urllib.parse
from typing import Optional, Tuple


class WebSocket:
    """One connection; `recv` returns (opcode, payload) and answers pings itself."""

    def __init__(self, sock: socket.socket) -> None:
        self.sock = sock
        self._buffer = b""

    @classmethod
    def connect(cls, url: str, token: str, timeout: float = 20.0) -> "WebSocket":
        parts = urllib.parse.urlsplit(url)
        secure = parts.scheme in ("https", "wss")
        port = parts.port or (443 if secure else 80)
        raw = socket.create_connection((parts.hostname, port), timeout=timeout)
        sock = ssl.create_default_context().wrap_socket(raw, server_hostname=parts.hostname) if secure else raw
        key = base64.b64encode(os.urandom(16)).decode()
        path = parts.path + (f"?{parts.query}" if parts.query else "")
        sock.sendall((f"GET {path} HTTP/1.1\r\nHost: {parts.netloc}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                      f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nAuthorization: Bearer {token}\r\n"
                      f"User-Agent: mlplatform-python\r\n\r\n").encode())
        response = b""
        while b"\r\n\r\n" not in response:
            chunk = sock.recv(4096)
            if not chunk:
                raise ConnectionError("debug relay closed during the handshake")
            response += chunk
        head, _, rest = response.partition(b"\r\n\r\n")
        status = head.split(b"\r\n", 1)[0]
        if b" 101 " not in status + b" ":
            raise ConnectionError(f"debug relay refused: {status.decode(errors='replace')}")
        sock.settimeout(None)
        connection = cls(sock)
        connection._buffer = rest
        return connection

    def send(self, payload: bytes, opcode: int = 2) -> None:
        mask = os.urandom(4)
        size = len(payload)
        if size < 126:
            header = struct.pack("!BB", 0x80 | opcode, 0x80 | size)
        elif size < 1 << 16:
            header = struct.pack("!BBH", 0x80 | opcode, 0x80 | 126, size)
        else:
            header = struct.pack("!BBQ", 0x80 | opcode, 0x80 | 127, size)
        self.sock.sendall(header + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(payload)))

    def recv(self) -> Optional[Tuple[int, bytes]]:
        """Next data message, or None once the connection is closed."""
        while True:
            first, second = self._read(2)
            opcode, size = first & 0x0F, second & 0x7F
            if size == 126:
                size = struct.unpack("!H", self._read(2))[0]
            elif size == 127:
                size = struct.unpack("!Q", self._read(8))[0]
            mask = self._read(4) if second & 0x80 else b""
            payload = self._read(size)
            if mask:
                payload = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
            if opcode == 8:
                return None
            if opcode == 9:
                self.send(payload, 10)
            elif opcode in (0, 1, 2):
                return opcode, payload

    def close(self) -> None:
        try:
            self.send(b"", 8)
            self.sock.close()
        except OSError:
            pass

    def _read(self, n: int) -> bytes:
        while len(self._buffer) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("closed")
            self._buffer += chunk
        data, self._buffer = self._buffer[:n], self._buffer[n:]
        return data
