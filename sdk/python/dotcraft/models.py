"""Wire DTO models for the DotCraft AppServer Wire Protocol."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal, TypeAlias


# ---------------------------------------------------------------------------
# JSON-RPC envelope
# ---------------------------------------------------------------------------


@dataclass
class JsonRpcMessage:
    """A parsed JSON-RPC 2.0 message."""

    method: str | None = None
    id: int | str | None = None
    params: dict | None = None
    result: Any = None
    error: dict | None = None

    @property
    def is_request(self) -> bool:
        """Has both id and method — a request (client or server-initiated)."""
        return self.id is not None and self.method is not None

    @property
    def is_notification(self) -> bool:
        """Has method but no id — a one-way notification."""
        return self.id is None and self.method is not None

    @property
    def is_response(self) -> bool:
        """Has id but no method — a response to a previous request."""
        return self.id is not None and self.method is None

    @classmethod
    def from_dict(cls, data: dict) -> JsonRpcMessage:
        return cls(
            method=data.get("method"),
            id=data.get("id"),
            params=data.get("params"),
            result=data.get("result"),
            error=data.get("error"),
        )

    def to_dict(self) -> dict:
        out: dict = {"jsonrpc": "2.0"}
        if self.id is not None:
            out["id"] = self.id
        if self.method is not None:
            out["method"] = self.method
        if self.params is not None:
            out["params"] = self.params
        if self.result is not None:
            out["result"] = self.result
        if self.error is not None:
            out["error"] = self.error
        return out


# ---------------------------------------------------------------------------
# Session identity
# ---------------------------------------------------------------------------


@dataclass
class SessionIdentity:
    """Identifies a user within a channel."""

    channel_name: str
    user_id: str
    workspace_path: str = ""
    channel_context: str = ""

    def to_wire(self) -> dict:
        d: dict = {
            "channelName": self.channel_name,
            "userId": self.user_id,
        }
        if self.workspace_path:
            d["workspacePath"] = self.workspace_path
        if self.channel_context:
            d["channelContext"] = self.channel_context
        return d


# ---------------------------------------------------------------------------
# Thread
# ---------------------------------------------------------------------------


@dataclass
class Thread:
    """A DotCraft conversation thread."""

    id: str
    status: str
    workspace_path: str = ""
    user_id: str = ""
    origin_channel: str = ""
    display_name: str | None = None
    created_at: str = ""
    last_active_at: str = ""
    metadata: dict = field(default_factory=dict)
    turns: list = field(default_factory=list)

    @classmethod
    def from_wire(cls, data: dict) -> Thread:
        return cls(
            id=data.get("id", ""),
            status=data.get("status", ""),
            workspace_path=data.get("workspacePath", ""),
            user_id=data.get("userId", ""),
            origin_channel=data.get("originChannel", ""),
            display_name=data.get("displayName"),
            created_at=data.get("createdAt", ""),
            last_active_at=data.get("lastActiveAt", ""),
            metadata=data.get("metadata", {}),
            turns=data.get("turns", []),
        )


# ---------------------------------------------------------------------------
# Turn
# ---------------------------------------------------------------------------


@dataclass
class Turn:
    """A single agent turn within a thread."""

    id: str
    thread_id: str
    status: str
    items: list = field(default_factory=list)
    started_at: str = ""
    completed_at: str = ""
    token_usage: dict | None = None
    error: str | None = None

    @classmethod
    def from_wire(cls, data: dict) -> Turn:
        return cls(
            id=data.get("id", ""),
            thread_id=data.get("threadId", ""),
            status=data.get("status", ""),
            items=data.get("items", []),
            started_at=data.get("startedAt", ""),
            completed_at=data.get("completedAt", ""),
            token_usage=data.get("tokenUsage"),
            error=data.get("error"),
        )


# ---------------------------------------------------------------------------
# Model catalog
# ---------------------------------------------------------------------------


@dataclass
class ModelInfo:
    """A model catalog entry returned by ``model/list``."""

    id: str
    display_name: str
    provider: str | None = None

    @classmethod
    def from_wire(cls, data: dict) -> ModelInfo:
        return cls(
            id=data.get("id") or data.get("modelId") or data.get("name") or "",
            display_name=data.get("displayName") or data.get("name") or data.get("id") or "",
            provider=data.get("provider"),
        )


# ---------------------------------------------------------------------------
# Initialize result
# ---------------------------------------------------------------------------


@dataclass
class ServerInfo:
    name: str
    version: str
    protocol_version: str
    extensions: list[str] = field(default_factory=list)

    @classmethod
    def from_wire(cls, data: dict) -> ServerInfo:
        return cls(
            name=data.get("name", ""),
            version=data.get("version", ""),
            protocol_version=data.get("protocolVersion", ""),
            extensions=data.get("extensions", []),
        )


@dataclass
class ServerCapabilities:
    app_binding_version: int = 0
    thread_management: bool = False
    thread_subscriptions: bool = False
    approval_flow: bool = False
    mode_switch: bool = False
    config_override: bool = False
    cron_management: bool = False
    heartbeat_management: bool = False
    skills_management: bool = False
    command_management: bool = False

    @classmethod
    def from_wire(cls, data: dict) -> ServerCapabilities:
        return cls(
            app_binding_version=data.get("appBindingVersion", 0),
            thread_management=data.get("threadManagement", False),
            thread_subscriptions=data.get("threadSubscriptions", False),
            approval_flow=data.get("approvalFlow", False),
            mode_switch=data.get("modeSwitch", False),
            config_override=data.get("configOverride", False),
            cron_management=data.get("cronManagement", False),
            heartbeat_management=data.get("heartbeatManagement", False),
            skills_management=data.get("skillsManagement", False),
            command_management=data.get("commandManagement", False),
        )


@dataclass
class InitializeResult:
    server_info: ServerInfo
    capabilities: ServerCapabilities

    @classmethod
    def from_wire(cls, data: dict) -> InitializeResult:
        return cls(
            server_info=ServerInfo.from_wire(data.get("serverInfo", {})),
            capabilities=ServerCapabilities.from_wire(data.get("capabilities", {})),
        )


# ---------------------------------------------------------------------------
# MCP runtime/control
# ---------------------------------------------------------------------------


@dataclass
class McpServerOrigin:
    kind: str
    plugin_id: str | None = None
    plugin_display_name: str | None = None
    declared_name: str | None = None
    thread_id: str | None = None
    binding_id: str | None = None

    @classmethod
    def from_wire(cls, data: dict) -> McpServerOrigin:
        return cls(
            kind=data.get("kind", ""),
            plugin_id=data.get("pluginId"),
            plugin_display_name=data.get("pluginDisplayName"),
            declared_name=data.get("declaredName"),
            thread_id=data.get("threadId"),
            binding_id=data.get("bindingId"),
        )


@dataclass
class McpServerRuntimeStatus:
    name: str
    server_info: Any = None
    tools: dict = field(default_factory=dict)
    resources: list = field(default_factory=list)
    resource_templates: list = field(default_factory=list)
    auth_status: str = "unsupported"
    declared_name: str | None = None
    runtime_name: str | None = None
    origin: McpServerOrigin | None = None

    @classmethod
    def from_wire(cls, data: dict) -> McpServerRuntimeStatus:
        origin = data.get("origin")
        return cls(
            name=data.get("name", ""),
            server_info=data.get("serverInfo"),
            tools=data.get("tools", {}),
            resources=data.get("resources", []),
            resource_templates=data.get("resourceTemplates", []),
            auth_status=data.get("authStatus", "unsupported"),
            declared_name=data.get("declaredName"),
            runtime_name=data.get("runtimeName"),
            origin=McpServerOrigin.from_wire(origin) if isinstance(origin, dict) else None,
        )


@dataclass
class McpServerStatusListResult:
    data: list[McpServerRuntimeStatus] = field(default_factory=list)
    next_cursor: str | None = None

    @classmethod
    def from_wire(cls, data: dict) -> McpServerStatusListResult:
        return cls(
            data=[McpServerRuntimeStatus.from_wire(item) for item in data.get("data", [])],
            next_cursor=data.get("nextCursor"),
        )


@dataclass
class McpServerResourceReadResult:
    contents: Any


@dataclass
class McpServerToolCallResult:
    content: Any
    structured_content: Any = None
    is_error: bool = False
    meta: Any = None

    @classmethod
    def from_wire(cls, data: dict) -> McpServerToolCallResult:
        return cls(
            content=data.get("content"),
            structured_content=data.get("structuredContent"),
            is_error=bool(data.get("isError", False)),
            meta=data.get("_meta"),
        )


@dataclass
class McpServerOAuthLoginResult:
    authorization_url: str


@dataclass
class McpServerReloadResult:
    pass


@dataclass
class McpServerStartupStatusUpdatedNotification:
    name: str
    status: Literal["starting", "ready", "failed", "cancelled"]
    thread_id: str | None = None
    error: str | None = None
    failure_reason: str | None = None


@dataclass
class McpServerOAuthLoginCompletedNotification:
    name: str
    success: bool
    thread_id: str | None = None
    error: str | None = None


@dataclass
class McpServerElicitationRequest:
    server_name: str
    mode: Literal["form", "url"]
    thread_id: str | None = None
    turn_id: str | None = None
    elicitation_id: str | None = None
    message: str | None = None
    url: str | None = None
    requested_schema: dict | None = None
    meta: Any = None


@dataclass
class McpServerElicitationResponse:
    action: Literal["accept", "decline", "cancel"]
    content: dict | None = None
    meta: Any = None

    def to_wire(self) -> dict:
        result: dict = {"action": self.action}
        if self.content is not None:
            result["content"] = self.content
        if self.meta is not None:
            result["_meta"] = self.meta
        return result


# ---------------------------------------------------------------------------
# Runtime Dynamic Tools
# ---------------------------------------------------------------------------


@dataclass
class DynamicToolFunction:
    """A Runtime Dynamic Function declaration."""

    name: str
    description: str
    input_schema: dict
    defer_loading: bool = False
    approval: dict | None = None
    type: Literal["function"] = field(default="function", init=False)

    def to_wire(self) -> dict:
        result: dict = {
            "type": self.type,
            "name": self.name,
            "description": self.description,
            "inputSchema": self.input_schema,
        }
        if self.defer_loading:
            result["deferLoading"] = True
        if self.approval is not None:
            result["approval"] = self.approval
        return result


@dataclass
class DynamicToolNamespace:
    """A named Runtime Dynamic namespace containing Function declarations."""

    name: str
    description: str
    tools: list[DynamicToolFunction]
    type: Literal["namespace"] = field(default="namespace", init=False)

    def to_wire(self) -> dict:
        return {
            "type": self.type,
            "name": self.name,
            "description": self.description,
            "tools": [tool.to_wire() for tool in self.tools],
        }


DynamicToolDeclaration: TypeAlias = DynamicToolFunction | DynamicToolNamespace


@dataclass
class DynamicToolResult:
    """Result returned to AppServer for a Runtime Dynamic Tool invocation."""

    success: bool
    content_items: list[dict] | None = None
    structured_content: Any = None
    error_code: str | None = None
    error_message: str | None = None

    def to_wire(self) -> dict:
        result: dict = {"success": self.success}
        if self.content_items is not None:
            result["contentItems"] = self.content_items
        if self.structured_content is not None:
            result["structuredContent"] = self.structured_content
        if self.error_code is not None:
            result["errorCode"] = self.error_code
        if self.error_message is not None:
            result["errorMessage"] = self.error_message
        return result


def dynamic_tool_text(text: str) -> dict:
    """Create a Runtime Dynamic text content item."""
    return {"type": "text", "text": text}


def dynamic_tool_image(
    media_type: str,
    *,
    url: str | None = None,
    data_base64: str | None = None,
) -> dict:
    """Create a Runtime Dynamic image with exactly one URL or base64 source."""
    if (url is None) == (data_base64 is None):
        raise ValueError("Exactly one of url or data_base64 must be provided.")
    result: dict[str, str] = {"type": "image", "mediaType": media_type}
    if url is not None:
        result["url"] = url
    else:
        assert data_base64 is not None
        result["dataBase64"] = data_base64
    return result


# ---------------------------------------------------------------------------
# Input parts
# ---------------------------------------------------------------------------


def text_part(text: str) -> dict:
    """Create a plain-text input part."""
    return {"type": "text", "text": text}


def image_url_part(url: str) -> dict:
    """Create a remote image URL input part."""
    return {"type": "image", "url": url}


def local_image_part(path: str, mime_type: str | None = None) -> dict:
    """Create a local image file input part."""
    part: dict = {"type": "localImage", "path": path}
    if mime_type:
        part["mimeType"] = mime_type
    return part


def skill_ref_part(name: str) -> dict:
    """Create a skill reference input part."""
    return {"type": "skillRef", "name": name}


def command_ref_part(raw_text: str) -> dict:
    """Create a command reference input part from leading ``/command args`` text."""
    stripped = raw_text.strip()
    body = stripped[1:] if stripped.startswith("/") else stripped
    name, _, args_text = body.partition(" ")
    part: dict = {"type": "commandRef", "name": name, "rawText": raw_text}
    if args_text:
        part["argsText"] = args_text
    return part


def file_ref_part(path: str, display_path: str | None = None) -> dict:
    """Create a file reference input part."""
    part: dict = {"type": "fileRef", "path": path}
    if display_path:
        part["displayPath"] = display_path
    return part


# ---------------------------------------------------------------------------
# Approval decisions
# ---------------------------------------------------------------------------

DECISION_ACCEPT = "accept"
DECISION_ACCEPT_FOR_SESSION = "acceptForSession"
DECISION_ACCEPT_ALWAYS = "acceptAlways"
DECISION_DECLINE = "decline"
DECISION_CANCEL = "cancel"


# ---------------------------------------------------------------------------
# DotCraft error codes
# ---------------------------------------------------------------------------

ERR_NOT_INITIALIZED = -32002
ERR_ALREADY_INITIALIZED = -32003
ERR_THREAD_NOT_FOUND = -32010
ERR_THREAD_NOT_ACTIVE = -32011
ERR_TURN_IN_PROGRESS = -32012
ERR_TURN_NOT_FOUND = -32013
ERR_TURN_NOT_RUNNING = -32014
ERR_APPROVAL_TIMEOUT = -32020
ERR_CHANNEL_REJECTED = -32030
ERR_CRON_NOT_FOUND = -32031
