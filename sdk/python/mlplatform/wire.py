"""Just enough protobuf encoding and gRPC-Web (HTTP/1.1) to talk to the platform without dependencies."""
from __future__ import annotations

import struct
import urllib.error
import urllib.parse
import urllib.request
from typing import Dict, Iterable, List, Optional, Tuple, Union

from .errors import MlpError

Fields = Dict[int, List[Union[int, bytes]]]

STATUS_NAMES = {
    "1": "CANCELLED", "2": "UNKNOWN", "3": "INVALID_ARGUMENT", "4": "DEADLINE_EXCEEDED", "5": "NOT_FOUND", "6": "ALREADY_EXISTS",
    "7": "PERMISSION_DENIED", "8": "RESOURCE_EXHAUSTED", "9": "FAILED_PRECONDITION", "10": "ABORTED", "11": "OUT_OF_RANGE",
    "12": "UNIMPLEMENTED", "13": "INTERNAL", "14": "UNAVAILABLE", "15": "DATA_LOSS", "16": "UNAUTHENTICATED",
}


def varint(n: int) -> bytes:
    """Unsigned LEB128; negative int64 values use their two's complement."""
    n &= (1 << 64) - 1
    out = bytearray()
    while True:
        byte = n & 0x7F
        n >>= 7
        out.append(byte | (0x80 if n else 0))
        if not n:
            return bytes(out)


def string(number: int, value: Optional[str]) -> bytes:
    return message(number, value.encode("utf-8")) if value else b""


def message(number: int, body: bytes) -> bytes:
    return varint(number << 3 | 2) + varint(len(body)) + body


def integer(number: int, value: int) -> bytes:
    return varint(number << 3) + varint(value) if value else b""


def boolean(number: int, value: bool) -> bytes:
    return integer(number, 1 if value else 0)


def double(number: int, value: float) -> bytes:
    return varint(number << 3 | 1) + struct.pack("<d", value)


def strings(number: int, values: Iterable[str]) -> bytes:
    return b"".join(message(number, v.encode("utf-8")) for v in values)


def decode(data: bytes) -> Fields:
    """Field number → raw values (int for varints and fixed64, bytes for length-delimited)."""
    fields: Fields = {}
    pos = 0
    while pos < len(data):
        key, pos = _read_varint(data, pos)
        number, kind = key >> 3, key & 7
        if kind == 0:
            value, pos = _read_varint(data, pos)
        elif kind == 1:
            value, pos = int.from_bytes(data[pos:pos + 8], "little"), pos + 8
        elif kind == 2:
            size, pos = _read_varint(data, pos)
            value, pos = data[pos:pos + size], pos + size
        elif kind == 5:
            value, pos = int.from_bytes(data[pos:pos + 4], "little"), pos + 4
        else:
            raise MlpError("INTERNAL", f"unsupported wire type {kind}")
        fields.setdefault(number, []).append(value)
    return fields


def text(fields: Fields, number: int) -> str:
    raw = fields.get(number, [b""])[-1]
    return raw.decode("utf-8") if isinstance(raw, bytes) else ""


def number_of(fields: Fields, number: int) -> int:
    raw = fields.get(number, [0])[-1]
    return raw if isinstance(raw, int) else 0


def texts(fields: Fields, number: int) -> List[str]:
    return [v.decode("utf-8") for v in fields.get(number, []) if isinstance(v, bytes)]


def nested(fields: Fields, number: int) -> Fields:
    raw = fields.get(number, [b""])[-1]
    return decode(raw) if isinstance(raw, bytes) else {}


def _read_varint(data: bytes, pos: int) -> Tuple[int, int]:
    shift = result = 0
    while True:
        byte = data[pos]
        pos += 1
        result |= (byte & 0x7F) << shift
        if not byte & 0x80:
            return result, pos
        shift += 7


def call(base_url: str, key: str, method: str, body: bytes, timeout: float = 30.0) -> Fields:
    """Unary gRPC-Web call, e.g. method="mlplatform.v1.TrackingService/OpenCodeRun"."""
    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/{method}", data=b"\x00" + len(body).to_bytes(4, "big") + body, method="POST",
        headers={"content-type": "application/grpc-web+proto", "accept": "application/grpc-web+proto", "x-grpc-web": "1",
                 "authorization": f"Bearer {key}", "user-agent": "mlplatform-python"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            headers, payload = dict(response.headers.items()), response.read()
    except urllib.error.HTTPError as e:
        headers, payload = dict(e.headers.items()), e.read()
        if not _status(headers):
            raise MlpError("UNAVAILABLE", f"HTTP {e.code} from {base_url}") from None
    except urllib.error.URLError as e:
        raise MlpError("UNAVAILABLE", f"cannot reach {base_url}: {e.reason}") from None
    reply, trailers = _frames(payload)
    status = _status(trailers) or _status(headers)
    if status and status[0] != "0":
        raise MlpError(STATUS_NAMES.get(status[0], status[0]), urllib.parse.unquote(status[1]))
    return decode(reply)


def _frames(payload: bytes) -> Tuple[bytes, Dict[str, str]]:
    reply, trailers, pos = b"", {}, 0
    while pos + 5 <= len(payload):
        flag, size = payload[pos], int.from_bytes(payload[pos + 1:pos + 5], "big")
        chunk = payload[pos + 5:pos + 5 + size]
        pos += 5 + size
        if flag & 0x80:
            for line in chunk.decode("utf-8", "replace").split("\r\n"):
                name, _, value = line.partition(":")
                if name:
                    trailers[name.strip().lower()] = value.strip()
        else:
            reply = chunk
    return reply, trailers


def _status(headers: Dict[str, str]) -> Optional[Tuple[str, str]]:
    lower = {k.lower(): v for k, v in headers.items()}
    return (lower["grpc-status"], lower.get("grpc-message", "")) if "grpc-status" in lower else None
