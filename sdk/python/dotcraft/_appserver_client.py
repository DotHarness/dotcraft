"""High-level AppServer operations built on the pure JSON-RPC Wire client."""

from __future__ import annotations

import asyncio
from collections.abc import Sequence
from typing import Any, AsyncIterator, Callable, Literal

from pydantic import BaseModel

from .client import DotCraftWireClient
from .models import (
    DynamicToolDeclaration,
    DynamicToolFunction,
    DynamicToolNamespace,
    DynamicToolResult,
    JsonRpcMessage,
)
from .contracts import (
    CommandExecuteParams,
    CommandExecuteResult,
    CommandInfo,
    CommandListParams,
    McpServerOAuthLoginResult,
    McpServerOAuthLoginParams,
    McpServerReloadResult,
    McpServerResourceReadParams,
    McpServerResourceReadResult,
    McpServerStatusListParams,
    McpServerStatusListResult,
    McpServerToolCallParams,
    McpServerToolCallResult,
    ModelCatalogItem,
    ModelListParams,
    RpcEmpty,
    SessionThread,
    SessionTurn,
    ThreadArchiveParams,
    ThreadDeleteParams,
    ThreadListParams,
    ThreadModeSetParams,
    ThreadPauseParams,
    ThreadReadParams,
    ThreadResumeParams,
    ThreadStartParams,
    ThreadSubscribeParams,
    ThreadSummary,
    ThreadUnsubscribeParams,
    TurnEnqueueParams,
    TurnEnqueueResult,
    TurnInterruptParams,
    TurnStartParams,
)


def _omit_none(values: dict) -> dict:
    return {key: value for key, value in values.items() if value is not None}


