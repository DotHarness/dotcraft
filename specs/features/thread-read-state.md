# DotCraft Thread Read State Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.9 |
| **Status** | Living |
| **Date** | 2026-10-29 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Desktop Client](../clients/desktop-client.md), [Mobile](../clients/mobile.md) |

Purpose: define the server-owned read receipt of a thread, so every client of a workspace agrees on which threads have results the user has not seen.

## 1. Scope

Each eligible thread has one durable read receipt owned by Session Core and stored with the workspace. A receipt records whether the thread holds an unseen result and, if so, where the unseen part starts. Clients read receipts through `thread/read` and `thread/list`, change them through `thread/readState/update`, and follow them through `thread/readState/changed`.

The feature includes:

- The receipt model, its storage, and its lifetime.
- The rules that make a thread unread when a new result is delivered.
- Explicit read and unread marks with optimistic concurrency.
- The AppServer projection.
- Desktop and Mobile behavior for showing receipts and marking threads read.

## 2. Goals and Non-goals

### 2.1 Goals

- Unread state survives client restarts and is the same in every Desktop window or process and on every paired phone.
- A thread becomes unread only when a result the user has not seen is delivered, never because a client loaded, opened, or listed it.
- A mark made from one client never silently erases a result or a mark that client has not seen.

### 2.2 Non-goals

- Side chats, SubAgent child threads, and other ineligible threads. Side chats keep their own client-side tab state.
- Unread counts, badges, tray or system notifications.
- Per-user receipts. A workspace thread has one receipt shared by all clients of that workspace.
- Read positions inside a thread beyond the first unread Turn.

## 3. Receipt Model

| Field | Semantics |
|-------|-----------|
| `firstUnread` | `null` when the thread is read; `{ type: "threadStart" }` for an explicit unread mark; `{ type: "turn", turnId }` for the earliest Turn whose result is unseen. |
| `revision` | Opaque string that changes on every receipt write. It is an equality token scoped to its thread: it is never ordered, compared across threads, or compared across hosts. |

A thread with no stored receipt reads as `firstUnread = null` with a stable initial revision. Writing a receipt never changes the thread's `lastActiveAt`, list ordering, status, or history.

### 3.1 Eligibility

