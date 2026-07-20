#!/usr/bin/env python3
"""Read-only DotCraft thread and state.db evidence summarizer."""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import pathlib
import re
import sqlite3
import sys
from typing import Any
from urllib.parse import quote


ERROR_NEEDLES = (
    "error",
    "exception",
    "failed",
    "failure",
    "traceback",
    "unsupportedparamserror",
    "rate limit",
    "unauthorized",
    "forbidden",
    "timeout",
    "报错",
    "失败",
)

TOOL_ITEM_TYPES = {
    "ToolCall",
    "PluginFunctionCall",
    "McpToolCall",
    "DynamicToolCall",
    "ToolExecution",
    "ToolResult",
    "CommandExecution",
}

MODEL_CONTENT_KINDS = {
    "text",
    "reasoning",
    "data",
    "function_call",
    "function_result",
    "hosted_image_generation",
    "image_generation_tool_call",
    "image_generation_tool_result",
    "tool_call_arguments_delta",
    "error",
    "uri",
    "usage",
}

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


def get_turn_id(payload: Any, line_number: int) -> str:
    if not isinstance(payload, dict):
        return f"line:{line_number}"
    turn = payload.get("turn")
    if isinstance(turn, dict) and turn.get("id"):
        return str(turn["id"])
    return str(payload.get("turnId") or payload.get("id") or f"line:{line_number}")


def object_payload(record: dict[str, Any], payload_name: str, line_number: int, result: dict[str, Any]) -> dict[str, Any] | None:
    payload = record.get(payload_name)
    if payload is None:
        return {}
    if isinstance(payload, dict):
        return payload
    result["parse_errors"].append(
        {
            "line": line_number,
            "error": f"{payload_name} must be a JSON object",
        }
    )
    return None


def summarize_item(
    item: Any,
    line_number: int,
    timestamp: Any,
    max_error_preview: int,
) -> dict[str, Any] | None:
    if not isinstance(item, dict):
        return None
    summary = {
        "line": line_number,
        "timestamp": timestamp,
        "item_id": item.get("id"),
        "type": item.get("type") or "(missing)",
        "status": item.get("status"),
        "payload_keys": sorted(
            key
            for key in item["payload"].keys()
            if str(key).replace("_", "").lower() not in SENSITIVE_PROPERTY_KEYS
        )
        if isinstance(item.get("payload"), dict)
        else [],
    }
    if summary["type"] == "Error":
        summary["payload_preview"] = preview(item.get("payload"), max_error_preview)
    return summary


def summarize_model_batch(payload: Any, line_number: int, timestamp: Any) -> dict[str, Any]:
    summary: dict[str, Any] = {
        "line": line_number,
        "timestamp": timestamp,
        "turn_id": payload.get("turnId") if isinstance(payload, dict) else None,
        "message_count": 0,
        "schema_versions": [],
        "content_kinds": [],
        "rejected": False,
    }
    if not isinstance(payload, dict) or not isinstance(payload.get("messages"), list):
        summary.update({"rejected": True, "rejection_reason": "malformed model batch"})
        return summary

    messages = payload["messages"]
    schemas: set[Any] = set()
    kinds: set[str] = set()
    rejected = False
    for message in messages:
        if not isinstance(message, dict):
            rejected = True
            continue
        schema = message.get("schemaVersion")
        schemas.add(schema if isinstance(schema, (str, int)) else "(missing)")
        if schema != 1:
            rejected = True
        contents = message.get("contents")
        if not isinstance(contents, list):
            rejected = True
            continue
        for content in contents:
            if not isinstance(content, dict) or not isinstance(content.get("kind"), str):
                rejected = True
                kinds.add("(missing)")
            else:
                kinds.add(content["kind"])
                if content["kind"] not in MODEL_CONTENT_KINDS:
                    rejected = True

    summary["message_count"] = len(messages)
    summary["schema_versions"] = sorted(schemas, key=str)
    summary["content_kinds"] = sorted(kinds)
    summary["rejected"] = rejected
    if rejected:
        summary["rejection_reason"] = "unsupported or malformed model history message"
    return summary


