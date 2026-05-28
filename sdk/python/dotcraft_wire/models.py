"""Wire DTO models for the DotCraft AppServer Wire Protocol."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


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
# Input parts
# ---------------------------------------------------------------------------


def text_part(text: str) -> dict:
    """Create a plain-text input part."""
    return {"type": "text", "text": text}


def image_url_part(url: str) -> dict:
    """Create a remote image URL input part."""
    return {"type": "image", "url": url}


def local_image_part(path: str) -> dict:
    """Create a local image file input part."""
    return {"type": "localImage", "path": path}


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
