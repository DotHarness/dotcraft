# DotCraft Memory Consolidation Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.1 |
| **Status** | Living |
| **Date** | 2026-05-28 |
| **Parent Specs** | [Session Core](session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: Define DotCraft's long-term memory consolidation flow. Memory consolidation is an independent persistence workflow for durable user and workspace knowledge. It is not a context compaction mechanism and does not depend on `CompactionPipeline`.

## 1. Scope

Memory consolidation turns completed conversation history into durable workspace memory.

In scope:

- Updating `MEMORY.md` with structured long-term facts.
- Appending `HISTORY.md` with timestamped, grep-searchable event summaries.
- Defining when consolidation runs and what conversation history it may inspect.
- Defining failure, configuration, and user-facing control semantics.

Out of scope:

- Short-term context compaction and token-pressure recovery.
- Provider prompt-cache behavior.
- Vector retrieval, semantic indexes, or cross-workspace memory sharing.
- Exact prompts, model-specific output formatting, and code-level implementation details.

## 2. Core Concepts

| Concept | Definition |
|---------|------------|
| **Short-term Compaction** | A context-window management workflow that reduces model-visible history so future model calls fit within token limits. |
| **Long-term Memory Consolidation** | A persistence workflow that extracts durable facts and events from completed conversation history. |
| **`MEMORY.md`** | The structured long-term memory file. It should contain stable facts about the user, workspace, preferences, and recurring project context. |
| **`HISTORY.md`** | The append-only event log. It should contain compact timestamped paragraphs useful for search and audit. |
| **Consolidation Window** | The conversation history snapshot given to the consolidation model for one consolidation attempt. |

Short-term compaction and long-term memory consolidation may observe similar conversation content, but they optimize for different outcomes. Compaction protects the next model call. Consolidation improves future sessions by preserving durable knowledge.

## 3. Triggering Model

Consolidation runs after successful turns, using a simple per-thread counter:

- Each successfully completed turn increments the thread's consolidation counter.
- When the counter reaches the configured interval, DotCraft snapshots model-visible history. If the snapshot is non-empty, it resets the counter and schedules a background consolidation task. Empty snapshots do not consume the scheduled attempt.
- Failed or cancelled turns do not increment the counter and do not reset it.
- Clearing or deleting a thread clears that thread's consolidation counter.

`CompactionPipeline` does not trigger consolidation. A compaction attempt, success, failure, or circuit-breaker state must not affect whether memory consolidation is eligible to run. Short-term compaction owns provider prompt-cache tradeoffs: hot auto-threshold compaction should prefer summary/fork over history-rewriting microcompact, while microcompact is limited to cold-cache old tool-result cleanup.

Manual consolidation may also be triggered explicitly through AppServer `thread/memory/consolidate/start`. Manual attempts use the same input scope and persistence contract as automatic attempts, but bypass `Memory.AutoConsolidateEnabled` because the user requested the maintenance action directly.

## 4. Input Scope

The consolidation input is the current thread's optimized model-visible conversation history at the end of a successful turn.

DotCraft intentionally uses the whole model-visible snapshot rather than only the messages since the last consolidation. This keeps triggering simple and lets the consolidation model compare current conversation history with existing `MEMORY.md` before deciding what changed.

If short-term compaction has already replaced older history with summaries, cleared tool-result markers, or another optimized projection, consolidation receives that optimized view rather than reconstructing pre-compaction rollout content. Compaction protects the active context window, and consolidation must not undo it by expanding older tool results or replaying pre-compaction history.

When consolidation is implemented as a same-model maintenance fork, DotCraft may preserve the full model-visible tool schema from the active agent request so provider prompt-cache shape remains stable. Tool-schema stability is not the security boundary. The execution layer must enforce a consolidation-specific policy that rejects all tool calls except scoped file reads, searches, writes, and edits for the memory files described below.

Maintenance forks keep provider-facing cache identity attached to the active thread id, but they may use a fork-local internal prompt-cache state path. Tool-executing consolidation forks use the prompt-cache `writeThrough` mode so they can reuse the main conversation's stable prefix while advancing their own task/tool-result tail breakpoints across continuation calls. The one-shot `readOnlyPrefix` mode used by no-tool maintenance forks does not apply to consolidation forks that execute tools. The fork-local state path must not change `prompt_cache_key`, OAuth `session-id` / `thread-id`, or dashboard trace session ownership.

## 5. Persistence Contract

Consolidation writes two memory layers:

- `MEMORY.md` is updated as a full replacement. The consolidation model reads the current memory and returns the complete updated long-term memory.
- `HISTORY.md` is append-only. Each consolidation attempt may append one timestamped paragraph describing key events, decisions, and topics.
- If `HISTORY.md` does not exist when a consolidation attempt starts, DotCraft creates an empty file before invoking the maintenance agent. This bootstrap creation does not count as a successful memory write.
- A tool-executing consolidation agent may directly update `MEMORY.md` and append `HISTORY.md`, but it must not read, search, write, or edit files outside those two memory files. Any attempted path traversal, absolute-path escape, or reparse-point escape must be denied before tool invocation.
- A successful consolidation may mark memory-derived prompt pages dirty so they can be re-read after their stable-page lifetime permits it. Any resulting base-instructions drift is accounted for by Session Core's context usage accounting rules and must not be treated as a full-history estimate trigger.

The operation is best-effort. The system should avoid corrupting existing memory files; if a consolidation attempt cannot produce a valid update, it should leave existing memory unchanged.

Concurrent consolidation attempts should be treated as independent background maintenance work across different threads. Implementations should serialize writes per memory store, replace `MEMORY.md` atomically via a temporary file, and append `HISTORY.md` under the same store lock.

## 6. Failure And Backpressure

Consolidation is best-effort maintenance:

- The active turn does not wait for consolidation to complete.
- Automatic consolidation is non-blocking: it does not make the thread maintenance-busy, does not set `maintenanceKind`, and does not prevent new user input from starting a Turn immediately.
- Automatic consolidation is serialized per thread. If another automatic trigger arrives while one is active, DotCraft records at most one pending follow-up attempt and starts it after the active attempt completes.
- Manual consolidation is blocking thread maintenance: new user input must be queued instead of starting a new turn immediately, and queued input starts only after the manual consolidation terminal event.
- Users may explicitly interrupt manual consolidation; interruption emits `consolidationCancelled` and does not create a persistent memory notice.
- A consolidation failure does not fail the turn.
- A consolidation failure does not trip the compaction circuit breaker.
- A skipped or failed consolidation attempt is acceptable; the worst expected user-visible outcome is that long-term memory was not updated.
- A same-model fork failure, provider timeout, prompt-too-large error, invalid final response, denied-tool dead end, or no effective file write should fall back to a bounded legacy consolidation path when possible. Provider timeouts must be recorded as `provider_timeout` rather than user cancellation unless the caller's cancellation token was explicitly cancelled.

Unlike context compaction, consolidation does not need a circuit breaker in the baseline design. The trigger should remain predictable and simple. If future implementations observe repeated provider failures or excessive load, a separate memory-specific backoff can be added without coupling it to compaction.

## 7. Event Emission

Memory consolidation emits transient `system/event` notifications so clients can display and dismiss maintenance status without blocking the active Turn:

- For automatic consolidation, `consolidating` is emitted through the active turn-scoped event channel after the successful Turn is marked complete and the baseline thread/session persistence attempt has finished, immediately before the background consolidation task is scheduled. It carries the completed Turn's `turnId` and represents a non-blocking background status, not thread maintenance.
- `consolidated` is emitted through the thread event broker after the background task successfully writes `MEMORY.md` or `HISTORY.md`. Because the turn-scoped event channel may already be closed, this event is thread-scoped and carries `turnId = null`.
- `consolidationSkipped` is emitted through the thread event broker when the background task completes without writing durable memory, such as when the model does not call `save_memory` or returns no valid changes. Clients should dismiss any active consolidation status without showing a success marker.
- `consolidationFailed` is emitted through the thread event broker if the background task fails. Clients should dismiss any active consolidation status and may surface the event `message`.
- `consolidationCancelled` is emitted through the thread event broker when the user interrupts active consolidation. Clients should dismiss any active consolidation status without showing a success marker.

On `consolidated`, Session Core also persists a `SystemNotice` item with `kind = "memoryConsolidated"` into the completed Turn and broadcasts `item/started` + `item/completed` through the thread event broker. This gives Desktop and other timeline clients a durable divider that survives thread reloads. Skipped and failed attempts do not create persistent conversation items.

Manual consolidation uses thread-scoped `system/event` notifications because there is no active turn-scoped event channel. It still emits `consolidating` before running and one terminal event after completion. Thread-scoped `consolidating` represents blocking thread maintenance and should put clients into queued-input mode. On success, the `memoryConsolidated` notice is appended to the latest completed Turn.

Dashboard trace events for same-model forks are recorded under the active thread trace session. `MaintenanceForkRequest` and `MaintenanceForkResponse` are maintenance envelope events and must be counted separately from normal LLM `Request` / `Response` events. Detailed tool-loop calls and token usage emitted by the trace collector remain in the same trace session so the dashboard can correlate the fork envelope with the underlying collector events.

## 8. Configuration

Memory consolidation is controlled by memory-specific configuration:

| Setting | Default | Meaning |
|---------|---------|---------|
| `Memory.AutoConsolidateEnabled` | `true` | Enables automatic turn-count-based consolidation. |
| `Memory.ConsolidateEveryNTurns` | `5` | Number of successful turns between automatic consolidation attempts per thread. |
| `ConsolidationModel` | empty | Optional model override for consolidation. Empty means use the main model. |

These settings are independent from `Compaction.*` settings. Disabling context compaction must not disable long-term memory consolidation, and disabling long-term memory consolidation must not change context compaction behavior.

## 9. UX Surface

Desktop exposes memory consolidation as a personalization setting:

- Label: "Enable long-term memory"
- Meaning: allow DotCraft to progressively accumulate facts about the user and workspace so future sessions can reference them.
- Destructive action: "Reset memory" calls AppServer `memory/reset` after confirmation. It clears the current workspace's `MEMORY.md`, `HISTORY.md`, and derived memory artifacts without deleting sessions, configuration, skills, plugins, plans, or automation tasks.

Other clients do not need dedicated UI to benefit from the same workspace setting. They may read or update the same workspace configuration through AppServer.

The setting should take effect for future successful turns without requiring an AppServer restart.

Resetting memory does not disable automatic consolidation. If `Memory.AutoConsolidateEnabled` remains enabled, future successful turns may create new memory files.

## 10. Future Work

Potential extensions:

- Incremental consolidation windows that include only messages since the last successful consolidation.
- Token-based or idle-time triggers.
- Vector or semantic retrieval over consolidated history.
- User-reviewable memory diffs before writing `MEMORY.md`.

## 11. Data Flow

```mermaid
flowchart LR
    subgraph sessionCore [Session Core]
        turnCompleted[Turn Completed Successfully]
        counter[Per-Thread Turn Counter]
        trigger{"Counter reached N"}
    end

    subgraph memoryFlow [Memory Consolidation]
        snapshot[Snapshot Thread History]
        consolidator[MemoryConsolidator]
        memoryFiles[MEMORY.md and HISTORY.md]
    end

    turnCompleted --> counter --> trigger
    trigger -->|yes| snapshot --> consolidator --> memoryFiles
    trigger -->|no| skip[Skip]

    compactionPipeline[CompactionPipeline] -. independent .-> consolidator
```