def summarize_checkpoint(payload: Any, line_number: int, timestamp: Any) -> dict[str, Any]:
    summary: dict[str, Any] = {
        "line": line_number,
        "timestamp": timestamp,
        "covered_through_turn_id": payload.get("coveredThroughTurnId")
        if isinstance(payload, dict)
        else None,
        "checkpoint_id": payload.get("checkpointId") if isinstance(payload, dict) else None,
        "decoded": True,
    }
    if not isinstance(payload, dict) or not isinstance(payload.get("replacementHistory"), list):
        summary.update({"decoded": False, "decode_error": "malformed checkpoint"})
        return summary

    replacement = payload["replacementHistory"]
    summary["replacement_message_count"] = len(replacement)
    if not payload.get("coveredThroughTurnId") or any(
        not isinstance(message, dict)
        or message.get("schemaVersion") != 1
        or not isinstance(message.get("contents"), list)
        or any(
            not isinstance(content, dict)
            or content.get("kind") not in MODEL_CONTENT_KINDS
            for content in message.get("contents", [])
        )
        for message in replacement
    ):
        summary.update({"decoded": False, "decode_error": "unsupported checkpoint history"})
    return summary


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Summarize DotCraft .craft thread JSONL and state.db evidence without modifying them."
    )
    parser.add_argument("--state-db", type=pathlib.Path, help="Path to .craft/state.db")
    parser.add_argument("--thread", type=pathlib.Path, help="Path to .craft/threads/.../{thread_id}.jsonl")
    parser.add_argument("--thread-id", help="Thread ID when no rollout file is available")
    parser.add_argument("--session-key", action="append", help="Extra trace session key to inspect")
    parser.add_argument("--max-error-preview", type=int, default=320)
    parser.add_argument("--json", action="store_true", help="Emit JSON instead of Markdown")
    return parser.parse_args()


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


def iter_text_lines(path: pathlib.Path, result: dict[str, Any]):
    try:
        with path.open("r", encoding="utf-8-sig") as handle:
            yield from enumerate(handle, start=1)
    except (OSError, UnicodeError) as exc:
        result["read_error"] = f"{type(exc).__name__}: {exc}"


