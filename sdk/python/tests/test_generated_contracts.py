from __future__ import annotations

from typing import Any

import pytest

from dotcraft._generated.appserver.client_methods_generated import (
    GeneratedAppServerClientMixin,
)
from dotcraft._generated.appserver.models_generated import (
    RuntimeDynamicToolDeclaration,
    ThreadReadParams,
    TokenUsageInfo,
)
from dotcraft._generated.appserver.notification_registry_generated import (
    parse_server_notification,
)
from dotcraft._generated.appserver.protocol_info_generated import CONTRACT_SHA256


class GeneratedClient(GeneratedAppServerClientMixin):
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict[str, Any]]] = []

    async def _request(self, method: str, params: dict | None = None) -> Any:
        self.calls.append((method, params or {}))
        return {"thread": {"id": "thread-1", "status": "idle"}}

    async def _notify(self, method: str, params: dict) -> None:
        self.calls.append((method, params))


def test_generated_models_use_snake_case_aliases_and_preserve_extra_fields() -> None:
    params = ThreadReadParams.model_validate(
        {"threadId": "thread-1", "includeTurns": None, "futureField": {"kept": True}}
    )

    assert params.thread_id == "thread-1"
    assert params.include_turns is None
    assert params.model_extra == {"futureField": {"kept": True}}
    assert params.model_dump(by_alias=True, exclude_unset=True, mode="json") == {
        "threadId": "thread-1",
        "includeTurns": None,
        "futureField": {"kept": True},
    }


def test_generated_models_distinguish_missing_from_explicit_null() -> None:
    missing = ThreadReadParams(thread_id="thread-1")
    explicit_null = ThreadReadParams(thread_id="thread-1", include_turns=None)

    assert "include_turns" not in missing.model_fields_set
    assert "include_turns" in explicit_null.model_fields_set
    assert "includeTurns" not in missing.model_dump(by_alias=True, exclude_unset=True)
    assert explicit_null.model_dump(by_alias=True, exclude_unset=True)["includeTurns"] is None


def test_generated_discriminated_union_and_opaque_json_round_trip() -> None:
    declaration = RuntimeDynamicToolDeclaration.model_validate(
        {
            "type": "function",
            "name": "lookup",
            "description": "Lookup data",
            "inputSchema": {"type": "object", "futureKeyword": [1, True, None]},
        }
    )

    assert declaration.root.type == "function"
    assert declaration.root.input_schema["futureKeyword"] == [1, True, None]


def test_generated_safe_integers_use_python_ints() -> None:
    usage = TokenUsageInfo.model_validate(
        {"inputTokens": 9_007_199_254_740_991, "totalTokens": 42}
    )

    assert usage.input_tokens == 9_007_199_254_740_991
    assert usage.total_tokens == 42
    assert usage.model_dump(by_alias=True, exclude_unset=True, mode="json") == {
        "inputTokens": 9_007_199_254_740_991,
        "totalTokens": 42,
    }


def test_unknown_notifications_keep_the_raw_fallback() -> None:
    params = {"preserveMe": True, "future": {"nested": [1, "two", None]}}
    assert parse_server_notification("fixture/unknownNotification", params) is params
    assert len(CONTRACT_SHA256) == 64


@pytest.mark.asyncio
async def test_generated_mixin_serializes_aliases_and_validates_results() -> None:
    client = GeneratedClient()
    result = await client.rpc_thread_read(ThreadReadParams(thread_id="thread-1"))

    assert result.thread.id == "thread-1"
    assert client.calls == [("thread/read", {"threadId": "thread-1"})]
