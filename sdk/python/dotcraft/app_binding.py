"""App Binding helpers: handoff parsing, typed app-side RPC, and standard tool errors.

Mirrors the App Binding profile from the Unified SDK Specification §3.5 and the
TypeScript/.NET SDKs. App Binding is a protocol any DotCraft SDK can speak.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any
from urllib.parse import parse_qs, urlparse

# Standard App Binding tool error codes, shared across SDKs.
APP_BINDING_ERROR_CODES = {
    "offline": "AppBindingOffline",
    "expired": "AppBindingExpired",
    "revoked": "AppBindingRevoked",
    "scope_denied": "AppBindingScopeDenied",
    "tool_unavailable": "AppBindingToolUnavailable",
    "protocol_violation": "AppBindingProtocolViolation",
}


@dataclass
class AppBindingHandoff:
    """A parsed App Binding deep-link handoff."""

    scheme: str
    operation: str
    app_id: str
    request_id: str
    request_token: str
    app_server_url: str | None = None

    @classmethod
    def parse(
        cls,
        url: str,
        expected_scheme: str | None = None,
        expected_app_id: str | None = None,
    ) -> "AppBindingHandoff":
        """Parse a handoff URL such as ``app://dotcraft/connect?app=...&request=...&token=...&endpoint=...``."""
        parsed = urlparse(url)
        scheme = parsed.scheme
        if expected_scheme is not None and scheme != expected_scheme:
            raise ValueError(f"Unexpected handoff scheme '{scheme}', expected '{expected_scheme}'.")

        operation = parsed.path.lstrip("/")
        query = parse_qs(parsed.query)

        def first(*keys: str) -> str | None:
            for key in keys:
                values = query.get(key)
                if values:
                    return values[0]
            return None

        app_id = first("app", "appId") or ""
        if expected_app_id is not None and app_id != expected_app_id:
            raise ValueError(f"Unexpected handoff appId '{app_id}', expected '{expected_app_id}'.")

        return cls(
            scheme=scheme,
            operation=operation,
            app_id=app_id,
            request_id=first("request", "requestId") or "",
            request_token=first("token", "requestToken") or "",
            app_server_url=first("endpoint", "appServer"),
        )


# ---------------------------------------------------------------------------
# Typed result DTOs
# ---------------------------------------------------------------------------


@dataclass
class AppInfo:
    app_id: str
    display_name: str
    developer_name: str
    description: str
    plugin_id: str
    installed: bool
    enabled: bool
    catalog_visible: bool
    connection_state: str
    account_label: str | None = None
    raw: dict = field(default_factory=dict)

    @classmethod
    def from_wire(cls, d: dict) -> "AppInfo":
        return cls(
            app_id=d.get("appId", ""),
            display_name=d.get("displayName", ""),
            developer_name=d.get("developerName", ""),
            description=d.get("description", ""),
            plugin_id=d.get("pluginId", ""),
            installed=bool(d.get("installed", False)),
            enabled=bool(d.get("enabled", False)),
            catalog_visible=bool(d.get("catalogVisible", False)),
            connection_state=d.get("connectionState", ""),
            account_label=d.get("accountLabel"),
            raw=d,
        )


@dataclass
class AppConnectionStatus:
    app_id: str
    state: str
    connected_at: str | None = None
    expires_at: str | None = None
    account_label: str | None = None
    diagnostic: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppConnectionStatus":
        return cls(
            app_id=d.get("appId", ""),
            state=d.get("state", ""),
            connected_at=d.get("connectedAt"),
            expires_at=d.get("expiresAt"),
            account_label=d.get("accountLabel"),
            diagnostic=d.get("diagnostic"),
        )


@dataclass
class AppConnectionStartResult:
    connection_request_id: str
    request_token: str
    expires_at: str
    handoff: dict = field(default_factory=dict)

    @classmethod
    def from_wire(cls, d: dict) -> "AppConnectionStartResult":
        return cls(d.get("connectionRequestId", ""), d.get("requestToken", ""), d.get("expiresAt", ""), d.get("handoff", {}))


@dataclass
class AppConnectionConnectResult:
    principal: dict
    credential: str

    @classmethod
    def from_wire(cls, d: dict) -> "AppConnectionConnectResult":
        return cls(dict(d.get("principal", {})), d.get("credential", ""))


