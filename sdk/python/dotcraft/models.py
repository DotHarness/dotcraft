"""Internal JSON-RPC envelope plus SDK authoring/input helpers.

Stable AppServer DTOs live exclusively in :mod:`dotcraft.contracts`.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal, TypeAlias


@dataclass
class JsonRpcMessage:
    method: str | None = None
    id: int | str | None = None
    params: dict | None = None
    result: Any = None
    error: dict | None = None

    @property
    def is_request(self) -> bool:
        return self.id is not None and self.method is not None

    @property
    def is_notification(self) -> bool:
        return self.id is None and self.method is not None

    @property
    def is_response(self) -> bool:
        return self.id is not None and self.method is None

    @classmethod
    def from_dict(cls, data: dict) -> "JsonRpcMessage":
        return cls(
            method=data.get("method"),
            id=data.get("id"),
            params=data.get("params"),
            result=data.get("result"),
            error=data.get("error"),
        )

    def to_dict(self) -> dict:
        value: dict = {"jsonrpc": "2.0"}
        if self.id is not None:
            value["id"] = self.id
        if self.method is not None:
            value["method"] = self.method
        if self.params is not None:
            value["params"] = self.params
        if self.result is not None:
            value["result"] = self.result
        if self.error is not None:
            value["error"] = self.error
        return value


@dataclass
class DynamicToolFunction:
    name: str
    description: str
    input_schema: dict
    defer_loading: bool = False
    approval: dict | None = None
    type: Literal["function"] = field(default="function", init=False)

    def to_wire(self) -> dict:
        value: dict = {
            "type": self.type,
            "name": self.name,
            "description": self.description,
            "inputSchema": self.input_schema,
        }
        if self.defer_loading:
            value["deferLoading"] = True
        if self.approval is not None:
            value["approval"] = self.approval
        return value


@dataclass
class DynamicToolNamespace:
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
    success: bool
    content_items: list[dict] | None = None
    structured_content: Any = None
    error_code: str | None = None
    error_message: str | None = None

    def to_wire(self) -> dict:
        value: dict = {"success": self.success}
        if self.content_items is not None:
            value["contentItems"] = self.content_items
        if self.structured_content is not None:
            value["structuredContent"] = self.structured_content
        if self.error_code is not None:
            value["errorCode"] = self.error_code
        if self.error_message is not None:
            value["errorMessage"] = self.error_message
        return value


def dynamic_tool_text(text: str) -> dict:
    return {"type": "text", "text": text}


def dynamic_tool_image(media_type: str, *, url: str | None = None, data_base64: str | None = None) -> dict:
    if (url is None) == (data_base64 is None):
        raise ValueError("Exactly one of url or data_base64 must be provided.")
    value: dict[str, str] = {"type": "image", "mediaType": media_type}
    if url is not None:
        value["url"] = url
    else:
        assert data_base64 is not None
        value["dataBase64"] = data_base64
    return value


def text_part(text: str) -> dict:
    return {"type": "text", "text": text}


def image_data_url_part(data_url: str) -> dict:
    return {"type": "image", "url": data_url}


def local_image_part(path: str, mime_type: str | None = None) -> dict:
    value = {"type": "localImage", "path": path}
    if mime_type:
        value["mimeType"] = mime_type
    return value


def skill_ref_part(name: str) -> dict:
    return {"type": "skillRef", "name": name}


def command_ref_part(raw_text: str) -> dict:
    stripped = raw_text.strip()
    body = stripped[1:] if stripped.startswith("/") else stripped
    name, _, args_text = body.partition(" ")
    value = {"type": "commandRef", "name": name, "rawText": raw_text}
    if args_text:
        value["argsText"] = args_text
    return value


def file_ref_part(path: str, display_path: str | None = None) -> dict:
    value = {"type": "fileRef", "path": path}
    if display_path:
        value["displayPath"] = display_path
    return value


DECISION_ACCEPT = "accept"
DECISION_ACCEPT_FOR_SESSION = "acceptForSession"
DECISION_ACCEPT_ALWAYS = "acceptAlways"
DECISION_DECLINE = "decline"
DECISION_CANCEL = "cancel"

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
