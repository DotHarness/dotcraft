"""High-level DotCraft client: facade, thread manager, active thread handle, and Run profile.

Parallel to the TypeScript and .NET high-level SDKs (see Unified SDK Specification §2.1).
"""

from __future__ import annotations

import asyncio
import getpass
from dataclasses import dataclass, field
from typing import Any, AsyncIterator, Awaitable, Callable

from pydantic import BaseModel

from .app_binding import AppBindingManager
from ._appserver_client import _AppServerClient
from .client import DotCraftError
from .errors import TurnCancelledError, TurnFailedError, TurnInProgressError
from .events import (
    AGENT_MESSAGE_DELTA,
    CANCELLED,
    FAILED,
    ITEM_COMPLETED,
    QUEUE_UPDATED,
    RUN_METHODS,
    RunEvent,
    extract_thread_id,
    is_terminal,
    merge_run_text,
    normalize,
)
from .hub import HubClient
from .models import (
    ERR_TURN_IN_PROGRESS,
    JsonRpcMessage,
    text_part,
)
from .contracts import (
    ModelCatalogItem,
    McpServerOAuthLoginResult,
    McpServerReloadResult,
    McpServerResourceReadResult,
    McpServerStatusListResult,
    McpServerToolCallResult,
    ServerCapabilities,
    ServerInfo,
    SessionThread,
    ThreadSummary,
    TurnEnqueueResult,
)

ApprovalHandler = Callable[[dict], Awaitable[str] | str]
UserInputHandler = Callable[[dict], Awaitable[dict] | dict]


def _default_user_id() -> str:
    try:
        return getpass.getuser() or "local-user"
    except Exception:
        return "local-user"


@dataclass
class RemoteOptions:
    url: str
    token: str | None = None
    client_name: str = "dotcraft-python"
    client_version: str = "0.0.0"
    client_title: str | None = None
    approval_handler: ApprovalHandler | None = None
    user_input_handler: UserInputHandler | None = None
    capabilities: dict | None = None


@dataclass
class LocalOptions:
    workspace_path: str
    client_name: str = "dotcraft-python"
    client_version: str = "0.0.0"
    client_title: str | None = None
    executable: str | None = None
    home_dir: str | None = None
    hub_startup_timeout: float = 15.0
    approval_handler: ApprovalHandler | None = None
    user_input_handler: UserInputHandler | None = None
    capabilities: dict | None = None


@dataclass
class LocalChatOptions:
    client_name: str = "dotcraft-python"
    client_version: str = "0.0.0"
    client_title: str | None = None
    executable: str | None = None
    home_dir: str | None = None
    hub_startup_timeout: float = 15.0
    approval_handler: ApprovalHandler | None = None
    user_input_handler: UserInputHandler | None = None
    capabilities: dict | None = None


@dataclass
class RunResult:
    thread_id: str
    turn_id: str | None
    text: str
    turn: dict | None = None
    raw_events: list | None = None


