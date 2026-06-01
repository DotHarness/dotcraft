---
name: context-handoff
description: Find and export DotCraft session, trace, and memory context for external coding agents or DotCraft Doctor troubleshooting. Use when a user needs to locate related sessions from an error, provider message, tool failure, symptom, or thread id, then produce a clean Markdown handoff.
---

# DotCraft Context Handoff

## Overview

Use this skill to locate relevant DotCraft sessions and export a cleaned Markdown context package for another coding agent. The workflow is read-only by default and uses `dotcraft context search` before `dotcraft context export` unless the exact thread id is already known.

## Safety Rules

- Treat `.craft/state.db`, `.craft/threads/**/*.jsonl`, and `.craft/memory/*` as evidence. Do not edit them during handoff work.
- Start with `--tool-results summary`. Use `--tool-results full` only when the user explicitly needs complete tool or command output.
- Prefer `--history tail` for handoffs. Use `--history full` only when old memory history is directly relevant.
- Do not paste full exports into chat unless requested. Summarize the file path, thread id, warnings, and strongest evidence.
- Preserve evidence links: include thread id, rollout path, trace source, event id, timestamp, and search preview when explaining why a session was chosen.

## Quick Commands

Search local session and trace evidence:

```powershell
dotcraft context search --query "rate limit provider timeout" --workspace "D:\path\to\workspace" --limit 5
```

Export a handoff Markdown file:

```powershell
dotcraft context export --thread thread_20260601_ab12cd --workspace "D:\path\to\workspace" --output ".\context-handoff.md"
```

Export with stricter privacy:

```powershell
dotcraft context export --thread thread_20260601_ab12cd --tool-results none --history tail
```

Export an audit transcript:

```powershell
dotcraft context export --thread thread_20260601_ab12cd --profile transcript --tool-results full --history full
```

## Workflow

1. Identify the workspace. Accept either the workspace root or the `.craft` directory in `--workspace`.
2. Search with the user's symptom, provider error, tool name, model id, trace event text, or thread id.
3. Pick the best hit by score and evidence, not score alone. Prefer hits with a rollout path and trace evidence.
4. Run export for that thread. Keep the default `handoff`, `summary`, and `tail` modes unless there is a specific reason to widen them.
5. Check export warnings. Rollback warnings, corrupt rollout lines, or ignored compaction checkpoints should be mentioned in the handoff summary.
6. Give the user a concise result: chosen thread, output file, why it matched, and any privacy or continuity caveats.

## What The Export Handles

- Surviving rollout turns after rollback are exported; rolled-back tail turns are not treated as current context.
- Compaction checkpoints are listed as continuity events. The latest surviving checkpoint is used to reconstruct current model-visible context when it can be decoded.
- `MEMORY.md` is included, and `HISTORY.md` follows the selected `--history` mode.
- Reasoning content is omitted. Tool calls are kept, and tool results follow `--tool-results`.

## Report Shape

Return:

- **Selected thread**: id, status, last activity, and rollout path.
- **Why it matched**: 2-4 evidence bullets from `context search`.
- **Export**: output file path or command used.
- **Continuity notes**: rollback, compaction, corrupt-line, or missing-DB warnings.
- **Next step**: how the external agent should use the Markdown.