def load_thread(path: pathlib.Path | None, max_error_preview: int) -> dict[str, Any]:
    if path is None:
        return {}

    result: dict[str, Any] = {
        "path": str(path),
        "exists": path.exists(),
        "line_count": 0,
        "kind_counts": {},
        "item_type_counts": {},
        "turns": [],
        "errors": [],
        "tool_items": [],
        "terminal_items": [],
        "model_history_batches": [],
        "checkpoints": [],
        "rollbacks": [],
        "error_like_lines": [],
        "parse_errors": [],
    }
    if not path.exists():
        return result

    kind_counts: collections.Counter[str] = collections.Counter()
    item_type_counts: collections.Counter[str] = collections.Counter()
    active_turns: dict[str, dict[str, Any]] = {}
    turn_items: dict[str, list[dict[str, Any]]] = {}

    for line_number, raw in iter_text_lines(path, result):
            result["line_count"] = line_number
            raw = raw.strip()
            if not raw:
                continue
            try:
                record = json.loads(raw)
            except json.JSONDecodeError as exc:
                result["parse_errors"].append({"line": line_number, "error": str(exc)})
                continue
            if not isinstance(record, dict):
                result["parse_errors"].append(
                    {"line": line_number, "error": "rollout record is not a JSON object"}
                )
                continue

            kind = record.get("kind") or "(missing)"
            timestamp = record.get("timestamp")
            kind_counts[kind] += 1

            raw_lower = raw.lower()
            if any(needle in raw_lower for needle in ERROR_NEEDLES):
                result["error_like_lines"].append(
                    {"line": line_number, "kind": kind, "timestamp": timestamp}
                )

            if kind == "turn_started":
                payload = object_payload(record, "turnStarted", line_number, result)
                if payload is not None:
                    turn_id = get_turn_id(payload, line_number)
                    active_turns.setdefault(turn_id, {"turn_id": turn_id})
                    active_turns[turn_id].update({"started_at": timestamp, "start_line": line_number})
            elif kind == "turn_completed":
                payload = object_payload(record, "turnCompleted", line_number, result)
                if payload is not None:
                    turn_id = get_turn_id(payload, line_number)
                    active_turns.setdefault(turn_id, {"turn_id": turn_id})
                    active_turns[turn_id].update(
                        {
                            "completed_at": timestamp,
                            "completed_line": line_number,
                            "status": payload.get("status"),
                        }
                    )
            elif kind == "item_appended":
                payload = object_payload(record, "itemAppended", line_number, result)
                if payload is not None:
                    item = payload.get("item")
                    if not isinstance(item, dict):
                        result["parse_errors"].append(
                            {"line": line_number, "error": "itemAppended.item must be a JSON object"}
                        )
                        item = None
                    if item is not None:
                        item_type = item.get("type") or "(missing)"
                        item_type_counts[item_type] += 1
                        turn_id = payload.get("turnId") or item.get("turnId")
                        if turn_id:
                            turn = active_turns.setdefault(turn_id, {"turn_id": turn_id})
                            turn["last_item_line"] = line_number
                            turn["last_item_type"] = item_type
                            turn["last_item_id"] = item.get("id")
                            item_summary = summarize_item(item, line_number, timestamp, max_error_preview)
                            if item_summary:
                                turn_items.setdefault(turn_id, []).append(item_summary)
            elif kind == "turn_state_replaced":
                payload = object_payload(record, "turnStateReplaced", line_number, result)
                if payload is None:
                    continue
                turn = payload.get("turn") if isinstance(payload, dict) else None
                turn_id = str(turn.get("id")) if isinstance(turn, dict) and turn.get("id") else None
                if turn_id:
                    prior = active_turns.get(turn_id, {"turn_id": turn_id})
                    replacement = {
                        "turn_id": turn_id,
                        "start_line": prior.get("start_line", line_number),
                        "started_at": turn.get("startedAt") or prior.get("started_at"),
                        "completed_at": turn.get("completedAt"),
                        "completed_line": line_number,
                        "status": turn.get("status"),
                    }
                    active_turns[turn_id] = replacement
                    replacement_items = []
                    for item in turn.get("items") or []:
                        item_summary = summarize_item(item, line_number, timestamp, max_error_preview)
                        if item_summary:
                            replacement_items.append(item_summary)
                    turn_items[turn_id] = replacement_items
                    if replacement_items:
                        replacement["last_item_line"] = line_number
                        replacement["last_item_type"] = replacement_items[-1]["type"]
                        replacement["last_item_id"] = replacement_items[-1]["item_id"]
            elif kind == "thread_rolled_back":
                payload = object_payload(record, "threadRolledBack", line_number, result)
                if payload is None:
                    continue
                count = payload.get("numTurns")
                removed: list[str] = []
                if isinstance(count, int) and not isinstance(count, bool) and count > 0:
                    removed = list(active_turns)[-count:]
                    for turn_id in removed:
                        active_turns.pop(turn_id, None)
                        turn_items.pop(turn_id, None)
                elif count is not None and (not isinstance(count, int) or isinstance(count, bool)):
                    result["parse_errors"].append(
                        {"line": line_number, "error": "threadRolledBack.numTurns must be an integer"}
                    )
                result["rollbacks"].append(
                    {"line": line_number, "timestamp": timestamp, "num_turns": count, "removed_turn_ids": removed}
                )
            elif kind == "model_history_messages_appended":
                result["model_history_batches"].append(
                    summarize_model_batch(record.get("modelHistoryMessagesAppended"), line_number, timestamp)
                )
            elif kind == "context_compacted":
                result["checkpoints"].append(
                    summarize_checkpoint(record.get("contextCompacted"), line_number, timestamp)
                )

    surviving_turn_ids = set(active_turns)
    result["model_history_batches"] = [
        batch
        for batch in result["model_history_batches"]
        if batch.get("turn_id") in surviving_turn_ids
        or (batch.get("turn_id") is None and batch.get("rejected"))
    ]
    for checkpoint in result["checkpoints"]:
        checkpoint["usable"] = bool(
            checkpoint.get("decoded")
            and checkpoint.get("covered_through_turn_id") in surviving_turn_ids
        )

    result["kind_counts"] = dict(kind_counts.most_common())
    surviving_items = [
        (turn_id, item)
        for turn_id in active_turns
        for item in turn_items.get(turn_id, [])
    ]
    item_type_counts = collections.Counter(item["type"] for _, item in surviving_items)
    result["item_type_counts"] = dict(item_type_counts.most_common())
    result["errors"] = [
        {**item, "turn_id": turn_id}
        for turn_id, item in surviving_items
        if item["type"] == "Error"
    ]
    result["tool_items"] = [
        {**item, "turn_id": turn_id}
        for turn_id, item in surviving_items
        if item["type"] in TOOL_ITEM_TYPES
    ]
    result["terminal_items"] = [
        {**items[-1], "turn_id": turn_id}
        for turn_id, items in turn_items.items()
        if turn_id in active_turns and items
    ]
    result["turns"] = sorted(
        active_turns.values(), key=lambda row: row.get("start_line") or row.get("completed_line") or 0
    )
    result["error_like_lines"] = result["error_like_lines"][:25]
    return result