class DotCraft:
    """One initialized AppServer connection with the high-level surface."""

    def __init__(self, client: _AppServerClient) -> None:
        self._client = client
        self._approval_configured = False
        self._user_input_configured = False
        self._server_info: ServerInfo | None = None
        self._capabilities: ServerCapabilities | None = None
        self._threads = ThreadManager(self)
        self._app_bindings = AppBindingManager(client)
        self._models = ModelManager(client)
        self._mcp_runtime = McpRuntimeManager(client)

    @property
    def server_info(self) -> ServerInfo | None:
        return self._server_info

    @property
    def capabilities(self) -> ServerCapabilities | None:
        return self._capabilities

    @property
    def threads(self) -> "ThreadManager":
        return self._threads

    @property
    def app_bindings(self) -> AppBindingManager:
        return self._app_bindings

    @property
    def models(self) -> "ModelManager":
        return self._models

    @property
    def mcp_runtime(self) -> "McpRuntimeManager":
        return self._mcp_runtime

    @property
    def client(self) -> _AppServerClient:
        return self._client

    @classmethod
    async def connect_remote(cls, options: RemoteOptions) -> "DotCraft":
        from .transport import WebSocketTransport

        transport = WebSocketTransport(options.url, token=options.token)
        client = _AppServerClient(transport, auto_reconnect=True)
        self = cls(client)
        self._install_handlers(options.approval_handler, options.user_input_handler)
        await client.connect()
        await self._initialize(options.client_name, options.client_version, options.client_title,
                               options.user_input_handler is not None, options.capabilities)
        return self

    @classmethod
    async def connect_local(cls, options: LocalOptions) -> "DotCraft":
        hub = HubClient(executable=options.executable, home_dir=options.home_dir)
        ensured = await hub.ensure_app_server(
            options.workspace_path,
            client_name=options.client_name,
            client_version=options.client_version,
            start_if_missing=True,
            startup_timeout=options.hub_startup_timeout,
        )
        return await cls.connect_remote(RemoteOptions(
            url=ensured.ws_url,
            token=ensured.token,
            client_name=options.client_name,
            client_version=options.client_version,
            client_title=options.client_title,
            approval_handler=options.approval_handler,
            user_input_handler=options.user_input_handler,
            capabilities=options.capabilities,
        ))

    @classmethod
    async def connect_local_chat(cls, options: LocalChatOptions | None = None) -> "DotCraft":
        value = options or LocalChatOptions()
        hub = HubClient(executable=value.executable, home_dir=value.home_dir)
        ensured = await hub.ensure_default_chat_app_server(
            client_name=value.client_name,
            client_version=value.client_version,
            start_if_missing=True,
            startup_timeout=value.hub_startup_timeout,
        )
        return await cls.connect_remote(RemoteOptions(
            url=ensured.ws_url,
            token=ensured.token,
            client_name=value.client_name,
            client_version=value.client_version,
            client_title=value.client_title,
            approval_handler=value.approval_handler,
            user_input_handler=value.user_input_handler,
            capabilities=value.capabilities,
        ))

    async def request_raw(self, method: str, params: dict | None = None) -> Any:
        """Raw AppServer request escape hatch."""
        return await self._client.request_raw(method, params)

    async def notify_raw(self, method: str, params: dict | None = None) -> None:
        await self._client.notify_raw(method, params)

    def on_raw(self, method: str, handler: Callable[[dict], Awaitable[None]]) -> Callable[[], None]:
        """Register a raw notification handler. Returns an unregister callable."""
        self._client.register_notification_raw(method, handler)
        return lambda: self._client.unregister_notification_raw(method, handler)

    def register_dynamic_tool_handler(self, handler, thread_id=None, namespace=None, tool=None):
        return self._client.register_dynamic_tool_handler(handler, thread_id, namespace, tool)

    async def close(self) -> None:
        await self._client.stop()

    async def __aenter__(self) -> "DotCraft":
        return self

    async def __aexit__(self, *exc) -> None:
        await self.close()

    # ------------------------------------------------------------------
    # Internal
    # ------------------------------------------------------------------

    def _install_handlers(self, approval: ApprovalHandler | None, user_input: UserInputHandler | None) -> None:
        if approval is not None:
            async def _approval(_request_id, contract_params: BaseModel):
                params = contract_params.model_dump(by_alias=True, exclude_unset=True, mode="json")
                result = approval(params)
                if asyncio.iscoroutine(result):
                    result = await result
                return {"decision": result}
            self._client.register_server_request_handler("item/approval/request", _approval)
            self._approval_configured = True

        if user_input is not None:
            async def _user_input(_request_id, contract_params: BaseModel):
                params = contract_params.model_dump(by_alias=True, exclude_unset=True, mode="json")
                result = user_input(params)
                if asyncio.iscoroutine(result):
                    result = await result
                return {"answers": result or {}}
            self._client.register_server_request_handler("item/tool/requestUserInput", _user_input)
            self._user_input_configured = True

    async def _initialize(self, client_name, client_version, client_title, request_user_input_support, capabilities):
        if capabilities and capabilities.get("approvalSupport") is True and not self._approval_configured:
            raise ValueError("approvalSupport requires an approval_handler")
        if capabilities and capabilities.get("requestUserInputSupport") is True and not self._user_input_configured:
            raise ValueError("requestUserInputSupport requires a user_input_handler")
        init = await self._client.initialize(
            client_name=client_name,
            client_version=client_version,
            client_title=client_title,
            approval_support=self._approval_configured,
            streaming_support=True,
            request_user_input_support=self._user_input_configured,
            config_change=True,
            extra_capabilities=capabilities,
        )
        self._server_info = init.server_info
        self._capabilities = init.capabilities


class ModelManager:
    """Lists the model catalog of the connected AppServer."""

    def __init__(self, client: _AppServerClient) -> None:
        self._client = client

    async def list(self) -> list[ModelCatalogItem]:
        return await self._client.model_list()


