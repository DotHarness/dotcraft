"""Transport layer: StdioTransport and WebSocketTransport."""

from __future__ import annotations

import asyncio
import json
import sys
from abc import ABC, abstractmethod

import logging

logger = logging.getLogger(__name__)


class TransportError(Exception):
    """Raised when the transport encounters an unrecoverable error."""


class TransportClosed(TransportError):
    """Raised when the transport is closed and no more messages can be read."""


# ---------------------------------------------------------------------------
# Abstract base
# ---------------------------------------------------------------------------


class Transport(ABC):
    """Abstract transport that reads/writes JSON-RPC messages."""

    @abstractmethod
    async def read_message(self) -> dict:
        """Read the next JSON-RPC message. Raises TransportClosed when done."""

    @abstractmethod
    async def write_message(self, msg: dict) -> None:
        """Write a JSON-RPC message."""

    @abstractmethod
    async def close(self) -> None:
        """Close the transport."""


# ---------------------------------------------------------------------------
# Stdio transport
# ---------------------------------------------------------------------------


class StdioTransport(Transport):
    """
    Newline-delimited JSON (JSONL) transport over stdin/stdout.

    Used when DotCraft spawns the adapter as a subprocess. Each JSON-RPC
    message occupies exactly one line. Diagnostic output should go to stderr.

    Reads are performed in a thread executor to avoid the Windows asyncio
    ProactorEventLoop bug with anonymous pipe transport (WinError 6 /
    _ProactorReadPipeTransport). This approach works correctly on all
    platforms without any asyncio pipe registration.
    """

    def __init__(self) -> None:
        self._closed = False

    def _blocking_readline(self) -> bytes:
        """Blocking readline from stdin — runs in a thread executor."""
        return sys.stdin.buffer.readline()

    async def read_message(self) -> dict:
        """Read the next newline-terminated JSON message from stdin."""
        while True:
            if self._closed:
                raise TransportClosed("StdioTransport is closed")
            loop = asyncio.get_event_loop()
            line = await loop.run_in_executor(None, self._blocking_readline)
            if not line:
                self._closed = True
                raise TransportClosed("stdin closed")
            text = line.decode("utf-8").strip()
            if not text:
                continue  # skip blank lines
            return json.loads(text)

    async def write_message(self, msg: dict) -> None:
        """Write a JSON message as a single newline-terminated line to stdout."""
        if self._closed:
            raise TransportClosed("StdioTransport is closed")
        line = json.dumps(msg, ensure_ascii=False) + "\n"
        sys.stdout.buffer.write(line.encode("utf-8"))
        sys.stdout.buffer.flush()

    async def close(self) -> None:
        self._closed = True


# ---------------------------------------------------------------------------
# WebSocket transport
# ---------------------------------------------------------------------------

try:
    import websockets
    import websockets.client
    _WEBSOCKETS_AVAILABLE = True
except ImportError:
    _WEBSOCKETS_AVAILABLE = False


class WebSocketTransport(Transport):
    """
    One JSON-RPC message per WebSocket text frame.

    Used when the adapter connects to DotCraft's AppServer WebSocket endpoint
    independently (transport: "websocket" in config). Supports automatic
    reconnection with exponential backoff.
    """

    def __init__(
        self,
        url: str,
        token: str | None = None,
        reconnect: bool = True,
        reconnect_initial_delay: float = 1.0,
        reconnect_max_delay: float = 30.0,
    ) -> None:
        if not _WEBSOCKETS_AVAILABLE:
            raise ImportError("websockets package is required for WebSocketTransport. Install it with: pip install websockets")

        self._base_url = url
        self._token = token
        self._reconnect = reconnect
        self._reconnect_initial_delay = reconnect_initial_delay
        self._reconnect_max_delay = reconnect_max_delay

        self._ws = None
        self._closed = False
        self._connect_lock = asyncio.Lock()

    @property
    def _url(self) -> str:
        if self._token:
            sep = "&" if "?" in self._base_url else "?"
            return f"{self._base_url}{sep}token={self._token}"
        return self._base_url

    async def connect(self) -> None:
        """Establish the WebSocket connection."""
        async with self._connect_lock:
            self._ws = await websockets.client.connect(self._url)
        logger.debug("WebSocketTransport connected to %s", self._base_url)

    async def _reconnect_loop(self) -> None:
        delay = self._reconnect_initial_delay
        while not self._closed:
            try:
                logger.info("WebSocketTransport: reconnecting in %.1fs...", delay)
                await asyncio.sleep(delay)
                await self.connect()
                return
            except Exception as e:
                logger.warning("WebSocketTransport: reconnect failed: %s", e)
                delay = min(delay * 2, self._reconnect_max_delay)

    async def read_message(self) -> dict:
        """Read the next WebSocket text frame as a JSON-RPC message."""
        if self._closed:
            raise TransportClosed("WebSocketTransport is closed")
        while True:
            try:
                if self._ws is None:
                    await self.connect()
                raw = await self._ws.recv()
                return json.loads(raw)
            except Exception as e:
                if self._closed:
                    raise TransportClosed("WebSocketTransport closed during read") from e
                if self._reconnect:
                    logger.warning("WebSocketTransport read error: %s", e)
                    self._ws = None
                    await self._reconnect_loop()
                    # Caller (DotCraftClient) is responsible for re-initializing
                    # after reconnect; raise a special exception to signal this.
                    raise TransportClosed("WebSocket reconnected; re-initialize required") from e
                raise TransportError(f"WebSocket read error: {e}") from e

    async def write_message(self, msg: dict) -> None:
        """Send a JSON-RPC message as a single WebSocket text frame."""
        if self._closed:
            raise TransportClosed("WebSocketTransport is closed")
        if self._ws is None:
            raise TransportError("WebSocket not connected; call connect() first")
        text = json.dumps(msg, ensure_ascii=False)
        await self._ws.send(text)

    async def close(self) -> None:
        self._closed = True
        if self._ws is not None:
            await self._ws.close()
            self._ws = None