def open_db(path: pathlib.Path) -> sqlite3.Connection:
    # Quote the path before constructing a read-only URI; unescaped '?', '#',
    # and '%' in Windows paths must remain part of the filename.
    encoded_path = quote(str(path.resolve()), safe="/:\\\\")
    uri = f"file:{encoded_path}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def quote_identifier(identifier: str) -> str:
    return '"' + identifier.replace('"', '""') + '"'


def table_exists(connection: sqlite3.Connection, table_name: str) -> bool:
    try:
        row = connection.execute(
            "select 1 from sqlite_master where type = 'table' and name = ?", (table_name,)
        ).fetchone()
        return row is not None
    except sqlite3.Error:
        return False


def table_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    try:
        return {
            row["name"]
            for row in connection.execute(f"pragma table_info({quote_identifier(table_name)})")
        }
    except sqlite3.Error:
        return set()


def rows(connection: sqlite3.Connection, sql: str, params: tuple[Any, ...] = ()) -> list[dict[str, Any]]:
    return [dict(row) for row in connection.execute(sql, params).fetchall()]


def optional_rows(
    connection: sqlite3.Connection,
    sql: str,
    params: tuple[Any, ...] = (),
) -> tuple[list[dict[str, Any]], str | None]:
    try:
        return rows(connection, sql, params), None
    except sqlite3.Error as exc:
        return [], f"{type(exc).__name__}: {exc}"


