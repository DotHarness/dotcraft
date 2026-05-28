#!/usr/bin/env python3
"""Read-only DotCraft thread and state.db evidence summarizer."""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import os
import pathlib
import sqlite3
import sys
from typing import Any


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
    if not isinstance(value, str):
        try:
            value = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
        except TypeError:
            value = str(value)
    text = " ".join(value.replace("\r", "\n").split())
    if len(text) <= limit:
        return text
    return text[: max(0, limit - 3)] + "..."


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
        "error_like_lines": [],
        "parse_errors": [],
    }
    if not path.exists():
        return result

    kind_counts: collections.Counter[str] = collections.Counter()
    item_type_counts: collections.Counter[str] = collections.Counter()
    active_turns: dict[str, dict[str, Any]] = {}

    with path.open("r", encoding="utf-8-sig") as handle:
        for line_number, raw in enumerate(handle, start=1):
            result["line_count"] = line_number
            raw = raw.strip()
            if not raw:
                continue
            try:
                record = json.loads(raw)
            except json.JSONDecodeError as exc:
                result["parse_errors"].append({"line": line_number, "error": str(exc)})
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
                payload = record.get("turnStarted") or {}
                turn_id = payload.get("turnId") or payload.get("id") or f"line:{line_number}"
                active_turns.setdefault(turn_id, {"turn_id": turn_id})
                active_turns[turn_id].update({"started_at": timestamp, "start_line": line_number})
            elif kind == "turn_completed":
                payload = record.get("turnCompleted") or {}
                turn_id = payload.get("turnId") or payload.get("id") or f"line:{line_number}"
                active_turns.setdefault(turn_id, {"turn_id": turn_id})
                active_turns[turn_id].update(
                    {
                        "completed_at": timestamp,
                        "completed_line": line_number,
                        "status": payload.get("status"),
                    }
                )
            elif kind == "item_appended":
                payload = record.get("itemAppended") or {}
                item = payload.get("item") or {}
                item_type = item.get("type") or "(missing)"
                item_type_counts[item_type] += 1
                turn_id = payload.get("turnId") or item.get("turnId")
                if turn_id:
                    turn = active_turns.setdefault(turn_id, {"turn_id": turn_id})
                    turn["last_item_line"] = line_number
                    turn["last_item_type"] = item_type
                    turn["last_item_id"] = item.get("id")
                if item_type == "Error":
                    result["errors"].append(
                        {
                            "line": line_number,
                            "timestamp": timestamp,
                            "turn_id": turn_id,
                            "item_id": item.get("id"),
                            "status": item.get("status"),
                            "payload_keys": sorted((item.get("payload") or {}).keys())
                            if isinstance(item.get("payload"), dict)
                            else [],
                            "payload_preview": preview(item.get("payload"), max_error_preview),
                        }
                    )

    result["kind_counts"] = dict(kind_counts.most_common())
    result["item_type_counts"] = dict(item_type_counts.most_common())
    result["turns"] = sorted(
        active_turns.values(), key=lambda row: row.get("start_line") or row.get("completed_line") or 0
    )
    result["error_like_lines"] = result["error_like_lines"][:25]
    return result


def open_db(path: pathlib.Path) -> sqlite3.Connection:
    uri = f"file:{path}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def table_exists(connection: sqlite3.Connection, table_name: str) -> bool:
    row = connection.execute(
        "select 1 from sqlite_master where type = 'table' and name = ?", (table_name,)
    ).fetchone()
    return row is not None


def table_columns(connection: sqlite3.Connection, table_name: str) -> set[str]:
    try:
        return {row["name"] for row in connection.execute(f"pragma table_info({table_name})")}
    except sqlite3.Error:
        return set()


def rows(connection: sqlite3.Connection, sql: str, params: tuple[Any, ...] = ()) -> list[dict[str, Any]]:
    return [dict(row) for row in connection.execute(sql, params).fetchall()]


