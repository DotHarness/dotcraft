"""
ChannelAdapter: high-level base class for building external channel adapters.

Implements the behavioral contract from specs/protocols/external-channel-adapter.md §10:
- initialize handshake with channelAdapter capability
- Thread-per-identity mapping via SessionIdentity
- Per-thread message serialization (queue while a turn is running)
- Approval flow: server request → platform UI → JSON-RPC response
- Delivery: ext/channel/send → platform send
"""

from __future__ import annotations

import asyncio
import logging
from abc import ABC, abstractmethod
from typing import AsyncIterator

from .appserver_client import DotCraftAppServerClient
from .client import DotCraftError
from .models import ERR_TURN_IN_PROGRESS, Thread
from .transport import Transport
from .turn_reply import (
    extract_agent_reply_text_from_turn_completed,
    extract_agent_reply_texts_from_turn_completed,
    merge_reply_text_from_delta_and_snapshot,
)

logger = logging.getLogger(__name__)


def _command_ref_part(raw_text: str) -> dict:
    normalized = raw_text.strip()
    token, _, rest = normalized.partition(" ")
    if not token:
        return {"type": "text", "text": normalized}
    name = token.lstrip("/")
    part = {"type": "commandRef", "name": name, "rawText": normalized}
    args_text = rest.strip()
    if args_text:
        part["argsText"] = args_text
    return part


