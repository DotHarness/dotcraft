"""App Binding workflow helpers backed directly by generated contracts."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from urllib.parse import parse_qs, urlparse

from .contracts import (
    AppBinding,
    AppBindingActivateParams,
    AppBindingRebindParams,
    AppBindingRequestGetParams,
    AppBindingRequestGetResult,
    AppConnectionAuthenticateParams,
    AppConnectionAuthenticateResult,
    AppConnectionConnectParams,
    AppConnectionConnectResult,
    AppConnectionRefreshResult,
    AppConnectionRevokeParams,
    AppConnectionRevokeResult,
    AppConnectionStartParams,
    AppConnectionStartResult,
    AppConnectionStatusParams,
    AppConnectionStatusResult,
    AppInfo,
    AppListParams,
    AppSurface,
    AppSurfacePublishParams,
    AppSurfaceResolveParams,
    AppViewParams,
    RpcEmpty,
    ThreadAppBindingConfirmCapabilitiesParams,
    ThreadAppBindingEnableParams,
    ThreadAppBindingEnableResult,
    ThreadAppBindingRevokeParams,
    ThreadAppBindingsListParams,
)

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
        parsed = urlparse(url)
        if expected_scheme is not None and parsed.scheme != expected_scheme:
            raise ValueError(f"Unexpected handoff scheme '{parsed.scheme}', expected '{expected_scheme}'.")
        query = parse_qs(parsed.query)
        app_id = query.get("app", [""])[0]
        request_id = query.get("request", [""])[0]
        request_token = query.get("token", [""])[0]
        if not app_id or not request_id or not request_token:
            raise ValueError("The handoff URL must contain app, request, and token query parameters.")
        if expected_app_id is not None and app_id != expected_app_id:
            raise ValueError(f"Unexpected handoff appId '{app_id}', expected '{expected_app_id}'.")
        return cls(
            scheme=parsed.scheme,
            operation=parsed.path.lstrip("/"),
            app_id=app_id,
            request_id=request_id,
            request_token=request_token,
            app_server_url=query.get("endpoint", [None])[0],
        )


def app_binding_tool_error(code: str, message: str, structured_content: Any = None) -> dict:
    value: dict = {
        "success": False,
        "errorCode": code,
        "errorMessage": message,
        "contentItems": [{"type": "text", "text": message}],
    }
    if structured_content is not None:
        value["structuredContent"] = structured_content
    return value


class AppBindingManager:
    def __init__(self, client) -> None:
        self._client = client

    async def list_apps(
        self,
        thread_id: str | None = None,
        include_disabled: bool = True,
        include_catalog: bool = True,
        force_refresh: bool = False,
    ) -> list[AppInfo]:
        result = await self._client.rpc_app_list(AppListParams(
            threadId=thread_id,
            includeDisabled=include_disabled,
            includeCatalog=include_catalog,
            forceRefresh=force_refresh,
            surface=None,
        ))
        return result.apps or []

    async def view_app(self, app_id: str, thread_id: str | None = None) -> AppInfo:
        result = await self._client.rpc_app_view(AppViewParams(appId=app_id, threadId=thread_id))
        if result.app is None:
            raise ValueError(f"App '{app_id}' was not returned by app/view.")
        return result.app

    async def start_connection(
        self, app_id: str, handoff_mode: str | None = None, return_to: str | None = None,
    ) -> AppConnectionStartResult:
        params = AppConnectionStartParams.model_validate({
            "appId": app_id,
            "handoffMode": handoff_mode,
            "returnTo": return_to,
        })
        return await self._client.rpc_app_connection_start(params)

    async def complete_connection(
        self, connection_request_id: str, request_token: str, account_label: str | None = None,
    ) -> AppConnectionConnectResult:
        return await self._client.rpc_app_connection_connect(AppConnectionConnectParams(
            connectionRequestId=connection_request_id,
            requestToken=request_token,
            accountLabel=account_label,
        ))

    async def connection_status(self, app_id: str) -> AppConnectionStatusResult:
        return await self._client.rpc_app_connection_status(AppConnectionStatusParams(appId=app_id))

    async def revoke_connection(self, app_id: str, reason: str | None = None) -> AppConnectionRevokeResult:
        return await self._client.rpc_app_connection_revoke(AppConnectionRevokeParams(appId=app_id, reason=reason))

    async def publish_surface(self, surface_id: str, endpoint: str, bearer: str) -> AppSurface:
        return await self._client.rpc_app_surface_publish(AppSurfacePublishParams(
            surfaceId=surface_id, endpoint=endpoint, bearer=bearer,
        ))

    async def resolve_surface(self, app_id: str, surface_id: str) -> AppSurface:
        return await self._client.rpc_app_surface_resolve(AppSurfaceResolveParams(
            appId=app_id, surfaceId=surface_id,
        ))

    async def enable(self, thread_id: str, app_id: str) -> ThreadAppBindingEnableResult:
        return await self._client.rpc_thread_app_bindings_enable(ThreadAppBindingEnableParams(
            threadId=thread_id, appId=app_id,
        ))

    async def get_binding_request(
        self, app_id: str, binding_request_id: str, request_token: str,
    ) -> AppBindingRequestGetResult:
        del app_id
        return await self._client.rpc_app_binding_request_get(AppBindingRequestGetParams(
            bindingRequestId=binding_request_id, requestToken=request_token,
        ))

    async def authenticate(self, app_id: str, credential: str) -> AppConnectionAuthenticateResult:
        return await self._client.rpc_app_connection_authenticate(AppConnectionAuthenticateParams(
            appId=app_id, credential=credential,
        ))

    async def refresh_credential(self) -> AppConnectionRefreshResult:
        return await self._client.rpc_app_connection_refresh(RpcEmpty())

    async def activate(
        self, binding_request_id: str, endpoint: str, bearer: str, bearer_expires_at: str | None = None,
    ) -> AppBinding:
        return await self._client.rpc_app_binding_activate(AppBindingActivateParams.model_validate({
            "bindingRequestId": binding_request_id,
            "endpoint": endpoint,
            "bearer": bearer,
            "bearerExpiresAt": bearer_expires_at,
        }))

    async def rebind(
        self, binding_id: str, authority_revision: int, endpoint: str, bearer: str,
        bearer_expires_at: str | None = None,
    ) -> AppBinding:
        return await self._client.rpc_app_binding_rebind(AppBindingRebindParams.model_validate({
            "bindingId": binding_id,
            "authorityRevision": authority_revision,
            "endpoint": endpoint,
            "bearer": bearer,
            "bearerExpiresAt": bearer_expires_at,
        }))

    async def confirm_capabilities(
        self, thread_id: str, binding_id: str, candidate_revision: int, decision: str,
    ) -> AppBinding:
        return await self._client.rpc_thread_app_bindings_confirm_capabilities(
            ThreadAppBindingConfirmCapabilitiesParams(
                threadId=thread_id,
                bindingId=binding_id,
                candidateRevision=candidate_revision,
                decision=decision,
            )
        )

    async def list_thread_bindings(self, thread_id: str, include_revoked: bool = False) -> list[AppBinding]:
        result = await self._client.rpc_thread_app_bindings_list(ThreadAppBindingsListParams(
            threadId=thread_id, includeRevoked=include_revoked,
        ))
        return result.bindings or []

    async def revoke_thread_binding(
        self, thread_id: str, binding_id: str, reason: str | None = None,
    ) -> AppBinding:
        return await self._client.rpc_thread_app_bindings_revoke(ThreadAppBindingRevokeParams(
            threadId=thread_id, bindingId=binding_id, reason=reason,
        ))

    async def refresh_thread_bindings(self, thread_id: str, binding_id: str | None = None) -> list[AppBinding]:
        del binding_id
        return await self.list_thread_bindings(thread_id)


__all__ = [
    "APP_BINDING_ERROR_CODES",
    "AppBindingHandoff",
    "AppBindingManager",
    "app_binding_tool_error",
]
