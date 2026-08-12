from __future__ import annotations

from typing import Any


PROVIDER_HISTORY_PAYLOADS = {
    "provider_history_items_appended": "providerHistoryItemsAppended",
    "provider_history_replaced": "providerHistoryReplaced",
    "provider_history_attempt_aborted": "providerHistoryAttemptAborted",
}


def summarize_provider_history(
    kind: str,
    payload: Any,
    line_number: int,
    timestamp: Any,
    expected_thread_id: str,
) -> dict[str, Any]:
    summary: dict[str, Any] = {
        "line": line_number,
        "timestamp": timestamp,
        "kind": kind,
        "valid": True,
        "issues": [],
    }
    if not isinstance(payload, dict):
        summary.update({"valid": False, "issues": ["payload must be an object"]})
        return summary

    for key in ("schemaVersion", "threadId", "protocol", "generationId"):
        summary[key] = payload.get(key)
    summary["contextWindowId"] = payload.get("contextWindowId")
    summary["turnId"] = payload.get("turnId")
    summary["coveredThroughTurnId"] = payload.get("coveredThroughTurnId")
    summary["source"] = payload.get("source")
    summary["reason"] = payload.get("reason")
    summary["hasAttemptId"] = bool(payload.get("attemptId"))

    issues: list[str] = []
    if payload.get("schemaVersion") != 1:
        issues.append("unsupported schemaVersion")
    if payload.get("protocol") != "openai-responses":
        issues.append("unsupported protocol")
    if payload.get("threadId") != expected_thread_id:
        issues.append("threadId does not match rollout")
    if not payload.get("generationId"):
        issues.append("missing generationId")

    if kind == "provider_history_items_appended":
        if not payload.get("turnId"):
            issues.append("missing turnId")
        if not payload.get("contextWindowId"):
            issues.append("missing contextWindowId")
        if payload.get("source") not in {"local_input", "provider_output"}:
            issues.append("unsupported source")
    elif kind == "provider_history_replaced":
        if not payload.get("contextWindowId"):
            issues.append("missing contextWindowId")
    elif not payload.get("turnId") or not payload.get("attemptId"):
        issues.append("missing turnId or attemptId")

    if kind != "provider_history_attempt_aborted":
        entries = payload.get("entries")
        summary["entryCount"] = len(entries) if isinstance(entries, list) else None
        if not isinstance(entries, list):
            issues.append("entries must be an array")
        elif any(
            not isinstance(entry, dict)
            or not entry.get("entryId")
            or not isinstance(entry.get("item"), dict)
            for entry in entries
        ):
            issues.append("invalid provider history entry")

    summary["valid"] = not issues
    summary["issues"] = issues
    return summary