def load_db(
    path: pathlib.Path | None,
    thread_id: str | None,
    session_keys: list[str],
    max_error_preview: int,
) -> dict[str, Any]:
    if path is None:
        return {}

    result: dict[str, Any] = {"path": str(path), "exists": path.exists()}
    if not path.exists():
        return result

    try:
        connection = open_db(path)
    except sqlite3.Error as exc:
        result["open_error"] = str(exc)
        return result

    with connection:
        table_names = [
            row["name"]
            for row in connection.execute(
                "select name from sqlite_master where type = 'table' order by name"
            )
        ]
        result["tables"] = table_names
        counts: dict[str, int] = {}
        for table in table_names:
            if table == "sqlite_sequence":
                continue
            try:
                counts[table] = int(connection.execute(f"select count(*) from {table}").fetchone()[0])
            except sqlite3.Error:
                pass
        result["counts"] = counts

        if thread_id and table_exists(connection, "threads"):
            thread_rows = rows(
                connection,
                """
                select thread_id, rollout_path, workspace_path, origin_channel, channel_context,
                       status, created_at, updated_at, archived_at, history_mode, turn_count
                from threads
                where thread_id = ?
                """,
                (thread_id,),
            )
            result["thread"] = thread_rows[0] if thread_rows else None

        if thread_id and table_exists(connection, "thread_context_usage"):
            usage_columns = table_columns(connection, "thread_context_usage")
            selected_usage_columns = [
                column
                for column in (
                    "thread_id",
                    "context_usage_tokens",
                    "message_count",
                    "prefix_fingerprint",
                    "updated_at",
                )
                if column in usage_columns
            ]
            usage_rows = rows(
                connection,
                f"""
                select {", ".join(selected_usage_columns)}
                from thread_context_usage
                where thread_id = ?
                """,
                (thread_id,),
            ) if selected_usage_columns else []
            result["context_usage"] = usage_rows[0] if usage_rows else None

        if thread_id and table_exists(connection, "thread_sessions"):
            session_row = connection.execute(
                "select updated_at, length(session_json) as session_json_bytes from thread_sessions where thread_id = ?",
                (thread_id,),
            ).fetchone()
            result["thread_session"] = dict(session_row) if session_row else None

        bound_keys: list[str] = []
        if thread_id and table_exists(connection, "trace_session_bindings"):
            binding_rows = rows(
                connection,
                """
                select session_key, root_thread_id, parent_session_key, binding_kind, created_at
                from trace_session_bindings
                where root_thread_id = ?
                order by created_at, session_key
                """,
                (thread_id,),
            )
            result["trace_bindings"] = binding_rows
            bound_keys = [row["session_key"] for row in binding_rows if row.get("session_key")]

        keys = list(dict.fromkeys([*bound_keys, *(session_keys or []), *([thread_id] if thread_id else [])]))
        if keys and table_exists(connection, "trace_sessions"):
            placeholders = ",".join("?" for _ in keys)
            result["trace_sessions"] = rows(
                connection,
                f"""
                select session_key, started_at, last_activity_at, request_count, response_count,
                       tool_call_count, error_count, context_compaction_count, thinking_count,
                       token_usage_count, total_input_tokens, total_output_tokens,
                       total_cached_input_tokens, total_cache_write_input_tokens,
                       total_reasoning_output_tokens, total_tool_duration_ms,
                       max_tool_duration_ms, last_finish_reason
                from trace_sessions
                where session_key in ({placeholders})
                order by last_activity_at, session_key
                """,
                tuple(keys),
            )

        if keys and table_exists(connection, "trace_events"):
            event_summaries: dict[str, Any] = {}
            for key in keys:
                by_type = rows(
                    connection,
                    """
                    select type, count(*) as count
                    from trace_events
                    where session_key = ?
                    group by type
                    order by count desc, type
                    """,
                    (key,),
                )
                errors = []
                for row in connection.execute(
                    """
                    select id, event_id, timestamp, type, tool_name, call_id, response_id,
                           message_id, model_id, finish_reason, duration_ms, event_json
                    from trace_events
                    where session_key = ?
                      and (type = 'Error'
                           or lower(event_json) like '%exception%'
                           or lower(event_json) like '%unsupportedparamserror%'
                           or lower(event_json) like '%traceback%'
                           or lower(event_json) like '%http 4%'
                           or lower(event_json) like '%http 5%'
                           or lower(event_json) like '%timeout%')
                    order by case when type = 'Error' then 0 else 1 end, id
                    limit 20
                    """,
                    (key,),
                ):
                    event = dict(row)
                    content_preview = ""
                    include_event = event.get("type") == "Error"
                    try:
                        event_json = json.loads(event.pop("event_json"))
                        if include_event:
                            diagnostic_value = event_json.get("Content")
                        else:
                            diagnostic_value = (
                                event_json.get("ToolResult")
                                or event_json.get("MetadataJson")
                                or event_json.get("Content")
                            )
                            diagnostic_text = preview(diagnostic_value, 4000).lower()
                            include_event = any(
                                needle in diagnostic_text
                                for needle in (
                                    "badrequesterror",
                                    "unsupportedparamserror",
                                    "exception:",
                                    "traceback",
                                    "http 4",
                                    "http 5",
                                    "exitcode",
                                    's="error"',
                                    "rate limit",
                                    "timeout",
                                )
                            )
                        content_preview = preview(diagnostic_value, max_error_preview)
                    except (json.JSONDecodeError, TypeError):
                        diagnostic_value = event.get("event_json")
                        content_preview = preview(diagnostic_value, max_error_preview)
                    if include_event:
                        event["content_preview"] = content_preview
                        errors.append(event)
                event_summaries[key] = {"type_counts": by_type, "error_like_events": errors}
            result["trace_events"] = event_summaries

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
        if thread.get("parse_errors"):
            print(f"- Parse errors: `{thread['parse_errors']}`")
        print()

    if db.get("context_usage") or db.get("thread_session"):
        print("## Session State")
        if db.get("context_usage"):
            print(f"- thread_context_usage: `{db['context_usage']}`")
        if db.get("thread_session"):
            print(f"- thread_sessions: `{db['thread_session']}`")
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
