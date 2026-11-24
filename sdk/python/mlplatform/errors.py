"""Errors raised by the SDK."""
from __future__ import annotations


class ConfigError(Exception):
    """The .env or environment settings are inconsistent."""


class MlpError(Exception):
    """The platform refused or failed a call; `code` is the gRPC status name."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(f"{code}: {message}")
        self.code = code
        self.message = message
