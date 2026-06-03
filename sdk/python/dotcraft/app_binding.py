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
class AppScopeDescriptor:
    id: str
    display_name: str
    description: str
    risk: str
    default_selected: bool | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppScopeDescriptor":
        return cls(d.get("id", ""), d.get("displayName", ""), d.get("description", ""), d.get("risk", ""), d.get("defaultSelected"))


@dataclass
class AppToolCatalogEntry:
    name: str
    scope: str
    risk: str
    default_exposure: str
    description: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppToolCatalogEntry":
        return cls(d.get("name", ""), d.get("scope", ""), d.get("risk", ""), d.get("defaultExposure", ""), d.get("description"))


@dataclass
class AppInfo:
    app_id: str
    tool_namespace: str
    display_name: str
    developer_name: str
    description: str
    plugin_id: str
    installed: bool
    enabled: bool
    catalog_visible: bool
    connection_state: str
    scopes: list[AppScopeDescriptor] = field(default_factory=list)
    tool_catalog: list[AppToolCatalogEntry] = field(default_factory=list)
    account_label: str | None = None
    raw: dict = field(default_factory=dict)

    @classmethod
    def from_wire(cls, d: dict) -> "AppInfo":
        return cls(
            app_id=d.get("appId", ""),
            tool_namespace=d.get("toolNamespace", ""),
            display_name=d.get("displayName", ""),
            developer_name=d.get("developerName", ""),
            description=d.get("description", ""),
            plugin_id=d.get("pluginId", ""),
            installed=bool(d.get("installed", False)),
            enabled=bool(d.get("enabled", False)),
            catalog_visible=bool(d.get("catalogVisible", False)),
            connection_state=d.get("connectionState", ""),
            scopes=[AppScopeDescriptor.from_wire(s) for s in d.get("scopes", []) if isinstance(s, dict)],
            tool_catalog=[AppToolCatalogEntry.from_wire(t) for t in d.get("toolCatalog", []) if isinstance(t, dict)],
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
    app_id: str
    state: str
    expires_at: str
    handoff: dict = field(default_factory=dict)

    @classmethod
    def from_wire(cls, d: dict) -> "AppConnectionStartResult":
        return cls(d.get("connectionRequestId", ""), d.get("appId", ""), d.get("state", ""), d.get("expiresAt", ""), d.get("handoff", {}))


@dataclass
class ThreadAppBinding:
    binding_id: str
    thread_id: str
    app_id: str
    state: str
    granted_scopes: list[str] = field(default_factory=list)
    attached_tool_count: int = 0
    binding_request_id: str | None = None
    display_name: str | None = None
    tool_namespace: str | None = None
    connection_state: str | None = None
    expires_at: str | None = None
    last_changed_at: str | None = None
    approval_mode: str | None = None
    audit_ref: str | None = None
    diagnostic: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "ThreadAppBinding":
        return cls(
            binding_id=d.get("bindingId", ""),
            thread_id=d.get("threadId", ""),
            app_id=d.get("appId", ""),
            state=d.get("state", ""),
            granted_scopes=list(d.get("grantedScopes", [])),
            attached_tool_count=int(d.get("attachedToolCount", 0)),
            binding_request_id=d.get("bindingRequestId"),
            display_name=d.get("displayName"),
            tool_namespace=d.get("toolNamespace"),
            connection_state=d.get("connectionState"),
            expires_at=d.get("expiresAt"),
            last_changed_at=d.get("lastChangedAt"),
            approval_mode=d.get("approvalMode"),
            audit_ref=d.get("auditRef"),
            diagnostic=d.get("diagnostic"),
        )


@dataclass
class AppBindingRequestInfo:
    binding_request_id: str
    thread_id: str
    app_id: str
    requested_scopes: list[AppScopeDescriptor] = field(default_factory=list)
    requested_tools: list[AppToolCatalogEntry] = field(default_factory=list)
    source: str = ""
    thread_title: str | None = None
    reason: str | None = None
    expires_at: str | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingRequestInfo":
        return cls(
            binding_request_id=d.get("bindingRequestId", ""),
            thread_id=d.get("threadId", ""),
            app_id=d.get("appId", ""),
            requested_scopes=[AppScopeDescriptor.from_wire(s) for s in d.get("requestedScopes", []) if isinstance(s, dict)],
            requested_tools=[AppToolCatalogEntry.from_wire(t) for t in d.get("requestedTools", []) if isinstance(t, dict)],
            source=d.get("source", ""),
            thread_title=d.get("threadTitle"),
            reason=d.get("reason"),
            expires_at=d.get("expiresAt"),
        )


@dataclass
class AppBindingRequestCreateResult:
    binding_request_id: str
    thread_id: str
    app_id: str
    requested_scopes: list[str] = field(default_factory=list)
    state: str = ""
    token_expires_at: str = ""
    handoff: dict = field(default_factory=dict)
    confirmation: dict | None = None

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingRequestCreateResult":
        return cls(
            binding_request_id=d.get("bindingRequestId", ""),
            thread_id=d.get("threadId", ""),
            app_id=d.get("appId", ""),
            requested_scopes=list(d.get("requestedScopes", [])),
            state=d.get("state", ""),
            token_expires_at=d.get("tokenExpiresAt", ""),
            handoff=d.get("handoff", {}),
            confirmation=d.get("confirmation"),
        )


@dataclass
class AppBindingAcceptResult:
    binding: ThreadAppBinding

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingAcceptResult":
        return cls(ThreadAppBinding.from_wire(d.get("binding", {})))


@dataclass
class AppBindingAttachToolsResult:
    binding: ThreadAppBinding
    accepted_tool_count: int = 0
    rejected_tools: list = field(default_factory=list)
    warnings: list = field(default_factory=list)

    @classmethod
    def from_wire(cls, d: dict) -> "AppBindingAttachToolsResult":
        return cls(
            ThreadAppBinding.from_wire(d.get("binding", {})),
            int(d.get("acceptedToolCount", 0)),
            list(d.get("rejectedTools", [])),
            list(d.get("warnings", [])),
        )


def app_binding_tool_error(code: str, message: str, structured_result: Any = None) -> dict:
    """Build a standard failed dynamic-tool result for an App Binding error."""
    result: dict = {
        "success": False,
        "errorCode": code,
        "errorMessage": message,
        "contentItems": [{"type": "text", "text": message}],
    }
    if structured_result is not None:
        result["structuredResult"] = structured_result
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
        app_id: str,
        account_label: str | None = None,
        expires_at: str | None = None,
        connection_proof: dict | None = None,
    ) -> AppConnectionStatus:
        result = await self._client.request("app/connection/connect", _compact({
            "connectionRequestId": connection_request_id,
            "requestToken": request_token,
            "appId": app_id,
            "accountLabel": account_label,
            "expiresAt": expires_at,
            "connectionProof": connection_proof,
        }))
        return AppConnectionStatus.from_wire(result)

    async def connection_status(self, app_id: str) -> AppConnectionStatus:
        result = await self._client.request("app/connection/status", {"appId": app_id})
        return AppConnectionStatus.from_wire(result)

    async def revoke_connection(self, app_id: str, reason: str | None = None) -> AppConnectionStatus:
        result = await self._client.request("app/connection/revoke", _compact({"appId": app_id, "reason": reason}))
        return AppConnectionStatus.from_wire(result)

    # Binding
    async def create_binding_request(
        self,
        thread_id: str,
        app_id: str,
        requested_scopes: list[str],
        source: str = "sdk",
        requested_tools: list[str] | None = None,
        reason: str | None = None,
    ) -> AppBindingRequestCreateResult:
        result = await self._client.request("app/binding/request/create", _compact({
            "threadId": thread_id,
            "appId": app_id,
            "requestedScopes": requested_scopes,
            "requestedTools": requested_tools,
            "reason": reason,
            "source": source,
        }))
        return AppBindingRequestCreateResult.from_wire(result)

    async def get_binding_request(self, app_id: str, binding_request_id: str, request_token: str) -> AppBindingRequestInfo:
        result = await self._client.request("app/binding/request/get", {
            "appId": app_id,
            "bindingRequestId": binding_request_id,
            "requestToken": request_token,
        })
        return AppBindingRequestInfo.from_wire(result)

    async def cancel_binding_request(self, binding_request_id: str, reason: str | None = None) -> dict:
        return await self._client.request("app/binding/request/cancel", _compact({"bindingRequestId": binding_request_id, "reason": reason}))

    async def accept_binding(
        self,
        binding_request_id: str,
        request_token: str,
        grant_id: str,
        granted_scopes: list[str],
        approval_mode: str,
        approved_by: str | None = None,
        expires_at: str | None = None,
        grant_proof: dict | None = None,
        audit_ref: str | None = None,
    ) -> AppBindingAcceptResult:
        result = await self._client.request("app/binding/accept", _compact({
            "bindingRequestId": binding_request_id,
            "requestToken": request_token,
            "grantId": grant_id,
            "grantedScopes": granted_scopes,
            "approvalMode": approval_mode,
            "approvedBy": approved_by,
            "expiresAt": expires_at,
            "grantProof": grant_proof,
            "auditRef": audit_ref,
        }))
        return AppBindingAcceptResult.from_wire(result)

    async def attach_tools(
        self,
        binding_id: str,
        thread_id: str,
        app_id: str,
        grant_id: str,
        tools: list[dict],
        tool_catalog: list[dict] | None = None,
        direct_tool_names: list[str] | None = None,
        deferred_tool_names: list[str] | None = None,
        grant_proof: dict | None = None,
    ) -> AppBindingAttachToolsResult:
        result = await self._client.request("app/binding/attachTools", _compact({
            "bindingId": binding_id,
            "threadId": thread_id,
            "appId": app_id,
            "grantId": grant_id,
            "tools": tools,
            "toolCatalog": tool_catalog,
            "directToolNames": direct_tool_names,
            "deferredToolNames": deferred_tool_names,
            "grantProof": grant_proof,
        }))
        return AppBindingAttachToolsResult.from_wire(result)

    # Thread bindings
    async def list_thread_bindings(self, thread_id: str, include_revoked: bool = False) -> list[ThreadAppBinding]:
        result = await self._client.request("thread/appBindings/list", {"threadId": thread_id, "includeRevoked": include_revoked})
        bindings = result.get("bindings", []) if isinstance(result, dict) else []
        return [ThreadAppBinding.from_wire(b) for b in bindings if isinstance(b, dict)]

    async def revoke_thread_binding(self, thread_id: str, binding_id: str, reason: str | None = None) -> dict:
        return await self._client.request("thread/appBindings/revoke", _compact({"threadId": thread_id, "bindingId": binding_id, "reason": reason}))

    async def refresh_thread_bindings(self, thread_id: str, binding_id: str | None = None) -> Any:
        return await self._client.request("thread/appBindings/refresh", _compact({"threadId": thread_id, "bindingId": binding_id}))
