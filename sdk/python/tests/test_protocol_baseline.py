from __future__ import annotations

import json
from pathlib import Path

from dotcraft.events import RAW, normalize


FIXTURES = json.loads(
    (
        Path(__file__).resolve().parents[3]
        / "specs"
        / "protocols"
        / "fixtures"
        / "appserver-v1"
        / "messages.json"
    ).read_text(encoding="utf-8")
)


def _case(name: str) -> dict:
    return next(item for item in FIXTURES["cases"] if item["name"] == name)


def test_shared_appserver_fixtures_cover_lifecycle_callbacks_and_extensions() -> None:
    assert FIXTURES["version"] == 1
    names = {item["name"] for item in FIXTURES["cases"]}
    assert {
        "initialize",
        "thread-start-response-before-notification",
        "thread-resume",
        "thread-read",
        "thread-list",
        "turn-start-and-complete",
        "turn-enqueue-and-interrupt",
        "turn-failed",
        "turn-cancelled",
        "approval-callback",
        "user-input-callback",
        "dynamic-tool-callback",
        "structured-error",
        "opaque-mcp-result",
        "core-domain-catalog",
        "mcp-elicitation-callback",
        "app-binding",
        "automation",
        "teams",
        "acp-callbacks",
        "node-repl-callback",
        "external-channel",
    } <= names


def test_unknown_notification_remains_available_as_raw_event() -> None:
    message = _case("unknown-notification")["messages"][0]
    event = normalize(message["method"], message["params"])

    assert event.type == RAW
    assert event.raw.method == "fixture/unknownNotification"
    assert event.params["preserveMe"] is True
    assert event.params["future"] == {"nested": [1, "two", None]}


def test_opaque_mcp_fields_are_preserved_for_dictionary_consumers() -> None:
    result = _case("opaque-mcp-result")["messages"][1]["result"]

    assert result["content"][0]["futureContentField"] == "kept"
    assert result["structuredContent"]["futureShape"] == ["kept"]
    assert result["futureResultField"] == {"kept": True}