class ChannelAdapter(ABC):
    """
    Base class for DotCraft external channel adapters.

    Subclasses implement on_deliver() and on_approval_request() for
    platform-specific behavior. Call handle_message() from platform event
    handlers to route user messages through DotCraft.
    """

    # Default workspace path. Override per instance or in handle_message().
    DEFAULT_WORKSPACE_PATH: str = ""

    def __init__(
        self,
        transport: Transport,
        channel_name: str,
        client_name: str,
        client_version: str,
        opt_out_notifications: list[str] | None = None,
    ) -> None:
        self._channel_name = channel_name
        self._client = DotCraftAppServerClient(transport, auto_reconnect=True)

        # Thread identity cache: identity_key -> thread_id
        self._thread_map: dict[str, str] = {}
        # Per-thread message queues to serialize concurrent messages
        self._thread_queues: dict[str, asyncio.Queue] = {}
        # Per-thread worker tasks
        self._thread_workers: dict[str, asyncio.Task] = {}

        self._client_name = client_name
        self._client_version = client_version
        self._opt_out = opt_out_notifications or []
        self._running = False

    # ------------------------------------------------------------------
    # Abstract methods — implement in subclass
    # ------------------------------------------------------------------

    @abstractmethod
    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        """
        Called when DotCraft asks the adapter to send a message to the platform.

        Args:
            target: Platform-specific delivery target (e.g. "group:12345").
            content: Message content (plain text or markdown).
            metadata: Optional channel-specific delivery hints.

        Returns:
            True if delivered successfully, False otherwise.
        """

    async def on_send(self, target: str, message: dict, metadata: dict) -> dict:
        """
        Called when DotCraft asks the adapter to send a structured payload.

        Default behavior preserves text-only compatibility by forwarding text
        messages to on_deliver() and rejecting non-text messages.
        """
        kind = str(message.get("kind", ""))
        if kind == "text":
            ok = await self.on_deliver(target, str(message.get("text", "")), metadata)
            return {"delivered": ok}

        return {
            "delivered": False,
            "errorCode": "UnsupportedDeliveryKind",
            "errorMessage": f"Adapter does not implement structured '{kind}' delivery.",
        }

    @abstractmethod
    async def on_approval_request(self, request: dict) -> str:
        """
        Called when the agent needs user approval for a sensitive operation.

        Present a platform-native approval UI and return the user's decision.

        Args:
            request: Full approval request params from the wire protocol.

        Returns:
            One of: "accept", "acceptForSession", "acceptAlways", "decline", "cancel".
        """

    # ------------------------------------------------------------------
    # Optional hooks — override for custom behavior
    # ------------------------------------------------------------------

    async def on_turn_completed(
        self,
        thread_id: str,
        turn_id: str,
        reply_text: str,
        channel_context: str,
    ) -> None:
        """
        Called when a turn completes and a reply is ready to send.

        Default behavior: call on_deliver() with the target derived from
        channel_context. Override for custom routing or formatting.
        """
        if reply_text:
            await self.on_deliver(channel_context, reply_text, {})

    async def on_turn_failed(self, thread_id: str, turn_id: str, error: str) -> None:
        """Called when a turn fails. Override to notify the user."""
        logger.error("Turn %s failed on thread %s: %s", turn_id, thread_id, error)

    async def on_turn_cancelled(self, thread_id: str, turn_id: str) -> None:
        """Called when a turn is cancelled."""
        logger.info("Turn %s cancelled on thread %s", turn_id, thread_id)

    async def on_segment_completed(
        self,
        thread_id: str,
        turn_id: str,
        segment_text: str,
        is_final: bool,
        channel_context: str,
    ) -> None:
        """Called when an assistant text segment is completed during streaming."""
        return

    def get_delivery_capabilities(self) -> dict | None:
        """Return channelAdapter.deliveryCapabilities for initialize, or None for text-only adapters."""
        return None

    def get_channel_tools(self) -> list[dict] | None:
        """Return channelAdapter.channelTools for initialize, or None when no tools are exposed."""
        return None

    async def on_tool_call(self, request: dict) -> dict:
        """Handle ext/channel/toolCall for a declared channel tool."""
        return {
            "success": False,
            "errorCode": "UnsupportedTool",
            "errorMessage": "Adapter does not implement channel tool calls.",
        }

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    async def start(self) -> None:
        """Connect, initialize, register handlers, and start the message loop."""
        await self._client.connect()
        await self._client.start()

        # High-level Channel policy is registered before capabilities are advertised.
        self._client.register_server_request_handler_raw("item/approval/request", self._handle_approval_request)
        self._client.register_server_request_handler_raw("ext/channel/send", self._handle_send_request)
        self._client.register_server_request_handler_raw("ext/channel/toolCall", self._handle_tool_call_request)
        self._client.register_server_request_handler_raw("ext/channel/heartbeat", self._handle_heartbeat)

        await self._client.initialize(
            client_name=self._client_name,
            client_version=self._client_version,
            approval_support=True,
            streaming_support=True,
            opt_out_notifications=self._opt_out,
            channel_name=self._channel_name,
            delivery_support=True,
            delivery_capabilities=self.get_delivery_capabilities(),
            channel_tools=self.get_channel_tools(),
        )
        self._running = True

        logger.info(
            "ChannelAdapter '%s' started (client: %s %s)",
            self._channel_name, self._client_name, self._client_version,
        )

    async def stop(self) -> None:
        """Stop the adapter and all worker tasks."""
        self._running = False
        for task in list(self._thread_workers.values()):
            task.cancel()
        await self._client.stop()
        logger.info("ChannelAdapter '%s' stopped", self._channel_name)

    # ------------------------------------------------------------------
    # Message handling
    # ------------------------------------------------------------------

    async def handle_message(
        self,
        user_id: str,
        user_name: str,
        text: str,
        channel_context: str = "",
        workspace_path: str = "",
        sender_extra: dict | None = None,
        skip_command: bool = False,
    ) -> None:
        """
        Route an incoming platform message to DotCraft.

        Slash commands are executed immediately when a thread is already cached
        for this identity (bypassing the per-identity queue) so commands like
        /stop can cancel an in-flight turn. Otherwise finds or creates the
        thread and enqueues the message for serial processing.

        Args:
            user_id: Platform user identifier.
            user_name: Human-readable user name (for SenderContext).
            text: Message text.
            channel_context: Group/chat identifier for group scenarios.
            workspace_path: Override the workspace path for thread/start.
            sender_extra: Additional fields for SenderContext.
            skip_command: Internal: when True, skip command fast-path; queued
                worker will not re-run command_execute (e.g. expandedPrompt text).
        """
        identity_key = self._identity_key(user_id, channel_context)
        effective_skip_command = skip_command
        input_parts: list[dict] | None = None

        # Command fast-path: bypass queue when we already know the thread id
        # (mirrors built-in adapters that resolve commands before session queue).
        if not skip_command:
            trimmed = text.strip()
            if trimmed.startswith("/"):
                thread_id = self._thread_map.get(identity_key)
                if thread_id:
                    sender: dict = {
                        "senderId": user_id,
                        "senderName": user_name,
                        **(sender_extra or {}),
                    }
                    if channel_context:
                        sender["groupId"] = channel_context
                    parts = trimmed.split()
                    command_name = parts[0]
                    command_arguments = parts[1:] if len(parts) > 1 else None
                    try:
                        command_result = await self._client.command_execute(
                            thread_id=thread_id,
                            command=command_name,
                            arguments=command_arguments,
                            sender=sender,
                        )
                        expanded_prompt = command_result.get("expandedPrompt")
                        if expanded_prompt:
                            effective_skip_command = True
                            input_parts = [_command_ref_part(trimmed)]
                        elif command_result.get("handled"):
                            await self._apply_command_reset_result(
                                identity_key,
                                user_id,
                                channel_context,
                                workspace_path or self.DEFAULT_WORKSPACE_PATH,
                                command_name,
                                command_result,
                            )
                            message = command_result.get("message")
                            if message:
                                await self.on_deliver(channel_context, message, {})
                            return
                        else:
                            # RPC consumed the line; do not re-enqueue or command_execute runs twice
                            return
                    except DotCraftError as e:
                        await self.on_deliver(channel_context, e.message or str(e), {})
                        return

        # Ensure a queue and worker exist for this identity
        if identity_key not in self._thread_queues:
            self._thread_queues[identity_key] = asyncio.Queue()
            self._thread_workers[identity_key] = asyncio.create_task(
                self._thread_worker(identity_key),
                name=f"dotcraft-worker-{identity_key}",
            )

        await self._thread_queues[identity_key].put({
            "user_id": user_id,
            "user_name": user_name,
            "text": text,
            "channel_context": channel_context,
            "workspace_path": workspace_path or self.DEFAULT_WORKSPACE_PATH,
            "sender_extra": sender_extra or {},
            "skip_command": effective_skip_command,
            "input_parts": input_parts,
        })

    def _identity_key(self, user_id: str, channel_context: str) -> str:
        return f"{user_id}:{channel_context}"

    def _on_threads_archived(self, identity_key: str, archived_thread_ids: list[str]) -> None:
        """Hook for subclasses to cleanup per-thread local state."""
        _ = identity_key
        _ = archived_thread_ids

    async def _apply_command_reset_result(
        self,
        identity_key: str,
        user_id: str,
        channel_context: str,
        workspace_path: str,
        command_name: str,
        command_result: dict,
    ) -> None:
        archived_ids = [str(v) for v in (command_result.get("archivedThreadIds") or []) if v]
        if archived_ids:
            self._on_threads_archived(identity_key, archived_ids)

        reset_thread = command_result.get("thread")
        session_reset = bool(command_result.get("sessionReset")) or isinstance(reset_thread, dict)
        if session_reset:
            if isinstance(reset_thread, dict) and reset_thread.get("id"):
                self._thread_map[identity_key] = str(reset_thread.get("id"))
            else:
                self._thread_map.pop(identity_key, None)
            return

        # Backward compatibility with servers that handle /new but do not return reset payload.
        if command_name.strip().lower() == "/new":
            await self._reset_identity_threads(user_id, channel_context, workspace_path)

    async def _reset_identity_threads(
        self,
        user_id: str,
        channel_context: str,
        workspace_path: str,
    ) -> list[str]:
        identity_key = self._identity_key(user_id, channel_context)
        archived_ids: set[str] = set()
        cached = self._thread_map.pop(identity_key, None)
        if cached:
            archived_ids.add(cached)

        threads = await self._client.thread_list(
            channel_name=self._channel_name,
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=workspace_path,
        )
        for thread in threads:
            if thread.status in ("active", "paused"):
                archived_ids.add(thread.id)

        for thread_id in archived_ids:
            try:
                await self._client.thread_archive(thread_id)
            except DotCraftError as e:
                logger.warning("Could not archive thread %s: %s", thread_id, e)
        archived = sorted(archived_ids)
        if archived:
            self._on_threads_archived(identity_key, archived)
        return archived

    async def _recover_thread_after_not_active(
        self,
        identity_key: str,
        user_id: str,
        channel_context: str,
        workspace_path: str,
        stale_thread_id: str,
    ) -> Thread:
        try:
            latest = await self._client.thread_read(stale_thread_id)
            if latest.status == "paused":
                resumed = await self._client.thread_resume(stale_thread_id)
                self._thread_map[identity_key] = resumed.id
                return resumed
            if latest.status == "active":
                self._thread_map[identity_key] = latest.id
                return latest
        except DotCraftError:
            pass

        self._thread_map.pop(identity_key, None)
        threads = await self._client.thread_list(
            channel_name=self._channel_name,
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=workspace_path,
        )
        active = [t for t in threads if t.status in ("active", "paused")]
        if active:
            thread = active[0]
            if thread.status == "paused":
                thread = await self._client.thread_resume(thread.id)
            else:
                thread = await self._client.thread_read(thread.id)
            self._thread_map[identity_key] = thread.id
            return thread

        thread = await self._client.thread_start(
            channel_name=self._channel_name,
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=workspace_path,
        )
        self._thread_map[identity_key] = thread.id
        return thread

    async def _thread_worker(self, identity_key: str) -> None:
        """Worker that processes messages for one identity serially."""
        queue = self._thread_queues[identity_key]
        while self._running:
            try:
                msg = await queue.get()
            except asyncio.CancelledError:
                break
            try:
                await self._process_message(identity_key, msg)
            except Exception as e:
                logger.error("Error processing message for %s: %s", identity_key, e)
            finally:
                queue.task_done()

    async def _process_message(self, identity_key: str, msg: dict) -> None:
        """Process a single message: find/create thread, start turn, stream events."""
        user_id = msg["user_id"]
        user_name = msg["user_name"]
        text = msg["text"]
        channel_context = msg["channel_context"]
        workspace_path = msg["workspace_path"]
        sender_extra = msg["sender_extra"]
        skip_command = msg.get("skip_command", False)
        input_parts = msg.get("input_parts")

        thread = await self._get_or_create_thread(
            identity_key, user_id, channel_context, workspace_path
        )

        sender: dict = {
            "senderId": user_id,
            "senderName": user_name,
        }
        sender.update(sender_extra)
        if channel_context:
            sender["groupId"] = channel_context

        trimmed_text = text.strip()
        if trimmed_text.startswith("/") and not skip_command:
            command_parts = trimmed_text.split()
            command_name = command_parts[0]
            command_arguments = command_parts[1:] if len(command_parts) > 1 else None
            try:
                command_result = await self._client.command_execute(
                    thread_id=thread.id,
                    command=command_name,
                    arguments=command_arguments,
                    sender=sender,
                )
            except DotCraftError as e:
                await self.on_deliver(channel_context, e.message or str(e), {})
                return

            expanded_prompt = command_result.get("expandedPrompt")
            if expanded_prompt:
                input_parts = [_command_ref_part(trimmed_text)]
                skip_command = True
            elif command_result.get("handled"):
                await self._apply_command_reset_result(
                    identity_key,
                    user_id,
                    channel_context,
                    workspace_path,
                    command_name,
                    command_result,
                )
                message = command_result.get("message")
                if message:
                    await self.on_deliver(channel_context, message, {})
                return

        try:
            turn = await self._client.turn_start(
                thread.id,
                input=input_parts if input_parts else [{"type": "text", "text": text}],
                sender=sender,
            )
        except DotCraftError as e:
            from .models import ERR_TURN_IN_PROGRESS, ERR_THREAD_NOT_ACTIVE
            if e.code == ERR_TURN_IN_PROGRESS:
                logger.warning("Turn already in progress on thread %s; message queued", thread.id)
                # Re-enqueue and wait a moment (shouldn't happen with serial worker)
                await asyncio.sleep(1)
                await self.handle_message(
                    user_id=user_id,
                    user_name=user_name,
                    text=text,
                    channel_context=channel_context,
                    workspace_path=workspace_path,
                    sender_extra=sender_extra,
                    skip_command=skip_command,
                )
                return
            if e.code == ERR_THREAD_NOT_ACTIVE:
                logger.info("Thread %s not active, resolving replacement", thread.id)
                thread = await self._recover_thread_after_not_active(
                    identity_key,
                    user_id,
                    channel_context,
                    workspace_path,
                    thread.id,
                )
                turn = await self._client.turn_start(
                    thread.id,
                    input=input_parts if input_parts else [{"type": "text", "text": text}],
                    sender=sender,
                )
            else:
                raise

        # Stream events and keep both full and segment-level buffers.
        all_delta_parts: list[str] = []
        current_segment_parts: list[str] = []

        async for event in self._client.stream_events(thread.id):
            if event.method == "item/agentMessage/delta":
                delta = (event.params or {}).get("delta", "")
                all_delta_parts.append(delta)
                current_segment_parts.append(delta)

            elif event.method == "item/started":
                params = event.params or {}
                item = params.get("item")
                if not isinstance(item, dict):
                    continue
                item_type = str(item.get("type", ""))
                if item_type != "toolCall":
                    continue

                segment_text = "".join(current_segment_parts)
                current_segment_parts.clear()
                if segment_text.strip():
                    await self.on_segment_completed(
                        thread.id,
                        turn.id,
                        segment_text,
                        False,
                        channel_context,
                    )

            elif event.method == "turn/completed":
                params = event.params or {}
                snapshot_parts = extract_agent_reply_texts_from_turn_completed(params)
                snapshot_text = extract_agent_reply_text_from_turn_completed(params)
                delta_text = "".join(all_delta_parts)
                delta_final_segment = "".join(current_segment_parts)
                final_segment = (
                    delta_final_segment
                    if delta_final_segment.strip()
                    else (snapshot_parts[-1] if snapshot_parts else "")
                )
                if final_segment.strip():
                    await self.on_segment_completed(
                        thread.id,
                        turn.id,
                        final_segment,
                        True,
                        channel_context,
                    )
                full_reply = merge_reply_text_from_delta_and_snapshot(delta_text, snapshot_text)
                await self.on_turn_completed(thread.id, turn.id, full_reply, channel_context)
                break

            elif event.method == "turn/failed":
                error = (event.params or {}).get("turn", {}).get("error", "Unknown error")
                await self.on_turn_failed(thread.id, turn.id, error)
                break

            elif event.method == "turn/cancelled":
                await self.on_turn_cancelled(thread.id, turn.id)
                break

    # ------------------------------------------------------------------
    # Thread management
    # ------------------------------------------------------------------

    async def _get_or_create_thread(
        self,
        identity_key: str,
        user_id: str,
        channel_context: str,
        workspace_path: str,
    ) -> Thread:
        """Find an existing active thread or create a new one."""
        # Check local cache first
        thread_id = self._thread_map.get(identity_key)
        if thread_id:
            try:
                thread = await self._client.thread_read(thread_id)
                if thread.status == "active":
                    return thread
                elif thread.status == "paused":
                    return await self._client.thread_resume(thread_id)
                # Archived or deleted — fall through to create new
            except DotCraftError:
                pass
            del self._thread_map[identity_key]

        # Query the server for existing threads
        threads = await self._client.thread_list(
            channel_name=self._channel_name,
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=workspace_path,
        )
        active = [t for t in threads if t.status in ("active", "paused")]
        if active:
            thread = active[0]
            if thread.status == "paused":
                thread = await self._client.thread_resume(thread.id)
            else:
                # thread/list returns persisted thread metadata but does not load per-thread runtime state.
                # thread/read is read-only: it does not open MCP or rebuild the per-thread agent
                # (see session-core.md). The server hydrates Configuration at turn/start before SubmitInput.
                thread = await self._client.thread_read(thread.id)
            self._thread_map[identity_key] = thread.id
            return thread

        # Create a new thread
        thread = await self._client.thread_start(
            channel_name=self._channel_name,
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=workspace_path,
        )
        self._thread_map[identity_key] = thread.id
        logger.info("Created thread %s for identity %s", thread.id, identity_key)
        return thread

    async def new_thread(self, user_id: str, channel_context: str = "") -> None:
        """
        Archive the existing thread (if any) and start a fresh one.

        Call this when the user issues a /new command.
        """
        await self._reset_identity_threads(
            user_id=user_id,
            channel_context=channel_context,
            workspace_path=self.DEFAULT_WORKSPACE_PATH,
        )

    # ------------------------------------------------------------------
    # Server-request handlers
    # ------------------------------------------------------------------

    async def _handle_approval_request(self, request_id, params: dict) -> dict:
        """Route approval request to the subclass."""
        try:
            return {"decision": await self.on_approval_request(params)}
        except Exception as e:
            logger.error("on_approval_request raised: %s", e)
            return {"decision": "cancel"}

    async def _handle_send_request(self, request_id, params: dict) -> dict:
        """Route ext/channel/send to the subclass or default structured handler."""
        target = params.get("target", "")
        message = params.get("message") or {}
        metadata = params.get("metadata") or {}
        try:
            return await self.on_send(str(target), message, metadata)
        except Exception as e:
            logger.error("on_send raised: %s", e)
            return {
                "delivered": False,
                "errorCode": "AdapterDeliveryFailed",
                "errorMessage": str(e),
            }

    async def _handle_tool_call_request(self, request_id, params: dict) -> dict:
        """Route ext/channel/toolCall to the subclass."""
        try:
            return await self.on_tool_call(params or {})
        except Exception as e:
            logger.error("on_tool_call raised: %s", e)
            return {
                "success": False,
                "errorCode": "AdapterToolCallFailed",
                "errorMessage": str(e),
            }

    async def _handle_heartbeat(self, request_id, params: dict) -> dict:
        """Respond to ext/channel/heartbeat immediately."""
        return {}
