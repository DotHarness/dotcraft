"""High-level AppServer operations built on the pure JSON-RPC Wire client."""

from __future__ import annotations

import asyncio
from collections.abc import Sequence
from typing import Any, AsyncIterator, Callable, Literal

from .client import DotCraftWireClient, Handler
from .models import (
    DynamicToolDeclaration,
    DynamicToolFunction,
    DynamicToolNamespace,
    DynamicToolResult,
    JsonRpcMessage,
    McpServerOAuthLoginResult,
    McpServerReloadResult,
    McpServerResourceReadResult,
    McpServerStatusListResult,
    McpServerToolCallResult,
    ModelInfo,
    Thread,
    Turn,
)


def _omit_none(values: dict) -> dict:
    return {key: value for key, value in values.items() if value is not None}


class DotCraftAppServerClient(DotCraftWireClient):
    """Business-oriented AppServer client layered over :class:`DotCraftWireClient`."""

    def __init__(self, *args: Any, **kwargs: Any) -> None:
        super().__init__(*args, **kwargs)
        self._dynamic_tool_handlers: dict[str, Callable] = {}
        self._default_dynamic_tool_handler: Callable | None = None
        self.register_server_request_handler_raw("item/tool/call", self._handle_dynamic_tool_request)

    async def initialize(self, *args: Any, **kwargs: Any):
        approval_support = kwargs.get("approval_support", args[3] if len(args) > 3 else False)
        request_user_input_support = kwargs.get(
            "request_user_input_support",
            args[5] if len(args) > 5 else False,
        )
        if approval_support and "item/approval/request" not in self._request_handlers:
            raise ValueError("approval_support requires an approval request handler")
        if request_user_input_support and "item/tool/requestUserInput" not in self._request_handlers:
            raise ValueError("request_user_input_support requires a user-input request handler")
        return await super().initialize(*args, **kwargs)

    async def thread_start(
        self,
        channel_name: str,
        user_id: str,
        workspace_path: str = "",
        channel_context: str = "",
        display_name: str | None = None,
        history_mode: str = "server",
        dynamic_tools: Sequence[DynamicToolDeclaration | dict] | None = None,
    ) -> Thread:
        identity: dict = {"channelName": channel_name, "userId": user_id}
        if workspace_path:
            identity["workspacePath"] = workspace_path
        if channel_context:
            identity["channelContext"] = channel_context
        params: dict = {"identity": identity, "historyMode": history_mode}
        if display_name is not None:
            params["displayName"] = display_name
        if dynamic_tools is not None:
            params["dynamicTools"] = [
                tool.to_wire() if isinstance(tool, (DynamicToolFunction, DynamicToolNamespace)) else tool
                for tool in dynamic_tools
            ]
        result = await self._request("thread/start", params)
        return Thread.from_wire(result["thread"])

    async def thread_resume(
        self,
        thread_id: str,
        dynamic_tools: Sequence[DynamicToolDeclaration | dict] | None = None,
    ) -> Thread:
        params: dict = {"threadId": thread_id}
        if dynamic_tools is not None:
            params["dynamicTools"] = [
                tool.to_wire() if isinstance(tool, (DynamicToolFunction, DynamicToolNamespace)) else tool
                for tool in dynamic_tools
            ]
        result = await self._request("thread/resume", params)
        return Thread.from_wire(result["thread"])

    async def thread_list(
        self,
        channel_name: str,
        user_id: str,
        workspace_path: str = "",
        channel_context: str = "",
        include_archived: bool = False,
        query: str | None = None,
        limit: int | None = None,
        cursor: str | None = None,
    ) -> list[Thread]:
        identity: dict = {"channelName": channel_name, "userId": user_id}
        if workspace_path:
            identity["workspacePath"] = workspace_path
        if channel_context:
            identity["channelContext"] = channel_context
        params = _omit_none({
            "identity": identity,
            "includeArchived": include_archived,
            "query": query,
            "limit": limit,
            "cursor": cursor,
        })
        result = await self._request("thread/list", params)
        return [Thread.from_wire(item) for item in result.get("data", [])]

    async def thread_read(
        self,
        thread_id: str,
        include_turns: bool = False,
        turn_limit: int | None = None,
        cursor: str | None = None,
    ) -> Thread:
        result = await self._request("thread/read", _omit_none({
            "threadId": thread_id,
            "includeTurns": include_turns,
            "turnLimit": turn_limit,
            "cursor": cursor,
        }))
        return Thread.from_wire(result["thread"])

    async def thread_subscribe(self, thread_id: str, replay_recent: bool = False) -> None:
        await self._request("thread/subscribe", {"threadId": thread_id, "replayRecent": replay_recent})

    async def thread_unsubscribe(self, thread_id: str) -> None:
        await self._request("thread/unsubscribe", {"threadId": thread_id})

    async def thread_pause(self, thread_id: str) -> None:
        await self._request("thread/pause", {"threadId": thread_id})

    async def thread_archive(self, thread_id: str) -> None:
        await self._request("thread/archive", {"threadId": thread_id})

    async def thread_delete(self, thread_id: str) -> None:
        await self._request("thread/delete", {"threadId": thread_id})

    async def thread_set_mode(self, thread_id: str, mode: str) -> None:
        await self._request("thread/mode/set", {"threadId": thread_id, "mode": mode})

    async def turn_start(self, thread_id: str, input: list[dict], sender: dict | None = None) -> Turn:
        params: dict = {"threadId": thread_id, "input": input}
        if sender:
            params["sender"] = sender
        result = await self._request("turn/start", params)
        return Turn.from_wire(result["turn"])

    async def turn_enqueue(self, thread_id: str, input: list[dict], sender: dict | None = None) -> dict:
        params: dict = {"threadId": thread_id, "input": input}
        if sender:
            params["sender"] = sender
        return await self._request("turn/enqueue", params)

    async def turn_interrupt(self, thread_id: str, turn_id: str) -> None:
        await self._request("turn/interrupt", {"threadId": thread_id, "turnId": turn_id})

    async def model_list(self) -> list[ModelInfo]:
        result = await self._request("model/list", {})
        items = result.get("models") or result.get("items") if isinstance(result, dict) else result
        return [ModelInfo.from_wire(item) for item in (items or []) if isinstance(item, dict)]

    async def command_list(self, language: str | None = None) -> list[dict]:
        result = await self._request("command/list", {"language": language} if language else {})
        return result.get("commands", [])

    async def command_execute(
        self,
        thread_id: str,
        command: str,
        arguments: list[str] | None = None,
        sender: dict | None = None,
    ) -> dict:
        return await self._request("command/execute", _omit_none({
            "threadId": thread_id,
            "command": command,
            "arguments": arguments,
            "sender": sender,
        }))

    async def mcp_server_status_list(
        self,
        thread_id: str | None = None,
        cursor: str | None = None,
        limit: int | None = None,
        detail: Literal["full", "toolsAndAuthOnly"] | None = None,
    ) -> McpServerStatusListResult:
        result = await self._request("mcpServerStatus/list", _omit_none({
            "threadId": thread_id, "cursor": cursor, "limit": limit, "detail": detail,
        }))
        return McpServerStatusListResult.from_wire(result)

    async def mcp_server_resource_read(
        self, server: str, uri: str, thread_id: str | None = None,
    ) -> McpServerResourceReadResult:
        result = await self._request("mcpServer/resource/read", _omit_none({
            "threadId": thread_id, "server": server, "uri": uri,
        }))
        return McpServerResourceReadResult(result.get("contents"))

    async def mcp_server_tool_call(
        self,
        thread_id: str,
        server: str,
        tool: str,
        arguments: dict | None = None,
        meta: Any = None,
    ) -> McpServerToolCallResult:
        result = await self._request("mcpServer/tool/call", _omit_none({
            "threadId": thread_id, "server": server, "tool": tool, "arguments": arguments, "_meta": meta,
        }))
        return McpServerToolCallResult.from_wire(result)

    async def mcp_server_oauth_login(
        self,
        name: str,
        thread_id: str | None = None,
        scopes: list[str] | None = None,
        timeout_secs: float | None = None,
    ) -> McpServerOAuthLoginResult:
        result = await self._request("mcpServer/oauth/login", _omit_none({
            "name": name, "threadId": thread_id, "scopes": scopes, "timeoutSecs": timeout_secs,
        }))
        return McpServerOAuthLoginResult(result.get("authorizationUrl", ""))

    async def mcp_server_reload(self) -> McpServerReloadResult:
        await self._request("config/mcpServer/reload")
        return McpServerReloadResult()

    async def stream_events(
        self,
        thread_id: str,
        terminal_methods: tuple[str, ...] = ("turn/completed", "turn/failed", "turn/cancelled"),
    ) -> AsyncIterator[JsonRpcMessage]:
        queue: asyncio.Queue[JsonRpcMessage] = asyncio.Queue()
        methods = [
            "thread/started", "thread/renamed", "thread/resumed", "thread/statusChanged",
            "turn/started", "turn/completed", "turn/failed", "turn/cancelled", "item/started",
            "item/completed", "item/agentMessage/delta", "item/reasoning/delta", "item/approval/resolved",
            "subagent/progress", "item/usage/delta", "system/event", "plan/updated",
        ]
        handlers: dict[str, Handler] = {}
        for method in methods:
            def create_handler(method_name: str) -> Handler:
                async def handler(params: dict) -> None:
                    if "threadId" not in params or params["threadId"] == thread_id:
                        await queue.put(JsonRpcMessage(method=method_name, params=params))

                return handler

            handler = create_handler(method)
            handlers[method] = handler
            self.register_notification_raw(method, handler)
        try:
            while True:
                event = await queue.get()
                yield event
                if event.method in terminal_methods:
                    break
        finally:
            for method, handler in handlers.items():
                self.unregister_notification_raw(method, handler)

    def register_dynamic_tool_handler(
        self,
        handler: Callable,
        thread_id: str | None = None,
        namespace: str | None = None,
        tool: str | None = None,
    ) -> Callable[[], None]:
        if thread_id is None and tool is None:
            self._default_dynamic_tool_handler = handler
            def unregister_default() -> None:
                if self._default_dynamic_tool_handler is handler:
                    self._default_dynamic_tool_handler = None
            return unregister_default
        key = self._tool_key(thread_id or "", namespace, tool or "")
        self._dynamic_tool_handlers[key] = handler
        def unregister() -> None:
            if self._dynamic_tool_handlers.get(key) is handler:
                self._dynamic_tool_handlers.pop(key, None)
        return unregister

    @staticmethod
    def _tool_key(thread_id: str, namespace: str | None, tool: str) -> str:
        return f"{thread_id}\x00{namespace or ''}\x00{tool}"

    async def _handle_dynamic_tool_request(self, _request_id: str | int, params: dict) -> dict:
        key = self._tool_key(params.get("threadId", ""), params.get("namespace"), params.get("tool", ""))
        handler = self._dynamic_tool_handlers.get(key) or self._default_dynamic_tool_handler
        if handler is None:
            return {
                "success": False,
                "errorCode": "UnsupportedTool",
                "errorMessage": "No handler registered for this runtime dynamic tool.",
            }
        try:
            result = handler(params)
            if asyncio.iscoroutine(result):
                result = await result
            return result.to_wire() if isinstance(result, DynamicToolResult) else result
        except Exception as error:
            return {
                "success": False,
                "errorCode": "AdapterToolCallFailed",
                "errorMessage": str(error),
            }
