from __future__ import annotations

import json
import re
from typing import Any


SENSITIVE_PROPERTY_KEYS = {
    "access_token",
    "additionalproperties",
    "api_key",
    "apikey",
    "authorization",
    "channel_context",
    "client_secret",
    "cookie",
    "credential",
    "credentials",
    "event_json",
    "final_system_prompt",
    "password",
    "private_key",
    "protecteddata",
    "rawrepresentation",
    "secret",
    "system_prompt",
    "token",
}

SENSITIVE_KEY_NORMALIZED = {
    key.replace("_", "").replace("-", "").lower() for key in SENSITIVE_PROPERTY_KEYS
}
SECRET_PATTERNS = (
    re.compile(r"(?i)(authorization\s*[:=]\s*bearer\s+)[^\s,;]+"),
    re.compile(r"(?i)(\b(?:api[_-]?key|access[_-]?token|client[_-]?secret|password|secret|token)\s*[:=]\s*)[^\s,;]+"),
    re.compile(r"\b(?:sk|gh[pousr]|xox[baprs])-[-_A-Za-z0-9]{8,}\b"),
)


def is_sensitive_key(key: Any) -> bool:
    normalized = str(key).replace("_", "").replace("-", "").lower()
    return normalized in SENSITIVE_KEY_NORMALIZED


def redact_text(value: str) -> str:
    redacted = value
    for pattern in SECRET_PATTERNS:
        redacted = pattern.sub(
            lambda match: (match.group(1) if match.lastindex else "") + "[REDACTED]",
            redacted,
        )
    return redacted


def sanitize_diagnostic_value(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            str(key): "[REDACTED]" if is_sensitive_key(key) else sanitize_diagnostic_value(nested)
            for key, nested in value.items()
            if not is_sensitive_key(key) or str(key).replace("_", "").lower() not in {"eventjson", "channelcontext", "finalsystemprompt"}
        }
    if isinstance(value, list):
        return [sanitize_diagnostic_value(nested) for nested in value]
    if isinstance(value, str):
        return redact_text(value)
    return value


def preview(value: Any, limit: int) -> str:
    if value is None:
        return ""
    value = sanitize_diagnostic_value(value)
    if not isinstance(value, str):
        try:
            value = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
        except TypeError:
            value = str(value)
    text = " ".join(value.replace("\r", "\n").split())
    if len(text) <= limit:
        return text
    return text[: max(0, limit - 3)] + "..."


def emit_markdown(summary: dict[str, Any]) -> None:
    thread = summary.get("thread_rollout") or {}
    db = summary.get("state_db") or {}
    print("# DotCraft Error Evidence Summary")
    print()
    print(f"- Thread ID: `{summary.get('thread_id') or '(unknown)'}`")
    if thread:
        print(f"- Rollout: `{thread.get('path')}`")
        print(f"- Rollout lines: {thread.get('line_count', 0)}")
    if db:
        print(f"- State DB: `{db.get('path')}`")
    print()

    if db.get("thread") is not None:
        print("## Thread Metadata")
        metadata = db["thread"]
        if metadata:
            for key, value in metadata.items():
                print(f"- {key}: `{value}`")
        else:
            print("- No matching row in `threads`.")
        print()

    if thread:
        print("## Rollout Timeline")
        print(f"- Record kinds: `{thread.get('kind_counts', {})}`")
        print(f"- Item types: `{thread.get('item_type_counts', {})}`")
        if thread.get("errors"):
            print("- Error items:")
            for error in thread["errors"]:
                print(
                    f"  - line {error.get('line')}, turn `{error.get('turn_id')}`, "
                    f"item `{error.get('item_id')}`, timestamp `{error.get('timestamp')}`"
                )
                if error.get("payload_preview"):
                    print(f"    preview: {error['payload_preview']}")
        else:
            print("- No explicit rollout `Error` items found.")
        if thread.get("tool_items"):
            print(f"- Surviving tool items: `{thread['tool_items']}`")
        if thread.get("terminal_items"):
            print(f"- Terminal items by surviving Turn: `{thread['terminal_items']}`")
        if thread.get("rollbacks"):
            print(f"- Rollbacks: `{thread['rollbacks']}`")
        if thread.get("model_history_batches"):
            print(f"- Model history batch metadata: `{thread['model_history_batches']}`")
        if thread.get("checkpoints"):
            print(f"- Compaction checkpoint metadata: `{thread['checkpoints']}`")
        if thread.get("provider_history"):
            print(f"- Provider history metadata: `{thread['provider_history']}`")
        if thread.get("parse_errors"):
            print(f"- Parse errors: `{thread['parse_errors']}`")
        print()

    if db.get("context_usage") or db.get("context_window"):
        print("## Runtime State")
        if db.get("context_usage"):
            print(f"- thread_context_usage: `{db['context_usage']}`")
        if db.get("context_window"):
            print(f"- thread_context_windows: `{db['context_window']}`")
        print()

    if db.get("trace_bindings") or db.get("trace_sessions") or db.get("trace_events"):
        print("## Trace Correlation")
        for binding in db.get("trace_bindings") or []:
            print(f"- binding: `{binding}`")
        for session in db.get("trace_sessions") or []:
            print(f"- trace_session: `{session}`")
        for key, events in (db.get("trace_events") or {}).items():
            print(f"- session `{key}` event types: `{events.get('type_counts')}`")
            for event in events.get("error_like_events") or []:
                print(
                    f"  - event {event.get('id')} `{event.get('type')}` at `{event.get('timestamp')}`"
                    f" tool=`{event.get('tool_name')}` finish=`{event.get('finish_reason')}`"
                )
                if event.get("content_preview"):
                    print(f"    preview: {event['content_preview']}")
        print()

    if db.get("counts"):
        print("## DB Table Counts")
        print(f"`{db['counts']}`")
