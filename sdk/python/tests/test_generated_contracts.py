from __future__ import annotations

from typing import Any

import pytest
from pydantic import ValidationError

from dotcraft._generated.appserver.client_methods_generated import (
    GeneratedAppServerClientMixin,
)
from dotcraft._generated.appserver.item_payloads_generated import (
    SESSION_ITEM_PAYLOAD_MODELS,
    parse_session_item_payload,
)
from dotcraft._generated.appserver.models_generated import (
    AgentMessagePayload,
    RuntimeDynamicToolDeclaration,
    ThreadItemsListParams,
    ThreadReadParams,
    TokenUsageInfo,
)
from dotcraft._generated.appserver.notification_registry_generated import (
    parse_server_notification,
)
from dotcraft._generated.appserver.protocol_info_generated import (
    CONTRACT_FORMAT_VERSION,
    CONTRACT_SHA256,
)
from dotcraft.contracts import ThreadReadParams as PublicThreadReadParams


class GeneratedClient(GeneratedAppServerClientMixin):
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict[str, Any]]] = []

    async def _request(self, method: str, params: dict | None = None) -> Any:
        self.calls.append((method, params or {}))
        return {
            "thread": {
                "id": "thread-1",
                "sessionId": "session-1",
                "workspacePath": "/workspace",
                "cwd": "/workspace",
                "runtimeWorkspaceRoots": ["/workspace"],
                "effectiveWorkspacePath": "/workspace",
                "ephemeral": False,
                "worktree": None,
                "originChannel": "test",
                "source": {"kind": "user"},
                "status": "idle",
                "createdAt": "2026-08-03T00:00:00Z",
                "lastActiveAt": "2026-08-03T00:00:00Z",
                "historyMode": "server",
                "metadata": {},
                "runtime": {},
                "queuedInputs": [],
            }
        }

    async def _notify(self, method: str, params: dict) -> None:
        self.calls.append((method, params))


def test_generated_models_use_snake_case_aliases_and_preserve_extra_fields() -> None:
    assert PublicThreadReadParams is ThreadReadParams
    params = ThreadReadParams.model_validate(
        {"threadId": "thread-1", "futureField": {"kept": True}}
    )

    assert params.thread_id == "thread-1"
    assert params.model_extra == {"futureField": {"kept": True}}
    assert params.model_dump(by_alias=True, exclude_unset=True, mode="json") == {
        "threadId": "thread-1",
        "futureField": {"kept": True},
    }


def test_generated_models_distinguish_missing_from_explicit_null() -> None:
    missing = ThreadItemsListParams(thread_id="thread-1")
    explicit_null = ThreadItemsListParams(thread_id="thread-1", turn_id=None)

    assert "turn_id" not in missing.model_fields_set
    assert "turn_id" in explicit_null.model_fields_set
    assert "turnId" not in missing.model_dump(by_alias=True, exclude_unset=True)
    assert explicit_null.model_dump(by_alias=True, exclude_unset=True)["turnId"] is None


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
    assert CONTRACT_FORMAT_VERSION == 1
    assert len(CONTRACT_SHA256) == 64


PAYLOAD_FIXTURES = {
    "userMessage": {"text": "hi"},
    "agentMessage": {"text": "done"},
    "reasoningContent": {"text": "thinking"},
    "commandExecution": {"command": "pwd", "workingDirectory": "/tmp", "source": "host", "status": "completed", "aggregatedOutput": "/tmp"},
    "toolExecution": {"callId": "call_1", "toolName": "shell", "status": "completed"},
    "imageGeneration": {"callId": "call_1", "status": "completed", "mediaType": "image/png"},
    "toolCall": {"toolName": "shell", "providerFlatName": "shell", "callId": "call_1"},
    "dynamicToolCall": {"toolName": "lookup", "providerFlatName": "lookup", "callId": "call_1", "status": "completed"},
    "mcpToolCall": {"toolName": "read", "providerFlatName": "mcp__read", "server": "docs", "origin": "workspace", "sourceToolId": "read", "callId": "call_1", "status": "completed"},
    "toolResult": {"callId": "call_1", "toolName": "shell", "providerFlatName": "shell", "result": "ok", "success": True},
    "approvalRequest": {"approvalType": "shell", "operation": "pwd", "target": "/tmp", "requestId": "req_1", "scopeKey": "shell:pwd", "reason": "required", "expiresAt": "2026-08-03T01:02:03Z"},
    "approvalResponse": {"requestId": "req_1", "approved": True, "decision": "accept"},
    "userInputRequest": {"requestId": "req_1", "questions": []},
    "userInputResponse": {"requestId": "req_1", "response": {"answers": {}}},
    "error": {"message": "failed", "code": "agent_error", "fatal": True},
    "systemNotice": {"kind": "compacted", "trigger": "manual", "mode": "partial", "tokensBefore": 100, "tokensAfter": 50, "percentLeftAfter": 0.5, "clearedToolResults": 0},
}


@pytest.mark.parametrize(("payload_kind", "payload"), PAYLOAD_FIXTURES.items())
def test_generated_item_payload_registry_parses_all_canonical_kinds(
    payload_kind: str, payload: dict[str, Any]
) -> None:
    raw = {**payload, "futureField": {"kept": True}}
    parsed = parse_session_item_payload(payload_kind, raw)

    assert set(SESSION_ITEM_PAYLOAD_MODELS) == set(PAYLOAD_FIXTURES)
    assert isinstance(parsed, SESSION_ITEM_PAYLOAD_MODELS[payload_kind])
    assert parsed.model_extra == {"futureField": {"kept": True}}


def test_generated_item_payload_parser_preserves_unknown_null_and_invalid_values() -> None:
    raw = {"future": [1, True, None]}
    assert parse_session_item_payload("futurePayload", raw) is raw
    assert parse_session_item_payload("agentMessage", None) is None
    with pytest.raises(ValidationError):
        parse_session_item_payload("agentMessage", {"notText": True})

    typed = parse_session_item_payload("agentMessage", {"text": "done"})
    assert isinstance(typed, AgentMessagePayload)


@pytest.mark.asyncio
async def test_generated_mixin_serializes_aliases_and_validates_results() -> None:
    client = GeneratedClient()
    result = await client.rpc_thread_read(ThreadReadParams(thread_id="thread-1"))

    assert result.thread.id == "thread-1"
    assert client.calls == [("thread/read", {"threadId": "thread-1"})]