def load_db(
    path: pathlib.Path | None,
    thread_id: str | None,
    session_keys: list[str],
    max_error_preview: int,
) -> dict[str, Any]:
    if path is None:
        return {}

    result: dict[str, Any] = {"path": str(path), "exists": path.exists(), "warnings": []}
    if not path.exists():
        return result

    try:
        connection = open_db(path)
    except (OSError, sqlite3.Error) as exc:
        result["open_error"] = f"{type(exc).__name__}: {exc}"
        return result

    try:
        try:
            table_names = [
                row["name"]
                for row in connection.execute(
                    "select name from sqlite_master where type = 'table' order by name"
                )
            ]
        except sqlite3.Error as exc:
            result["query_error"] = f"{type(exc).__name__}: {exc}"
            return result

        result["tables"] = table_names
        counts: dict[str, int] = {}
        for table in table_names:
            if table == "sqlite_sequence":
                continue
            try:
                counts[table] = int(
                    connection.execute(
                        f"select count(*) from {quote_identifier(table)}"
                    ).fetchone()[0]
                )
            except sqlite3.Error as exc:
                result["warnings"].append(
                    {"code": "table_count_failed", "table": table, "message": str(exc)}
                )
        result["counts"] = counts

        if thread_id and table_exists(connection, "threads"):
            columns = table_columns(connection, "threads")
            selected = [
                column
                for column in (
                    "thread_id", "rollout_path", "workspace_path", "origin_channel",
                    "status", "created_at", "updated_at", "archived_at", "history_mode", "turn_count",
                )
                if column in columns
            ]
            if selected and "thread_id" in selected:
                try:
                    thread_rows = rows(
                        connection,
                        f"select {', '.join(quote_identifier(column) for column in selected)} "
                        "from \"threads\" where \"thread_id\" = ?",
                        (thread_id,),
                    )
                    result["thread"] = sanitize_diagnostic_value(thread_rows[0]) if thread_rows else None
                except sqlite3.Error as exc:
                    result["warnings"].append(
                        {"code": "query_failed", "table": "threads", "message": str(exc)}
                    )
            else:
                result["warnings"].append({"code": "missing_columns", "table": "threads"})

        if thread_id and table_exists(connection, "thread_context_usage"):
            usage_columns = table_columns(connection, "thread_context_usage")
            selected = [
                column
                for column in (
                    "thread_id", "context_usage_tokens", "message_count", "prefix_fingerprint", "updated_at"
                )
                if column in usage_columns
            ]
            if selected and "thread_id" in selected:
                try:
                    usage_rows = rows(
                        connection,
                        f"select {', '.join(quote_identifier(column) for column in selected)} "
                        "from \"thread_context_usage\" where \"thread_id\" = ?",
                        (thread_id,),
                    )
                    result["context_usage"] = usage_rows[0] if usage_rows else None
                except sqlite3.Error as exc:
                    result["warnings"].append(
                        {"code": "query_failed", "table": "thread_context_usage", "message": str(exc)}
                    )

        if thread_id and table_exists(connection, "thread_sessions"):
            try:
                session_row = connection.execute(
                    "select updated_at, length(session_json) as session_json_bytes "
                    "from thread_sessions where thread_id = ?",
                    (thread_id,),
                ).fetchone()
                result["legacy_thread_session"] = dict(session_row) if session_row else None
            except sqlite3.Error as exc:
                result["warnings"].append(
                    {"code": "query_failed", "table": "thread_sessions", "message": str(exc)}
                )

        bound_keys: list[str] = []
        if thread_id and table_exists(connection, "trace_session_bindings"):
            try:
                binding_rows = rows(
                    connection,
                    "select session_key, root_thread_id, parent_session_key, binding_kind, created_at "
                    "from trace_session_bindings where root_thread_id = ? order by created_at, session_key",
                    (thread_id,),
                )
                result["trace_bindings"] = sanitize_diagnostic_value(binding_rows)
                bound_keys = [row["session_key"] for row in binding_rows if row.get("session_key")]
            except sqlite3.Error as exc:
                result["warnings"].append(
                    {"code": "query_failed", "table": "trace_session_bindings", "message": str(exc)}
                )

        keys = list(dict.fromkeys([*bound_keys, *(session_keys or []), *([thread_id] if thread_id else [])]))
        if keys and table_exists(connection, "trace_sessions"):
            columns = table_columns(connection, "trace_sessions")
            selected = [
                column
                for column in (
                    "session_key", "started_at", "last_activity_at", "request_count", "response_count",
                    "tool_call_count", "error_count", "context_compaction_count", "thinking_count",
                    "token_usage_count", "total_input_tokens", "total_output_tokens",
                    "total_cached_input_tokens", "total_cache_write_input_tokens",
                    "total_reasoning_output_tokens", "total_tool_duration_ms", "max_tool_duration_ms",
                    "last_finish_reason",
                )
                if column in columns
            ]
            if selected and "session_key" in selected:
                placeholders = ",".join("?" for _ in keys)
                try:
                    order_column = "last_activity_at" if "last_activity_at" in selected else "session_key"
                    result["trace_sessions"] = rows(
                        connection,
                        f"select {', '.join(quote_identifier(column) for column in selected)} "
                        f"from trace_sessions where session_key in ({placeholders}) "
                        f"order by {quote_identifier(order_column)}, session_key",
                        tuple(keys),
                    )
                except sqlite3.Error as exc:
                    result["warnings"].append(
                        {"code": "query_failed", "table": "trace_sessions", "message": str(exc)}
                    )

        if keys and table_exists(connection, "trace_events"):
            columns = table_columns(connection, "trace_events")
            safe_event_columns = [
                column
                for column in (
                    "id", "event_id", "timestamp", "type", "tool_name", "call_id", "response_id",
                    "message_id", "model_id", "finish_reason", "duration_ms",
                )
                if column in columns
            ]
            has_event_json = "event_json" in columns
            event_summaries: dict[str, Any] = {}
            for key in keys:
                try:
                    by_type = rows(
                        connection,
                        "select type, count(*) as count from trace_events where session_key = ? "
                        "group by type order by count desc, type",
                        (key,),
                    )
                    if not safe_event_columns:
                        event_summaries[key] = {"type_counts": by_type, "error_like_events": []}
                        continue
                    select_columns = ", ".join(quote_identifier(column) for column in safe_event_columns)
                    if has_event_json:
                        select_columns += ', "event_json"'
                    where = "where session_key = ?"
                    if has_event_json:
                        where += " and (type = 'Error' or lower(event_json) like '%exception%' " \
                                 "or lower(event_json) like '%unsupportedparamserror%' " \
                                 "or lower(event_json) like '%traceback%' or lower(event_json) like '%http 4%' " \
                                 "or lower(event_json) like '%http 5%' or lower(event_json) like '%timeout%')"
                    order_column = "id" if "id" in columns else "rowid"
                    event_rows = rows(
                        connection,
                        f"select {select_columns} from trace_events {where} "
                        f"order by case when type = 'Error' then 0 else 1 end, {quote_identifier(order_column)} limit 20",
                        (key,),
                    )
                    errors = []
                    for event in event_rows:
                        raw_event_json = event.pop("event_json", None)
                        include_event = event.get("type") == "Error"
                        diagnostic_value: Any = None
                        if raw_event_json is not None:
                            try:
                                event_json = json.loads(raw_event_json)
                                if isinstance(event_json, dict):
                                    diagnostic_value = event_json.get("Content")
                                    if not include_event:
                                        diagnostic_value = (
                                            event_json.get("ToolResult")
                                            or event_json.get("MetadataJson")
                                            or event_json.get("Content")
                                        )
                                        diagnostic_text = preview(diagnostic_value, 4000).lower()
                                        include_event = any(
                                            needle in diagnostic_text
                                            for needle in (
                                                "badrequesterror", "unsupportedparamserror", "exception:",
                                                "traceback", "http 4", "http 5", "exitcode", 's="error"',
                                                "rate limit", "timeout",
                                            )
                                        )
                            except (json.JSONDecodeError, TypeError):
                                result["warnings"].append(
                                    {"code": "invalid_event_json", "table": "trace_events", "session_key": key}
                                )
                        if include_event:
                            event["content_preview"] = preview(diagnostic_value, max_error_preview)
                            errors.append(sanitize_diagnostic_value(event))
                    event_summaries[key] = {"type_counts": by_type, "error_like_events": errors}
                except sqlite3.Error as exc:
                    result["warnings"].append(
                        {"code": "query_failed", "table": "trace_events", "session_key": key, "message": str(exc)}
                    )
            result["trace_events"] = event_summaries
    finally:
        connection.close()

    return result