@dataclass
class AppSurface:
    app_id: str
    surface_id: str
    endpoint: str
    bearer: str
    expires_at: str

    @classmethod
    def from_wire(cls, d: dict) -> "AppSurface":
        return cls(
            app_id=d.get("appId", ""),
            surface_id=d.get("surfaceId", ""),
            endpoint=d.get("endpoint", ""),
            bearer=d.get("bearer", ""),
            expires_at=d.get("expiresAt", ""),
        )


@dataclass
class ThreadAppBinding:
    binding_id: str
    thread_id: str
    app_id: str
    state: str
    authority_revision: int = 0
    approved_capability_revision: int = 0
    candidate_capability_revision: int | None = None
    display_name: str | None = None
    approved_tools: list[dict] = field(default_factory=list)
    pending_changes: list[dict] = field(default_factory=list)
    failure_reason: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "ThreadAppBinding":
        return cls(
            binding_id=d.get("bindingId", ""),
            thread_id=d.get("threadId", ""),
            app_id=d.get("appId", ""),
            state=d.get("state", ""),
            authority_revision=int(d.get("authorityRevision", 0)),
            approved_capability_revision=int(d.get("approvedCapabilityRevision", 0)),
            candidate_capability_revision=d.get("candidateCapabilityRevision"),
            display_name=d.get("displayName"),
            approved_tools=list(d.get("approvedTools", [])),
            pending_changes=list(d.get("pendingChanges", [])),
            failure_reason=d.get("failureReason"),
        )


@dataclass
class AppBindingRequestInfo:
    binding_request_id: str
    binding_id: str
    thread_id: str
    app_id: str
    state: str = ""
    expires_at: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingRequestInfo":
        return cls(
            binding_request_id=d.get("bindingRequestId", ""),
            binding_id=d.get("bindingId", ""),
            thread_id=d.get("threadId", ""),
            app_id=d.get("appId", ""),
            state=d.get("state", ""),
            expires_at=d.get("expiresAt"),
        )


@dataclass
class AppBindingRequestCreateResult:
    binding_request_id: str
    binding_id: str
    state: str = ""
    expires_at: str = ""
    handoff: dict = field(default_factory=dict)

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingRequestCreateResult":
        return cls(
            binding_request_id=d.get("bindingRequestId", ""),
            binding_id=d.get("bindingId", ""),
            state=d.get("state", ""),
            expires_at=d.get("expiresAt", ""),
            handoff=d.get("handoff", {}),
        )


def app_binding_tool_error(code: str, message: str, structured_content: Any = None) -> dict:
    """Build a standard failed dynamic-tool result for an App Binding error."""
    result: dict = {
        "success": False,
        "errorCode": code,
        "errorMessage": message,
        "contentItems": [{"type": "text", "text": message}],
    }
    if structured_content is not None:
        result["structuredContent"] = structured_content
    return result


def _compact(params: dict) -> dict:
    """Drop None-valued params so optional fields are omitted from the wire payload."""
    return {k: v for k, v in params.items() if v is not None}