class McpRuntimeManager:
    """MCP runtime and control operations."""

    def __init__(self, client: _AppServerClient) -> None:
        self._client = client

    async def list_status(self, **kwargs: Any) -> McpServerStatusListResult:
        return await self._client.mcp_server_status_list(**kwargs)

    async def read_resource(
        self, server: str, uri: str, thread_id: str | None = None
    ) -> McpServerResourceReadResult:
        return await self._client.mcp_server_resource_read(server, uri, thread_id)

    async def call_tool(
        self,
        thread_id: str,
        server: str,
        tool: str,
        arguments: dict | None = None,
        meta: Any = None,
    ) -> McpServerToolCallResult:
        return await self._client.mcp_server_tool_call(
            thread_id, server, tool, arguments, meta
        )

    async def login_oauth(self, **kwargs: Any) -> McpServerOAuthLoginResult:
        return await self._client.mcp_server_oauth_login(**kwargs)

    async def reload(self) -> McpServerReloadResult:
        return await self._client.mcp_server_reload()


class ThreadManager:
    """Starts, resumes, lists, reads, and reuses threads."""

    def __init__(self, dotcraft: DotCraft) -> None:
        self._dotcraft = dotcraft
        self._client = dotcraft.client

    async def start(
        self,
        user_id: str | None = None,
        channel_name: str = "sdk",
        channel_context: str = "",
        workspace_path: str = "",
        display_name: str | None = None,
        dynamic_tools: list[dict] | None = None,
    ) -> "DotCraftThread":
        model = await self._client.thread_start(
            channel_name,
            user_id or _default_user_id(),
            workspace_path,
            channel_context,
            display_name,
            dynamic_tools=dynamic_tools,
        )
        return DotCraftThread(self._dotcraft, model)

    async def resume(self, thread_id: str) -> "DotCraftThread":
        model = await self._client.thread_resume(thread_id)
        return DotCraftThread(self._dotcraft, model)

    async def list(
        self,
        user_id: str | None = None,
        channel_name: str = "sdk",
        channel_context: str = "",
        include_archived: bool = False,
    ) -> list[ThreadSummary]:
        return await self._client.thread_list(
            channel_name,
            user_id or _default_user_id(),
            channel_context=channel_context,
            include_archived=include_archived,
        )

    async def read(self, thread_id: str, include_turns: bool = False) -> SessionThread:
        return await self._client.thread_read(thread_id, include_turns)

    async def get_or_create(
        self,
        user_id: str | None = None,
        channel_name: str = "sdk",
        channel_context: str = "",
        **start_opts,
    ) -> "DotCraftThread":
        existing = await self._client.thread_list(
            channel_name, user_id or _default_user_id(), channel_context=channel_context,
        )
        for model in existing:
            if model.status == "active":
                active = await self._client.thread_read(model.id)
                return DotCraftThread(self._dotcraft, active)
            if model.status == "paused":
                resumed = await self._client.thread_resume(model.id)
                return DotCraftThread(self._dotcraft, resumed)
        return await self.start(
            user_id=user_id, channel_name=channel_name, channel_context=channel_context, **start_opts,
        )


