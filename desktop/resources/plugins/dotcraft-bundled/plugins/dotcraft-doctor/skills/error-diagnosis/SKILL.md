---
name: error-diagnosis
description: Diagnose DotCraft LLM, agent, tool-call, LiteLLM, provider, context, or session failures by inspecting a workspace's `.craft/state.db` and `.craft/threads/active|archived/*.jsonl` evidence. Use when a user reports that an LLM request failed, an agent turn errored, a thread cannot resume correctly, tool calls behaved unexpectedly, or DotCraft trace/thread persistence needs to be correlated to find the root cause.
---

# DotCraft LLM Error Diagnosis

## Overview

Use this skill to reconstruct what happened during a failed DotCraft turn from persisted evidence. Treat `.craft/threads/.../*.jsonl` as the canonical UI/session rollout and `.craft/state.db` as queryable metadata, serialized agent session state, trace events, bindings, usage, goals, plans, and attachments.

## Safety Rules

- Work read-only unless the user explicitly asks for a repair. Do not edit `state.db`, thread JSONL, or serialized `thread_sessions.session_json` during diagnosis.
- Avoid dumping full user or assistant content. Prefer IDs, timestamps, event kinds, item types, tool names, status, token counts, and short error previews.
- Copy the DB and thread file to a temporary location before experimental queries if a tool might open SQLite in write mode.
- Preserve the evidence chain: every conclusion should cite the source table, event ID, line number, timestamp, or item ID that supports it.
- If the user provides absolute paths outside the current repo, inspect only the requested `.craft` files and nearby `.craft` metadata needed to answer the question.

## Quick Start

Run the bundled summarizer first:

```powershell
python path\to\error-diagnosis\scripts\analyze_dotcraft_thread.py `
  --state-db "D:\path\to\workspace\.craft\state.db" `
  --thread "D:\path\to\workspace\.craft\threads\active\thread_x.jsonl"
```

For ad-hoc Python on PowerShell, prefer a script file or pipe a here-string to stdin:

```powershell
@'
import sqlite3
print("ok")
'@ | python -
```

Avoid `python -c "...\"SQL\"..."` for SQLite probes on PowerShell; `\"` is not a PowerShell double-quote escape and can corrupt the Python or SQL string.

If only a thread ID is known, find candidate rollout files:

```powershell
Get-ChildItem "D:\path\to\workspace\.craft\threads" -Recurse -Filter "thread_x.jsonl"
```

If `sqlite3` is available, use read-only connections:

```powershell
sqlite3 "file:D:\path\to\workspace\.craft\state.db?mode=ro" `
  "select thread_id, rollout_path, status, updated_at, turn_count from threads where thread_id='thread_x';"
```

## Diagnosis Workflow

1. **Identify the thread and time window**
   - Derive `thread_id` from the rollout filename when needed.
   - Read the matching `threads` row: `thread_id`, `rollout_path`, `workspace_path`, `origin_channel`, `status`, `created_at`, `updated_at`, `turn_count`.
   - Check whether the rollout path in DB points to the file being inspected.

2. **Build the rollout timeline**
   - Group JSONL records by `kind`: `thread_opened`, `turn_started`, `item_appended`, `turn_completed`, queued-input events, and status/name changes.
   - For `item_appended`, count item `type` values such as `UserMessage`, `AgentMessage`, `ToolCall`, `ToolExecution`, `ToolResult`, `CommandExecution`, and `Error`.
   - Locate explicit `Error` items and their `turnId`, `itemId`, timestamp, status, and payload keys.

3. **Correlate trace storage**
   - Read `trace_session_bindings` for `root_thread_id = thread_id`. The main session often has `binding_kind = threadMain`; subagents and maintenance forks may have different session keys.
   - For each session key, inspect `trace_sessions` counters: `request_count`, `response_count`, `tool_call_count`, `error_count`, token totals, and `last_finish_reason`.
   - Query `trace_events` by `session_key` and timestamp. Prioritize events with `type = 'Error'`, tool failures, unusual finish reasons, or a failed request immediately before the rollout `Error` item.

4. **Inspect context and session shape**
   - Use `thread_context_usage` to check whether context size or message count is suspicious.
   - Confirm `thread_sessions` has a row and recent `updated_at`; do not print the full `session_json` unless the user explicitly asks.
   - When resume behavior is involved, compare `thread_sessions.updated_at`, `threads.updated_at`, and the last rollout `turn_completed`.

5. **Classify the failure**
   - **Provider/API error**: Trace `Error` content mentions HTTP status, provider, unsupported params, rate limit, authentication, model, or payload validation.
   - **Tool error**: The last `ToolCallCompleted`, `ToolResult`, or `CommandExecution` before the failure contains an exception, non-zero exit, timeout, or malformed output.
   - **Persistence mismatch**: DB `threads.rollout_path`, rollout file location, `thread_sessions.updated_at`, or `turn_count` disagrees with the JSONL timeline.
   - **Context/session error**: Context usage is extreme, compaction events appear around the failure, or serialized session state is missing/stale.
   - **Adapter/channel error**: `origin_channel`, `channel_context`, queued input events, or status transitions show the failure happened outside model execution.

6. **Recommend the fix**
   - State the minimal root cause in one sentence.
   - Cite the strongest evidence from rollout and DB/trace.
   - Propose the smallest durable fix: config change, provider parameter normalization, tool schema fix, session recovery, retry path, validation, or test coverage.
   - If changing DotCraft code, update the relevant spec first when protocol or persistence behavior changes.

## Useful Queries

Use Python `sqlite3` or an equivalent read-only SQLite client.

```sql
select thread_id, rollout_path, workspace_path, origin_channel, status,
       created_at, updated_at, turn_count
from threads
where thread_id = :thread_id;
```

```sql
select session_key, binding_kind, parent_session_key, created_at
from trace_session_bindings
where root_thread_id = :thread_id
order by created_at;
```

```sql
select session_key, request_count, response_count, tool_call_count,
       error_count, context_compaction_count, total_input_tokens,
       total_output_tokens, last_finish_reason, last_activity_at
from trace_sessions
where session_key in (:session_keys);
```

```sql
select id, timestamp, type, tool_name, call_id, response_id, message_id,
       model_id, finish_reason, duration_ms, event_json
from trace_events
where session_key = :session_key
order by id;
```

## Report Format

Return a concise report with:

- **Finding**: root cause or most likely failure class.
- **Evidence**: 3-6 bullets with timestamps, rollout line/item IDs, trace event IDs, and table names.
- **Fix**: concrete remediation steps.
- **Residual risk**: what remains uncertain and what extra evidence would settle it.

