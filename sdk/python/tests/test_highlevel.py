"""Tests for the high-level DotCraft Python SDK: Run profile, callbacks, App Binding."""

from __future__ import annotations

import asyncio
import json
from pathlib import Path

import dotcraft as dotcraft_sdk
import pytest

from dotcraft import (
    AppBindingHandoff,
    DotCraft,
    TurnFailedError,
    TurnInProgressError,
    image_data_url_part,
)

from dotcraft.client import DotCraftWireClient, RequestTimeoutError
from dotcraft.appserver_client import DotCraftAppServerClient
from dotcraft.events import merge_run_text, normalize
from dotcraft.transport import Transport, TransportClosed

APP_BINDING_V2_FIXTURE = json.loads(
    (Path(__file__).resolve().parents[3] / "specs/protocols/fixtures/app-binding-v2.json").read_text(encoding="utf-8")
)


def test_app_binding_v2_canonical_fixture_is_stable() -> None:
    assert APP_BINDING_V2_FIXTURE["version"] == 2
    assert APP_BINDING_V2_FIXTURE["states"] == [
        "connecting", "syncing", "active", "offline", "needsConfirmation", "revoked", "failed", "cancelled"
    ]
    assert APP_BINDING_V2_FIXTURE["errors"]["upgradeRequired"] == "AppBindingUpgradeRequired"


def test_wire_surface_is_protocol_only() -> None:
    wire = DotCraftWireClient(FakeTransport())
    appserver = DotCraftAppServerClient(FakeTransport())
    assert not hasattr(wire, "thread_start")
    assert not hasattr(wire, "stream_events")
    assert hasattr(appserver, "thread_start")
    assert hasattr(appserver, "stream_events")


def test_image_data_url_part_replaces_remote_url_helper() -> None:
    data_url = "data:image/png;base64,iVBORw0KGgo="
    assert image_data_url_part(data_url) == {"type": "image", "url": data_url}
    assert not hasattr(dotcraft_sdk, "image_url_part")


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
    client = DotCraftAppServerClient(transport)
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


async def test_wire_request_timeout_uses_stable_error_and_state():
    transport = FakeTransport()
    client = DotCraftWireClient(transport, default_timeout=0.01)
    await client.start()

    with pytest.raises(RequestTimeoutError) as error:
        await client.request_raw("fixture/timeout")

    assert error.value.method == "fixture/timeout"
    assert client.state == "ready"
    await client.stop()


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


async def test_mcp_runtime_uses_canonical_methods_and_typed_results():
    dotcraft, transport = await _connect()
    client = dotcraft.client

    status_task = asyncio.create_task(client.mcp_server_status_list("thread-1", "2", 25, "full"))
    request = await _expect(transport, "mcpServerStatus/list", {
        "data": [{
            "name": "docs", "serverInfo": {"name": "Docs", "version": "1"},
            "tools": {"search": {"name": "search"}}, "resources": [],
            "resourceTemplates": [], "authStatus": "oAuth",
            "declaredName": "docs", "runtimeName": "docs",
        }],
        "nextCursor": None,
    })
    assert request["params"] == {"threadId": "thread-1", "cursor": "2", "limit": 25, "detail": "full"}
    status = await status_task
    assert status.data[0].runtime_name == "docs"

    resource_task = asyncio.create_task(client.mcp_server_resource_read("docs", "docs://intro", "thread-1"))
    request = await _expect(transport, "mcpServer/resource/read", {"contents": [{"uri": "docs://intro"}]})
    assert request["params"] == {"threadId": "thread-1", "server": "docs", "uri": "docs://intro"}
    assert (await resource_task).contents[0]["uri"] == "docs://intro"

    tool_task = asyncio.create_task(client.mcp_server_tool_call(
        "thread-1", "docs", "search", {"query": "MCP"}, {"trace": "t1"}
    ))
    request = await _expect(transport, "mcpServer/tool/call", {
        "content": [{"type": "text", "text": "found"}],
        "structuredContent": {"count": 1}, "isError": False, "_meta": {"source": "docs"},
    })
    assert request["params"]["_meta"] == {"trace": "t1"}
    assert (await tool_task).structured_content == {"count": 1}

    login_task = asyncio.create_task(client.mcp_server_oauth_login("docs", "thread-1", ["read"], 60))
    request = await _expect(transport, "mcpServer/oauth/login", {"authorizationUrl": "https://auth.example/"})
    assert request["params"]["timeoutSecs"] == 60
    assert (await login_task).authorization_url == "https://auth.example/"

    reload_task = asyncio.create_task(client.mcp_server_reload())
    request = await _expect(transport, "config/mcpServer/reload", {})
    assert "params" not in request
    assert await reload_task is not None


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


async def test_approval_without_handler_returns_method_not_found():
    dotcraft, transport = await _connect()
    await transport.push({"jsonrpc": "2.0", "id": 5, "method": "item/approval/request", "params": {"threadId": "thread_1"}})
    response = await transport.read_outbound()
    assert response["error"]["code"] == -32601
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
    client = DotCraftAppServerClient(transport)
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
        return {"success": True, "structuredContent": {"tool": call["tool"]}}

    thread.on_tool_call("sample", "Echo", echo)
    await transport.push({"jsonrpc": "2.0", "id": 99, "method": "item/tool/call", "params": {"threadId": "thread_1", "namespace": "sample", "tool": "Echo", "arguments": {"message": "hi"}}})

    response = await transport.read_outbound()
    assert response["result"]["success"] is True
    assert response["result"]["structuredContent"]["tool"] == "Echo"
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


