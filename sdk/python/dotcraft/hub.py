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
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, AsyncIterator, Literal
from urllib.parse import quote, urlparse


HubBinaryMatchPolicy = Literal["ignore", "restartIfMismatch", "errorIfMismatch"]


class HubError(Exception):
    """Hub discovery, startup, or request failure."""

    def __init__(self, code: str, message: str, details: Any = None) -> None:
        super().__init__(f"[{code}] {message}")
        self.code = code
        self.message = message
        self.details = details


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
class HubRuntimeToolsRequest:
    ripgrep_path: str | None = None
    node_bin: str | None = None
    node_run_as_node: bool | None = None
    modules_dir: str | None = None
    built_in_plugin_roots: str | None = None
    default_plugin_registry_url: str | None = None

    def to_wire(self) -> dict[str, Any]:
        values = {
            "ripgrepPath": self.ripgrep_path,
            "nodeBin": self.node_bin,
            "nodeRunAsNode": self.node_run_as_node,
            "modulesDir": self.modules_dir,
            "builtInPluginRoots": self.built_in_plugin_roots,
            "defaultPluginRegistryUrl": self.default_plugin_registry_url,
        }
        return {key: value for key, value in values.items() if value is not None}


@dataclass
class HubAppServerResponse:
    workspace_path: str
    canonical_workspace_path: str
    state: str
    endpoints: dict[str, str]
    service_status: dict[str, Any]
    started_by_hub: bool
    pid: int | None = None
    server_version: str | None = None
    exit_code: int | None = None
    last_error: str | None = None
    recent_stderr: str | None = None
    token: str | None = None
    raw: dict[str, Any] = field(default_factory=dict)

    @property
    def ws_url(self) -> str:
        return self.endpoints.get("appServerWebSocket", "")

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "HubAppServerResponse":
        endpoints = data.get("endpoints")
        service_status = data.get("serviceStatus")
        return cls(
            workspace_path=str(data.get("workspacePath", "")),
            canonical_workspace_path=str(data.get("canonicalWorkspacePath", "")),
            state=str(data.get("state", "")),
            endpoints=dict(endpoints) if isinstance(endpoints, dict) else {},
            service_status=dict(service_status) if isinstance(service_status, dict) else {},
            started_by_hub=bool(data.get("startedByHub", False)),
            pid=data.get("pid") if isinstance(data.get("pid"), int) else None,
            server_version=data.get("serverVersion"),
            exit_code=data.get("exitCode") if isinstance(data.get("exitCode"), int) else None,
            last_error=data.get("lastError"),
            recent_stderr=data.get("recentStderr"),
            token=data.get("token"),
            raw=data,
        )


EnsuredAppServer = HubAppServerResponse


@dataclass
class HubStatusResponse:
    hub_version: str
    pid: int
    started_at: str
    state_path: str
    api_base_url: str
    binary_path: str | None
    capabilities: dict[str, Any]
    raw: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "HubStatusResponse":
        return cls(
            hub_version=str(data.get("hubVersion", "")),
            pid=int(data.get("pid", 0)),
            started_at=str(data.get("startedAt", "")),
            state_path=str(data.get("statePath", "")),
            api_base_url=str(data.get("apiBaseUrl", "")),
            binary_path=data.get("binaryPath"),
            capabilities=dict(data.get("capabilities", {})),
            raw=data,
        )


@dataclass
class HubEvent:
    kind: str
    at: str
    workspace_path: str | None = None
    data: Any = None


def hub_lock_path(home_dir: str | Path | None = None) -> Path:
    """Resolve the Hub lock file path (``~/.craft/hub/hub.lock``)."""
    root = Path(home_dir) if home_dir is not None else Path.home()
    return root / ".craft" / "hub" / "hub.lock"


def default_chat_workspace_path(home_dir: str | Path | None = None) -> Path:
    """Resolve the default Chat workspace path (``~/.craft/workspaces/chats``)."""
    root = Path(home_dir) if home_dir is not None else Path.home()
    return root / ".craft" / "workspaces" / "chats"


