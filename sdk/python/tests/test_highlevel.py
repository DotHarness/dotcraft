"""Tests for the high-level DotCraft Python SDK: Run profile, callbacks, App Binding."""

from __future__ import annotations

import asyncio

import pytest

from dotcraft import (
    AppBindingHandoff,
    DotCraft,
    TurnFailedError,
    TurnInProgressError,
)
from dotcraft.client import DotCraftClient
from dotcraft.events import merge_run_text, normalize
from dotcraft.transport import Transport, TransportClosed


class FakeTransport(Transport):
    """In-memory transport for driving the client in tests."""

    def __init__(self) -> None:
        self.inbound: asyncio.Queue = asyncio.Queue()
        self.outbound: asyncio.Queue = asyncio.Queue()

    async def read_message(self) -> dict:
        message = await self.inbound.get()
        if message is None:
            raise TransportClosed()
        return message

    async def write_message(self, message: dict) -> None:
        await self.outbound.put(message)

    async def close(self) -> None:
        await self.inbound.put(None)

    async def read_outbound(self) -> dict:
        return await asyncio.wait_for(self.outbound.get(), timeout=5)

    async def push(self, message: dict) -> None:
        await self.inbound.put(message)


def _response(request: dict, result: dict) -> dict:
    return {"jsonrpc": "2.0", "id": request["id"], "result": result}


def _notification(method: str, params: dict) -> dict:
    return {"jsonrpc": "2.0", "method": method, "params": params}


async def _connect(approval=None, user_input=None):
    transport = FakeTransport()
    client = DotCraftClient(transport)
    dotcraft = DotCraft(client)
    dotcraft._install_handlers(approval, user_input)
    await client.start()

    init_task = asyncio.create_task(
        dotcraft._initialize("test", "0.1", None, user_input is not None, None)
    )
    request = await transport.read_outbound()
    assert request["method"] == "initialize"
    await transport.push(_response(request, {
        "serverInfo": {"name": "dotcraft", "version": "1", "protocolVersion": "1"},
        "capabilities": {"threadManagement": True, "threadSubscriptions": True},
    }))
    initialized = await transport.read_outbound()
    assert initialized["method"] == "initialized"
    await init_task
    return dotcraft, transport


async def _start_thread(dotcraft, transport):
    start_task = asyncio.create_task(dotcraft.threads.start(user_id="user"))
    request = await transport.read_outbound()
    assert request["method"] == "thread/start"
    await transport.push(_response(request, {"thread": {"id": "thread_1", "status": "active"}}))
    return await start_task


async def _expect(transport, method, result):
    request = await transport.read_outbound()
    assert request["method"] == method
    await transport.push(_response(request, result))
    return request


# ---------------------------------------------------------------------------
# Run profile
# ---------------------------------------------------------------------------


async def test_run_merges_text_from_turn_completed():
    dotcraft, transport = await _connect()
    thread = await _start_thread(dotcraft, transport)

    run_task = asyncio.create_task(thread.run("hello"))
    await _expect(transport, "thread/subscribe", {"ok": True})
    await _expect(transport, "turn/start", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "running"}})

    await transport.push(_notification("item/agentMessage/delta", {"threadId": "thread_1", "turnId": "turn_1", "itemId": "item_1", "delta": "Hello, "}))
    await transport.push(_notification("item/agentMessage/delta", {"threadId": "thread_1", "turnId": "turn_1", "itemId": "item_1", "delta": "world."}))
    await transport.push(_notification("item/completed", {"threadId": "thread_1", "turnId": "turn_1", "item": {"id": "item_1", "type": "agentMessage", "payload": {"text": "Hello, world."}}}))
    await transport.push(_notification("turn/completed", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "completed", "items": [{"id": "item_1", "type": "agentMessage", "payload": {"text": "Hello, world."}}]}}))

    result = await asyncio.wait_for(run_task, timeout=5)
    assert result.thread_id == "thread_1"
    assert result.turn_id == "turn_1"
    assert result.text == "Hello, world."
    await dotcraft.close()


