# DotCraft Thread History Loading Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.9 |
| **Status** | Living |
| **Date** | 2026-10-30 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) (Section 4.4) |
| **Related Specs** | [Session Core](../architecture/session-core.md) |

Purpose: define bounded Turn and Item loading for thread transcripts, with summary-first presentation and viewport-driven hydration under the [Thread Restoration and Observation](thread-restoration.md) contract.

## 1. Overview

A completed Turn renders as its user message, a folded "Worked for" process, the results that stay outside the fold, and the final reply. Most of a Turn's stored bytes are tool output that appears only after the user opens the fold and then a tool row, so clients read completed Turns as summaries and read a Turn's full Items on demand. History reads on the server are served concurrently with each other and with the running Turn's writes.

## 2. Goals

- Opening an active thread applies an ordered resume snapshot with a five-Turn summary page. A concurrent read-only header/summary preview can render before runtime preparation finishes, without making the conversation actionable.
- A summary Turn renders the same collapsed transcript as the fully loaded Turn: user message, activity boundary and duration, pinned results, final reply, and file changes.
- Opening a folded process or revealing an Item loads only the required bounded Item pages, preserving unread cursors and already usable content.
- History reads on a current projection neither wait for nor block each other or the running Turn's writes.
- Desktop and Mobile follow one loading contract.

## 3. Scope

- The `itemsView` and `summaryTools` parameters of `thread/turns/list` and the summary Turn shape.
- The rule that selects summary Items.
- Server cost and observability of `thread/read`, `thread/turns/list`, and `thread/items/list`.
- Loading, expansion, reveal, and live-update behavior for the Desktop and Mobile conversation transcripts.

## 4. Non-goals

- Changing how Items are stored, the rollout format, or cursor semantics.
- Agent-facing retrieval, governed by [Thread Context Retrieval](thread-context-retrieval.md), and explicit thread/context export. Transcript viewers, side chats, and previews use bounded view selection rather than requiring full hydration.
- Evicting full Items after they load.
- Bounding the size of a summary or of a Turn-scoped Item page beyond the existing page limits.

## 5. Core design

### 5.1 Summary view

