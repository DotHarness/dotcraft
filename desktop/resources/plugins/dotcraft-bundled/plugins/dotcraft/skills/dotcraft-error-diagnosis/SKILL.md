---
name: dotcraft-error-diagnosis
description: Diagnose DotCraft Hub, AppServer, startup, process, request, provider, agent, tool, context, or session failures from operational logs and workspace evidence. Use when DotCraft fails to start or stay running, a request or turn errors, a thread cannot resume, a tool behaves unexpectedly, or logs, rollout, state, and traces must be correlated.
---

# DotCraft Error Diagnosis

Use persisted evidence to explain a DotCraft failure. Check operational logs first. For thread failures, treat rollout JSONL as the authority for thread history and use `state.db` for projections, runtime state, and traces.

## Safety

- Keep diagnosis read-only. Do not edit logs, `state.db`, or rollout files unless the user asks for repair.
- Read SQLite with `mode=ro`; copy evidence to a temporary directory before using a client that may write.
- Do not dump prompts, model output, tool arguments/results, provider history entries, credentials, or raw trace JSON. Use bounded sanitized previews only when necessary.
- Cite log timestamps/categories, rollout lines/items, or trace event IDs for each conclusion.

## Workflow

1. **Set the failure window.** Record the approximate local time, workspace, component, and any thread, Turn, request, process, or connection ID.
2. **Check operational logs.** Read the newest files around that window:
   - Hub: `~/.craft/logs/dotcraft-hub-*.log`
   - Workspace host: `<workspace>/.craft/logs/dotcraft-*.log`
   - Read the applicable `Logging.Directory` setting before assuming `logs`. `Logging.Enabled`, retention, or a custom directory can explain missing files.
   - For managed AppServer startup, inspect both the Hub log and the target workspace log.
   - Correlate timestamp, severity, PID, category, and scopes such as `Module`, `WorkspacePath`, `RequestMethod`, `RequestId`, `ThreadId`, and `TurnId`.
3. **Inspect thread evidence when relevant.** Run the bundled summarizer against the matching rollout and database:

```powershell
python path\to\dotcraft-error-diagnosis\scripts\analyze_dotcraft_thread.py `
  --state-db "D:\path\to\workspace\.craft\state.db" `
  --thread "D:\path\to\workspace\.craft\threads\active\thread_x.jsonl"
```

4. **Reconstruct current thread state.**
   - Apply `turn_state_replaced` by Turn ID and let it replace earlier incremental state for that Turn.
   - Apply `thread_rolled_back` before reporting Turns; ignore rolled-back tail Turns as current evidence.
   - Report model and provider history only as schema, boundary, count, source, reason, and validation metadata.
5. **Correlate state and traces.**
   - Read `trace_session_bindings` for `root_thread_id = thread_id`. The main session often has `binding_kind = threadMain`; subagents and maintenance forks may have different session keys.
   - Inspect `trace_sessions` counters, duration, finish reason, maintenance forks, and prompt drift.
   - Query `trace_events` by `session_key` and timestamp. Prioritize events with `type = 'Error'`, tool failures, unusual finish reasons, or a failed request immediately before the rollout `Error` item.
6. **Conclude from the strongest evidence.** Separate a confirmed root cause from an inference. Recommend the smallest durable fix and relevant regression test.

## Output

- **Finding**: root cause or most likely failure class.
- **Evidence**: 3-6 bullets with timestamps, rollout line/item IDs, trace event IDs, and table names.
- **Fix**: concrete remediation steps.
- **Residual risk**: what remains uncertain and what extra evidence would settle it.

