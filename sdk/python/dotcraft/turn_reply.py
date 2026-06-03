"""
Final assistant reply from turn/completed snapshots vs streamed deltas.

Delta aggregation can miss early chunks if notification handlers attach after turn/start returns.
Prefer the snapshot when it is at least as long as the concatenated deltas.
"""

from __future__ import annotations

from typing import Any


def extract_agent_reply_text_from_turn_completed(params: dict[str, Any] | None) -> str:
    """Concatenate text from all agentMessage items in turn/completed params (wire order)."""
    return "".join(extract_agent_reply_texts_from_turn_completed(params))


def extract_agent_reply_texts_from_turn_completed(params: dict[str, Any] | None) -> list[str]:
    """Extract text from each agentMessage item in turn/completed params (wire order)."""
    if not params:
        return []
    turn = params.get("turn")
    if not isinstance(turn, dict):
        return []
    items = turn.get("items")
    if not isinstance(items, list):
        return []
    parts: list[str] = []
    for raw in items:
        if not isinstance(raw, dict):
            continue
        if raw.get("type") != "agentMessage":
            continue
        payload = raw.get("payload")
        if not isinstance(payload, dict):
            continue
        text = payload.get("text")
        if isinstance(text, str) and len(text) > 0:
            parts.append(text)
    return parts


def merge_reply_text_from_delta_and_snapshot(delta_text: str, snapshot_text: str) -> str:
    """Prefer the longer of snapshot (from turn/completed) vs streamed deltas."""
    if len(snapshot_text) >= len(delta_text):
        return snapshot_text
    return delta_text