async def test_app_binding_activate_uses_v2_method():
    dotcraft, transport = await _connect()
    activate_task = asyncio.create_task(dotcraft.app_bindings.activate(
        binding_request_id="bind_req_1",
        endpoint="https://example.test/mcp",
        bearer="secret",
    ))
    request = await transport.read_outbound()
    assert request["method"] == "app/binding/activate"
    assert request["params"]["bindingRequestId"] == "bind_req_1"
    assert request["params"]["endpoint"] == "https://example.test/mcp"
    await transport.push(_response(request, {"bindingId": "bind_1", "state": "active"}))
    result = await asyncio.wait_for(activate_task, timeout=5)
    assert result["bindingId"] == "bind_1"
    await dotcraft.close()


async def test_app_binding_surface_methods_use_typed_contracts():
    dotcraft, transport = await _connect()

    publish_task = asyncio.create_task(dotcraft.app_bindings.publish_surface(
        surface_id="board",
        endpoint="http://127.0.0.1:43120/",
        bearer="surface-secret",
    ))
    request = await transport.read_outbound()
    assert request["method"] == "app/surface/publish"
    assert request["params"] == {
        "surfaceId": "board",
        "endpoint": "http://127.0.0.1:43120/",
        "bearer": "surface-secret",
    }
    surface_wire = {
        "appId": "com.example.board",
        "surfaceId": "board",
        "endpoint": "http://127.0.0.1:43120/",
        "bearer": "surface-secret",
        "expiresAt": "2026-07-16T12:02:00Z",
    }
    await transport.push(_response(request, surface_wire))
    published = await asyncio.wait_for(publish_task, timeout=5)
    assert published.app_id == "com.example.board"
    assert published.surface_id == "board"
    assert published.endpoint == "http://127.0.0.1:43120/"
    assert published.bearer == "surface-secret"
    assert published.expires_at == "2026-07-16T12:02:00Z"

    resolve_task = asyncio.create_task(dotcraft.app_bindings.resolve_surface(
        app_id="com.example.board",
        surface_id="board",
    ))
    request = await transport.read_outbound()
    assert request["method"] == "app/surface/resolve"
    assert request["params"] == {
        "appId": "com.example.board",
        "surfaceId": "board",
    }
    await transport.push(_response(request, surface_wire))
    resolved = await asyncio.wait_for(resolve_task, timeout=5)
    assert resolved == published
    await dotcraft.close()


async def test_app_binding_list_thread_bindings_typed():
    dotcraft, transport = await _connect()
    list_task = asyncio.create_task(dotcraft.app_bindings.list_thread_bindings("thread_1"))
    request = await transport.read_outbound()
    assert request["method"] == "thread/appBindings/list"
    await transport.push(_response(request, {"bindings": [
        {"bindingId": "bind_1", "threadId": "thread_1", "appId": "app",
         "state": "active", "authorityRevision": 3, "approvedCapabilityRevision": 2},
    ]}))
    bindings = await asyncio.wait_for(list_task, timeout=5)
    assert len(bindings) == 1
    assert bindings[0].binding_id == "bind_1"
    assert bindings[0].authority_revision == 3
    await dotcraft.close()


# ---------------------------------------------------------------------------
# App Binding + pure helpers
# ---------------------------------------------------------------------------


def test_app_binding_handoff_parse():
    url = "board-example://dotcraft/connect?app=com.example.board&request=req_1&token=tok_1&endpoint=ws://127.0.0.1:1234/x"
    handoff = AppBindingHandoff.parse(url, expected_scheme="board-example", expected_app_id="com.example.board")
    assert handoff.operation == "connect"
    assert handoff.app_id == "com.example.board"
    assert handoff.request_id == "req_1"
    assert handoff.request_token == "tok_1"
    assert handoff.app_server_url == "ws://127.0.0.1:1234/x"


def test_app_binding_handoff_rejects_wrong_scheme():
    url = "evil://dotcraft/connect?app=x&request=r&token=t"
    with pytest.raises(ValueError):
        AppBindingHandoff.parse(url, expected_scheme="board-example")


def test_app_binding_handoff_rejects_alternate_query_names():
    url = "board-example://dotcraft/connect?appId=com.example.board&requestId=req_1&requestToken=tok_1"
    with pytest.raises(ValueError):
        AppBindingHandoff.parse(url)


def test_merge_run_text_prefers_turn_items():
    terminal = {"turn": {"items": [{"type": "agentMessage", "payload": {"text": "final"}}]}}
    assert merge_run_text({"item_1": "fin"}, {}, ["item_1"], terminal) == "final"


def test_merge_run_text_falls_back_to_snapshot():
    assert merge_run_text({"item_1": "par"}, {"item_1": "partial snapshot"}, ["item_1"], None) == "partial snapshot"


def test_normalize_unknown_method_is_raw():
    event = normalize("some/unknown", {"threadId": "thread_1"})
    assert event.type == "raw"
    assert event.thread_id == "thread_1"