`thread/turns/list` accepts `itemsView`: `none` (default) returns Turns without Items; `summary` returns each Turn with its summary Items. Resume initial pages also accept explicit `full` for selected Turns and default to `summary`. These views apply to running, waiting, completed, failed, and cancelled Turns. With `summary`, a client may also pass `summaryTools` ([5.2](#52-summary-tools)). A summary Turn carries:

- `itemsView: "summary"`;
- `items`: the Turn's summary Items in stored order, each identical to the Item `thread/items/list` returns for this connection;
- `itemCount`: the number of Items `thread/items/list` returns for the Turn on this connection, so a client knows whether the summary omitted any.

Summary selection preserves pinned results and the final reply without copying process-only Items merely because the Turn has no final reply or is still active. A summary includes every Item that is one of:

1. a `userMessage`, `agentMessage`, `error`, or `systemNotice`;
2. an `imageGeneration` in a terminal status;
3. a `webSearch`, so citations in later replies resolve;
4. a terminal `mcpToolCall` that is an MCP App candidate;
5. a `toolResult` whose `structuredContent` carries a `fileChange`, with its `toolCall`;
6. a `toolCall` or `dynamicToolCall` of a tool named in `summaryTools`, with its `toolResult`.

Every other Item is omitted: reasoning, other tool calls and results, command and tool executions, other dynamic tool calls, approvals, and user input requests and responses. Summary Items are never truncated or reshaped; a client merges them with full Items by stable id.

### 5.2 Summary tools

Which tool results stay outside a folded process is a client layout decision, so the client names those tools. `summaryTools` is a list of at most 32 `{ name, namespace? }` entries. An entry matches a call whose `toolName` equals `name` and whose `namespace` equals `namespace`, or has none when `namespace` is omitted. Desktop and Mobile name the tools whose results they pin: plan creation, workflow launch, automation changes, and thread creation and messaging.

### 5.3 Server reads

- A history read whose projection is current serves the last committed projection without acquiring the thread's write gate, opening a write transaction, or deserializing the thread snapshot.
- State database connections keep private caches under WAL, so a reader never waits on another connection's open write transaction.
- A projection that is missing, stale, or corrupt is repaired under the write gate before the read is served.
- `thread/items/list` reads only the selected Item page; connection-scoped filtering is applied to the Items themselves. Metadata and summary views are selected in the database query and current-view projection before payload hydration or copying, not after loading all Items. Explicit full hydration reuses selected payloads and reads only missing Item identities.
- Each history query logs its scope, row count, duration, and gate wait; a read taking longer than one second logs a warning with those fields.

### 5.4 Client loading

Both clients follow these rules for conversation transcripts:

1. Reuse a warm attached view when available. Otherwise stage incoming notifications and requests while a read-only header/five-Turn summary preview runs concurrently with runtime preparation and authoritative resume.
2. Resume requests `initialTurnsPage: { limit: 5, itemsView: "summary", summaryTools }`. Apply its header, current interaction state and reconciled page as one coherent view before exposing actions. A preview alone is not readiness; no additional head-read barrier follows the authoritative response.
3. Preserve summary rendering for every Turn status. A running, waiting, failed, or cancelled Turn is not forced to full view. Live updates merge by stable identity without marking partial content fully loaded.
4. Opening detail reads at most one 100-Item page per selected Turn within the five-Turn head, using at most four concurrent workers. Preview hydration uses at most two concurrent jobs. Retain a cursor per Turn and publish usable content without waiting for a large Turn to drain.
5. Viewport demand and explicit process/Item expansion drive further pages. Restoring a runtime flag or opening the transcript does not drain remaining Turn or Item cursors. Stale target/viewport work does not keep launching reads.
6. Reuse summary Items and loaded payloads. A missing original input or known interaction Item uses a small targeted read. Unknown targets use bounded page/navigation reads rather than fully expanding unrelated Turns.
7. Loading or failed detail pages retain the existing summary, usable payloads, retry behavior and scroll anchor. Desktop shows a local loading status for active detail reads. A Turn is fully loaded only when its Item cursor is exhausted, and remains loaded while its history is held.
8. Hydrating changing content in the observed current Turn uses the ordered current-view response boundary without acquiring another observation. Apply the selected Item base before its later deltas; do not infer overlap from text prefixes. Older committed history retains independent paged reads.
9. File changes, citations and other summary-derived content use available summary Items. Mobile offline history preserves partial/summary state and unread cursors, not an invented fully-loaded state.

Pending approvals/questions and plan implementation choices use current interaction state and the matching entities; they do not wait for full history or process expansion. Waiting flags continue gating submission until the corresponding request arrives. Archived transcripts use header/pages only, without resume or Agent activation.

## 6. Workflows

### 6.1 Open a long thread

The client shows its cache or parallel read-only summary preview while resume prepares/reuses the runtime. It then applies the authoritative five-Turn page, current state and staged feed updates before making the selected interaction actionable. Older Turn pages and per-Turn Item pages load only as the viewport or an explicit expansion needs them.

### 6.2 Open a folded process

The user opens "Worked for" on a summary Turn. The process retains its summary and shows a local loading status while detail arrives. Automatic viewport demand reads bounded pages; an explicit complete expansion may exhaust the selected Turn's remaining cursor. Opening an already loaded row does not read it again.

Desktop synchronously restores its window-local disclosure choice by workspace, thread, and Turn. The default follows final-answer/Turn state; cancelled processes stay expanded. Initial presentation uses the restored state without an interaction transition.

### 6.3 Reveal a call in older history

A background task card asks to reveal its starting call. When the Turn/Item identity is known, the client reads that target directly or loads a bounded segment around it. Otherwise it searches bounded summary/navigation pages, hydrates the required Item pages, opens the enclosing disclosures, and highlights the call without expanding every intervening process.

## 7. Behavioral contracts

- A summary is a connection-scoped projection of selected committed Items or the ordered current view. It never differs in content from the corresponding full Items at that boundary.
- `itemCount` and the summary are consistent with each other and with Turn-scoped `thread/items/list` for the same connection at the time of the read.
- Rollback, fork, and archive invalidate summaries with the same rules as other history pages.
- A Turn page in `summary` view has the same cursor, ordering, and limits as in `none` view.
- `summaryTools` without `itemsView: "summary"`, more than 32 entries, an entry without a name, or an unsupported `itemsView` returns `InvalidParams`. Explicit `full` is supported by resume initial pages; ordinary Turn listing supports `none` and `summary`.
- When a projection cannot be produced, the methods return `ThreadHistoryUnavailable`; there is no fallback to full Items.
