from __future__ import annotations

from types import SimpleNamespace

import pytest

from dotcraft_wire.adapter import ChannelAdapter
from dotcraft_wire.client import DotCraftError
from dotcraft_wire.models import ERR_TURN_IN_PROGRESS, Thread
from dotcraft_wire.transport import Transport


class _DummyTransport(Transport):
    async def read_message(self) -> dict:
        raise RuntimeError("not used in tests")

    async def write_message(self, msg: dict) -> None:
        _ = msg

    async def close(self) -> None:
        return


class _TestAdapter(ChannelAdapter):
    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        _ = target, content, metadata
        return True

    async def on_approval_request(self, request: dict) -> str:
        _ = request
        return "accept"


@pytest.mark.asyncio
async def test_process_message_reenqueue_sets_skip_command_true_after_expanded_prompt(monkeypatch: pytest.MonkeyPatch) -> None:
    adapter = _TestAdapter(
        transport=_DummyTransport(),
        channel_name="test",
        client_name="test-client",
        client_version="0.0.0",
    )
    thread = Thread(id="thread-1", status="active")

    async def fake_get_or_create_thread(
        identity_key: str,
        user_id: str,
        channel_context: str,
        workspace_path: str,
    ) -> Thread:
        _ = identity_key, user_id, channel_context, workspace_path
        return thread

    async def fake_sleep(_seconds: float) -> None:
        return

    command_execute_calls = 0

    async def fake_command_execute(**_kwargs: object) -> dict:
        nonlocal command_execute_calls
        command_execute_calls += 1
        return {"expandedPrompt": "expanded"}

    async def fake_turn_start(*_args: object, **_kwargs: object) -> object:
        raise DotCraftError(ERR_TURN_IN_PROGRESS, "turn already in progress")

    reenqueue_calls: list[dict] = []

    async def fake_handle_message(**kwargs: object) -> None:
        reenqueue_calls.append(dict(kwargs))

    adapter._get_or_create_thread = fake_get_or_create_thread  # type: ignore[method-assign]
    adapter.handle_message = fake_handle_message  # type: ignore[method-assign]
    adapter._client = SimpleNamespace(
        command_execute=fake_command_execute,
        turn_start=fake_turn_start,
    )
    monkeypatch.setattr("dotcraft_wire.adapter.asyncio.sleep", fake_sleep)

    await adapter._process_message(
        "user-1:",
        {
            "user_id": "user-1",
            "user_name": "User",
            "text": "/summarize some text",
            "channel_context": "",
            "workspace_path": "E:/Git/dotcraft",
            "sender_extra": {},
            "skip_command": False,
            "input_parts": None,
        },
    )

    assert command_execute_calls == 1
    assert len(reenqueue_calls) == 1
    assert reenqueue_calls[0]["skip_command"] is True