async def test_run_streamed_yields_normalized_events_in_order():
    dotcraft, transport = await _connect()
    thread = await _start_thread(dotcraft, transport)

    events = []

    async def consume():
        async for event in thread.run_streamed("hi"):
            events.append(event)

    run_task = asyncio.create_task(consume())
    await _expect(transport, "thread/subscribe", {"ok": True})
    await _expect(transport, "turn/start", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "running"}})

    await transport.push(_notification("turn/started", {"threadId": "thread_1", "turnId": "turn_1"}))
    await transport.push(_notification("item/started", {"threadId": "thread_1", "turnId": "turn_1", "item": {"id": "item_1", "type": "agentMessage"}}))
    await transport.push(_notification("item/agentMessage/delta", {"threadId": "thread_1", "turnId": "turn_1", "itemId": "item_1", "delta": "Hi."}))
    await transport.push(_notification("item/completed", {"threadId": "thread_1", "turnId": "turn_1", "item": {"id": "item_1", "type": "agentMessage", "payload": {"text": "Hi."}}}))
    await transport.push(_notification("turn/completed", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "completed"}}))

    await asyncio.wait_for(run_task, timeout=5)
    assert [e.type for e in events] == [
        "turn_started", "item_started", "agent_message_delta", "item_completed", "completed",
    ]
    await dotcraft.close()


async def test_run_raises_turn_failed():
    dotcraft, transport = await _connect()
    thread = await _start_thread(dotcraft, transport)

    run_task = asyncio.create_task(thread.run("hi"))
    await _expect(transport, "thread/subscribe", {"ok": True})
    await _expect(transport, "turn/start", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "running"}})
    await transport.push(_notification("turn/failed", {"turn": {"id": "turn_1", "threadId": "thread_1", "status": "failed"}, "error": "model overloaded"}))

    with pytest.raises(TurnFailedError) as info:
        await asyncio.wait_for(run_task, timeout=5)
    assert info.value.thread_id == "thread_1"
    assert info.value.turn_id == "turn_1"
    await dotcraft.close()


async def test_run_raises_turn_in_progress_when_busy():
    dotcraft, transport = await _connect()
    thread = await _start_thread(dotcraft, transport)

    run_task = asyncio.create_task(thread.run("hi"))
    await _expect(transport, "thread/subscribe", {"ok": True})

    request = await transport.read_outbound()
    assert request["method"] == "turn/start"
    await transport.push({"jsonrpc": "2.0", "id": request["id"], "error": {"code": -32012, "message": "Turn in progress"}})

    with pytest.raises(TurnInProgressError):
        await asyncio.wait_for(run_task, timeout=5)
    await dotcraft.close()


# ---------------------------------------------------------------------------
# Callbacks
# ---------------------------------------------------------------------------


async def test_approval_handler_responds_with_decision():
    captured = {}

    async def approval(params):
        captured.update(params)
        return "decline"

    dotcraft, transport = await _connect(approval=approval)
    await transport.push({"jsonrpc": "2.0", "id": 42, "method": "item/approval/request", "params": {"threadId": "thread_1", "callId": "call_1"}})

    response = await transport.read_outbound()
    assert response["id"] == 42
    assert response["result"]["decision"] == "decline"
    assert captured["callId"] == "call_1"
    await dotcraft.close()


async def test_approval_auto_accepts_without_handler():
    dotcraft, transport = await _connect()
    await transport.push({"jsonrpc": "2.0", "id": 5, "method": "item/approval/request", "params": {"threadId": "thread_1"}})
    response = await transport.read_outbound()
    assert response["result"]["decision"] == "accept"
    await dotcraft.close()


async def test_user_input_handler_responds_with_answers():
    async def user_input(params):
        return {"q1": "a1"}

    dotcraft, transport = await _connect(user_input=user_input)
    await transport.push({"jsonrpc": "2.0", "id": 7, "method": "item/tool/requestUserInput", "params": {"threadId": "thread_1"}})
    response = await transport.read_outbound()
    assert response["result"]["answers"]["q1"] == "a1"
    await dotcraft.close()


async def test_initialize_advertises_user_input_support_when_handler_provided():
    transport = FakeTransport()
    client = DotCraftClient(transport)
    dotcraft = DotCraft(client)

    async def user_input(params):
        return {}

    dotcraft._install_handlers(None, user_input)
    await client.start()
    init_task = asyncio.create_task(dotcraft._initialize("t", "0.1", None, True, None))
    request = await transport.read_outbound()
    assert request["params"]["capabilities"]["requestUserInputSupport"] is True
    await transport.push(_response(request, {"serverInfo": {"name": "d", "version": "1", "protocolVersion": "1"}, "capabilities": {}}))
    await transport.read_outbound()
    await init_task
    await dotcraft.close()


