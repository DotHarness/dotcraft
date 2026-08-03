"""DotCraftWireClient: JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol."""

from __future__ import annotations

import asyncio
import json
import logging
import random
from typing import Any, Awaitable, Callable, Literal

from ._generated.appserver.client_methods_generated import GeneratedAppServerClientMixin
from ._generated.appserver.method_groups_generated import (
    SERVER_NOTIFICATION_METHODS,
    SERVER_REQUEST_METHODS,
)
from ._generated.appserver.notification_registry_generated import (
    parse_server_notification,
    parse_server_request,
)
from pydantic import BaseModel

from .contracts import InitializeResult
from .models import JsonRpcMessage
from .transport import Transport, TransportClosed, TransportError

logger = logging.getLogger(__name__)

# Type aliases
Handler = Callable[[dict], Awaitable[Any]]
RequestHandler = Callable[[str | int, dict], Awaitable[Any]]
WireConnectionState = Literal[
    "connecting", "initializing", "ready", "disconnected", "reconnecting", "reconnectError", "closed"
]
_TIMEOUT_UNSET = object()


class DotCraftError(Exception):
    """Base class for stable high-level SDK errors."""

    def __init__(self, code: str, message: str, cause: Any = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.cause = cause


class JsonRpcError(DotCraftError):
    """A JSON-RPC error response from AppServer."""

    def __init__(self, rpc_code: int, message: str, data: Any = None, code: str = "jsonRpcError") -> None:
        super().__init__(code, message, data)
        self.rpc_code = rpc_code
        self.data = data


class RequestTimeoutError(DotCraftError):
    """Raised when a Wire request exceeds its configured timeout."""

    def __init__(self, method: str, timeout: float) -> None:
        super().__init__("requestTimeout", f"Request '{method}' timed out after {timeout:g}s.")
        self.method = method
        self.timeout = timeout


class ReconnectQueueFullError(DotCraftError):
    def __init__(self, message: str = "Wire reconnect request queue is full") -> None:
        super().__init__("reconnectQueueFull", message)


class DotCraftWireClient(GeneratedAppServerClientMixin):
    """
    Transport-agnostic JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol.

    Handles:
    - Request/response correlation via asyncio Futures
    - Notification dispatch to registered handlers
    - Server-initiated request handling (approval, delivery, heartbeat)
    - Background reader loop
    """

    def __init__(
        self,
        transport: Transport,
        *,
        default_timeout: float | None = 30.0,
        initialize_timeout: float | None = None,
        auto_reconnect: bool = False,
        max_reconnect_queue: int = 1024,
        reconnect_initial_delay: float = 1.0,
        reconnect_max_delay: float = 30.0,
    ) -> None:
        self._transport = transport
        self._default_timeout = default_timeout
        self._initialize_timeout = initialize_timeout
        self._auto_reconnect = auto_reconnect
        self._max_reconnect_queue = max_reconnect_queue
        self._reconnect_initial_delay = reconnect_initial_delay
        self._reconnect_max_delay = reconnect_max_delay
        self._queued_requests = 0
        self._next_id = 1
        self._pending: dict[int | str, asyncio.Future] = {}
        self._handlers: dict[str, list[Handler]] = {}
        self._request_handlers: dict[str, RequestHandler] = {}
        self._reader_task: asyncio.Task | None = None
        self._initialized = False
        self._initialize_kwargs: dict[str, Any] | None = None
        self._state: WireConnectionState = "disconnected"
        self._state_handlers: list[Callable[[WireConnectionState, Exception | None], None]] = []
        self._ready = asyncio.Event()

    @property
    def state(self) -> WireConnectionState:
        return self._state

    def on_state_changed(
        self, handler: Callable[[WireConnectionState, Exception | None], None]
    ) -> Callable[[], None]:
        self._state_handlers.append(handler)
        return lambda: self._state_handlers.remove(handler) if handler in self._state_handlers else None

    def _set_state(self, state: WireConnectionState, error: Exception | None = None) -> None:
        self._state = state
        for handler in list(self._state_handlers):
            handler(state, error)

    # ------------------------------------------------------------------
    # Connection lifecycle
    # ------------------------------------------------------------------

    async def connect(self) -> None:
        """Connect the underlying transport (WebSocket mode only)."""
        from .transport import WebSocketTransport
        self._set_state("connecting")
        if isinstance(self._transport, WebSocketTransport):
            await self._transport.connect()
        self._ready.set()
        self._set_state("ready")

    async def start(self) -> None:
        """Start the background reader loop."""
        self._reader_task = asyncio.create_task(self._reader_loop(), name="dotcraft-reader")
        self._ready.set()
        self._set_state("ready")

    async def stop(self) -> None:
        """Stop the client and close the transport."""
        if self._reader_task and not self._reader_task.done():
            self._reader_task.cancel()
            try:
                await self._reader_task
            except asyncio.CancelledError:
                pass
        await self._transport.close()
        self._ready.clear()
        self._set_state("closed")

    # ------------------------------------------------------------------
    # Initialization handshake
    # ------------------------------------------------------------------

    async def initialize(
        self,
        client_name: str,
        client_version: str,
        client_title: str | None = None,
        approval_support: bool = False,
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
        self._initialize_kwargs = {
            "client_name": client_name,
            "client_version": client_version,
            "client_title": client_title,
            "approval_support": approval_support,
            "streaming_support": streaming_support,
            "request_user_input_support": request_user_input_support,
            "config_change": config_change,
            "opt_out_notifications": opt_out_notifications,
            "channel_name": channel_name,
            "delivery_support": delivery_support,
            "delivery_capabilities": delivery_capabilities,
            "channel_tools": channel_tools,
            "extra_capabilities": extra_capabilities,
        }
        if self._reader_task is None:
            await self.start()
        self._ready.clear()
        self._set_state("initializing")

        capabilities: dict = {
            "approvalSupport": approval_support,
            "streamingSupport": streaming_support,
            "appBindingVersion": 2,
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

        result = await self._request_internal("initialize", {
            "clientInfo": client_info,
            "capabilities": capabilities,
        }, self._initialize_timeout, bypass_ready=True)

        # Send the initialized notification
        await self._notify_internal("initialized", {}, bypass_ready=True)
        self._initialized = True
        self._ready.set()
        self._set_state("ready")

        return InitializeResult.model_validate(result)

    # ------------------------------------------------------------------
    # Event streaming
    # ------------------------------------------------------------------

    def on_raw(self, method: str) -> Callable:
        """
        Decorator to register a notification handler.

        Usage::

            @client.on_raw("turn/completed")
            async def handle_done(params):
                print("Turn completed", params)
        """
        def decorator(fn: Handler) -> Handler:
            self._handlers.setdefault(method, []).append(fn)
            return fn
        return decorator

    def register_notification_raw(self, method: str, fn: Handler) -> None:
        """Register a notification handler programmatically."""
        self._handlers.setdefault(method, []).append(fn)

    def register_notification(self, method: str, fn: Callable) -> Callable[[], None]:
        """Register a generated-model handler for a known server notification."""
        if method not in SERVER_NOTIFICATION_METHODS:
            raise ValueError(f"Unknown server notification method: {method}")

        async def typed_handler(params: dict) -> None:
            result = fn(parse_server_notification(method, params))
            if asyncio.iscoroutine(result):
                await result

        self.register_notification_raw(method, typed_handler)
        return lambda: self.unregister_notification_raw(method, typed_handler)

    def unregister_notification_raw(self, method: str, fn: Handler) -> None:
        """Remove a previously registered notification handler."""
        if method in self._handlers:
            try:
                self._handlers[method].remove(fn)
            except ValueError:
                pass

    def on_server_request_raw(self, method: str) -> Callable:
        """
        Decorator to register a handler for server-initiated requests.

        The handler receives (request_id, params) and must return the result dict.

        Usage::

            @client.on_server_request_raw("ext/channel/send")
            async def handle_send(request_id, params):
                print("Send:", params["message"])
                return {"delivered": True}
        """
        def decorator(fn: RequestHandler) -> RequestHandler:
            self._request_handlers[method] = fn
            return fn
        return decorator

    def register_server_request_handler_raw(self, method: str, fn: RequestHandler) -> Callable[[], None]:
        """Register an unknown or extension server-request handler."""
        self._request_handlers[method] = fn

        def unregister() -> None:
            if self._request_handlers.get(method) is fn:
                self._request_handlers.pop(method, None)

        return unregister

    def register_server_request_handler(self, method: str, fn: Callable) -> Callable[[], None]:
        """Register a generated-model handler for a known server request."""
        if method not in SERVER_REQUEST_METHODS:
            raise ValueError(f"Unknown server request method: {method}")

        async def typed_handler(request_id: str | int, params: dict) -> Any:
            result = fn(request_id, parse_server_request(method, params))
            if asyncio.iscoroutine(result):
                result = await result
            if isinstance(result, BaseModel):
                return result.model_dump(by_alias=True, exclude_unset=True, mode="json")
            return result

        return self.register_server_request_handler_raw(method, typed_handler)

    # ------------------------------------------------------------------
    # Raw escape hatch
    # ------------------------------------------------------------------

    async def request_raw(
        self,
        method: str,
        params: dict | None = None,
        *,
        timeout: float | None | object = _TIMEOUT_UNSET,
    ) -> Any:
        """Send a raw JSON-RPC request and return the result. Public escape hatch."""
        effective_timeout: float | None
        if timeout is _TIMEOUT_UNSET:
            effective_timeout = self._default_timeout
        else:
            effective_timeout = timeout  # type: ignore[assignment]
        return await self._request_internal(method, params, effective_timeout)

    async def notify_raw(self, method: str, params: dict | None = None) -> None:
        """Send a raw JSON-RPC notification."""
        await self._notify(method, params or {})

    # ------------------------------------------------------------------
    # Internal: JSON-RPC primitives
    # ------------------------------------------------------------------

    def _next_request_id(self) -> int:
        rid = self._next_id
        self._next_id += 1
        return rid

    async def _request(self, method: str, params: dict | None = None) -> Any:
        """Send a JSON-RPC request and wait for the response."""
        return await self._request_internal(method, params, self._default_timeout)

    async def _request_internal(
        self,
        method: str,
        params: dict | None,
        timeout: float | None,
        *,
        bypass_ready: bool = False,
    ) -> Any:
        async def send_and_wait() -> Any:
            if not bypass_ready and not self._ready.is_set():
                if self._state not in ("initializing", "reconnecting", "reconnectError"):
                    raise TransportClosed("Wire transport is not ready")
                if self._queued_requests >= self._max_reconnect_queue:
                    raise ReconnectQueueFullError("Wire reconnect request queue is full")
                self._queued_requests += 1
                try:
                    await self._ready.wait()
                finally:
                    self._queued_requests -= 1

            rid = self._next_request_id()
            future: asyncio.Future = asyncio.get_running_loop().create_future()
            self._pending[rid] = future
            try:
                msg = JsonRpcMessage(method=method, id=rid, params=params)
                await self._transport.write_message(msg.to_dict())
                return await future
            finally:
                pending = self._pending.pop(rid, None)
                if pending is not None and not pending.done():
                    pending.cancel()

        if timeout is None:
            return await send_and_wait()
        try:
            return await asyncio.wait_for(send_and_wait(), timeout=timeout)
        except asyncio.TimeoutError as exc:
            raise RequestTimeoutError(method, timeout) from exc

    async def _notify(self, method: str, params: dict) -> None:
        """Send a JSON-RPC notification (no id, no response expected)."""
        await self._notify_internal(method, params)

    async def _notify_internal(
        self, method: str, params: dict, *, bypass_ready: bool = False
    ) -> None:
        if not bypass_ready and not self._ready.is_set():
            if self._state not in ("initializing", "reconnecting", "reconnectError"):
                raise TransportClosed("Wire transport is not ready")
            await self._ready.wait()
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
            except TransportClosed as e:
                if await self._handle_disconnect(e):
                    continue
                break
            except TransportError as e:
                if await self._handle_disconnect(e):
                    continue
                break
            except Exception as e:
                logger.error("DotCraftWireClient: unexpected error in reader loop: %s", e)
                break

            try:
                msg = JsonRpcMessage.from_dict(raw)
                await self._dispatch(msg)
            except Exception as e:
                logger.error("DotCraftWireClient: error dispatching message: %s", e)

    async def _handle_disconnect(self, error: Exception) -> bool:
        from .transport import WebSocketTransport

        self._ready.clear()
        self._initialized = False
        for future in list(self._pending.values()):
            if not future.done():
                future.set_exception(TransportClosed("Wire transport disconnected"))
        self._pending.clear()

        if (
            not self._auto_reconnect
            or self._initialize_kwargs is None
            or not isinstance(self._transport, WebSocketTransport)
            or self._state == "closed"
        ):
            self._set_state("disconnected", error)
            return False

        delay = self._reconnect_initial_delay
        self._set_state("reconnecting", error)
        while self._state != "closed":
            jittered = delay * random.uniform(0.8, 1.2)
            await asyncio.sleep(jittered)
            try:
                await self._transport.connect()
                handshake = asyncio.create_task(self.initialize(**self._initialize_kwargs))
                while not handshake.done():
                    raw = await self._transport.read_message()
                    await self._dispatch(JsonRpcMessage.from_dict(raw))
                    await asyncio.sleep(0)
                await handshake
                return True
            except asyncio.CancelledError:
                raise
            except Exception as reconnect_error:
                if 'handshake' in locals() and not handshake.done():
                    handshake.cancel()
                self._set_state("reconnectError", reconnect_error)
                delay = min(delay * 2, self._reconnect_max_delay)
                self._set_state("reconnecting", reconnect_error)
        return False

    async def _dispatch(self, msg: JsonRpcMessage) -> None:
        """Dispatch a parsed message to the appropriate handler."""
        if msg.is_response:
            # Server replied to one of our requests
            request_id = msg.id
            if request_id is None:
                return
            fut = self._pending.pop(request_id, None)
            if fut is None:
                logger.warning("Received response for unknown id: %s", msg.id)
                return
            if msg.error:
                from .errors import error_for_code
                exc = error_for_code(
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
        if request_id is None:
            return

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