class _AppServerClient(DotCraftWireClient):
    """Business-oriented AppServer client layered over :class:`DotCraftWireClient`."""

    def __init__(self, *args: Any, **kwargs: Any) -> None:
        super().__init__(*args, **kwargs)
        self._dynamic_tool_handlers: dict[str, Callable] = {}
        self._default_dynamic_tool_handler: Callable | None = None
        self.register_server_request_handler("item/tool/call", self._handle_dynamic_tool_request)

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
    ) -> SessionThread:
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
        result = await self.rpc_thread_start(ThreadStartParams.model_validate(params))
        return result.thread

    async def thread_resume(
        self,
        thread_id: str,
        dynamic_tools: Sequence[DynamicToolDeclaration | dict] | None = None,
    ) -> SessionThread:
        params: dict = {"threadId": thread_id}
        if dynamic_tools is not None:
            params["dynamicTools"] = [
                tool.to_wire() if isinstance(tool, (DynamicToolFunction, DynamicToolNamespace)) else tool
                for tool in dynamic_tools
            ]
        result = await self.rpc_thread_resume(ThreadResumeParams.model_validate(params))
        return result.thread

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
    ) -> list[ThreadSummary]:
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
        result = await self.rpc_thread_list(ThreadListParams.model_validate(params))
        return result.data

    async def thread_read(
        self,
        thread_id: str,
        include_turns: bool = False,
        turn_limit: int | None = None,
        cursor: str | None = None,
    ) -> SessionThread:
        result = await self.rpc_thread_read(ThreadReadParams.model_validate(_omit_none({
            "threadId": thread_id,
            "includeTurns": include_turns,
            "turnLimit": turn_limit,
            "cursor": cursor,
        })))
        return result.thread

    async def thread_subscribe(self, thread_id: str, replay_recent: bool = False) -> None:
        await self.rpc_thread_subscribe(ThreadSubscribeParams.model_validate({
            "threadId": thread_id,
            "replayRecent": replay_recent,
        }))

    async def thread_unsubscribe(self, thread_id: str) -> None:
        await self.rpc_thread_unsubscribe(ThreadUnsubscribeParams.model_validate({"threadId": thread_id}))

    async def thread_pause(self, thread_id: str) -> None:
        await self.rpc_thread_pause(ThreadPauseParams.model_validate({"threadId": thread_id}))

    async def thread_archive(self, thread_id: str) -> None:
        await self.rpc_thread_archive(ThreadArchiveParams.model_validate({"threadId": thread_id}))

    async def thread_delete(self, thread_id: str) -> None:
        await self.rpc_thread_delete(ThreadDeleteParams.model_validate({"threadId": thread_id}))

    async def thread_set_mode(self, thread_id: str, mode: str) -> None:
        await self.rpc_thread_mode_set(ThreadModeSetParams.model_validate({"threadId": thread_id, "mode": mode}))

    async def turn_start(self, thread_id: str, input: list[dict], sender: dict | None = None) -> SessionTurn:
        params: dict = {"threadId": thread_id, "input": input}
        if sender:
            params["sender"] = sender
        result = await self.rpc_turn_start(TurnStartParams.model_validate(params))
        return result.turn

    async def turn_enqueue(
        self, thread_id: str, input: list[dict], sender: dict | None = None,
    ) -> TurnEnqueueResult:
        params: dict = {"threadId": thread_id, "input": input}
        if sender:
            params["sender"] = sender
        return await self.rpc_turn_enqueue(TurnEnqueueParams.model_validate(params))

    async def turn_interrupt(self, thread_id: str, turn_id: str) -> None:
        await self.rpc_turn_interrupt(TurnInterruptParams.model_validate({
            "threadId": thread_id,
            "turnId": turn_id,
        }))

    async def model_list(self) -> list[ModelCatalogItem]:
        result = await self.rpc_model_list(ModelListParams.model_validate({}))
        return result.models or []

    async def command_list(self, language: str | None = None) -> list[CommandInfo]:
        result = await self.rpc_command_list(CommandListParams.model_validate({"language": language}))
        return result.commands or []

    async def command_execute(
        self,
        thread_id: str,
        command: str,
        arguments: list[str] | None = None,
        sender: dict | None = None,
    ) -> CommandExecuteResult:
        return await self.rpc_command_execute(CommandExecuteParams.model_validate(_omit_none({
            "threadId": thread_id,
            "command": command,
            "arguments": arguments,
            "sender": sender,
        })))

    async def mcp_server_status_list(
        self,
        thread_id: str | None = None,
        cursor: str | None = None,
        limit: int | None = None,
        detail: Literal["full", "toolsAndAuthOnly"] | None = None,
    ) -> McpServerStatusListResult:
        return await self.rpc_mcp_server_status_list(McpServerStatusListParams.model_validate(_omit_none({
            "threadId": thread_id, "cursor": cursor, "limit": limit, "detail": detail,
        })))

    async def mcp_server_resource_read(
        self, server: str, uri: str, thread_id: str | None = None,
    ) -> McpServerResourceReadResult:
        return await self.rpc_mcp_server_resource_read(McpServerResourceReadParams.model_validate(_omit_none({
            "threadId": thread_id, "server": server, "uri": uri,
        })))

    async def mcp_server_tool_call(
        self,
        thread_id: str,
        server: str,
        tool: str,
        arguments: dict | None = None,
        meta: Any = None,
    ) -> McpServerToolCallResult:
        return await self.rpc_mcp_server_tool_call(McpServerToolCallParams.model_validate(_omit_none({
            "threadId": thread_id, "server": server, "tool": tool, "arguments": arguments, "_meta": meta,
        })))

    async def mcp_server_oauth_login(
        self,
        name: str,
        thread_id: str | None = None,
        scopes: list[str] | None = None,
        timeout_secs: float | None = None,
    ) -> McpServerOAuthLoginResult:
        return await self.rpc_mcp_server_oauth_login(McpServerOAuthLoginParams.model_validate(_omit_none({
            "name": name, "threadId": thread_id, "scopes": scopes, "timeoutSecs": timeout_secs,
        })))

    async def mcp_server_reload(self) -> McpServerReloadResult:
        return await self.rpc_config_mcp_server_reload(RpcEmpty())

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
        disposers: list[Callable[[], None]] = []
        for method in methods:
            def create_handler(method_name: str) -> Callable:
                async def handler(contract_params: BaseModel) -> None:
                    params = contract_params.model_dump(by_alias=True, exclude_unset=True, mode="json")
                    if "threadId" not in params or params["threadId"] == thread_id:
                        await queue.put(JsonRpcMessage(method=method_name, params=params))

                return handler

            handler = create_handler(method)
            disposers.append(self.register_notification(method, handler))
        try:
            while True:
                event = await queue.get()
                yield event
                if event.method in terminal_methods:
                    break
        finally:
            for dispose in disposers:
                dispose()

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

    async def _handle_dynamic_tool_request(
        self, _request_id: str | int, contract_params: BaseModel,
    ) -> dict:
        params = contract_params.model_dump(by_alias=True, exclude_unset=True, mode="json")
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