A thread is eligible when it is persisted, not hidden ([AppServer Protocol §6.1](../protocols/appserver-protocol.md#61-thread-notifications)), and its source is not a SubAgent. Ephemeral threads, internal helper threads and forks, and SubAgent children have no receipt. Eligibility does not depend on what started a Turn: Turns started by automations, channels, or goal continuation follow the same rules.

### 3.2 Storage and lifetime

Receipts are SQLite durable business authority ([Session Core §9.4.1](../architecture/session-core.md#941-persistence-authority-classification)). They are not rebuilt from rollouts; losing the workspace database leaves every thread read.

- Deleting a thread deletes its receipt in the same operation.
- Archive and unarchive keep the receipt.
- A forked thread starts read; inherited history is never a new result.
- A thread restored through `thread/recovery/restore` starts read.

## 4. Lifecycle

### 4.1 New results

When an eligible thread's Turn reaches a terminal state in the running process, Session Core decides whether it delivered a new result:

| Terminal state | New result when |
|---|---|
| `Completed` | The Turn has a final assistant message with text, or an error. |
| `Failed` | Always. |
| `Cancelled` | The Turn has an assistant message with text, or an error. |

A Turn is not a new result when:

- the thread's goal is still `active` after the Turn ends and automatic goal continuation is enabled, because continuation follows;
- it is cancelled by cold-load reconciliation of an interrupted Turn;
- it arrives through session import or any other replay of existing history.

A new result keeps an existing `firstUnread`, otherwise sets it to that Turn, and always replaces the revision, so a client that saw the older receipt can tell a later result arrived. Each Turn is applied at most once.

The receipt is committed, and `thread/readState/changed` is published, before the Turn's terminal notification (`turn/completed`, `turn/failed`, or `turn/cancelled`). A receipt write that fails is reported as a diagnostic and never changes the Turn's outcome.

### 4.2 Explicit marks

`thread/readState/update` applies one operation against the revision the client last saw:

- `read` sets `firstUnread` to `null`.
- `unread` sets `firstUnread` to `threadStart`.

Both require an exact revision match and write a new revision even when `firstUnread` does not change. A mismatch changes nothing and returns the current receipt. Marks are allowed on running, idle, and archived threads and never load or resume the thread.

### 4.3 Rollback

When `thread/rollback` removes the Turn that `firstUnread` points at, the receipt becomes read with a new revision. A `threadStart` mark survives rollback.

## 5. AppServer Projection

| Surface | Contract |
|---|---|
| `thread/read` | Result `thread.readState`: the receipt of an eligible thread; omitted for an ineligible thread or when receipts are unavailable. |
| `thread/list` | Result `readStates`: a map from each eligible thread id on the returned page to its receipt. Omitted when receipts are unavailable; an id without an entry is ineligible. |
| `thread/readState/update` | Params `{ threadId, expectedRevision, operation: { type: "read" \| "unread" } }`. Result `{ readState }`. |
| `thread/readState/changed` | `{ threadId, readState }`, broadcast to every initialized connection that has not opted out, subscribed or not, on every receipt change. |

Errors for `thread/readState/update`:

- Unknown thread: `-32010` Thread not found.
- Ineligible thread or unavailable receipts: `-32600`, `error.data.code = "readStateUnavailable"`.
- Revision mismatch: `-32600`, `error.data.code = "readStateConflict"`, with the current receipt in `error.data.readState`.

Clients never blindly retry a conflict: the client's action may predate a result or another client's mark it has not shown. Notifications for one thread are delivered in order on a connection; when responses and notifications disagree, `thread/read` returns the current receipt.

## 6. Client Behavior

A thread row shows the unread dot while its receipt has a non-null `firstUnread` and nothing more urgent occupies its status slot. Clients hydrate receipts from `thread/list` and `thread/read` and apply `thread/readState/changed` as the incremental source of truth. A missing receipt means unknown and shows no dot.

A client marks a thread read only while the user can actually see it, so a window left open elsewhere does not consume results meant for another device. Seeing a thread means:

- it is opened by the user;
- a new result arrives while it is open in a focused Desktop window or the foreground Mobile chat screen;
- the Desktop window regains focus, or the Mobile app returns to the foreground, with it open and a Turn position unread.

An explicit `threadStart` mark is cleared only by the user opening the thread again. On a conflict the client adopts the returned receipt and applies the same rules to it.

### 6.1 Desktop

- Foreground and secondary project rows show receipts; `thread/readState/changed` is a thread-list notification, so secondary connections consume it.
- A foreground project's eligible thread row context menu offers **Mark as unread** while the thread is read and **Mark as read** while it is unread.
- Side chat tabs keep their own unread state.

### 6.2 Mobile

- Chat rows on Home and Project screens show receipts.
- Opening a chat marks it read under the rules above. Mobile offers no unread mark.
- Against a host that returns no receipts, Mobile shows no unread dots and sends no marks.

## 7. Acceptance Checklist

- [ ] A completed Turn with a reply makes an unopened eligible thread unread, visible from `thread/list`, `thread/read`, and `thread/readState/changed`, before `turn/completed`.
- [ ] Reading, listing, opening without user visibility, resuming, and forking never mark a thread read.
- [ ] A stale `read` or `unread` returns `readStateConflict` with the current receipt and changes nothing.
- [ ] Goal continuation Turns, cold-load cancellations, and imported history never make a thread unread.
- [ ] Rollback past the first unread Turn clears it; a `threadStart` mark survives.
- [ ] Ineligible threads have no receipt and reject updates with `readStateUnavailable`.
- [ ] Receipts survive AppServer restart and are removed with their thread.
- [ ] Desktop and Mobile show the same unread threads, and marking read on one clears the dot on the other.
- [ ] A Desktop window that is not focused never marks a thread read.
