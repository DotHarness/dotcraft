"""Local Hub bootstrap: discover or start the Hub and ensure a workspace AppServer.

Mirrors the TypeScript/.NET Hub Bootstrap profile. See Unified SDK Specification §3.2.
"""

from __future__ import annotations

import asyncio
import json
import os
import subprocess
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import urlparse


class HubError(Exception):
    """Hub discovery, startup, or request failure."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(f"[{code}] {message}")
        self.code = code
        self.message = message


@dataclass
class HubLockInfo:
    pid: int
    api_base_url: str
    token: str
    started_at: str | None = None
    version: str | None = None
    binary_path: str | None = None

    @classmethod
    def from_dict(cls, data: dict) -> "HubLockInfo":
        return cls(
            pid=int(data.get("pid", 0)),
            api_base_url=data.get("apiBaseUrl", ""),
            token=data.get("token", ""),
            started_at=data.get("startedAt"),
            version=data.get("version"),
            binary_path=data.get("binaryPath"),
        )


@dataclass
class EnsuredAppServer:
    ws_url: str
    token: str | None = None
    raw: dict | None = None


def hub_lock_path() -> Path:
    """Resolve the Hub lock file path (``~/.craft/hub/hub.lock``)."""
    return Path.home() / ".craft" / "hub" / "hub.lock"


def is_loopback_host(host: str) -> bool:
    return host in ("127.0.0.1", "localhost", "::1")


def is_process_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes

        process_query_limited_information = 0x1000
        handle = ctypes.windll.kernel32.OpenProcess(process_query_limited_information, False, pid)
        if handle:
            ctypes.windll.kernel32.CloseHandle(handle)
            return True
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


class HubClient:
    """Discovers, validates, starts the local Hub, and ensures workspace AppServers."""

    def __init__(self, dotcraft_bin: str | None = None, lock_path: str | None = None) -> None:
        self._dotcraft_bin = dotcraft_bin
        self._lock_path = Path(lock_path) if lock_path else hub_lock_path()

    def read_lock(self) -> HubLockInfo | None:
        """Read and parse the Hub lock file, or None if absent/unparseable."""
        try:
            data = json.loads(self._lock_path.read_text(encoding="utf-8"))
        except (FileNotFoundError, ValueError, OSError):
            return None
        return HubLockInfo.from_dict(data)

    def validate_lock(self, lock: HubLockInfo | None) -> bool:
        """A lock is trusted only when the process is live and the URL is loopback HTTP."""
        if lock is None or not lock.api_base_url:
            return False
        if not is_process_alive(lock.pid):
            return False
        parsed = urlparse(lock.api_base_url)
        if parsed.scheme != "http" or parsed.port is None or not is_loopback_host(parsed.hostname or ""):
            return False
        return True

    async def try_get_live_hub(self) -> HubLockInfo | None:
        """Return a validated, status-probed live Hub lock, or None."""
        lock = self.read_lock()
        if not self.validate_lock(lock):
            return None
        try:
            await self._get(lock, "/v1/status")
        except HubError:
            return None
        return lock

    async def ensure_app_server(
        self,
        workspace_path: str,
        client_name: str = "dotcraft-python",
        client_version: str = "0.0.0",
        start_if_missing: bool = True,
        startup_timeout: float = 30.0,
    ) -> EnsuredAppServer:
        """Discover or start the Hub, then ensure a workspace AppServer WebSocket endpoint."""
        if not workspace_path:
            raise HubError("invalidWorkspace", "workspace_path is required.")

        lock = await self.try_get_live_hub()
        if lock is None:
            if not start_if_missing:
                raise HubError("hubUnavailable", "Hub is not running and start_if_missing is False.")
            self._start_hub()
            lock = await self._wait_for_hub(startup_timeout)

        result = await self._post(
            lock,
            "/v1/appservers/ensure",
            {
                "workspacePath": workspace_path,
                "client": {"name": client_name, "version": client_version},
                "startIfMissing": True,
            },
        )
        endpoints = result.get("endpoints") if isinstance(result, dict) else None
        ws_url = endpoints.get("appServerWebSocket") if isinstance(endpoints, dict) else None
        if not ws_url:
            raise HubError("hubInvalidResponse", "Hub response did not include endpoints.appServerWebSocket.")
        token = result.get("token") if isinstance(result, dict) else None
        return EnsuredAppServer(ws_url=ws_url, token=token, raw=result)

    # ------------------------------------------------------------------
    # Internal
    # ------------------------------------------------------------------

    def _start_hub(self) -> None:
        args = self._hub_command()
        creationflags = 0
        if os.name == "nt":
            creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0) | getattr(subprocess, "DETACHED_PROCESS", 0)
        try:
            subprocess.Popen(
                args,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=creationflags,
                close_fds=True,
            )
        except OSError as e:
            raise HubError("hubUnavailable", f"Failed to start Hub: {e}") from e

    def _hub_command(self) -> list[str]:
        if self._dotcraft_bin and self._dotcraft_bin.endswith(".dll"):
            return ["dotnet", self._dotcraft_bin, "hub"]
        return [self._dotcraft_bin or "dotcraft", "hub"]

    async def _wait_for_hub(self, timeout: float) -> HubLockInfo:
        deadline = timeout
        interval = 0.2
        waited = 0.0
        while waited < deadline:
            lock = await self.try_get_live_hub()
            if lock is not None:
                return lock
            await asyncio.sleep(interval)
            waited += interval
        raise HubError("hubUnavailable", "Timed out waiting for the Hub to become ready.")

    async def _get(self, lock: HubLockInfo, path: str) -> Any:
        return await asyncio.to_thread(self._http, lock, "GET", path, None)

    async def _post(self, lock: HubLockInfo, path: str, body: dict) -> Any:
        return await asyncio.to_thread(self._http, lock, "POST", path, body)

    @staticmethod
    def _http(lock: HubLockInfo, method: str, path: str, body: dict | None) -> Any:
        url = lock.api_base_url.rstrip("/") + path
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(url, data=data, method=method)
        request.add_header("Authorization", f"Bearer {lock.token}")
        if body is not None:
            request.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(request, timeout=10) as response:
                raw = response.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            if e.code == 401:
                raise HubError("unauthorized", "Hub rejected bearer authorization.") from e
            raise HubError("hubRequestFailed", f"Hub request failed: {e.code}") from e
        except urllib.error.URLError as e:
            raise HubError("hubUnavailable", f"Hub request failed: {e.reason}") from e
        if not raw:
            return {}
        try:
            return json.loads(raw)
        except ValueError as e:
            raise HubError("hubInvalidResponse", "Hub returned invalid JSON.") from e