async def test_dynamic_tool_call_routes_to_handler():
    dotcraft, transport = await _connect()
    thread = await _start_thread(dotcraft, transport)

    def echo(call):
        return {"success": True, "structuredResult": {"tool": call["tool"]}}

    thread.on_tool_call("oratorio", "Echo", echo)
    await transport.push({"jsonrpc": "2.0", "id": 99, "method": "item/tool/call", "params": {"threadId": "thread_1", "namespace": "oratorio", "tool": "Echo", "arguments": {"message": "hi"}}})

    response = await transport.read_outbound()
    assert response["result"]["success"] is True
    assert response["result"]["structuredResult"]["tool"] == "Echo"
    await dotcraft.close()


async def test_models_list_returns_typed_info():
    dotcraft, transport = await _connect()
    list_task = asyncio.create_task(dotcraft.models.list())
    request = await transport.read_outbound()
    assert request["method"] == "model/list"
    await transport.push(_response(request, {"models": [
        {"id": "claude-opus-4-8", "displayName": "Claude Opus 4.8", "provider": "anthropic"},
    ]}))
    models = await asyncio.wait_for(list_task, timeout=5)
    assert len(models) == 1
    assert models[0].id == "claude-opus-4-8"
    assert models[0].display_name == "Claude Opus 4.8"
    assert models[0].provider == "anthropic"
    await dotcraft.close()


async def test_app_binding_accept_returns_typed_binding():
    dotcraft, transport = await _connect()
    accept_task = asyncio.create_task(dotcraft.app_bindings.accept_binding(
        binding_request_id="bind_req_1",
        request_token="tok",
        grant_id="grant_1",
        granted_scopes=["board.read"],
        approval_mode="appAccepted",
        approved_by="alice",
    ))
    request = await transport.read_outbound()
    assert request["method"] == "app/binding/accept"
    assert request["params"]["bindingRequestId"] == "bind_req_1"
    assert request["params"]["grantedScopes"] == ["board.read"]
    await transport.push(_response(request, {"binding": {
        "bindingId": "bind_1", "threadId": "thread_1", "appId": "app",
        "state": "active", "grantedScopes": ["board.read"], "attachedToolCount": 0,
    }}))
    result = await asyncio.wait_for(accept_task, timeout=5)
    assert result.binding.binding_id == "bind_1"
    assert result.binding.state == "active"
    assert result.binding.granted_scopes == ["board.read"]
    await dotcraft.close()


async def test_app_binding_list_thread_bindings_typed():
    dotcraft, transport = await _connect()
    list_task = asyncio.create_task(dotcraft.app_bindings.list_thread_bindings("thread_1"))
    request = await transport.read_outbound()
    assert request["method"] == "thread/appBindings/list"
    await transport.push(_response(request, {"bindings": [
        {"bindingId": "bind_1", "threadId": "thread_1", "appId": "app",
         "state": "active", "grantedScopes": ["board.read"], "attachedToolCount": 2},
    ]}))
    bindings = await asyncio.wait_for(list_task, timeout=5)
    assert len(bindings) == 1
    assert bindings[0].binding_id == "bind_1"
    assert bindings[0].attached_tool_count == 2
    await dotcraft.close()


# ---------------------------------------------------------------------------
# App Binding + pure helpers
# ---------------------------------------------------------------------------


def test_app_binding_handoff_parse():
    url = "oratorio://dotcraft/connect?app=com.dotharness.oratorio&request=req_1&token=tok_1&endpoint=ws://127.0.0.1:1234/x"
    handoff = AppBindingHandoff.parse(url, expected_scheme="oratorio", expected_app_id="com.dotharness.oratorio")
    assert handoff.operation == "connect"
    assert handoff.app_id == "com.dotharness.oratorio"
    assert handoff.request_id == "req_1"
    assert handoff.request_token == "tok_1"
    assert handoff.app_server_url == "ws://127.0.0.1:1234/x"


def test_app_binding_handoff_rejects_wrong_scheme():
    url = "evil://dotcraft/connect?app=x&request=r&token=t"
    with pytest.raises(ValueError):
        AppBindingHandoff.parse(url, expected_scheme="oratorio")


def test_merge_run_text_prefers_turn_items():
    terminal = {"turn": {"items": [{"type": "agentMessage", "payload": {"text": "final"}}]}}
    assert merge_run_text({"item_1": "fin"}, {}, ["item_1"], terminal) == "final"


def test_merge_run_text_falls_back_to_snapshot():
    assert merge_run_text({"item_1": "par"}, {"item_1": "partial snapshot"}, ["item_1"], None) == "partial snapshot"


def test_normalize_unknown_method_is_raw():
    event = normalize("some/unknown", {"threadId": "thread_1"})
    assert event.type == "raw"
    assert event.thread_id == "thread_1"
