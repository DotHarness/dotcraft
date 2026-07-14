"""DotCraftClient: JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol."""

from __future__ import annotations

import asyncio
import json
import logging
from typing import Any, AsyncIterator, Callable, Coroutine, Literal

from .models import (
    DynamicToolDeclaration,
    DynamicToolResult,
    InitializeResult,
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
from .transport import Transport, TransportClosed, TransportError

logger = logging.getLogger(__name__)

# Type aliases
Handler = Callable[[dict], Coroutine]
RequestHandler = Callable[[str | int, dict], Coroutine[Any, Any, Any]]


def _omit_none(values: dict) -> dict:
    return {key: value for key, value in values.items() if value is not None}


class DotCraftError(Exception):
    """Raised when the server returns a JSON-RPC error response."""

    def __init__(self, code: int, message: str, data: Any = None) -> None:
        super().__init__(f"[{code}] {message}")
        self.code = code
        self.message = message
        self.data = data


class DotCraftClient:
    """
    Transport-agnostic JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol.

    Handles:
    - Request/response correlation via asyncio Futures
    - Notification dispatch to registered handlers
    - Server-initiated request handling (approval, delivery, heartbeat)
    - Background reader loop
    """

    def __init__(self, transport: Transport) -> None:
        self._transport = transport
        self._next_id = 1
        self._pending: dict[int | str, asyncio.Future] = {}
        self._handlers: dict[str, list[Handler]] = {}
        self._request_handlers: dict[str, RequestHandler] = {}
        self._approval_handler: RequestHandler | None = None
        self._user_input_handler: RequestHandler | None = None
        self._dynamic_tool_handlers: dict[str, Callable] = {}
        self._fallback_dynamic_tool_handler: Callable | None = None
        self._reader_task: asyncio.Task | None = None
        self._initialized = False

    # ------------------------------------------------------------------
    # Connection lifecycle
    # ------------------------------------------------------------------

    async def connect(self) -> None:
        """Connect the underlying transport (WebSocket mode only)."""
        from .transport import WebSocketTransport
        if isinstance(self._transport, WebSocketTransport):
            await self._transport.connect()

    async def start(self) -> None:
        """Start the background reader loop."""
        self._reader_task = asyncio.create_task(self._reader_loop(), name="dotcraft-reader")

    async def stop(self) -> None:
        """Stop the client and close the transport."""
        if self._reader_task and not self._reader_task.done():
            self._reader_task.cancel()
            try:
                await self._reader_task
            except asyncio.CancelledError:
                pass
        await self._transport.close()

    # ------------------------------------------------------------------
    # Initialization handshake
    # ------------------------------------------------------------------

    async def initialize(
        self,
        client_name: str,
        client_version: str,
        client_title: str | None = None,
        approval_support: bool = True,
        streaming_support: bool = True,
        request_user_input_support: bool = False,
        config_change: bool = False,
        opt_out_notifications: list[str] | None = None,
        channel_name: str | None = None,
        delivery_support: bool = True,
        delivery_capabilities: dict | None = None,
        channel_tools: list[dict] | None = None,
        extra_capabilities: dict | None = None,
    ) -> InitializeResult:
        """
        Perform the initialize / initialized handshake.

        If channel_name is provided, the channelAdapter capability is included,
        identifying this client as an external channel adapter.
        """
        if self._reader_task is None:
            await self.start()

        capabilities: dict = {
            "approvalSupport": approval_support,
            "streamingSupport": streaming_support,
        }
        if request_user_input_support:
            capabilities["requestUserInputSupport"] = True
        if config_change:
            capabilities["configChange"] = True
        if extra_capabilities:
            capabilities.update(extra_capabilities)
        if opt_out_notifications:
            capabilities["optOutNotificationMethods"] = opt_out_notifications
        if channel_name:
            capabilities["channelAdapter"] = {
                "channelName": channel_name,
                "deliverySupport": delivery_support,
            }
            if delivery_capabilities is not None:
                capabilities["channelAdapter"]["deliveryCapabilities"] = delivery_capabilities
            if channel_tools:
                capabilities["channelAdapter"]["channelTools"] = channel_tools

        client_info: dict = {
            "name": client_name,
            "version": client_version,
        }
        if client_title:
            client_info["title"] = client_title

        result = await self._request("initialize", {
            "clientInfo": client_info,
            "capabilities": capabilities,
        })

        # Send the initialized notification
        await self._notify("initialized", {})
        self._initialized = True

        return InitializeResult.from_wire(result)

    # ------------------------------------------------------------------
    # Thread methods
    # ------------------------------------------------------------------

    async def thread_start(
        self,
        channel_name: str,
        user_id: str,
        workspace_path: str = "",
        channel_context: str = "",
        display_name: str | None = None,
        history_mode: str = "server",
        dynamic_tools: list[DynamicToolDeclaration | dict] | None = None,
    ) -> Thread:
        """Create a new thread."""
        identity: dict = {
            "channelName": channel_name,
            "userId": user_id,
        }
        if workspace_path:
            identity["workspacePath"] = workspace_path
        if channel_context:
            identity["channelContext"] = channel_context

        params: dict = {
            "identity": identity,
            "historyMode": history_mode,
        }
        if display_name is not None:
            params["displayName"] = display_name
        if dynamic_tools is not None:
            params["dynamicTools"] = [
                tool.to_wire() if hasattr(tool, "to_wire") else tool
                for tool in dynamic_tools
            ]

        result = await self._request("thread/start", params)
        return Thread.from_wire(result["thread"])

    async def thread_resume(
        self,
        thread_id: str,
        dynamic_tools: list[DynamicToolDeclaration | dict] | None = None,
    ) -> Thread:
        """Resume a paused thread."""
        params: dict = {"threadId": thread_id}
        if dynamic_tools is not None:
            params["dynamicTools"] = [
                tool.to_wire() if hasattr(tool, "to_wire") else tool
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
        """List threads for a given identity."""
        identity: dict = {
            "channelName": channel_name,
            "userId": user_id,
        }
        if workspace_path:
            identity["workspacePath"] = workspace_path
        if channel_context:
            identity["channelContext"] = channel_context

        params: dict = {
            "identity": identity,
            "includeArchived": include_archived,
        }
        if query:
            params["query"] = query
        if limit is not None:
            params["limit"] = limit
        if cursor:
            params["cursor"] = cursor

        result = await self._request("thread/list", params)
        return [Thread.from_wire(t) for t in result.get("data", [])]

    async def thread_read(
        self,
        thread_id: str,
        include_turns: bool = False,
        turn_limit: int | None = None,
        cursor: str | None = None,
    ) -> Thread:
        """Read a thread by ID."""
        params: dict = {
            "threadId": thread_id,
            "includeTurns": include_turns,
        }
        if turn_limit is not None:
            params["turnLimit"] = turn_limit
        if cursor:
            params["cursor"] = cursor
        result = await self._request("thread/read", params)
        return Thread.from_wire(result["thread"])

    async def thread_subscribe(self, thread_id: str, replay_recent: bool = False) -> None:
        """Subscribe to future events for a thread."""
        await self._request("thread/subscribe", {
            "threadId": thread_id,
            "replayRecent": replay_recent,
        })

    async def thread_unsubscribe(self, thread_id: str) -> None:
        """Remove subscription from a thread."""
        await self._request("thread/unsubscribe", {"threadId": thread_id})

    async def thread_pause(self, thread_id: str) -> None:
        """Pause an active thread."""
        await self._request("thread/pause", {"threadId": thread_id})

    async def thread_archive(self, thread_id: str) -> None:
        """Archive a thread."""
        await self._request("thread/archive", {"threadId": thread_id})

    async def thread_delete(self, thread_id: str) -> None:
        """Permanently delete a thread."""
        await self._request("thread/delete", {"threadId": thread_id})

    async def thread_set_mode(self, thread_id: str, mode: str) -> None:
        """Set the agent mode for a thread."""
        await self._request("thread/mode/set", {"threadId": thread_id, "mode": mode})

    # ------------------------------------------------------------------
    # Turn methods
    # ------------------------------------------------------------------

    async def turn_start(
        self,
        thread_id: str,
        input: list[dict],
        sender: dict | None = None,
    ) -> Turn:
        """Submit user input to a thread and begin agent execution."""
        params: dict = {
            "threadId": thread_id,
            "input": input,
        }
        if sender:
            params["sender"] = sender

        result = await self._request("turn/start", params)
        return Turn.from_wire(result["turn"])

    async def turn_enqueue(
        self,
        thread_id: str,
        input: list[dict],
        sender: dict | None = None,
    ) -> dict:
        """Enqueue input to run after the active turn finishes."""
        params: dict = {
            "threadId": thread_id,
            "input": input,
        }
        if sender:
            params["sender"] = sender
        return await self._request("turn/enqueue", params)

    async def turn_interrupt(self, thread_id: str, turn_id: str) -> None:
        """Request cancellation of an in-progress turn."""
        await self._request("turn/interrupt", {
            "threadId": thread_id,
            "turnId": turn_id,
        })

    async def model_list(self) -> list[ModelInfo]:
        """List available models from the connected AppServer (``model/list``)."""
        result = await self._request("model/list", {})
        items = None
        if isinstance(result, dict):
            items = result.get("models") or result.get("items")
        elif isinstance(result, list):
            items = result
        return [ModelInfo.from_wire(m) for m in (items or []) if isinstance(m, dict)]

    async def command_list(self, language: str | None = None) -> list[dict]:
        """List commands exposed by the server command registry."""
        params: dict = {}
        if language:
            params["language"] = language
        result = await self._request("command/list", params)
        return result.get("commands", [])

    async def command_execute(
        self,
        thread_id: str,
        command: str,
        arguments: list[str] | None = None,
        sender: dict | None = None,
    ) -> dict:
        """Execute a slash command via the server command pipeline."""
        params: dict = {
            "threadId": thread_id,
            "command": command,
        }
        if arguments is not None:
            params["arguments"] = arguments
        if sender is not None:
            params["sender"] = sender
        return await self._request("command/execute", params)

    # ------------------------------------------------------------------
    # MCP runtime/control
    # ------------------------------------------------------------------

    async def mcp_server_status_list(
        self,
        thread_id: str | None = None,
        cursor: str | None = None,
        limit: int | None = None,
        detail: Literal["full", "toolsAndAuthOnly"] | None = None,
    ) -> McpServerStatusListResult:
        params = _omit_none({
            "threadId": thread_id,
            "cursor": cursor,
            "limit": limit,
            "detail": detail,
        })
        return McpServerStatusListResult.from_wire(
            await self._request("mcpServerStatus/list", params)
        )

    async def mcp_server_resource_read(
        self,
        server: str,
        uri: str,
        thread_id: str | None = None,
    ) -> McpServerResourceReadResult:
        result = await self._request("mcpServer/resource/read", _omit_none({
            "threadId": thread_id,
            "server": server,
            "uri": uri,
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
            "threadId": thread_id,
            "server": server,
            "tool": tool,
            "arguments": arguments,
            "_meta": meta,
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
            "name": name,
            "threadId": thread_id,
            "scopes": scopes,
            "timeoutSecs": timeout_secs,
        }))
        return McpServerOAuthLoginResult(result.get("authorizationUrl", ""))

    async def mcp_server_reload(self) -> McpServerReloadResult:
        await self._request("config/mcpServer/reload")
        return McpServerReloadResult()

    # ------------------------------------------------------------------
    # Event streaming
    # ------------------------------------------------------------------

    def on(self, method: str) -> Callable:
        """
        Decorator to register a notification handler.

        Usage::

            @client.on("turn/completed")
            async def handle_done(params):
                print("Turn completed", params)
        """
        def decorator(fn: Handler) -> Handler:
            self._handlers.setdefault(method, []).append(fn)
            return fn
        return decorator

    def register_handler(self, method: str, fn: Handler) -> None:
        """Register a notification handler programmatically."""
        self._handlers.setdefault(method, []).append(fn)

    def unregister_handler(self, method: str, fn: Handler) -> None:
        """Remove a previously registered notification handler."""
        if method in self._handlers:
            try:
                self._handlers[method].remove(fn)
            except ValueError:
                pass

    def on_server_request(self, method: str) -> Callable:
        """
        Decorator to register a handler for server-initiated requests.

        The handler receives (request_id, params) and must return the result dict.

        Usage::

            @client.on_server_request("ext/channel/deliver")
            async def handle_deliver(request_id, params):
                print("Deliver:", params["content"])
                return {"delivered": True}
        """
        def decorator(fn: RequestHandler) -> RequestHandler:
            self._request_handlers[method] = fn
            return fn
        return decorator

    @property
    def on_approval_request(self) -> Callable:
        """
        Decorator to register the approval request handler.

        The handler receives (request_id, params) and must return a decision string.

        Usage::

            @client.on_approval_request
            async def handle_approval(request_id, params):
                return "accept"
        """
        def decorator(fn: RequestHandler) -> RequestHandler:
            self._approval_handler = fn
            return fn
        return decorator

    async def stream_events(
        self,
        thread_id: str,
        terminal_methods: tuple[str, ...] = ("turn/completed", "turn/failed", "turn/cancelled"),
    ) -> AsyncIterator[JsonRpcMessage]:
        """
        Async generator that yields notifications for a thread until the turn ends.

        Filters notifications by threadId where applicable.
        Stops automatically when a terminal turn notification is received.
        """
        queue: asyncio.Queue[JsonRpcMessage | None] = asyncio.Queue()
        terminal_seen = False

        async def enqueue(params: dict) -> None:
            nonlocal terminal_seen
            # Filter by threadId when present in params
            if "threadId" in params and params["threadId"] != thread_id:
                return
            msg = JsonRpcMessage(method=_current_method[0], params=params)
            await queue.put(msg)

        # Sentinel to track current method inside closure
        _current_method: list[str] = [""]

        # Register handlers for all relevant methods
        all_methods = [
            "thread/started", "thread/renamed", "thread/resumed", "thread/statusChanged",
            "turn/started", "turn/completed", "turn/failed", "turn/cancelled",
            "item/started", "item/completed",
            "item/agentMessage/delta", "item/reasoning/delta",
            "item/approval/resolved",
            "subagent/progress", "item/usage/delta", "system/event", "plan/updated",
        ]

        async def make_handler(method_name: str) -> Handler:
            async def handler(params: dict) -> None:
                _current_method[0] = method_name
                await enqueue(params)
            return handler

        handlers: dict[str, Handler] = {}
        for m in all_methods:
            h = await make_handler(m)
            handlers[m] = h
            self.register_handler(m, h)

        try:
            while True:
                msg = await queue.get()
                if msg is None:
                    break
                yield msg
                if msg.method in terminal_methods:
                    break
        finally:
            for m, h in handlers.items():
                self.unregister_handler(m, h)

    # ------------------------------------------------------------------
    # Raw escape hatch
    # ------------------------------------------------------------------

    async def request(self, method: str, params: dict | None = None) -> Any:
        """Send a raw JSON-RPC request and return the result. Public escape hatch."""
        return await self._request(method, params)

    async def notify(self, method: str, params: dict | None = None) -> None:
        """Send a raw JSON-RPC notification."""
        await self._notify(method, params or {})

    # ------------------------------------------------------------------
    # User-input and runtime dynamic tool callbacks
    # ------------------------------------------------------------------

    @property
    def on_user_input_request(self) -> Callable:
        """Decorator to register the user-input request handler.

        The handler receives (request_id, params) and returns an answers dict.
        """
        def decorator(fn: RequestHandler) -> RequestHandler:
            self._user_input_handler = fn
            return fn
        return decorator

    def register_dynamic_tool_handler(
        self,
        handler: Callable,
        thread_id: str | None = None,
        namespace: str | None = None,
        tool: str | None = None,
    ) -> Callable[[], None]:
        """Register a runtime dynamic tool handler.

        With no thread_id/tool, registers a catch-all fallback. Returns an unregister callable.
        The handler receives a call dict and returns a result dict or ``DynamicToolResult``
        (``{"success": True, "contentItems": [...], "structuredContent": {...}}``
        or ``{"success": False, "errorCode": "...", "errorMessage": "..."}``).
        """
        if thread_id is None and tool is None:
            self._fallback_dynamic_tool_handler = handler

            def _unregister_fallback() -> None:
                if self._fallback_dynamic_tool_handler is handler:
                    self._fallback_dynamic_tool_handler = None

            return _unregister_fallback

        key = self._tool_key(thread_id or "", namespace, tool or "")
        self._dynamic_tool_handlers[key] = handler

        def _unregister() -> None:
            if self._dynamic_tool_handlers.get(key) is handler:
                self._dynamic_tool_handlers.pop(key, None)

        return _unregister

    @staticmethod
    def _tool_key(thread_id: str, namespace: str | None, tool: str) -> str:
        return f"{thread_id}\x00{namespace or ''}\x00{tool}"

    # ------------------------------------------------------------------
    # Internal: JSON-RPC primitives
    # ------------------------------------------------------------------

    def _next_request_id(self) -> int:
        rid = self._next_id
        self._next_id += 1
        return rid

    async def _request(self, method: str, params: dict | None = None) -> Any:
        """Send a JSON-RPC request and wait for the response."""
        rid = self._next_request_id()
        future: asyncio.Future = asyncio.get_event_loop().create_future()
        self._pending[rid] = future

        msg = JsonRpcMessage(method=method, id=rid, params=params)
        await self._transport.write_message(msg.to_dict())

        try:
            return await future
        except asyncio.CancelledError:
            self._pending.pop(rid, None)
            raise

    async def _notify(self, method: str, params: dict) -> None:
        """Send a JSON-RPC notification (no id, no response expected)."""
        msg = JsonRpcMessage(method=method, params=params)
        await self._transport.write_message(msg.to_dict())

    async def _send_response(self, request_id: int | str, result: Any) -> None:
        """Send a JSON-RPC response to a server-initiated request."""
        msg = JsonRpcMessage(id=request_id, result=result)
        await self._transport.write_message(msg.to_dict())

    async def _send_error_response(
        self, request_id: int | str, code: int, message: str
    ) -> None:
        """Send a JSON-RPC error response."""
        msg = JsonRpcMessage(id=request_id, error={"code": code, "message": message})
        await self._transport.write_message(msg.to_dict())

    # ------------------------------------------------------------------
    # Internal: Reader loop
    # ------------------------------------------------------------------

    async def _reader_loop(self) -> None:
        """Background task: read messages and dispatch them."""
        while True:
            try:
                raw = await self._transport.read_message()
            except TransportClosed:
                logger.debug("DotCraftClient: transport closed, stopping reader loop")
                # Cancel all pending futures
                for fut in self._pending.values():
                    if not fut.done():
                        fut.cancel()
                break
            except TransportError as e:
                logger.error("DotCraftClient: transport error: %s", e)
                break
            except Exception as e:
                logger.error("DotCraftClient: unexpected error in reader loop: %s", e)
                break

            try:
                msg = JsonRpcMessage.from_dict(raw)
                await self._dispatch(msg)
            except Exception as e:
                logger.error("DotCraftClient: error dispatching message: %s", e)

    async def _dispatch(self, msg: JsonRpcMessage) -> None:
        """Dispatch a parsed message to the appropriate handler."""
        if msg.is_response:
            # Server replied to one of our requests
            fut = self._pending.pop(msg.id, None)
            if fut is None:
                logger.warning("Received response for unknown id: %s", msg.id)
                return
            if msg.error:
                exc = DotCraftError(
                    msg.error.get("code", -1),
                    msg.error.get("message", "Unknown error"),
                    msg.error.get("data"),
                )
                fut.set_exception(exc)
            else:
                fut.set_result(msg.result)

        elif msg.is_notification:
            # Server pushed a notification
            await self._dispatch_notification(msg)

        elif msg.is_request:
            # Fire-and-forget: do not block the reader loop on long-running handlers (e.g.
            # approval waiting for user input), or heartbeat responses will not be read in time.

            async def _safe_server_request():
                try:
                    await self._dispatch_server_request(msg)
                except Exception as e:
                    logger.error("Error in server request handler for %s: %s", msg.method, e)

            asyncio.create_task(_safe_server_request())

    async def _dispatch_notification(self, msg: JsonRpcMessage) -> None:
        """Call all registered handlers for a notification method."""
        handlers = self._handlers.get(msg.method or "", [])
        params = msg.params or {}
        for handler in list(handlers):
            async def _safe_call(h=handler, p=params):
                try:
                    await h(p)
                except Exception as e:
                    logger.error("Error in notification handler for %s: %s", msg.method, e)
            asyncio.create_task(_safe_call())

    async def _dispatch_server_request(self, msg: JsonRpcMessage) -> None:
        """Handle a server-initiated JSON-RPC request and send the response."""
        method = msg.method or ""
        params = msg.params or {}
        request_id = msg.id

        # Approval request has a dedicated handler
        if method == "item/approval/request":
            handler = self._approval_handler
            if handler is None:
                # Default: auto-accept if no handler registered
                logger.warning("No approval handler registered; auto-accepting")
                await self._send_response(request_id, {"decision": "accept"})
                return
            try:
                decision = await handler(request_id, params)
                await self._send_response(request_id, {"decision": decision})
            except Exception as e:
                logger.error("Approval handler error: %s", e)
                await self._send_response(request_id, {"decision": "cancel"})
            return

        # User-input request (Plan Mode and tools)
        if method == "item/tool/requestUserInput":
            handler = self._user_input_handler
            if handler is None:
                await self._send_response(request_id, {"answers": {}})
                return
            try:
                answers = await handler(request_id, params)
                await self._send_response(request_id, {"answers": answers or {}})
            except Exception as e:
                logger.error("User-input handler error: %s", e)
                await self._send_response(request_id, {"answers": {}})
            return

        # Runtime dynamic tool call
        if method == "item/tool/call":
            await self._send_response(request_id, await self._handle_dynamic_tool_call(params))
            return

        # Heartbeat: always respond immediately
        if method == "ext/channel/heartbeat":
            await self._send_response(request_id, {})
            return

        # Other server requests (ext/channel/deliver, etc.)
        handler = self._request_handlers.get(method)
        if handler is None:
            logger.warning("No handler registered for server request method: %s", method)
            await self._send_error_response(request_id, -32601, f"Method not handled: {method}")
            return

        try:
            result = await handler(request_id, params)
            await self._send_response(request_id, result or {})
        except Exception as e:
            logger.error("Server request handler error for %s: %s", method, e)
            await self._send_error_response(request_id, -32603, str(e))

    async def _handle_dynamic_tool_call(self, params: dict) -> dict:
        """Route a server-initiated item/tool/call to a registered dynamic tool handler."""
        thread_id = params.get("threadId", "")
        namespace = params.get("namespace")
        tool = params.get("tool", "")
        key = self._tool_key(thread_id, namespace, tool)
        handler = self._dynamic_tool_handlers.get(key) or self._fallback_dynamic_tool_handler
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
        except Exception as e:
            return {
                "success": False,
                "errorCode": "AdapterToolCallFailed",
                "errorMessage": str(e),
            }