class DotCraftThread:
    """Active handle to one server-backed thread, exposing the Run profile."""

    def __init__(self, dotcraft: DotCraft, model: SessionThread) -> None:
        self._dotcraft = dotcraft
        self._client = dotcraft.client
        self._model = model
        self.id = model.id
        self._subscribed = False
        self._subscribe_lock = asyncio.Lock()

    @property
    def snapshot(self) -> SessionThread:
        return self._model

    async def run(
        self,
        input,
        sender: dict | None = None,
        collect_raw_events: bool = False,
        enqueue_if_busy: bool = False,
        throw_on_failure: bool = True,
    ) -> RunResult:
        deltas: dict[str, str] = {}
        snapshots: dict[str, str] = {}
        order: list[str] = []
        turn_id: str | None = None
        terminal_type: str | None = None
        terminal_params: dict | None = None
        raw: list = []

        async for event in self.run_streamed(input, sender=sender, enqueue_if_busy=enqueue_if_busy):
            if collect_raw_events:
                raw.append(event.raw)
            _accept(event, deltas, snapshots, order)
            if turn_id is None and event.turn_id:
                turn_id = event.turn_id
            if is_terminal(event.type):
                terminal_type = event.type
                terminal_params = event.params

        text = merge_run_text(deltas, snapshots, order, terminal_params)

        if throw_on_failure and terminal_type == FAILED:
            message = _terminal_error(terminal_params, FAILED) or "The turn failed."
            raise TurnFailedError(message, self.id, turn_id)
        if throw_on_failure and terminal_type == CANCELLED:
            raise TurnCancelledError(self.id, turn_id, _terminal_error(terminal_params, CANCELLED))

        turn_obj = terminal_params.get("turn") if isinstance(terminal_params, dict) else None
        return RunResult(
            thread_id=self.id,
            turn_id=turn_id,
            text=text,
            turn=turn_obj,
            raw_events=raw if collect_raw_events else None,
        )

    async def run_streamed(
        self,
        input,
        sender: dict | None = None,
        enqueue_if_busy: bool = False,
    ) -> AsyncIterator[RunEvent]:
        parts, sender = _to_parts(input, sender)
        await self._ensure_subscribed()

        queue: asyncio.Queue = asyncio.Queue()
        disposers: list[Callable[[], None]] = []
        for method in RUN_METHODS:
            async def handler(contract_params: BaseModel, _method=method):
                params = contract_params.model_dump(by_alias=True, exclude_unset=True, mode="json")
                tid = extract_thread_id(params)
                if tid is not None and tid != self.id:
                    return
                await queue.put((_method, params))
            disposers.append(self._client.register_notification(method, handler))

        try:
            try:
                await self._client.turn_start(self.id, parts, sender)
            except DotCraftError as error:
                if getattr(error, "rpc_code", None) == ERR_TURN_IN_PROGRESS:
                    if not enqueue_if_busy:
                        raise TurnInProgressError() from error
                    enqueued = await self._client.turn_enqueue(self.id, parts, sender)
                    yield RunEvent(
                        QUEUE_UPDATED,
                        self.id,
                        None,
                        JsonRpcMessage(
                            method="turn/enqueue",
                            params=enqueued.model_dump(by_alias=True, exclude_unset=True, mode="json"),
                        ),
                    )
                    return
                raise

            while True:
                method, params = await queue.get()
                event = normalize(method, params)
                yield event
                if is_terminal(event.type):
                    break
        finally:
            for dispose in disposers:
                dispose()

    async def enqueue(self, input, sender: dict | None = None) -> TurnEnqueueResult:
        parts, sender = _to_parts(input, sender)
        return await self._client.turn_enqueue(self.id, parts, sender)

    async def interrupt(self, turn_id: str) -> None:
        await self._client.turn_interrupt(self.id, turn_id)

    async def subscribe(self, replay_recent: bool = False) -> None:
        await self._client.thread_subscribe(self.id, replay_recent)
        self._subscribed = True

    async def unsubscribe(self) -> None:
        await self._client.thread_unsubscribe(self.id)
        self._subscribed = False

    async def set_mode(self, mode: str) -> None:
        await self._client.thread_set_mode(self.id, mode)

    async def archive(self) -> None:
        await self._client.thread_archive(self.id)

    async def delete(self) -> None:
        await self._client.thread_delete(self.id)

    async def refresh(self, include_turns: bool = False) -> SessionThread:
        self._model = await self._client.thread_read(self.id, include_turns)
        return self._model

    def on_tool_call(self, namespace: str | None, name: str, handler: Callable) -> Callable[[], None]:
        return self._client.register_dynamic_tool_handler(handler, self.id, namespace, name)

    async def _ensure_subscribed(self) -> None:
        if self._subscribed:
            return
        async with self._subscribe_lock:
            if self._subscribed:
                return
            await self._client.thread_subscribe(self.id, replay_recent=False)
            self._subscribed = True


def _to_parts(input, sender):
    if isinstance(input, str):
        return [text_part(input)], sender
    if isinstance(input, dict) and "input" in input:
        return input["input"], input.get("sender", sender)
    if isinstance(input, list):
        return input, sender
    raise TypeError("Run input must be a string, a list of parts, or a {'input': [...]} mapping.")


def _accept(event: RunEvent, deltas: dict[str, str], snapshots: dict[str, str], order: list[str]) -> None:
    if event.type == AGENT_MESSAGE_DELTA:
        item_id = event.params.get("itemId")
        delta = event.params.get("delta")
        if not isinstance(item_id, str) or not isinstance(delta, str):
            return
        deltas[item_id] = deltas.get(item_id, "") + delta
        if item_id not in order:
            order.append(item_id)
    elif event.type == ITEM_COMPLETED:
        item = event.params.get("item")
        if not isinstance(item, dict) or item.get("type") != "agentMessage":
            return
        item_id = item.get("id")
        payload = item.get("payload")
        text = payload.get("text") if isinstance(payload, dict) else None
        if isinstance(item_id, str) and isinstance(text, str):
            snapshots[item_id] = text
            if item_id not in order:
                order.append(item_id)


def _terminal_error(params: dict | None, terminal_type: str) -> str | None:
    if not isinstance(params, dict):
        return None
    if terminal_type == FAILED:
        error = params.get("error")
        if isinstance(error, str):
            return error
        turn = params.get("turn")
        if isinstance(turn, dict) and isinstance(turn.get("error"), str):
            return turn["error"]
    if terminal_type == CANCELLED:
        reason = params.get("reason")
        if isinstance(reason, str):
            return reason
    return None
