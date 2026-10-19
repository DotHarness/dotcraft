# DotCraft Context Export CLI Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Parent Spec** | [Session Core](../architecture/session-core.md) |

Purpose: define a local, read-only CLI that turns DotCraft workspace sessions, trace metadata, and memory into handoff artifacts for external coding agents and troubleshooting workflows.

## 1. Overview

The context export CLI exposes two local commands:

- `dotcraft context export`: render one persisted DotCraft thread as Markdown.
- `dotcraft context search`: find threads related to user-provided text by inspecting `.craft/state.db` first, then reading rollout snippets for top candidates.

The feature is a local CLI surface, not an AppServer protocol extension. It reads persisted workspace evidence and must not require a running AppServer or mutate `.craft` state.

## 2. Goals

- Provide a one-command Markdown handoff for an external coding agent.
- Preserve conversation continuity around rollback and context compaction.
- Allow users and Doctor skills to locate relevant historical sessions from an error message, symptom, thread id, tool name, provider message, or natural-language query.
- Make output scope explicit, especially for tool results.

## 3. Non-Goals

- No remote AppServer or JSON-RPC API.
- No LLM, embedding, or network-backed ranking.
- No mutation, repair, rollback, compaction, memory writes, or workspace cleanup.

## 4. Command Contract

### 4.1 `dotcraft context export`

Required:

- `--thread <threadId>` identifies a server-managed thread.

Optional:

- `--workspace <path>` resolves the workspace root; defaults to the current directory.
- `--output <file>` writes Markdown to a file; omission writes to stdout.
- `--profile handoff|transcript` records the requested export profile in metadata. Default: `handoff`. Both profiles use the same document shape, honoring rollback and the selected tool-result scope.
- `--tool-results none|summary|full` controls tool result rendering. Default: `summary`.

Export output includes:

- thread metadata
- the thread's memory store: its index and memory files (see [Memory](memory.md#3-store-layout-and-format))
- continuity events for rollback and compaction
- a current-context section that reflects the latest surviving compaction checkpoint when one exists
- surviving turns ordered by turn start time

### 4.2 `dotcraft context search`

Required:

- `--query <text>` provides the search text.

Optional:

- `--workspace <path>` resolves the workspace root; defaults to the current directory.
- `--limit <n>` limits ranked hits. Default: `10`.
- `--status active|archived|all` filters thread metadata status. Default: `all`.
- `--json` emits machine-readable results.

Search inspects `.craft/state.db` first:

- `threads` for thread id, title, first message, channel, status, timestamps, and rollout path
- `trace_session_bindings` for root thread correlation
- `trace_sessions` for aggregate error, compaction, usage, model, and tool metadata
- `trace_events` for request, response, error, tool, maintenance, and rollback evidence

Rollout files may be read for top candidates to add short evidence snippets and item counts. Evidence text is a faithful bounded projection of the selected displayable field. Hidden compaction checkpoint replacement history must not be emitted in search results.

## 5. Continuity Rules

- Rollback records remove the rolled-back tail from exported conversation history.
- Rollback records are still listed as continuity events with timestamp, thread id, and removed turn count.
- Context compaction checkpoint records are recovery evidence, not user-visible transcript items.
- Export may use the latest checkpoint whose covered turn still survives to describe current model-visible context, then append later surviving turns.
- If a checkpoint is corrupt, invalid, or covers a rolled-back turn, export falls back to surviving rollout turns and emits a warning.
- Persistent system notice items, such as manual compaction notices, may be rendered as timeline notices.

## 6. Fidelity, Scope, and Read-Only Behavior

- Commands are read-only and must not create `.craft/state.db`, thread directories, memory files, or rollout files when they do not exist.
- `--tool-results none` must omit tool output bodies while preserving tool names, call ids, success flags, and timestamps where available.
- `--tool-results summary` must emit a faithful bounded prefix of each tool output body.
- `--tool-results full` must emit each tool output body verbatim.
- `RequestUserInput` calls and results follow the same `none|summary|full` scope as every other tool result; answer content has no special hiding rule.
- Export and search never classify or redact persisted values by key name or text pattern. Reasoning content, protected model data, arbitrary model metadata, and internal recovery records are omitted only because they are outside the displayable Session projection.
- Search results favor evidence metadata and bounded previews over unbounded content while preserving the selected evidence text.

Missing workspace, state DB or thread and invalid arguments produce nonzero CLI failures. Corrupt rollout records produce bounded diagnostics rather than silent evidence loss.

## 7. Doctor Skill Integration

The built-in Doctor plugin provides a skill that teaches agents to:

- run `dotcraft context search` when only an error, symptom, or provider/tool message is known
- run `dotcraft context export` for the selected thread
- choose `--tool-results none` when the report should exclude tool output bodies
- cite source evidence such as DB table names, trace event ids, rollout lines, and thread ids
