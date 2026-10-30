# DotCraft Thread History Loading Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.9 |
| **Status** | Living |
| **Date** | 2026-10-30 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) (Section 4.4) |
| **Related Specs** | [Session Core](../architecture/session-core.md) |

Purpose: define how clients open and page a thread's history so that a long thread opens quickly, by reading each completed Turn as the summary its folded transcript shows and reading a Turn's full Items only when someone opens its work.

## 1. Overview

A completed Turn renders as its user message, a folded "Worked for" process, the results that stay outside the fold, and the final reply. Most of a Turn's stored bytes are tool output that appears only after the user opens the fold and then a tool row, so clients read completed Turns as summaries and read a Turn's full Items on demand. History reads on the server are served concurrently with each other and with the running Turn's writes.

## 2. Goals

- Opening a thread reads its header and the newest page of Turns in two requests, independent of how many Items those Turns hold.
- A summary Turn renders the same collapsed transcript as the fully loaded Turn: user message, activity boundary and duration, pinned results, final reply, and file changes.
- Opening a folded process, or revealing an Item inside it, loads that Turn's full Items and then renders it exactly as a fully loaded Turn.
- History reads on a current projection neither wait for nor block each other or the running Turn's writes.
- Desktop and Mobile follow one loading contract.

## 3. Scope

- The `itemsView` and `summaryTools` parameters of `thread/turns/list` and the summary Turn shape.
- The rule that selects summary Items.
- Server cost and observability of `thread/read`, `thread/turns/list`, and `thread/items/list`.
- Loading, expansion, reveal, and live-update behavior for the Desktop and Mobile conversation transcripts.

## 4. Non-goals

- Changing how Items are stored, the rollout format, or cursor semantics.
- Paging or summarizing inside the running Turn; it keeps full Items and live streaming.
- Agent-facing thread reads (`ReadThread`, thread export, context export), SubAgent transcripts, side chats, and the Summary aside, which read full Items.
- Turn navigation previews.
- Evicting full Items after they load.
- Bounding the size of a summary or of a Turn-scoped Item page beyond the existing page limits.

## 5. Core design

### 5.1 Summary view

`thread/turns/list` accepts `itemsView`: `none` (default) returns Turns without Items; `summary` returns each Turn with its summary Items. With `summary`, a client may also pass `summaryTools` ([5.2](#52-summary-tools)). A summary Turn carries:

- `itemsView: "summary"`;
- `items`: the Turn's summary Items in stored order, each identical to the Item `thread/items/list` returns for this connection;
- `itemCount`: the number of Items `thread/items/list` returns for the Turn on this connection, so a client knows whether the summary omitted any.

The summary of a Turn is every Item that is one of:

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
- `thread/items/list` reads only Items; connection-scoped filtering is applied to the Items themselves.
- Each history query logs its scope, row count, duration, and gate wait; a read taking longer than one second logs a warning with those fields.

### 5.4 Client loading

Both clients follow these rules for the conversation transcript.

1. Opening a thread subscribes first, then reads the header and the newest Turn page with `itemsView: "summary"` concurrently.
2. Only a completed Turn folds its work, so only a completed Turn renders from its summary. A running, waiting, failed, or cancelled Turn in the page is read in full with Turn-scoped `thread/items/list` before it renders, and a live one then follows notifications.
3. Older pages are read with `itemsView: "summary"`.
4. A summary Turn renders its user message, activity boundary and duration, pinned results, file changes (including the changes card of a Turn without a reply), and final reply exactly as the fully loaded Turn would. Its activity boundary is expandable when `itemCount` exceeds the number of summary Items.
5. Opening the folded process of a summary Turn reads all its Items with Turn-scoped `thread/items/list`. The process shows a loading state until the Items arrive; on failure it shows the error with a retry and the summary stays in place. The Turn then stays fully loaded while the thread's history is held, and its Items take the Turn's place in the transcript.
6. Revealing an Item that is not in any loaded Turn loads the full Items of summary Turns, newest first, before reading older pages.
7. Live notifications for a summary Turn merge by stable id and never mark it fully loaded. A Turn that starts after the thread opened is fully loaded.
8. Thread-wide derivations treat a summary Turn as known by its summary: file changes and citations come from summary Items.
9. A saved summary Turn (Mobile offline history) restores as a summary Turn.

## 6. Workflows

### 6.1 Open a long thread

The client subscribes, then reads `thread/read` and `thread/turns/list { limit, itemsView: "summary", summaryTools }` together. Completed Turns render collapsed from their summaries; the transcript is usable once both responses arrive. A Turn that is not completed additionally reads its Items, and a running one streams.

### 6.2 Open a folded process

The user opens "Worked for" on a summary Turn. The process shows a loading state, the client pages the Turn's Items in order, and the tool rows appear. Opening it again, or opening any tool row inside it, reads nothing more.

### 6.3 Reveal a call in older history

A background task card asks to reveal its starting call. The client loads the full Items of summary Turns, then older summary pages, until the call is present, opens the enclosing disclosures, and highlights the call.

## 7. Behavioral contracts

- A summary is a pure projection of persisted Items for the connection; it never includes runtime-only Items and never differs in content from the Items it repeats.
- `itemCount` and the summary are consistent with each other and with Turn-scoped `thread/items/list` for the same connection at the time of the read.
- Rollback, fork, and archive invalidate summaries with the same rules as other history pages.
- A Turn page in `summary` view has the same cursor, ordering, and limits as in `none` view.
- `summaryTools` without `itemsView: "summary"`, more than 32 entries, an entry without a name, or any other `itemsView` value returns `InvalidParams`.
- When a projection cannot be produced, the methods return `ThreadHistoryUnavailable`; there is no fallback to full Items.