def ensure_default_chat_workspace(home_dir: str | Path | None = None) -> Path:
    """Create the default Chat workspace skeleton without overwriting config."""
    workspace = default_chat_workspace_path(home_dir)
    craft = workspace / ".craft"
    (craft / "memory").mkdir(parents=True, exist_ok=True)
    (craft / "skills").mkdir(parents=True, exist_ok=True)
    (craft / "security").mkdir(parents=True, exist_ok=True)
    config = craft / "config.json"
    if not config.exists():
        config.write_text("{}\n", encoding="utf-8")
    return workspace


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

    def __init__(
        self,
        executable: str | None = None,
        expected_executable: str | None = None,
        binary_match_policy: HubBinaryMatchPolicy = "ignore",
        lock_path: str | None = None,
        home_dir: str | Path | None = None,
        startup_timeout: float = 15.0,
        shutdown_timeout: float = 5.0,
    ) -> None:
        self._executable = executable
        self._expected_executable = expected_executable
        self._binary_match_policy = binary_match_policy if expected_executable else "ignore"
        self._home_dir = Path(home_dir) if home_dir is not None else None
        self._lock_path = Path(lock_path) if lock_path else hub_lock_path(self._home_dir)
        self._startup_timeout = startup_timeout
        self._shutdown_timeout = shutdown_timeout

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
        if lock is None or not self.validate_lock(lock):
            return None
        try:
            status = await self._get(lock, "/v1/status")
        except HubError:
            return None
        if isinstance(status, dict) and isinstance(status.get("binaryPath"), str):
            lock.binary_path = status["binaryPath"]
        return lock

    async def ensure_hub(self) -> HubLockInfo:
        """Return a live Hub, applying the configured executable match policy."""
        lock = await self.try_get_live_hub()
        if lock is not None:
            mismatch = self._binary_mismatch(lock)
            if mismatch is None or self._binary_match_policy == "ignore":
                return lock
            if self._binary_match_policy == "errorIfMismatch":
                raise HubError("hubBinaryMismatch", "Hub is running from a different executable.", mismatch)
            await self._shutdown_mismatched_hub(lock, mismatch)
        self._start_hub()
        return await self._wait_for_hub(self._startup_timeout)

    async def ensure_app_server(
        self,
        workspace_path: str,
        client_name: str = "dotcraft-python",
        client_version: str = "0.0.0",
        start_if_missing: bool = True,
        startup_timeout: float = 15.0,
        runtime_tools: HubRuntimeToolsRequest | None = None,
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
        else:
            mismatch = self._binary_mismatch(lock)
            if mismatch is not None and self._binary_match_policy == "errorIfMismatch":
                raise HubError("hubBinaryMismatch", "Hub is running from a different executable.", mismatch)
            if mismatch is not None and self._binary_match_policy == "restartIfMismatch":
                await self._shutdown_mismatched_hub(lock, mismatch)
                self._start_hub()
                lock = await self._wait_for_hub(startup_timeout)

        result = await self._post(
            lock,
            "/v1/appservers/ensure",
            {
                "workspacePath": workspace_path,
                "client": {"name": client_name, "version": client_version},
                "startIfMissing": start_if_missing,
                "runtimeTools": runtime_tools.to_wire() if runtime_tools else None,
            },
        )
        endpoints = result.get("endpoints") if isinstance(result, dict) else None
        ws_url = endpoints.get("appServerWebSocket") if isinstance(endpoints, dict) else None
        if not ws_url:
            raise HubError("hubInvalidResponse", "Hub response did not include endpoints.appServerWebSocket.")
        token = result.get("token") if isinstance(result, dict) else None
        return HubAppServerResponse.from_dict(result)

    async def ensure_default_chat_app_server(
        self,
        client_name: str = "dotcraft-python",
        client_version: str = "0.0.0",
        start_if_missing: bool = True,
        startup_timeout: float = 15.0,
    ) -> EnsuredAppServer:
        """Ensure the default Chat workspace AppServer using the standard Hub endpoint."""
        workspace_path = ensure_default_chat_workspace(self._home_dir)
        return await self.ensure_app_server(
            str(workspace_path),
            client_name=client_name,
            client_version=client_version,
            start_if_missing=start_if_missing,
            startup_timeout=startup_timeout,
        )

    async def get_app_server_by_workspace(self, workspace_path: str) -> HubAppServerResponse | None:
        lock = await self.try_get_live_hub()
        if lock is None:
            return None
        try:
            result = await self._get(lock, f"/v1/appservers/by-workspace?path={quote(workspace_path)}")
        except HubError as error:
            if error.code == "notFound":
                return None
            raise
        return HubAppServerResponse.from_dict(result)

    async def restart_app_server(
        self, workspace_path: str, runtime_tools: HubRuntimeToolsRequest | None = None
    ) -> HubAppServerResponse:
        lock = await self.ensure_hub()
        result = await self._post(lock, "/v1/appservers/restart", {
            "workspacePath": workspace_path,
            "runtimeTools": runtime_tools.to_wire() if runtime_tools else None,
        })
        return HubAppServerResponse.from_dict(result)

    async def stop_app_server(self, workspace_path: str) -> HubAppServerResponse:
        lock = await self.ensure_hub()
        result = await self._post(lock, "/v1/appservers/stop", {"workspacePath": workspace_path})
        return HubAppServerResponse.from_dict(result)

    async def list_app_servers(self) -> list[HubAppServerResponse]:
        lock = await self.ensure_hub()
        result = await self._get(lock, "/v1/appservers")
        return [HubAppServerResponse.from_dict(item) for item in result if isinstance(item, dict)]

    async def get_status(self) -> HubStatusResponse:
        lock = await self.ensure_hub()
        result = await self._get(lock, "/v1/status")
        return HubStatusResponse.from_dict(result)

    async def shutdown_hub(self) -> None:
        lock = await self.try_get_live_hub()
        if lock is not None:
            await self._post(lock, "/v1/shutdown", {})

    async def subscribe_events(self) -> AsyncIterator[HubEvent]:
        lock = await self.ensure_hub()
        iterator = self._event_iterator(lock)
        while True:
            item = await asyncio.to_thread(_next_or_none, iterator)
            if item is None:
                break
            yield item

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
        if self._executable and self._executable.endswith(".dll"):
            return ["dotnet", self._executable, "hub"]
        return [self._executable or "dotcraft", "hub"]

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
            try:
                error_body = json.loads(e.read().decode("utf-8"))
            except (ValueError, OSError):
                error_body = {}
            error = error_body.get("error", error_body) if isinstance(error_body, dict) else {}
            code = error.get("code") if isinstance(error, dict) else None
            message = error.get("message") if isinstance(error, dict) else None
            details = error.get("details") if isinstance(error, dict) else None
            if e.code == 404 and not code:
                code = "notFound"
            if e.code == 401 and not code:
                code = "unauthorized"
            raise HubError(code or "hubRequestFailed", message or f"Hub request failed: {e.code}", details) from e
        except urllib.error.URLError as e:
            raise HubError("hubUnavailable", f"Hub request failed: {e.reason}") from e
        if not raw:
            return {}
        try:
            return json.loads(raw)
        except ValueError as e:
            raise HubError("hubInvalidResponse", "Hub returned invalid JSON.") from e

    def _binary_mismatch(self, lock: HubLockInfo) -> dict[str, str | None] | None:
        if not self._expected_executable:
            return None
        expected = str(Path(self._expected_executable).resolve())
        actual = str(Path(lock.binary_path).resolve()) if lock.binary_path else None
        normalize = os.path.normcase
        if actual is not None and normalize(actual) == normalize(expected):
            return None
        return {"expectedExecutable": expected, "actualExecutable": actual}

    async def _shutdown_mismatched_hub(self, lock: HubLockInfo, details: dict[str, Any]) -> None:
        try:
            await self._post(lock, "/v1/shutdown", {})
        except HubError as error:
            raise HubError(
                "hubMismatchShutdownFailed",
                "Hub uses a different executable and could not be stopped.",
                details,
            ) from error
        waited = 0.0
        while waited < self._shutdown_timeout:
            if not is_process_alive(lock.pid):
                return
            await asyncio.sleep(0.2)
            waited += 0.2
        raise HubError(
            "hubMismatchShutdownTimeout",
            "Hub uses a different executable and did not stop after shutdown.",
            details,
        )

    @staticmethod
    def _event_iterator(lock: HubLockInfo):
        request = urllib.request.Request(lock.api_base_url.rstrip("/") + "/v1/events", method="GET")
        request.add_header("Authorization", f"Bearer {lock.token}")
        with urllib.request.urlopen(request, timeout=None) as response:
            for raw_line in response:
                line = raw_line.decode("utf-8").strip()
                if not line.startswith("data:"):
                    continue
                try:
                    data = json.loads(line[len("data:"):].strip())
                except ValueError:
                    continue
                if isinstance(data, dict):
                    yield HubEvent(
                        kind=str(data.get("kind", "")),
                        at=str(data.get("at", "")),
                        workspace_path=data.get("workspacePath"),
                        data=data.get("data"),
                    )


def _next_or_none(iterator):
    try:
        return next(iterator)
    except StopIteration:
        return None
