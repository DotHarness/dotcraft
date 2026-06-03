"""Normalized streaming run events, parallel to the TypeScript/.NET SDK event model."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .models import JsonRpcMessage

# Normalized run event type strings (snake_case), mirroring the TypeScript/.NET SDKs.
THREAD_STARTED = "thread_started"
THREAD_RESUMED = "thread_resumed"
THREAD_STATUS_CHANGED = "thread_status_changed"
THREAD_RUNTIME_CHANGED = "thread_runtime_changed"
QUEUE_UPDATED = "queue_updated"
TURN_STARTED = "turn_started"
ITEM_STARTED = "item_started"
ITEM_COMPLETED = "item_completed"
AGENT_MESSAGE_DELTA = "agent_message_delta"
REASONING_DELTA = "reasoning_delta"
TOOL_ARGUMENTS_DELTA = "tool_arguments_delta"
APPROVAL_RESOLVED = "approval_resolved"
USAGE_DELTA = "usage_delta"
SUBAGENT_PROGRESS = "subagent_progress"
PLAN_UPDATED = "plan_updated"
SYSTEM_EVENT = "system_event"
COMPLETED = "completed"
FAILED = "failed"
CANCELLED = "cancelled"
RAW = "raw"

TERMINAL_TYPES = (COMPLETED, FAILED, CANCELLED)

_METHOD_TO_TYPE = {
    "thread/started": THREAD_STARTED,
    "thread/resumed": THREAD_RESUMED,
    "thread/statusChanged": THREAD_STATUS_CHANGED,
    "thread/runtimeChanged": THREAD_RUNTIME_CHANGED,
    "thread/queue/updated": QUEUE_UPDATED,
    "turn/started": TURN_STARTED,
    "item/started": ITEM_STARTED,
    "item/completed": ITEM_COMPLETED,
    "item/agentMessage/delta": AGENT_MESSAGE_DELTA,
    "item/reasoning/delta": REASONING_DELTA,
    "item/toolCall/argumentsDelta": TOOL_ARGUMENTS_DELTA,
    "item/approval/resolved": APPROVAL_RESOLVED,
    "item/usage/delta": USAGE_DELTA,
    "subagent/progress": SUBAGENT_PROGRESS,
    "plan/updated": PLAN_UPDATED,
    "system/event": SYSTEM_EVENT,
    "turn/completed": COMPLETED,
    "turn/failed": FAILED,
    "turn/cancelled": CANCELLED,
}

# Wire methods the run loop subscribes to.
RUN_METHODS = tuple(_METHOD_TO_TYPE.keys())


@dataclass
class RunEvent:
    """A normalized streaming run event. The original notification is on ``raw``."""

    type: str
    thread_id: str
    turn_id: str | None
    raw: JsonRpcMessage

    @property
    def params(self) -> dict:
        return self.raw.params or {}


def event_type(method: str) -> str:
    """Map a wire notification method to a normalized run event type."""
    return _METHOD_TO_TYPE.get(method, RAW)


def is_terminal(event_type_value: str) -> bool:
    return event_type_value in TERMINAL_TYPES


def extract_thread_id(params: dict | None) -> str | None:
    """Extract the thread id from a notification's params, including nested turn/thread."""
    if not isinstance(params, dict):
        return None
    thread_id = params.get("threadId")
    if isinstance(thread_id, str):
        return thread_id
    turn = params.get("turn")
    if isinstance(turn, dict) and isinstance(turn.get("threadId"), str):
        return turn["threadId"]
    thread = params.get("thread")
    if isinstance(thread, dict) and isinstance(thread.get("id"), str):
        return thread["id"]
    return None


def extract_turn_id(params: dict | None) -> str | None:
    if not isinstance(params, dict):
        return None
    turn_id = params.get("turnId")
    if isinstance(turn_id, str):
        return turn_id
    turn = params.get("turn")
    if isinstance(turn, dict) and isinstance(turn.get("id"), str):
        return turn["id"]
    return None


def normalize(method: str, params: dict | None) -> RunEvent:
    return RunEvent(
        type=event_type(method),
        thread_id=extract_thread_id(params) or "",
        turn_id=extract_turn_id(params),
        raw=JsonRpcMessage(method=method, params=params or {}),
    )


def merge_run_text(deltas: dict[str, str], snapshots: dict[str, str], order: list[str], terminal_params: dict[str, Any] | None) -> str:
    """Merge streamed agent-message deltas with final snapshots without duplication.

    Prefers the authoritative ``turn/completed`` items, falling back to streamed
    snapshots/deltas (preferring a snapshot when at least as long as its deltas).
    """
    # Authoritative final items from turn/completed.
    if isinstance(terminal_params, dict):
        turn = terminal_params.get("turn")
        if isinstance(turn, dict) and isinstance(turn.get("items"), list):
            parts = []
            for item in turn["items"]:
                if not isinstance(item, dict) or item.get("type") != "agentMessage":
                    continue
                payload = item.get("payload")
                if isinstance(payload, dict) and isinstance(payload.get("text"), str) and payload["text"]:
                    parts.append(payload["text"])
            if parts:
                return "\n\n".join(parts)

    merged = []
    for item_id in order:
        snapshot = snapshots.get(item_id)
        delta = deltas.get(item_id, "")
        if snapshot is not None and (item_id not in deltas or len(snapshot) >= len(delta)):
            text = snapshot
        else:
            text = delta
        if text:
            merged.append(text)
    return "\n\n".join(merged)