def infer_thread_id(thread_path: pathlib.Path | None, explicit: str | None) -> str | None:
    if explicit:
        return explicit
    if thread_path:
        return thread_path.stem
    return None


def emit_markdown(summary: dict[str, Any]) -> None:
    thread = summary.get("thread_rollout") or {}
    db = summary.get("state_db") or {}
    print("# DotCraft LLM Error Evidence Summary")
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
        if thread.get("parse_errors"):
            print(f"- Parse errors: `{thread['parse_errors']}`")
        print()

    if db.get("context_usage") or db.get("legacy_thread_session"):
        print("## Runtime And Legacy State")
        if db.get("context_usage"):
            print(f"- thread_context_usage: `{db['context_usage']}`")
        if db.get("legacy_thread_session"):
            print(f"- optional legacy thread_sessions evidence: `{db['legacy_thread_session']}`")
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


def main() -> int:
    args = parse_args()
    thread_id = infer_thread_id(args.thread, args.thread_id)
    summary = {
        "generated_at": dt.datetime.now(dt.timezone.utc).isoformat(),
        "thread_id": thread_id,
        "thread_rollout": load_thread(args.thread, args.max_error_preview),
        "state_db": load_db(args.state_db, thread_id, args.session_key or [], args.max_error_preview),
    }
    if args.json:
        json.dump(summary, sys.stdout, ensure_ascii=False, indent=2)
        print()
    else:
        emit_markdown(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
