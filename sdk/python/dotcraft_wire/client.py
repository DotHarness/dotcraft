"""DotCraftClient: JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol."""

from __future__ import annotations

import asyncio
import json
import logging
from typing import Any, AsyncIterator, Callable, Coroutine

from .models import (
    InitializeResult,
    JsonRpcMessage,
    Thread,
    Turn,
)
from .transport import Transport, TransportClosed, TransportError

logger = logging.getLogger(__name__)

# Type aliases
Handler = Callable[[dict], Coroutine]
RequestHandler = Callable[[str | int, dict], Coroutine[Any, Any, Any]]


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
        opt_out_notifications: list[str] | None = None,
        channel_name: str | None = None,
        delivery_support: bool = True,
        delivery_capabilities: dict | None = None,
        channel_tools: list[dict] | None = None,
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

        result = await self._request("thread/start", params)
        return Thread.from_wire(result["thread"])

    async def thread_resume(self, thread_id: str) -> Thread:
        """Resume a paused thread."""
        result = await self._request("thread/resume", {"threadId": thread_id})
        return Thread.from_wire(result["thread"])

    async def thread_list(
        self,
        channel_name: str,
        user_id: str,
        workspace_path: str = "",
        channel_context: str = "",
        include_archived: bool = False,
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

        result = await self._request("thread/list", {
            "identity": identity,
            "includeArchived": include_archived,
        })
        return [Thread.from_wire(t) for t in result.get("data", [])]

    async def thread_read(self, thread_id: str, include_turns: bool = False) -> Thread:
        """Read a thread by ID."""
        result = await self._request("thread/read", {
            "threadId": thread_id,
            "includeTurns": include_turns,
        })
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

    async def turn_interrupt(self, thread_id: str, turn_id: str) -> None:
        """Request cancellation of an in-progress turn."""
        await self._request("turn/interrupt", {
            "threadId": thread_id,
            "turnId": turn_id,
        })

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

        msg = JsonRpcMessage(method=method, id=rid, params=params or {})
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