class AppBindingManager:
    """Typed app-side and application-side App Binding RPC helpers over a connected client."""

    def __init__(self, client) -> None:
        self._client = client

    # Discovery
    async def list_apps(
        self,
        thread_id: str | None = None,
        include_disabled: bool = True,
        include_catalog: bool = True,
        force_refresh: bool = False,
    ) -> list[AppInfo]:
        result = await self._client.request("app/list", _compact({
            "threadId": thread_id,
            "includeDisabled": include_disabled,
            "includeCatalog": include_catalog,
            "forceRefresh": force_refresh,
        }))
        apps = result.get("apps", []) if isinstance(result, dict) else []
        return [AppInfo.from_wire(a) for a in apps if isinstance(a, dict)]

    async def view_app(self, app_id: str, thread_id: str | None = None) -> AppInfo:
        result = await self._client.request("app/view", _compact({"appId": app_id, "threadId": thread_id}))
        app = result.get("app") if isinstance(result, dict) else None
        if not isinstance(app, dict):
            raise ValueError(f"App '{app_id}' was not returned by app/view.")
        return AppInfo.from_wire(app)

    # Connection
    async def start_connection(self, app_id: str, handoff_mode: str | None = None, return_to: str | None = None) -> AppConnectionStartResult:
        result = await self._client.request("app/connection/start", _compact({"appId": app_id, "handoffMode": handoff_mode, "returnTo": return_to}))
        return AppConnectionStartResult.from_wire(result)

    async def complete_connection(
        self,
        connection_request_id: str,
        request_token: str,
        account_label: str | None = None,
    ) -> AppConnectionConnectResult:
        result = await self._client.request("app/connection/connect", _compact({
            "connectionRequestId": connection_request_id,
            "requestToken": request_token,
            "accountLabel": account_label,
        }))
        return AppConnectionConnectResult.from_wire(result)

    async def connection_status(self, app_id: str) -> AppConnectionStatus:
        result = await self._client.request("app/connection/status", {"appId": app_id})
        return AppConnectionStatus.from_wire(result)

    async def revoke_connection(self, app_id: str, reason: str | None = None) -> AppConnectionStatus:
        result = await self._client.request("app/connection/revoke", _compact({"appId": app_id, "reason": reason}))
        return AppConnectionStatus.from_wire(result)

    async def publish_surface(self, surface_id: str, endpoint: str, bearer: str) -> AppSurface:
        result = await self._client.request("app/surface/publish", {
            "surfaceId": surface_id,
            "endpoint": endpoint,
            "bearer": bearer,
        })
        return AppSurface.from_wire(result)

    async def resolve_surface(self, app_id: str, surface_id: str) -> AppSurface:
        result = await self._client.request("app/surface/resolve", {
            "appId": app_id,
            "surfaceId": surface_id,
        })
        return AppSurface.from_wire(result)

    # Binding
    async def enable(self, thread_id: str, app_id: str) -> AppBindingRequestCreateResult:
        result = await self._client.request("thread/appBindings/enable", {
            "threadId": thread_id,
            "appId": app_id,
        })
        return AppBindingRequestCreateResult.from_wire(result)

    async def get_binding_request(self, app_id: str, binding_request_id: str, request_token: str) -> AppBindingRequestInfo:
        result = await self._client.request("app/binding/request/get", {
            "bindingRequestId": binding_request_id,
            "requestToken": request_token,
        })
        return AppBindingRequestInfo.from_wire(result)

    async def authenticate(self, app_id: str, credential: str) -> dict:
        return await self._client.request("app/connection/authenticate", {"appId": app_id, "credential": credential})

    async def refresh_credential(self) -> dict:
        return await self._client.request("app/connection/refresh", {})

    async def activate(self, binding_request_id: str, endpoint: str, bearer: str, bearer_expires_at: str | None = None) -> dict:
        return await self._client.request("app/binding/activate", _compact({"bindingRequestId": binding_request_id, "endpoint": endpoint, "bearer": bearer, "bearerExpiresAt": bearer_expires_at}))

    async def rebind(self, binding_id: str, authority_revision: int, endpoint: str, bearer: str, bearer_expires_at: str | None = None) -> dict:
        return await self._client.request("app/binding/rebind", _compact({"bindingId": binding_id, "authorityRevision": authority_revision, "endpoint": endpoint, "bearer": bearer, "bearerExpiresAt": bearer_expires_at}))

    async def confirm_capabilities(self, thread_id: str, binding_id: str, candidate_revision: int, decision: str) -> dict:
        return await self._client.request("thread/appBindings/confirmCapabilities", {"threadId": thread_id, "bindingId": binding_id, "candidateRevision": candidate_revision, "decision": decision})

    # Thread bindings
    async def list_thread_bindings(self, thread_id: str, include_revoked: bool = False) -> list[ThreadAppBinding]:
        result = await self._client.request("thread/appBindings/list", {"threadId": thread_id, "includeRevoked": include_revoked})
        bindings = result.get("bindings", []) if isinstance(result, dict) else []
        return [ThreadAppBinding.from_wire(b) for b in bindings if isinstance(b, dict)]

    async def revoke_thread_binding(self, thread_id: str, binding_id: str, reason: str | None = None) -> dict:
        return await self._client.request("thread/appBindings/revoke", _compact({"threadId": thread_id, "bindingId": binding_id, "reason": reason}))

    async def refresh_thread_bindings(self, thread_id: str, binding_id: str | None = None) -> Any:
        return await self._client.request("thread/appBindings/list", {"threadId": thread_id})
