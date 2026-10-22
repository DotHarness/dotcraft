# DotCraft Session Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.2 |
| **Status** | Living |
| **Date** | 2026-10-22 |

Purpose: Define the **server-managed** session model (Thread / Turn / Item), including lifecycle, persistence, event semantics, approval semantics, and adapter boundaries.

## 1. Scope

This specification defines the domain model and execution semantics for every channel whose conversation state is owned by the server and executed through the Session Core service. All such channels share Thread/Turn/Item state, rollout persistence, event ordering, and approval lifecycle. Adapters translate transport, authentication, and presentation; they do not own another session engine. Transport protocols project these primitives without redefining their lifecycle or persistence.

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Unified session primitives**: Thread, Turn, and Item are the shared model for all server-managed channels.
2. **Single server-side execution path**: channels invoke the agent through Session Core rather than channel-specific streaming loops.
3. **Unified event stream**: Session Core emits a structured event stream that adapters translate into transport-specific output.
4. **Cross-channel resume** for channels that share the same identity shape.
5. **Thin adapters**: channel code translates transport messages and approvals; it does not own session orchestration.
6. **Unified approvals**: approval requests are Items within a Turn with a defined lifecycle.

### 2.2 Non-Goals

- Changing the model/tool execution pipeline. Session Core wraps it; it does not replace it.
- Prescribing channel-specific UX. The model defines *what* happened, not *how* to display it.
- Real-time cross-device sync. Idle channels are not notified when a thread updates elsewhere; they discover state on resume.
- Simultaneous multi-user editing of one thread. Sequential group input is supported (Section 13).
- Host composition and module registration.

## 3. System Overview

### 3.1 Main Components and Boundaries

Session Core owns lifecycle, per-thread serialization, events, and persistence. It invokes the agent execution pipeline and exposes operations to thin channel adapters. Canonical domain and model-visible history lives in thread JSONL; SQLite stores explicitly classified state and rebuildable projections.

- Agent construction builds immutable agent instances and their model-client pipelines. It does not own a second session model.
- Per-thread mutual exclusion is internal to Session Core. Channels do not acquire session locks.
- Session Core invokes PrePrompt, Stop, PreToolUse, and PostToolUse hooks at their points in the Turn lifecycle and records trace events. Adapters do not invoke hooks or the trace collector.
- Approval requests and responses are Items with an explicit lifecycle; Session Core routes them to the adapter.

### 3.2 Runtime State Ownership

Session Core is the single authority for live state associated with threads and turns: active execution, event delivery, approval and user-input pauses, maintenance work, background continuation, and transient state used to resume, cancel, or report work. Each thread's live state has one ownership boundary. Adapters, protocol projections, and background workers may request operations or observe events, but must not keep competing mutable thread runtime state.

Thread deletion is both a persistence operation and a runtime teardown: it stops or invalidates outstanding work and clears all live state for the thread. Concurrent flows distinguish reading existing state from creating or restoring it as part of a lifecycle operation; event delivery, cleanup, retries, notifications, and delayed continuations use the existing-state path so they cannot resurrect a deleted or archived execution context.

### 3.3 Runtime Command Ownership

Every loaded Thread has one runtime command owner. External mutations of live Thread state -- Turn admission and cancellation, approval and user-input resolution, queued-input changes, effective configuration publication, maintenance admission, and lifecycle teardown -- are serialized by that owner. Long-running model or maintenance work may execute outside the dispatcher, but returns completion to the same owner before the active-work slot is cleared or follow-up work is admitted.

The owner stays responsive while a Turn runs, so cancellation, approvals, user-input answers, steering, and configuration changes do not wait for model completion. A configuration change committed during an active Turn applies to the next Turn; it never mutates the active Turn's captured context.

Each runtime activation has a generation identity. Delayed completion from an earlier generation, including after permanent deletion or reload, must not mutate, persist, publish, or recreate live Thread state.

### 3.4 Turn Execution Ownership

Turn admission captures an immutable execution context: effective configuration, agent, workspace and runtime roots, client capabilities, and request origin. The effective tool snapshot is frozen with the Turn; provider-history identity is derived from the admitted Thread and Turn before the first provider request. Caller-owned input, input snapshots, and client-managed history are copied at admission.

Mutable Turn state is separate: cancellation, pending approval and user-input waiters, tool projections, usage accounting, item sequence allocation, and same-Turn steering. The command owner requests cancellation through it; the Turn applies the terminal success, cancellation, and failure policy. The agent pipeline owns model sampling and tool invocation.

An Item that a running Turn did not produce on its own execution path still belongs to that Turn. Its producer delivers it to the live Turn, which records it at its next Item boundary, allocating from the Turn's item sequence and publishing on its event stream. A producer must not wait on a Thread-level serialization point held by the running Turn. When no Turn is live, the producer appends to the latest completed Turn under ordinary serialization, above that Turn's highest item sequence; a Thread with no Turn records nothing. Resolving the target Turn never admits, restores, or recreates a Turn. An Item's creation time is the moment it is appended, so Items stay ordered by creation time; when the instant an event occurred matters, it belongs in the payload. Imported threads are the one exception: their Turns and Items keep the source session's monotonic timestamps.

Terminal persistence is owned by one Turn committer, which atomically commits terminal Turn state, the model-history suffix, and any compaction checkpoint. Live Turn-state cleanup returns through the command owner and is rejected when its generation is no longer current. Durable queued input has priority over autonomous Goal continuation.

## 4. Core Domain Model

### 4.1 Entities

#### 4.1.1 Thread

A Thread is a persistent conversation between one user and one agent, tied to a workspace.

Fields:

- `Id` — globally unique, assigned at creation, immutable (Section 4.3).
- `WorkspacePath` — absolute path of the state workspace. It is not persisted: a Thread belongs to the workspace whose data directory stores it, so loading sets it from that location and a moved or renamed workspace keeps its Threads.
- `UserId` (nullable) — opaque user identifier from the originating channel, used for discovery. Null for system-initiated threads; independent automation runs use a distinct identity per run.
- `OriginChannel` — channel that created the Thread. Informational; does not restrict which channels may resume it.
- `DisplayName` (nullable) — see naming rules below.
- `Source` — why the thread exists. `kind: "user"` is a top-level conversation; `kind: "subagent"` marks a child thread and carries parent thread, parent turn, root thread, depth, nickname, role, profile, runtime, and capabilities. The complete source is part of the canonical baseline and survives cold-start replay.
- `ForkedFromId` (nullable) — source thread id for a fork.
- `Ephemeral` — process-local; not persisted or listed by default.
- `Worktree` (nullable) — DotCraft-managed Git worktree bound to the thread.
- `GitInfo` (nullable) — branch and `origin` remote last observed in the thread's execution workspace (Section 4.1.1.6).
- `Status` — `Active` or `Archived` (Section 5.1).
- `CreatedAt`, `LastActiveAt` — `LastActiveAt` updates when a Turn starts or completes.
- `Metadata` — string map for channel-specific data. Preserved, never interpreted.
- `Configuration` (nullable) — per-thread agent configuration (Section 12). Null means workspace defaults.
- `Turns` — the Thread's visible Turn history. Rollback removes a visible tail; the rollout keeps the removed records. A loaded runtime holds only its recent Turns (Section 9.8); complete history is read from the history projection.
- `QueuedInputs` — persisted FIFO of inputs submitted while a Turn or blocking maintenance is active (Section 4.1.1.3).

Naming: an explicit name wins. Otherwise the first eligible top-level user message supplies a provisional name (whitespace collapsed, truncated to 36 characters), after which Session Core makes one best-effort, non-blocking title request using the effective SubAgent model at low reasoning effort. The request asks for a `{ "title": ... }` JSON object; because some providers and gateways ignore the structured response format, a reply that is a single line of plain text is accepted as the title too, while multi-line or fenced replies are discarded. A generated title replaces the provisional name only if `DisplayName` still exactly equals it; failures and intervening renames leave the name unchanged, and generation is not retried. A fork uses an explicit fork name, else the source's name, else its first retained user message.

Queue execution: when a Turn completes successfully and no blocking maintenance is active, Session Core dequeues at most one queued input and starts it as the next Turn. Failed or cancelled Turns do not consume the queue. When the latest Turn was cancelled (by a user interrupt, a disconnected caller, or cold-load recovery), the queue is paused: no path starts a queued input automatically, including maintenance end, enqueue, task notifications, and resume, until `StartQueuedTurn` starts one or a later Turn completes. The pause is derived from persisted Turn state, so it survives restart. When blocking maintenance ends in any outcome, Session Core starts at most one queued input if the thread is otherwise idle. Clients may reorder the full queue; reordering changes only execution order.

#### 4.1.1.1 Thread Forks

A fork creates a new conversation branch from a source thread and leaves the source unchanged.

- A persistent fork gets a new id, its own rollout, and `ForkedFromId`. An ephemeral fork gets the same identity fields but stays process-local and unlisted.
- History is copied, not linked; later source edits, archive, delete, rollback, or compaction never rewrite the fork.
- The fork copies the selected turn/item prefix. A `forkPoint` targets a whole turn or an item boundary; `"after"` includes the target and `"before"` excludes it.
- A copied active or partial source turn ends at an explicit interrupted boundary.
- Forks do not inherit queued inputs, pending approvals or user-input requests, app bindings, active goals, or plan state. Copied transcript items stay visible.
- Forks copy thread configuration, then apply request-level overrides.
- A persisted `SystemNotice` with `kind = "forked"` and `sourceThreadId` marks the boundary between inherited history and new work. It is client-visible, not model-visible.
- A persistent fork materializes its own optimized model history. When the fork prefix fully retains a source compaction checkpoint's covered turn, the checkpoint is rebased into the fork rollout followed by the retained later model history, with an estimated context usage snapshot; a fork point inside or before that turn falls back to an older compatible checkpoint. Forks never expand compacted source history back into the full transcript when a compatible checkpoint exists.

Side chat forks:

- A side chat is an internal ephemeral fork of a non-side-chat thread. It copies the full current history and may be taken while a source Turn runs.
- Its model history reuses the source's persisted model history for every completed Turn, so the prefix is byte-identical; a running source Turn is rebuilt from its copied Items and ends at the interrupted boundary.
- It inherits the source's tool-planning role and cache routing root, so tool surface and base instructions match.
- Before each Turn, Session Core appends a side-chat boundary context item when the history has none. It follows the copied history, is restored after compaction, and is not a visible Item.
- A side chat cannot own persistent child threads, so spawning a child agent fails.
- Archiving or deleting the source deletes its side chats.

#### 4.1.1.2 Worktrees

A managed worktree is a Git worktree DotCraft creates under `<dataPath>/worktrees/` and binds to at most one thread as that thread's execution workspace.

- `WorkspacePath` remains the state workspace: it owns `.craft/threads`, `.craft/state.db`, memory, goals, plans, app bindings, skills, plugins, and thread metadata.
- The worktree is the execution workspace: file, shell, Git, LSP, and first-party file surfaces use it.
- `ordinaryCwd = Cwd ?? WorkspaceOverride ?? WorkspacePath`; `effectiveWorkspacePath = ExecutionWorkspaceOverride ?? ordinaryCwd`.
- `ExecutionWorkspaceOverride` changes execution location only; it never relocates rollout, memory, goals, plans, app bindings, or workspace configuration.
- Registered worktree roots are allowed execution roots for their bound threads even though ordinary workspace browsing hides `.craft/worktrees/**`.

Creation:

- A worktree is created on a new branch from `baseRef` (default: the source execution workspace `HEAD`). An omitted branch name is allocated as `<Worktrees.BranchPrefix>/<slug>-<8 hex>`, where the slug comes from the thread name or id; a requested name that already exists is rejected.
- Gitignored files at the repository root that match the patterns in a root `.worktreeinclude` file (`.gitignore` syntax) are copied into the new worktree. Tracked files never are.
- Creation is atomic: a failure after `git worktree add` removes the new worktree and the branch it created, and no thread is bound.

Moving a thread:

- Fork and start ([AppServer §4.17](../protocols/appserver-protocol.md#417-worktree-methods)) leave the source thread and its working tree untouched; uncommitted source changes are copied into the worktree when requested.
- Handoff moves the same thread and is refused while it has queued input. Local → worktree moves uncommitted changes when requested: they are applied in the worktree and removed from the source checkout. It targets a new worktree or one no thread is bound to; an existing target keeps its branch, and its own uncommitted changes are stashed and kept. Worktree → local requires a clean local checkout: it checks out the worktree's branch there, applies the worktree's uncommitted changes, and removes the worktree. Staged and unstaged versions stay apart, a failed step restores both checkouts, and each step is reported as it runs.
- The agent may move its own thread with the worktree tools ([Tool Architecture §7.5](tools-architecture.md#75-worktree-tools)). Such a move takes effect for the agent's later tool calls in the same Turn and never moves uncommitted changes.

Lifecycle:

- Archiving a thread removes its worktree when it has no uncommitted changes and keeps the branch; a worktree with uncommitted changes stays on disk. Unarchiving checks the branch out again at the recorded path.
- Deleting a thread removes its worktree under the same rule and deletes the branch only when it has no commits beyond its base.
- A Turn that starts in a bound worktree whose directory is missing first checks its branch out again; if that fails, the Turn fails with the reason.
- A worktree no thread is bound to stays until it is removed explicitly. Removing a worktree with uncommitted changes or commits beyond its base requires an explicit discard.

#### 4.1.1.3 QueuedTurnInput

A durable snapshot of user input waiting to become a future Turn.

- `Id`, `ThreadId`, `CreatedAt`.
- `NativeInputParts` — transport-native parts (text, file, skill, command, local image references).
- `MaterializedInputParts` — model-visible parts used when the input executes.
- `DisplayText` — label derived from the native snapshot.
- `Sender` (nullable) — sender identity for group sessions.
- `Status` — `"queued"` for FIFO execution, or `"guidancePending"` after promotion into current-Turn guidance.
- `ReadyAfterTurnId` (nullable) — active Turn when queued.
- `TriggerKind`, `TriggerLabel`, `TriggerRefId`, `TriggerStatus` (nullable) — present when the input was synthesized by a server mechanism rather than typed by a human (Section 4.2, UserMessage). `TriggerStatus` is set only on task notifications.

A queued input that starts a Turn copies its trigger metadata and id into the `UserMessage` payload; promoted guidance preserves the same metadata. Before guidance is admitted, a client may set it back to `"queued"` without changing payload, metadata, or position. Admission atomically persists the guidance `UserMessage` and removes the queued input; after that it is not retractable.

##### Task notifications

A task notification is a queued input with `triggerKind` `terminal`, `subagent`, or `workflow`, created by Session Core when a background task ends or a shell task stalls. Envelope, wake policy, and per-kind fields are owned by the background-task subsystem. Session-level delivery:

- While the owner Thread has an active Turn, the notification is added as `guidancePending` and drained at the next sampling boundary. It does not preempt a model request, does not hand over a waiting foreground command, and skips `UserPromptSubmit` hooks. If the Turn ends first, it becomes queued input.
- Otherwise it is added as `queued`, and an Active idle Thread starts the next queued Turn when the wake policy allows.
- Every Turn admits all `queued` task notifications as guidance before its first sampling request, so notifications that end together reach the model with that Turn's input.
- Selecting the next queued Turn skips notifications that may not wake the Thread. A notification is never dropped; on a non-Active Thread it waits for the next Turn.

Queued-input materialization is local-only. Inline `image` parts (base64 data URLs) are decoded without network access; `localImage` parts are read from their persisted paths. Rebuilding model history from an input snapshot (forks, interruption, a Turn without stored model history) reads `localImage` files the same way; an unreadable file becomes the text `[localImage:<path>]`. Session Core never dereferences HTTP(S) image URLs when starting a queued Turn or admitting guidance; such a part becomes a text notice that remote image URLs are not supported, and the remaining parts continue.

#### 4.1.1.4 SubAgent Child Threads

Child agents are ordinary Threads with `Source.kind = "subagent"` and `OriginChannel = "subagent"`. Native children use the same Turn, Item, approval, persistence, and resume contracts. External runtimes persist synthetic Turns with input, final output or failure, and available usage.

Parent-child edges, stored child configuration, and pending communication are durable. Child control identity is independent of the display name. Root-scoped communication must not leak into another root's execution or wakeups.

Default discovery excludes children. Parent archive and deletion cascade to descendants; parent restore restores only still-open child edges. Closing a child cancels its active work and archives its subtree. Closed children are not reopened by root resume.

#### 4.1.1.5 Provider Context Windows

Session Core keeps an internal provider context-window record per persistent thread so provider integrations can describe the current model-visible history lineage. It is not part of the public Thread model.

The record is keyed by `thread_id` and stores `first_window_id`, `previous_window_id`, `current_window_id`, `generation`, and `updated_at` (runtime-generated UUIDs). It is created on the first provider request that needs it. `current_window_id` is stable across turns, retries, tool loops, queued inputs, and resume.

A successful compaction that replaces model-visible history (`micro` or `partial`, from any trigger) advances the window from the durable replacement boundary: the previous current id becomes `previous_window_id`, the replacement supplies the new current id, and `generation` increments. A provider-native replacement carries the exact next window identity and is authoritative over a stale projection. Skipped, failed, cancelled, or diagnostic-only attempts do not advance the window.

The window record is routing metadata: never conversation content, never in Thread wire DTOs, never the prompt-cache key. For Responses, `prompt_cache_key` and OAuth `session-id` use the root cache-session Thread id, while OAuth `thread-id` and `x-client-request-id` use the executing Thread id. Root threads and ordinary forks are their own cache-session roots; subagents use the persisted root thread id from their source.

#### 4.1.1.6 Git Context

`GitInfo` tells clients which branch a thread was working on, so they can relate the thread to that branch's pull request even when several threads share one working tree.

- At each Turn start, Session Core reads the current branch name and the `origin` remote URL of the thread's effective workspace. The read is best-effort and bounded by a short timeout; it never delays the Turn beyond that bound or fails it.
- A detached HEAD records no branch. A workspace outside any Git working tree records no `GitInfo`. A failed or timed-out read keeps the previous value.
- The origin URL is stored without credentials or other user information.
- Turns whose tools run on a Remote Tool Host keep the previous value, because the Agent Host's file system is not the execution workspace.
- A changed value is persisted with the thread baseline and announced as a thread update. Threads that have not started a Turn since this was recorded carry no `GitInfo`.

#### 4.1.2 Turn

A Turn is one unit of agent work initiated by input. It ends when the agent finishes, fails, or is cancelled.

- `Id` — unique within the Thread (Section 4.3).
- `ThreadId`.
- `Status` — `Running`, `WaitingApproval`, `WaitingInput`, `Completed`, `Failed`, `Cancelled` (Section 5.2).
- `Input` — the `UserMessage` Item that started the Turn.
- `Items` — all Items of the Turn, including the input. Append-only.
- `StartedAt`, `CompletedAt` (set on a terminal state).
- `TokenUsage` (nullable) — billing usage accumulated across every model request of the Turn, not context occupancy: `InputTokens`, `OutputTokens`, `CachedInputTokens` (cache read), `CacheWriteInputTokens`, `ReasoningOutputTokens`, `TotalTokens`, and derived `FreshInputTokens = max(0, Input - Cached - CacheWrite)` and `NonCachedInputTokens = max(0, Input - Cached)`.
- `Error` (nullable) — description when `Failed`.

#### 4.1.3 Item

An Item is the atomic unit of input/output within a Turn, with a typed payload and an explicit lifecycle.

- `Id` — unique within the Turn (Section 4.3).
- `TurnId`.
- `Type`:
  - `UserMessage` — user input.
  - `AgentMessage` — agent response text, may stream.
  - `ReasoningContent` — model reasoning, when exposed.
  - `CommandExecution` — observable shell execution for `Exec`-style tools.
  - `ToolExecution` — runtime lifecycle projection of a normal tool invocation.
  - `ImageGeneration` — lifecycle of the `image_gen.imagegen` tool.
  - `WebSearch` — lifecycle of the `web.run` tool.
  - `ToolCall` — native or plugin tool invocation, including managed channel tools.
  - `McpToolCall` — MCP invocation lifecycle.
  - `DynamicToolCall` — runtime Dynamic tool callback lifecycle.
  - `ToolResult` — result paired with a standard `ToolCall`.
  - `ApprovalRequest`, `ApprovalResponse` — approval of a sensitive operation.
  - `UserInputRequest`, `UserInputResponse` — structured question to the user and its answer.
  - `Error` — an error during the Turn.
  - `SystemNotice` — persistent system marker in the timeline (for example a compaction point). Emitted as `item/started` + `item/completed` with no streaming phase.
- `Status` — `Started` (payload may be partial), `Streaming` (receiving deltas; valid for `AgentMessage`, `ReasoningContent`, `CommandExecution`, and streamed `ToolCall` argument previews), `Completed` (payload final).
- `Payload` — type-specific (Section 4.2).
- `CreatedAt`, `CompletedAt`.

Thread maintenance is a thread-level busy state for long-running blocking work outside the Turn stream, such as manual compaction. While active, new input is accepted only through the queue and starts after the maintenance terminal event.

#### 4.1.4 SessionIdentity

A SessionIdentity maps a channel-specific user context to Threads for discovery and creation.

- `ChannelName` — the requesting channel.
- `UserId` (nullable) — channel-specific user id.
- `ChannelContext` (nullable) — channel-specific context key (a group or chat id, a workspace URI), allowing several threads per user in one channel.
- `WorkspacePath` — the workspace of the identity.

Discovery returns threads matching the workspace, user, and optionally channel context (Section 9.5). The adapter decides whether to resume one or create a new thread.

### 4.2 Item Payload Schemas

Payload field shapes are owned by the Session Protocol contracts and generated schemas. This section defines what each payload means and the rules other subsystems depend on.

#### UserMessage

Fields: `text`, `nativeInputParts`, `materializedInputParts`, sender fields (`senderId`, `senderName`, `senderRole`, `channelName`, `channelContext`, `groupId`; Section 13.1), optional local `images` metadata for UI rehydration, `deliveryMode`, `queuedInputId`, `clientUserMessageId`, and the trigger fields `triggerKind`, `triggerLabel`, `triggerRefId`, `triggerStatus`.

- `nativeInputParts` is authoritative for history rendering and editor rehydration. `materializedInputParts` is the exact prompt/image snapshot Session Core received after transport-side materialization. `text` is a compatibility and preview value derived from the native snapshot, not the source of truth.
- Typed `contextRef` native parts keep source snapshots and owned image references; their materialized parts contain the model-facing expansion.
- `clientUserMessageId` correlates a client submission with its persisted message through queued and guidance delivery. It is not an idempotency key.
- `deliveryMode` is `"normal"` (or absent) for a direct Turn start, `"queued"` for a queued input that became a Turn, and `"guidance"` for input appended to an active Turn. `queuedInputId` names the queue record that produced the message.
- Trigger fields are set when input is synthesized by a server mechanism and absent for real user input. `triggerKind` values: `automation`, `goal` (label `Goal continuation`, ref = goal id), `app`, `mcpApp`, `team`, `subagentFollowupTask` (a parent starts or queues work for a child), `subagentInput` (direct or resumable external input), `subagentMailbox` (mailbox drain; `triggerRefId` is an agent path, not necessarily a navigable thread id), and the task notification kinds `terminal`, `subagent`, `workflow` (label = task label, ref = task id, `triggerStatus` = final status; see [Task notifications](#task-notifications)). Mailbox drains and task notifications are model-visible notifications that clients do not present as user input.

#### AgentMessage

`{ text }`; streaming deltas carry `{ textDelta }`. Optional `deliveryMode = "async"` marks a model-initiated message sent while the Turn continues (Section 10.4): it is persisted and broadcast immediately but excluded from rebuilt model-visible assistant history and from the Turn's final answer; the tool call and result that produced it remain model-visible.

#### ReasoningContent

`{ text }`.

#### ToolCall

Fields: canonical `namespace` (absent for top-level tools) and `toolName`; `providerFlatName`; immutable invocation identity (`definitionId`, `runtimeBindingId`, `bindingRevision`, `snapshotRevision`); safe provenance (`sourceKind`, `sourceToolId`, `sourceProvenance`, `pluginId`, `functionId`); trusted `presentation`; `arguments`; and `callId`, the provider correlation id linking the call to its result.

- At the model callback boundary, Session Core captures the active thread and Turn in an immutable invocation context used for dispatch and projection. An invocation with no Turn may run for an authorized host audience but must not create or update a Turn item.
- Streamed arguments and dispatcher lifecycle for one call are coordinated by `(threadId, turnId, callId, projectionShape)` and atomically upsert the same `ToolCall`; neither arrival order creates a duplicate. Identity, provenance, and presentation come from the frozen registration, never inferred from name, arguments, or result.
- While arguments stream, the started projection has null `arguments`; an empty object means the final arguments are known to be empty. The completed projection always carries the authoritative arguments.

#### ToolExecution

Fields: `callId`, `toolName`, `status` (`inProgress`, `completed`, `failed`, `cancelled`), `success`, `durationMs`, optional `resultPreview` and `errorMessage`. A UI runtime projection only; `ToolCall` remains the request record and `ToolResult` the complete model-visible result.

#### ImageGeneration

Fields: `callId`, `status` (`inProgress`, `completed`, `failed`), `revisedPrompt`, base64 `result`, `mediaType` (default `image/png`), `savedPath`, backend request id and generation id when returned, `errorMessage`.

The specialized projection of `image_gen.imagegen`: it starts when the dispatcher records the call, completes once from the tool result, and has no companion `ToolCall` or `ToolResult`. Completed bytes are saved under `.craft/generated_images/{threadId}/{callId}.png` (or in the captured remote workspace). A failed request keeps the request id. When model history is rebuilt from Items, a terminal item replays as an `image_gen.imagegen` call whose result is the image and saved-path hint, or the failure text.

#### WebSearch

Fields: `callId`, `status` (`inProgress`, `completed`, `failed`), display `query` (empty while in progress), `action` (`search`, `openPage`, `findInPage`, `other`; null while in progress), opaque `results`, `arguments`, `output` (text returned to the model), `errorMessage`.

The specialized projection of `web.run`, with the same single-item lifecycle as `ImageGeneration`. Action and query are derived from the call's commands. On history rebuild, a terminal item replays as a `web.run` call with its `arguments` and result `output`, or the failure text.

#### McpToolCall

Fields: canonical `namespace`/`toolName`, `providerFlatName`, runtime `server`, `origin` (`workspace`, `plugin`, `thread`, `binding`), `sourceToolId`, invocation identity and provenance as on `ToolCall`, `callId` (distinct from `Item.id`), `arguments`, `status`, `durationMs`, normalized `contentItems`, client-only `structuredContent`, sanitized host-only `_meta`, raw MCP `rawContent`, optional `mcpAppResourceUri`, `isError`, `success`, `errorCode`, `errorMessage`.

- One lifecycle item with no companion `ToolResult`. History reconstruction uses only `contentItems`; `structuredContent`, `_meta`, and `rawContent` never enter model context.
- `mcpAppResourceUri` and the bounded result are the only persisted MCP App presentation inputs. Availability is recomputed against the current registration and authority at projection time. View handles, UI resource content, CSP, iframe state, pending context, credentials, and live authority are runtime-only and never persisted. When availability cannot be established, the item uses generic result presentation.

#### DynamicToolCall

Fields: canonical `namespace`/`toolName`, `providerFlatName`, `callId` (distinct from `Item.id`), `arguments`, `status`, `durationMs` (terminal), `contentItems` (`text` or `image` with `mediaType`/`dataBase64`), client-only `structuredContent`, `success`, `errorCode`, `errorMessage`. A runtime Dynamic tool invocation is this single item; no paired `ToolCall`/`ToolResult` is emitted.

#### ToolResult

Fields: `callId`, the complete immutable invocation identity, provenance, and presentation of its `ToolCall`; `durationMs`; `result` (model/history-safe text fallback); optional model-safe `contentItems` (`text` or `image`); client-only `structuredContent`; sanitized host-only `_meta`; `success`, `errorCode`, `errorMessage`.

- Clients never join against the current registry to recover provenance or presentation; the result carries it.
- Standard projection appends exactly one terminal `ToolResult` per call. `McpToolCall` and `DynamicToolCall` instead reach exactly one terminal state on their own item. All projections share one atomic terminal guard across completion, rejection, cancellation, timeout, and failure.
- A provider callback naming an invalid, unknown, or unavailable function produces a recoverable tool failure with `success = false` and `errorCode = "tool_not_found"`, returned to the model rather than failing the Turn.
- For non-text tool output, `result` remains the text fallback and clients may use `contentItems`. History reconstruction may restore a validated image as structured model media but never serializes its data URL or base64 as text. Provider-specific projection may replace historical tool media with the text fallback. `structuredContent` and `_meta` never enter provider history.

`WriteFile` and `EditFile` attach their write outcome and exact file change as `structuredContent`:

```
{
  "kind": "fileChange",
  "writeState": "notApplied" | "unknown" | "applied",
  "warnings": [{ "code": string, "message": string }],
  "changes": [
    {
      "path": string,       // Display path of the written file
      "kind": "add" | "update",
      "diff": string,       // Git-style unified diff of this call; omitted when truncated
      "additions": number,
      "deletions": number,
      "truncated": boolean  // Present and true when the diff exceeded the per-call cap
    }
  ]
}
```

- `writeState` is `notApplied` before writing, `unknown` if writing fails, `applied` on completion. A diff-reporting failure preserves a successful write and adds a `file_change_report_failed` warning to both the result text and the payload. Failed or unavailable diffs give an empty `changes` array, never invented counts.
- `path` is workspace-relative with `/` separators under the tool's workspace root (a Remote Tool Host uses its own root), otherwise absolute with `/` separators.
- `kind` is `add` when `WriteFile` created a new file and `update` for every overwrite and every `EditFile`. Native file tools never delete or rename, so there are no delete or move kinds.
- `diff` covers the whole file from before to after this call in git format: `diff --git a/<path> b/<path>`, `new file mode 100644` for an add, `index <oid>..<oid>` with git blob SHA-1 ids (zeros for the missing side), `--- a/<path>` or `--- /dev/null`, `+++ b/<path>`, three-line-context hunks, and `\ No newline at end of file` markers. It reproduces exact text including line endings.
- Over 128 KiB, `diff` is omitted and `truncated` is `true`; `additions` and `deletions` are always present.

This payload never enters model context. It is the durable per-edit record; the aggregated Turn diff ([Turn Diff Events](#turn-diff-events)) is live-only.

#### CommandExecution

Fields: `callId`, `command`, `workingDirectory`, `source` (`"host"`), `status` (`inProgress`, `completed`, `failed`, `cancelled`, `killed`, `lost`), `aggregatedOutput`, `sessionId` (background terminal id when the command outlives its tool call), `outputPath`, `originalOutputChars`, `truncated`, `backgroundReason`, `exitCode`, `durationMs`.

`CommandExecution` is the persisted observable summary of an `Exec`-style call. Foreground wait budgets, background handover, stdin, and stop semantics belong to the shell tool and background-task subsystems. Session-level rules:

- A command handed to the background terminal runtime keeps its Item in progress, records `sessionId`, and completes it with final output, status, and exit code when the process exits, even after its Turn has completed. The late completion is persisted into that Turn and published as `item/completed`. Output after handover reaches clients through terminal notifications, not `CommandExecution` deltas.
- When a thread loads, such an Item still in progress on a finished Turn whose terminal is no longer running (for example after a restart) is completed from the terminal's final state, or with `status = "lost"` when the terminal is unknown, and persisted.
- While a foreground command of a regular Turn waits, guidance admitted for that Turn hands the process over at once, so the guidance reaches the model at the next sampling boundary.
- A process still owned by the active tool invocation is foreground work: cancelling the Turn stops its process tree, completes the Item with `status = "cancelled"`, and only then completes Turn cancellation. A process started in the background, or handed over after its yield time, is owned by the background terminal runtime and is not stopped by Turn cancellation.
- `WriteStdin` keeps its own `ToolCall`/`ToolResult`. Clients present successful input on the target command — live through the terminal interaction delta (Section 6.3), and on reload by matching the call's `sessionId` argument to the command's `sessionId` — rather than as a separate tool call. A failed call wrote nothing and remains a tool call.
- Terminal-capable clients consume live output from terminal notifications; clients that consume both paths merge by `callId` without duplicating output.

#### ApprovalRequest

Fields: `approvalType` (`file` or `shell`), `operation` (file: `read`, `write`, `edit`, `list`; shell: the command), `target` (file path, or shell working directory), `requestId`, and, for shell requests, a `shell` block produced by the shell safety kernel (`reasons`, `rememberedPrefixes` a permanent approval will store, `remembersExactCommand`). The shell runtime supplies the approval key in `scopeKey`; session-scoped approval never authorizes a command with a different key.

#### ApprovalResponse

`{ requestId, approved }`.

#### UserInputRequest

`{ requestId, isBlocking, questions[] }`. Each question has a stable answer key `id`, short `header`, user-facing `question`, `isOther` (free-form answer allowed), `isSecret`, and `options` (`label`, `description`). `isBlocking` is set by Session Core from the active mode (Section 10.3).

#### UserInputResponse

`{ requestId, response: { answers: { "<questionId>": { answers: [string] } } } }`.

#### Error

`{ message, code, fatal }` — `code` is machine-readable (for example `agent_error`, `timeout`); `fatal` says whether the error terminates the Turn.

#### SystemNotice

`kind` classifies the notice: `compacted`, `forked`, `remoteRoute`, or `channel`. Kind-specific fields:

- `compacted`: `trigger` (`auto`, `reactive`, `manual`), `mode`, `tokensBefore`, `tokensAfter`, `percentLeftAfter` (fraction of the effective context window, 0.0–1.0), `clearedToolResults`.
- `forked`: `sourceThreadId`.
- `remoteRoute`: `reason` (`connected`, `disconnected`, `leaseLost`), `initiator` (`client`, `agent`, or `system` only with `leaseLost`), `hostId`, `hostName`, `workspaceId`, `workspaceName`.
- `channel`: `reason` (`bound`, `unbound`), `channelName`, `targetName`. Owned by App Binding.

Notices are persisted through the normal rollout path, survive reload and paged history, and render as inline timeline dividers rather than model conversation.

- Session Core creates a `compacted` notice only for partial compaction (neutral or provider-native), so persisted notices carry `mode = "partial"`. Micro compaction emits only the transient `system/event` that refreshes context usage.
- `forked` notices are not model-visible and never mutate the source thread.
- `remoteRoute` notices mark where eligible tools started or stopped running on a remote execution location. They follow the out-of-band placement rule in Section 3.4: appended to the running Turn at the point the route changed, otherwise to the latest completed Turn. Connects and disconnects are recorded only when a person or the model caused them; thread release and process teardown are not recorded. A lost lease is always recorded with `initiator = "system"`. `hostId` and `workspaceId` name the route the change was about, including the route a disconnect removed. Hosts outside Session Core append notices through the Session API with the same placement and never wait for a running Turn.

### 4.3 Stable Identifiers and Normalization Rules

- **Thread ID**: `thread_{yyyyMMdd}_{6-char-random}`, generated by Session Core, unique within the workspace. Primary key for persistence and resume.
- **Turn ID**: `turn_{3-digit-sequence}`, sequential and never reused within a Thread. Session Core keeps a sequence high-water mark across rollback, restart, fork initialization, subagent execution, and ordinary creation; removing a visible Turn never lowers it.
- **Item ID**: `item_{3-digit-sequence}`, sequential within a Turn. Continuing an existing Turn resumes after its highest allocated sequence. Lifecycle updates reuse an id only for the same logical Item; a different type, call, or source never overwrites it.
- **Item reference**: across Thread-level protocols an Item is addressed by `(turnId, itemId)`. `itemId` alone is not Thread-wide.
- **Canonical tool identity**: the persisted `(namespace, toolName)` pair is the exact case-sensitive tool name selected by the Turn snapshot, never parsed from a provider string. Each present component matches `^[A-Za-z0-9_]+$`, and its deterministic flat form fits within 64 ASCII bytes.
- **Flat provider alias**: `providerFlatName` is the deterministic alias chosen by the same snapshot for providers without namespaces. It is required on every new tool invocation payload and never parsed to recover canonical or routing identity.
- **UserId**: stored as received from the adapter. Resolving whether ids from different channels denote the same person is out of scope.

## 5. Session Lifecycle Specification

### 5.1 Thread Lifecycle

```
                    ┌──────────┐   ArchiveThread    ┌───────────┐
     CreateThread   │          │ ─────────────────► │           │
    ─────────────►  │  Active  │                    │ Archived  │
                    │          │ ◄───────────────── │           │
                    └──────────┘  UnarchiveThread   └───────────┘
```

- `CreateThread(identity)` → `Active`. Session Core assigns the id and sets `CreatedAt` and `LastActiveAt`.
- `ResumeThread(threadId)` keeps an Active thread Active. Any compatible adapter may resume it; Archived threads cannot be resumed. An unloaded thread is loaded from rollout (state and model history), a runtime agent session is constructed, and `LastActiveAt` updates.
- `Active` → `Archived` on explicit request or archival policy. Archived threads are read-only: listable and inspectable, not resumable. Archiving a parent archives its SubAgent subtree.
- `Archived` → `Active` on explicit restore, which restores the SubAgent subtree.

Invariants:

- At most one Turn per Thread is `Running`, `WaitingApproval`, or `WaitingInput`.
- At most one thread maintenance operation is active per Thread.
- A Thread may contain Turns from different channels; each Turn records its originating channel.

### 5.1.1 Thread-Owned Runtime Artifacts

Thread-owned runtime artifacts are not a second source of conversation authority, but their ownership and cleanup follow the thread lifecycle:

- Background terminal metadata and output logs are owned by the originating Thread and stay addressable after the process ends until the owner is permanently deleted or retention removes them.
- Oversized tool-result spill files are owned by the originating Thread. The model-visible message holds a preview of at most 2,000 characters and a workspace-relative reference; the spill file is not reconstructable from the rollout.
- One tool round's text results, including tool and MCP results made only of text items, together hold at most 200,000 characters. When a round exceeds that, its largest results spill the same way until it fits. A result that carries an image stays whole and is not counted. The decision is made once, when the round's results are built, so a result the model has seen never changes and the prompt-cache prefix stays stable.
- Archive is not deletion. Archiving stops or invalidates active terminal processes but preserves terminal metadata, logs, and spill files for inspection, export, and tool-result reads.
- Permanent deletion removes the complete owned subtree: it stops active terminals, removes terminal and spill artifacts for the thread and all descendant SubAgent threads, then removes durable state. Cleanup is idempotent and retryable per filesystem operation. A child artifact never survives deletion of its parent because the child is hidden from default lists.
- Compaction may replace or clear model-visible tool-result content but never deletes a spill file a current rollout or retained view may reference. Spill cleanup is retention maintenance, not a compaction side effect.
- Graceful shutdown stops active terminals, flushes metadata and rollout writers, and releases handles; it never deletes persistent history or artifacts. The next startup marks persisted `running` terminals as `lost` before retention processing.
- A terminal output log is complete output; preview bounds in UIs or APIs never permit truncating it.
- A spill file name is stable for its tool call (the persisted call id, otherwise a deterministic artifact key). Replaying the same call is idempotent and never creates a second file or overwrites another call's file. A spill file is removed only when permanent deletion or retention can prove the owner and all current rollout references are gone.
- The terminal `OutputRetentionDays` setting governs retention of completed and lost terminal artifacts; running terminals are never removed by TTL. Tool-result retention may share the workspace policy but is conservative when reference status is unknown.
- A throttled retention janitor may run at startup and in long-lived processes. It coordinates through a workspace/process lock, skips active owners, continues after individual failures, and reports a retryable result; a marker may suppress duplicate scans but never a retry after an incomplete scan. Thread deletion is the authoritative synchronous cleanup; if database deletion succeeds while file cleanup fails, the failure is observable and the orphan stays eligible for the janitor.

### 5.1.2 Session Recovery Snapshots

Session Core can export and restore a versioned JSON recovery snapshot for a server-managed, non-ephemeral Thread, preserving the executable state needed to continue with a later Turn.

- Export takes only a Thread id. Session Core loads the thread, admits thread maintenance through the runtime command owner, flushes durable state, and captures only while the thread is idle and its newest Turn is terminal. The result reports the Turn boundary captured so an adapter can compare it with its own run binding.
- Version 1 is one JSON document with the original Thread id and normalized workspace, execution configuration, source and metadata, latest terminal Turn identity and state, the Turn sequence high-water mark, the encoded model history, and provider continuity state.
- Snapshots are exchanged only through a restricted staging directory under the workspace `.craft` path; callers cannot supply arbitrary paths. Callers clean up staged snapshots, and DotCraft maintenance may remove old ones.
- Restore requires the same workspace and original Thread id, and a target Thread that does not exist. It decodes model history and replays provider state, materializes the Thread header, captured terminal boundary, session checkpoint, and provider checkpoint, and preserves the Turn sequence high-water mark.
- The snapshot is validated before the Thread becomes visible; a failed restore removes state it created.
- Restore returns only the Thread id and emits no synthetic Turn or Item. Execution resources are created only by a later `ResumeThread`.

### 5.2 Turn Lifecycle

Creating a Running Turn is an atomic Thread-local transition: validating that the Thread is Active with no active Turn, reserving the sequence, inserting the Turn, and registering runtime state happen under one start gate. Concurrent submissions create at most one Running Turn; other contenders fail with the turn-in-progress error and append no Items or start events.

```
SubmitInput
    │
    ▼
Running ──approval request──► WaitingApproval ──resolved──► Running
Running ──input request─────► WaitingInput    ──resolved──► Running
Running ────────────────────► Completed | Failed | Cancelled
WaitingApproval/WaitingInput ──────────► Cancelled
```

- `SubmitInput(threadId, content)` → `Running`. Session Core creates the Turn and its `UserMessage` Item and invokes the agent. Precondition: the Thread is Active, no Turn is active, and no thread maintenance is active.
- `Running` → `WaitingApproval`: a tool needs approval. Session Core creates an `ApprovalRequest` Item and pauses; the adapter presents it.
- `WaitingApproval` → `Running`: the adapter resolves the request; Session Core records an `ApprovalResponse` and resumes.
- `Running` → `WaitingInput`: a root-thread agent requests structured user input (Section 10.3). Session Core creates a `UserInputRequest` Item and pauses.
- `WaitingInput` → `Running`: the adapter returns a `UserInputResponse`; Session Core records it and resumes.
- `Running` → `Completed`: the agent finishes; the final `AgentMessage` is completed, the terminal rollout batch is committed, and Stop hooks run.
- `Running` → `Failed`: an unrecoverable error (agent exception, timeout, output-limit truncation). Session Core creates an `Error` Item, sets `Turn.Error`, and persists partial state. Completed model messages and any compaction replacement are appended first, so the next Turn sees the failed Turn's completed user, assistant, and paired tool history without expanding history a surviving compaction checkpoint replaced.
- `Running`, `WaitingApproval`, or `WaitingInput` → `Cancelled`: the adapter requests cancellation. Session Core resolves pending approval and user-input requests (Section 10.2), cancels execution, completes any streaming agent or reasoning Items with their accumulated text, and persists partial domain and model history. Cancellation never restores tool results or summaries that compaction had removed from model-visible history.

`Completed`, `Failed`, and `Cancelled` are terminal; a terminal Turn never transitions.

On the first load of a persisted Thread into a new process, a Turn still `Running`, `WaitingApproval`, or `WaitingInput` has no execution owner and is treated as interrupted. Before the load returns, Session Core sets it to `Cancelled`, sets `CompletedAt`, and commits the terminal Turn with its completed model-visible history. Existing Items, usage, and error data are preserved; no error, response, approval, or user-input Item is synthesized. The commit must succeed before the Thread is registered or `turn/cancelled` is published; a commit failure fails the load and nothing is cached. Session Core then publishes exactly one `turn/cancelled` with reason `"interrupted"`, and the Turn's unresolved requests are no longer replayed. This reconciliation happens only at the cold-load boundary; a Thread already registered in the process is never reconciled by load, resume, or read. Until then, reads of an unloaded Thread report such a Turn as `Cancelled` and its runtime as idle without persisting the change; observing a Thread's events never loads it.

A loaded Thread is unloaded after 30 minutes without events or subscription changes when no connection subscribes to it, no Turn or maintenance is active, no goal continuation is starting, and no App Binding MCP session is attached. Ephemeral and not-yet-persisted Threads are never unloaded. Unloading drops the process-local runtime (agent, Thread MCP connections, tool snapshot); the next operation that needs the Thread loads it again exactly as after a restart. Event subscriptions survive unloading, and an operation that races an unload runs on the reloaded Thread.

### 5.3 Item Lifecycle

```
    ┌─────────┐      ┌───────────┐      ┌───────────┐
    │ Started │ ───► │ Streaming │ ───► │ Completed │
    └─────────┘      └───────────┘      └───────────┘
         │                                     ▲
         └─────────────────────────────────────┘
              (non-streaming items skip Streaming)
```

- `Started` → `Streaming` → `Completed` applies to `AgentMessage`, `ReasoningContent`, and `CommandExecution`. Each delta emits `item/delta`; the completed payload holds the final value. Hosts may project streamed tool arguments as `ToolCall` argument previews; the canonical persisted `ToolCall` is finalized at completion.
- `Started` → `Completed` applies to other Items. `ToolResult`, approval, user-input, and `Error` Items are created complete. `ImageGeneration`, `WebSearch`, `McpToolCall`, and `DynamicToolCall` start `inProgress` when the call is recorded and complete once with a terminal completed or failed payload.

Invariants:

- Item status never moves backward, and a completed payload is immutable.
- A tool invocation has exactly one source-declared projection shape; streaming observation and dispatcher recording atomically upsert its item by Turn, call id, and shape.
- Each invocation publishes at most one terminal projection; competing completion, rejection, timeout, cancellation, and failure paths converge on one terminal guard.
- Items within a Turn are ordered by creation time; this is the canonical sequence of the Turn.
- A tool-call boundary finalizes preceding streaming `AgentMessage` and `ReasoningContent` Items: their payloads are persisted and `item/completed` is emitted before any approval or user-input request from that tool can pause execution.
- Before model-visible history goes to a provider, every assistant tool call is immediately followed by its tool result. An incomplete historical call is repaired or filtered at the request boundary, without mutating rollout evidence.

### 5.4 Turn Item Sequence (Normative)

```
1. UserMessage (input)
2. [ReasoningContent]
3. [ToolCall → ToolResult | ImageGeneration | WebSearch | McpToolCall | DynamicToolCall]*
   3a. [ApprovalRequest → ApprovalResponse] (within a tool call)
   3b. [UserInputRequest → UserInputResponse]
4. AgentMessage (final response, streamed)
5. [Error]
```

The sequence may repeat: the agent may reason, call tools, and reason again before responding. Session Core emits Items in the order they occur; adapters render them according to channel capability.

### 5.4.1 MCP App Direct Actions and Transient Context

An MCP App view action does not belong to the completed Turn that produced its source result.

- A view `tools/call` uses the common dispatcher with no Turn. It creates no Turn or tool Item and never enters provider history; tracing records its origin (`mcpApp`, source item id) separately.
- An accepted `ui/message` is the only view action that creates conversational input. It starts a Turn when the thread is idle or is queued otherwise, marked `triggerKind = "mcpApp"` with safe server, tool, and source-item provenance. The view handle and UI resource data are not persisted.
- Each live view owns one last-write-wins pending context. A view message consumes only its own value; an ordinary user Turn consumes all live pending values for the thread. Consumption happens at the central Turn-start boundary, including the queued path, and each value is injected at most once.
- Pending context is bounded, marked untrusted, and transient: not an Item, rollout record, configuration, or provider-history message by itself, and it cannot change system policy or tool authority. Teardown, revoke, disconnect, archive, delete, or MCP generation replacement discards it, and it does not survive a restart even if its queued message does.
- Unknown context content shapes reject the whole update. Text and image content use the normal safe input path; structured content is injected only as bounded JSON.

### 5.5 Cross-Channel Resume Semantics

1. **Discovery**: the adapter calls `FindThreads(identity, includeArchived, crossChannelOrigins)` and presents the result or picks the most recently active thread (Section 9.5).
2. **Resume**: `ResumeThread(threadId)` loads the Thread, sets it `Active`, and updates `LastActiveAt`.
3. **Session load**: Session Core rebuilds request-local model history from the rollout's exact model-history records and latest usable compaction checkpoint. Domain Items are a fallback only where exact records are absent.
4. **Ready**: `SubmitInput` starts a new Turn attributed to the resuming channel.

The resumed agent has the full context of earlier Turns regardless of channel.

History reconstruction rules:

- Provider reasoning metadata is preserved on assistant messages that contain tool calls. A sampling segment that produced reasoning, visible text, and tool calls before their results is reconstructed as one assistant message containing all three, as required by providers whose thinking mode round-trips reasoning on tool-call messages.
- Tool history reconstruction is source-neutral. A valid `ToolCall`/`ToolResult` pair or terminal `McpToolCall`/`DynamicToolCall` expands into the provider's function-call/result sequence with the original `callId`. Namespace-capable providers receive the persisted `(namespace, toolName)`; flat-only providers receive the persisted `providerFlatName`. Reconstruction never consults the current tool inventory, parses or regenerates a flat alias, or substitutes MCP server/source names. Orphaned, duplicated, incomplete, or identity-incomplete calls are diagnosed and skipped or rejected at the request boundary; pairings are never guessed. Only normalized model content participates.
- Validated tool-result images stay structured media until compaction. Summary-producing compaction replaces them with their bounded textual description in the replacement history. Images on a user message remain only if that message is in the preserved tail.

## 6. Event Model

### 6.1 Overview

Session Core emits a structured, in-process event stream during Turn execution. It is the contract between Session Core and channel adapters, which translate events into their transport format.

### 6.2 Event Envelope

Every event carries `EventId` (monotonic within a Turn), `EventType`, `ThreadId`, `TurnId` (null for thread-level events), `ItemId` (null for turn- and thread-level events), `Timestamp`, and a type-specific `Payload`.

### 6.3 Event Types

#### Thread Events

- `thread/created` — `{ thread }`.
- `thread/resumed` — `{ thread, resumedBy }` (resuming channel).
- `thread/archived`, `thread/unarchived` — `{ threadId }`.

Rename (explicit, provisional, or generated title) and permanent deletion are not events on the Turn stream; the wire protocol projects them as its own notifications. Permanent deletion closes the thread recorder, deletes the canonical rollout, removes database state and attachment references, and finally best-effort deletes managed attachment files no remaining thread references. If the rollout cannot be deleted, database state and attachments are left intact.

#### Turn Events

- `turn/started` — `{ turn }` with `Status = Running` and the input Item.
- `turn/completed` — `{ turn }` with all Items and usage. Provider output-limit termination is not successful completion.
- `turn/failed` — `{ turn, error }`. Also emitted when the provider output token limit truncates the response, so clients can retry instead of accepting it.
- `turn/cancelled` — `{ turn, reason }`.

#### Item Events

- `item/started` — `{ item }` with `Status = Started`. Adapters may begin rendering immediately.
- `item/delta` — incremental payload (for example `{ textDelta }`) for streaming Items and `ToolCall` argument previews. Adapters may forward or ignore deltas; the completed payload is the source of truth. A `CommandExecution` Item also receives a terminal interaction delta `{ sessionId, stdin }` when `WriteStdin` writes to or polls (empty `stdin`) its process; it carries the command's own Turn id, may arrive after the Item completed or during a later Turn, and is not persisted.
- `item/completed` — `{ item }` with the full payload.

#### Approval Events

- `approval/requested` — `{ item }` (`ApprovalRequest`). Distinct from `item/started` because the adapter must act. The Turn enters `WaitingApproval`.
- `approval/resolved` — `{ item }` (`ApprovalResponse`). The Turn returns to `Running`.

#### User Input Request Events

- `userInput/requested` — `{ item }` (`UserInputRequest`). The Turn enters `WaitingInput`.
- `userInput/resolved` — `{ item }` (`UserInputResponse`). The Turn returns to `Running`.

#### SubAgent Progress Events

- `subagent/progress` — a complete snapshot of all SubAgents tracked in the Turn, emitted about every 200 ms while any is active: `{ entries: [{ label, currentTool, inputTokens, outputTokens, isCompleted }] }`. `label` matches the spawn nickname; `currentTool` is null while thinking. Clients replace local state on each receipt. Tracking starts with the first spawn in a Turn and stops when the Turn ends or all tracked SubAgents complete. The event is a sideband signal and may interleave with Item events.

#### System Events

- `system/event` — session maintenance and transient runtime status that is not conversational output. Payload: `kind`, `messageKey` (localization key), `params`, `fallbackText` (English), `message` (alias of `fallbackText`), `percentLeft` (unused fraction of the effective context window), `tokenCount` (estimated prompt tokens), `contextUsage` (full context usage snapshot).

| Kind | Meaning |
|------|---------|
| `compactWarning` | Context usage crossed the warning threshold. Advisory. |
| `compactError` | Context usage crossed the error threshold. Auto-compaction may run on the next Turn. |
| `compacting` | Compaction is starting. `percentLeft`/`tokenCount` are a projected hint and carry no `contextUsage`. |
| `compacted` | Compaction succeeded (`micro` or `partial`). Values reflect the post-compaction model-visible state. Only `partial` creates a persisted notice. |
| `compactSkipped` | Compaction evaluated but not executed. |
| `compactFailed` | Compaction attempted and failed. |
| `compactCancelled` | Manual compaction was interrupted; nothing is installed. |
| `guidanceBlocked` | A `UserPromptSubmit` hook blocked pending guidance (Section 6.5). |
| `streamError` | A provider stream broke before the sampling request completed and Session Core is retrying. `params` carry one-based `attempt`, `max`, classified `providerError`, and `httpStatus` when known; `fallbackText` is `Reconnecting... x/y`. |

Emission rules:

- Every compaction attempt emits `compacting` followed by exactly one terminal event. Threshold advisories and successful `compacted` events carry `contextUsage` when available; clients prefer it over `tokenCount`/`percentLeft` and never update context UI from a `compacting` event.
- Turn-scoped system events arrive before the Turn's terminal event. Thread-scoped maintenance events (manual compaction) may arrive later.
- Reactive compaction after a context-overflow rejection still fails the Turn, but its `compacting` and terminal events are emitted first.
- Manual compaction is thread maintenance (`maintenanceKind = "compacting"`): allowed only for Active server-managed threads with history and no active Turn or maintenance, it blocks new Turns until its terminal event and on success appends a `compacted` notice with `trigger = "manual"` to the latest completed Turn. Interrupting thread maintenance is a Session API operation.
- `streamError` is transient and never creates a `SystemNotice`. The first attempt of a retry sequence is not surfaced. Retry does not depend on whether the attempt delivered output: a stream that broke after delivering updates is committed to model history as a response that ended early, its tool calls are settled, and the next attempt continues from the grown history. Delivered Items and deltas are never withdrawn. A failure before any update reaches the runtime is retried by the transport layer, which suppresses usage, provider error frames, and echoed input from the discarded attempt. Idle-timeout detection surfaces the retry or failure promptly; cleanup of the failed stream never delays it.
- System events carry `messageKey`, optional `params`, and English `fallbackText`; localizing clients translate the key. User text, model output, and tool output are never translated.

Context usage accounting:

- `contextUsage.tokens` is server-authoritative active context-window occupancy, separate from billing usage: after each provider usage snapshot, the latest request's provider-visible input plus its output.
- Sources, in order: a valid provider usage anchor plus model-visible messages appended after it; a prefix-adjusted anchor when the anchored prefix and non-instruction request shape still match (base-instruction changes adjust by their estimated token delta, and prompt drift alone does not force a fallback); the latest provider snapshot only while it belongs to the current request boundary; persisted provider display values for UI only; and finally a full model-visible history estimate.
- Auto-compaction may trigger from provider context only while the anchor or request-boundary snapshot is valid for the current history. After rollback, compaction, deletion, or another history replacement, anchors and trackers are invalidated and a replacement-history estimate drives the next decision until a new provider snapshot arrives; a provider-native replacement uses its generation-scoped estimator. Persisted display values without a valid anchor never trigger auto-compaction.
- The effective context window is resolved for the thread's effective model (Section 12.2).

#### Usage Events

- `usage/delta` — emitted once per model request whose usage is non-zero, carrying that request's incremental usage: `inputTokens`, `outputTokens`, `cachedInputTokens`, `cacheWriteInputTokens`, `freshInputTokens`, `reasoningOutputTokens`, `llmCallDelta`, `contextInputTokens`, and optional cumulative `turnInputTokens`, `turnOutputTokens`, `turnLlmCalls`, plus `contextUsage` when available.
  - `contextInputTokens` is the latest main-agent request input, not a sum: request inputs `12000 | 20000 | 41000` give `turnInputTokens = 73000` and `contextInputTokens = 41000`. Cache totals follow the same request-sum rule.
  - `contextUsage.tokens` may exceed `contextInputTokens` because it includes the request's output and messages appended after an anchored request.
  - The sum of a Turn's `usage/delta` events equals the main-agent portion of `Turn.TokenUsage`; SubAgent usage is reported through `subagent/progress` and added at Turn completion.
  - A sideband signal that may interleave with Item events.

#### Turn Diff Events

- `turn/diff/updated` — `{ diff }`: the complete latest git-style unified diff (per-call `fileChange` format) of the Turn's `WriteFile` and `EditFile` edits; may be empty.
  - Tracking: one tracker per user Turn. The first edit of a path records its pre-edit text as baseline; later edits update only current text. A path that did not exist has no baseline. Paths render baseline-to-current sorted by display path, so create-then-edit is one add, an overwrite is an update against prior content, and an unchanged path renders nothing. Path keys are case-insensitive on Windows.
  - Edits by in-process SubAgents during the Turn are included. Shell-command file changes are not tracked and do not invalidate the tracker.
  - The tracker is invalid for the rest of the Turn when an edit's exact before/after text is unknown (for example an edit on a Remote Tool Host) or the aggregate exceeds 1 MiB. Invalid emissions carry an empty `diff`, sent only if a non-empty aggregate was emitted before.
  - Emitted right after the `item/completed` of the `ToolResult` that changed the aggregate, and flushed before the Turn's terminal event if it changed since the last emission. Identical snapshots are not re-sent.
  - Not persisted, though recent emissions are replayed to new subscribers. After reload, changes are rebuilt from the per-call `fileChange` records on [ToolResult](#toolresult); adapters also fall back to them when `diff` is empty.

Adapters that do not need a sideband event type (`subagent/progress`, `system/event`, `usage/delta`, `turn/diff/updated`) may ignore it or opt out of it.

### 6.4 Event Delivery Semantics

- **Ordering**: events within a Turn are causal. `item/started` precedes `item/delta` and `item/completed` for the same Item; `turn/started` precedes all Item events of the Turn; the Turn's terminal event is last.
- **At-most-once**: events are not durably queued. A disconnected adapter loses them and reconstructs state from the persisted Thread.
- **Decoupled emission**: Session Core writes events into a per-Turn in-memory channel and does not wait for rendering.
- **Single consumer per Turn**: one adapter consumes a Turn's events, following from the one-active-Turn invariant.

### 6.5 Session Input API

`SubmitInput` starts a Turn and returns that Turn's authoritative event stream; there is no separate subscribe operation, and callback-style consumption is a helper concern. Input is a list of multimodal content parts plus an optional sender context. When the transport provides native input parts (commands, skills, file references), it validates and materializes them before submission; Session Core persists both snapshots on the `UserMessage`, derives `text` from the native snapshot, and passes the materialized content to the agent.

- `EnqueueTurnInput` appends a `queued` input while another Turn may run. The queue is persisted as rollout records and drained per Section 4.1.1, with `deliveryMode = "queued"`.
- `RemoveQueuedTurnInput` removes a queued input without starting a Turn.
- `StartQueuedTurn` starts a chosen or the first queued input as a new Turn on an idle Thread, resuming a paused queue.
- `UpdateQueuedTurnInput(threadId, queuedInputId, expectedTurnId, status)` sets status to `"queued"` or `"guidancePending"`. Promotion requires `expectedTurnId` to be the active Turn; restoring `queued` validates the input's bound Turn without requiring it to be active. Setting the current status again succeeds idempotently.
- `SteerTurn(threadId, expectedTurnId, content)` atomically appends a `guidancePending` input for the active regular Turn. It rejects a missing or mismatched active Turn, maintenance, Internal threads, and SubAgent child threads, and creates no Turn and no start event.

Guidance admission happens only at the pre-sampling drain point ([Running input and history consistency](#running-input-and-history-consistency)). One drain admits every `guidancePending` input bound to the Turn, in queue order, each as its own `UserMessage`. Before admission, outside the queue lock, `UserPromptSubmit` runs for each input with its display text, except SubAgent follow-up tasks. A blocked input is removed without an Item or model history and reported as `guidanceBlocked`; an input that resolves to nothing is removed silently; context from a non-blocking hook follows its input as model-only history. The final status recheck, `UserMessage` persistence, and queue removal happen under the per-thread queue lock shared with update and removal: if an update to `queued` wins, admission skips the input; if admission wins, the later update fails. If the Turn ends first, pending guidance returns to `queued`.

## 7. Channel Adapter Contract

### 7.1 Role

A channel adapter is the boundary between a transport and Session Core. It turns inbound user actions into Session API calls, turns events into channel UX, routes approval and user-input decisions back to the active Turn, and exposes discovery and resume in whatever UX the channel supports. It is part of the channel implementation, not a separate framework interface.

### 7.2 Contract

- `CreateThread`, `ResumeThread`, and `FindThreads` define thread lifecycle at the transport boundary.
- `GetThread` returns persisted state and may cache the thread **without** building execution resources such as per-thread MCP connections.
- Before Turn execution, Session Core ensures the thread is loaded and, when `Configuration` is present, that the effective agent matches it — the same outcome as `ResumeThread` from disk — without changing status or emitting `thread/resumed`. This covers threads that exist only on disk or were cached without agent hydration.
- `SubmitInput` starts a Turn and returns its event stream.
- `ResolveApproval`, user-input resolution, and `CancelTurn` participate in interactive control flow.
- `SetThreadMode` and `UpdateThreadConfiguration` support per-thread behavior where a channel exposes it.
- `ExportThreadRecovery` and `RestoreThreadRecovery` expose the recovery lifecycle (Section 5.1.2) to trusted adapters.

### 7.3 Design Constraints

- Adapters may choose any UX pattern (streaming, buffered, structured, non-interactive).
- Adapters never own persistence, agent lifecycle, or hook orchestration.

## 8. Agent Execution Integration

Session Core wraps the agent pipeline rather than redefining it. Agent creation, tool invocation, tracing, and middleware keep their own owners; Session Core standardizes how their output becomes Threads, Turns, Items, and events. Host entry points that wrap a Turn (for example the automation runner) delegate to Session Core rather than forming a second session model.

For each submitted Turn, Session Core validates thread state and mutual exclusion, creates the Turn and its input Item, executes the agent against history hydrated from the rollout, translates output into typed Items and events, collects usage, persists domain and model history, and emits the terminal event.

## 9. Persistence Specification

### 9.1 Storage Layout

```
.craft/
├── threads/
│   ├── active/
│   │   ├── {threadId}.jsonl                 # Canonical rollout when threadId is a safe file name
│   │   └── thread-{sha256(threadId)}.jsonl  # Canonical name when threadId has invalid file-name characters
│   └── archived/                            # Same naming
├── terminals/{threadDir}/
│   ├── {terminalSessionId}.json             # Terminal metadata
│   └── {terminalSessionId}.log              # Complete terminal output
├── tool-results/{threadDir}/
│   └── {stableToolCallArtifact}.txt         # Tool-result spill file
├── state.db                                 # Classified SQLite state, projections, diagnostics, usage
├── attachments/                             # Managed attachments: images/ for images, <id>/ for files and pasted text
└── cache/                                   # Rebuildable cache; not part of the thread lifecycle
```

`{threadDir}` uses the same rule as rollout names: a safe Thread id stays readable, otherwise `thread-{sha256(threadId)}`. Paths always resolve under the corresponding `.craft` root; no other directory shape is part of the contract.

### 9.1.1 Workspace Data Upgrades

`state.db` records the release that last upgraded the workspace's data in `workspace_meta` (`data_version`). When a release changes the shape of existing data, it ships one upgrade that runs once when the state database is first opened for writing, before any Thread loads: it rewrites the affected rollouts (to a temporary file, then an atomic replace) and tables, then records its release. Code reads only the current shape, so there are no compatibility readers. The upgrade is removed in the next release, so a workspace that skips a release keeps that release's old data shape.

The 0.9.2 upgrade removes the workspace path from `thread_opened` and the `threads` table, stores rollout paths relative to the data directory, and drops the `turn_started`, `item_appended`, and `turn_completed` records that a later `turn_state_replaced` for the same Turn supersedes, then clears those Threads' history projections so they rebuild.

### 9.2 Thread File Format

Each thread is an append-only JSONL rollout. Every line is a record `{ kind, timestamp, <payload> }` describing one state transition. Record kinds:

- `thread_opened` — thread baseline, including the versioned source union and `providerHistorySchemaVersion`.
- `turn_started`, `item_appended`, `turn_completed` — incremental Turn records, still readable.
- `turn_state_replaced` — a complete Turn mutation. The normal terminal path writes one per Turn; replay replaces that Turn atomically, including its ordered Items, terminal state, input, usage, and error. Streaming deltas and other transient updates are never recorded.
- `queued_input_added`, `queued_input_removed`, `queued_input_updated`, `queued_input_reordered`.
- `model_history_messages_appended`, `context_compacted` — model-visible history (Section 9.3).
- `provider_history_items_appended`, `provider_history_replaced`, `provider_history_attempt_aborted` — protocol-native provider history.
- `thread_rolled_back` — rollback (Section 9.7).

Session Core reconstructs a Thread by replaying the rollout in order; Turns are ordered by the position where they first appear in the rollout.

A change is persisted as the records it produces. Queue changes append their `queued_input_*` record, and new or changed Items of a running Turn append `item_appended` against what that Turn has already persisted. No write reads the rollout: the thread store remembers, per Thread, the baseline it last persisted (seeded from the history projection) and appends a new `thread_opened`, `thread_name_updated`, or `thread_status_changed` only for the fields that changed.

`thread_opened.source` is a DotCraft-owned versioned union (`schemaVersion = 1`), not a serialized runtime type. The user variant stores `kind` and optional `spawnedFromThreadId`; the subagent variant also stores parent thread, root thread, parent turn, parent call, depth, path, task, nickname, role, profile, runtime, and all send, resume, message, follow-up, and close capabilities. A source or `GitInfo` change appends a new `thread_opened`; replay uses the latest. Every rollout begins with a canonical `thread_opened`. An unknown source schema version or structurally invalid source is an identity safety error that stops replay; it is never downgraded to an ordinary user thread.

### 9.3 Model-History Storage and Runtime Execution

The rollout is the sole authority for both the domain model and optimized model-visible history. Session Core replays it into DotCraft-owned model-history messages, converts them to request-local history, and passes that history explicitly to the agent. Agent objects and middleware are not persistence records.

The domain history and the model history are distinct durable representations: `turn_state_replaced` preserves client-visible Turn and Item state; `model_history_messages_appended` preserves the exact provider-facing message sequence. Neither must be losslessly derivable from the other, and model metadata is not copied into Items merely to remove duplicate text.

- **Append**: every successful mutation of server-managed model history appends its records to the same rollout before it is considered durable.
- **Load**: the first execution after a load reconstructs model history and world state with the bounded replayer, and the runtime keeps them in memory: each committed suffix is appended and each compaction replacement installed. Provider-history records are kept in memory by the thread store as they are written. Later Turns never read the rollout to build a request. The in-memory model history is valid only while no other model-history write has occurred and it covers exactly the surviving Turns; otherwise, and after unload, the next execution replays again. Turns rebuilt from their Items on replay (imported or synthetic Turns) are never kept.
- **Fork**: a persistent fork writes its materialized model history into its own rollout.

A successful context replacement has one recoverable commit boundary: the compaction checkpoint and replacement history are committed before derived context-usage, provider-history, context-window, or cache projections are published. Recovery replays the checkpoint and repairs the derived projections; it never pairs a new provider window with pre-compaction history or compacted history with an unrelated provider prefix. In-memory history and UI notifications become observable only after the commit.

`context_compacted` is an internal replacement record, not an Item or client event, and uses the same model-history schema. During replay the newest checkpoint whose covered Turn survives establishes a new baseline; earlier model-history records are discarded and later surviving records are appended. Rollback that removes the covered Turn invalidates the checkpoint; removing only later Turns keeps it.

Current rollouts require `thread_opened.providerHistorySchemaVersion = 1` and may persist protocol-native provider history (for OpenAI Responses, the source of the future wire `input` array). Session Core stores these records opaquely and never exposes them through display history; the generic model history remains the cross-provider representation.

Workspace-managed attachments under `.craft/attachments/`, referenced by persisted `localImage`, `fileRef`, or `contextRef` input parts, are indexed in SQLite. Attachment bytes are independent assets: the rollout stores references only. A file stays available while any active or archived thread references it. Permanent deletion removes the thread's references and best-effort deletes now-unreferenced files. Attachments that never enter history (unsent drafts, inputs dropped before a Turn, failed sends) are removed by an orphan sweep once a day old. The sweep runs at most hourly, only after a discovery pass in which every rollout's projection is current, and is skipped while any rollout is unreadable.

Per-thread plans are stored in SQLite; archive and unarchive keep them, and permanent deletion cascades. Native agent trees own durable shared discussion state independently of transcript projections.

#### 9.3.0 Model-History Record Format

`model_history_messages_appended` atomically stores one Turn-local ordered batch of versioned messages. Each message carries `schemaVersion`, `turnId`, role, optional message identity, author, and timestamp, and ordered contents. The format is independent of runtime type envelopes and serializer-specific shapes.

Every content value is `{ kind, payload }` with schema version 1. The payload union contains only these durable fields, plus content-level `additionalProperties`:

| Kind | Durable fields |
|---|---|
| `text` | `text` |
| `reasoning` | `text`, `protectedData` |
| `data` | `base64Data`, `mediaType`, `name` |
| `function_call` | `callId`, `name`, `arguments`, `informationalOnly`, `namespace`, `providerFlatName` |
| `function_result` | `callId`, versioned result union |
| `error` | `message`, `errorCode`, `details` |
| `uri` | `uri`, `mediaType` |
| `usage` | standard token counts and `additionalCounts` |
| `deferred_tool_reference` | `toolName` |

- Only version 1 is written or read; unknown versions are rejected through bounded replay. Rollout records are never migrated or rewritten.
- A function result is either `json` (any JSON value) or `contents` (an ordered list from the same content union). Binary data is stored once as base64; derived data URIs are not duplicated.
- Function-call `namespace` and `providerFlatName` are strong fields even when also present in extension properties. A conflict between them makes the content invalid, and replay applies its whole-Turn fallback rather than choosing one.
- Message- and content-level extension properties round-trip as JSON; runtime object identity is not preserved. Raw provider representations and runtime exception objects are never persisted.
- Streaming tool-argument deltas are transient and never written. Any other unsupported runtime content fails the write; the writer never silently drops an unknown content type.

#### Running input and history consistency

The active model/tool loop keeps an ordered history of initial input, model responses, tool results, and admitted steering and passive input. Delivered input remains available to later Turns, cold loading, and recovery export.

Running input enters the loop at one pre-sampling drain point:

- The first sample of a Turn admits only passive mailbox input and queued task notifications.
- After a tool round, pre-sampling preparation (including compaction) runs first and then the drain, so admitted input always follows any compaction baseline verbatim.
- After a final answer, the loop continues only while user guidance is pending, up to a fixed limit per model request, running the same preparation and drain; if the drain admits no guidance, the Turn ends without sampling again.
- Stream-failure reissues and provider-managed continuations do not drain, except after an instant interrupt.
- A drain takes the thread's SubAgent inbox lock before the queue lock, the same order SubAgent follow-up delivery uses. It appends, in order: goal steering, each admitted guidance input followed by its hook context, SubAgent mailbox input, and world-state changes.
- The request snapshot used for context estimates and reactive compaction is taken from the final request after the drain.

With instant interrupt enabled (the default), pending user guidance also preempts the current model request at any point while it streams, including during reasoning or assistant text, and while the loop waits before a stream-failure reissue. Only `guidancePending` user input for the Turn triggers preemption — not SubAgent follow-up tasks, task notifications, goal steering, mailbox, world state, or queued input. Preemption cancels only the provider request, does not consume the retry budget, and reissues after the same preparation and drain as a tool round. A request preempted before any effective output (assistant text, reasoning, or a tool call) discards its partial updates and any provider-native output recorded for it. A request preempted after effective output ends like a stream failure that is reissued: its partial output stays in the transcript and model history, and its complete tool calls run before the reissue. The same guidance also yields a `CodeMode` call waiting on its cell; see [Code Mode](../features/code-mode.md#44-yielding-and-codemodewait). With the option off, guidance never preempts.

Session Core persists the model-history prefix containing an input before consuming its queue entry or confirming communication delivery. Each admitted input has one identity record linking input id, Item id, and Turn id in model-message metadata; restart reconciliation uses it to complete interrupted delivery confirmation. Provider message ids are a separate identity domain. A history write failure stops execution and leaves delivery unconfirmed.

Completion, failure, and cancellation commit only the unpersisted history suffix. Compaction installs a replacement baseline that later appends and terminal commits build on. The agent pipeline reports history appends and replacements to Session Core, which owns incremental persistence; standalone execution outside Session Core buffers changes and commits caller-owned history on success. Request sanitization and generated instructions stay local to sampling requests.

### 9.3.1 Ordered Rollout Writer

All records for a thread pass through one workspace-lifetime recorder keyed by thread id, with one bounded queue that owns its pending suffix and any open append stream. Enqueue order is append order. A terminal Turn mutation enqueues the Turn replacement, any compaction replacement, and its model-history suffix as one batch behind one flush barrier. The file handle may be released after a barrier so ordinary readers can inspect the rollout; recorder lifetime is independent of handle lifetime.

A flush receipt reports the confirmed byte offset and record sizes; SQLite projections advance only after the barrier succeeds. Shutdown flushes and closes every recorder. Different threads write concurrently; one thread has exactly one append order.

Records leave the pending suffix only after their complete lines are written. On I/O failure the recorder reopens the file and retries without rewriting confirmed lines; a non-newline-terminated tail is terminated first so readers can skip the damaged line. Exhausted retries block later writes for that thread and fail the affected Turn with a persistence error; a later flush or shutdown may retry the retained suffix.

### 9.3.2 Bounded Model-History Replay

One shared, read-only replayer selects model history for runtime hydration and current-context export. It takes a rollout path, the ordered surviving Turn ids (from the history projection), an optional excluded current Turn, and the expected Thread id. The path must resolve under `.craft/threads/{active,archived}`, and the canonical `thread_opened` identity and every model-history and checkpoint payload must match the expected Thread id. For uncompressed JSONL it scans backward in bounded blocks and stops at the newest decodable `context_compacted` whose covered Turn survives. The result is the checkpoint replacement followed by later model-history messages in physical order, including later messages of the covered Turn.

- Malformed lines, records, batches, and contents are rejected individually: skipped, counted, and reported as a structured warning without protected or extension payloads. If a rejected record belongs to a Turn, that Turn's entire model-visible contribution is rebuilt from its Items, never mixed with its valid exact batches. Other Turns keep exact history.
- An invalid checkpoint is discarded whole and scanning continues to an earlier one. Without a usable checkpoint the scan reaches the start and recovers every decodable surviving record, without first materializing a second complete domain thread. A surviving tail Turn with no exact records also uses the Item projection.
- Canonical `thread_opened` source-schema errors stay fail-fast.
- The result reports messages, warnings, rejected-record count, fallback Turn ids, bytes read, and decoded-record count.

Full forward domain replay is limited to recovery export, current-context export, and projection rebuilds. UI history reads and runtime operations use the paged projection (Section 9.4.2).

### 9.3.3 Rollout Observability and Search Safety

- Rollout diagnostics measure record count and bytes by kind, flush duration and outcome, and resume bytes, decoded records, and rejected records. Dimensions stay low-cardinality and never contain thread ids, message content, protected data, or extension properties.
- Context search treats model-history, provider-history, and compaction records as internal recovery state and never indexes or previews them, protected data, extension properties, system prompts, raw trace JSON, channel context, or tool arguments and results. Searchable evidence comes from explicitly displayable domain fields, not raw lines; a value duplicated in model history counts once. Tool arguments and results are omitted or `[redacted]` at presentation.
- Diagnostic readers apply Session Core replay semantics: later `turn_state_replaced` records win, rollback removes the visible tail, and only surviving terminal Items contribute errors and tool summaries. Model batches may expose counts, Turn ids, schema versions, content kinds, and rejection status, never payloads. Compaction diagnostics expose only checkpoint boundaries and decode status.
- Current-context export uses the shared replayer. Its Markdown excludes reasoning, protected data, extension properties, internal provider metadata, and the free-form thread `Metadata` map (which may hold credentials, external session ids, or private paths). Export uses an explicit allowlist of thread fields — display name, status, timestamps, origin channel, history mode, Turn count — and surfaces sanitized replay warnings.

#### Dashboard Diagnostic Reads

- Deleting one trace session removes its trace and dashboard usage rows; a session bound to a thread cascades through permanent thread deletion. Clearing all trace sessions deletes the selected trace and thread state and associated usage rows, but keeps global usage rows with no thread or session key, and may then reclaim SQLite space.
- Internal prompt-suggestion forks bind to their parent trace session and record parent Turn, fork Turn, and outcome. Provider completion metadata stays separate from Turn status, so an empty successful response is distinguishable from a transport failure. Temporary fork cleanup keeps diagnostics under trace retention.
- Trace event reads are paged newest-first with an opaque `beforeCursor` for older events. Maintenance events are filterable and counted separately from LLM request/response totals.
- The effective `AGENTS.md` context snapshot is recorded as an `AgentInstructions` trace event: the rendered instructions before any world-state notice, with schema version 1, item kind `agents_md.instructions`, role `user`, the content-and-sources fingerprint, and ordered absolute source paths. It is recorded at thread lifecycle boundaries and before provider use, de-duplicated by fingerprint; an empty snapshot is recorded as empty. Dashboard reads it from the trace store and never re-reads instruction files or infers sources from history. It is diagnostic-only and never projected into rollout or model history. Model-visible AGENTS changes travel as world-state context, not through this event.
- Dashboard may project read-only rollback operations from `thread_rolled_back` records (`type = rollback`, thread id, timestamp, removed Turn count, source). Hidden recovery records such as compaction checkpoints are never exposed.

### 9.4 Thread Discovery

Discovery runs a filesystem-first read-repair pass, then reads the SQLite thread metadata projection. Rollouts remain canonical; summary rows are derived.

Each metadata row records the confirmed rollout byte offset from the flush that produced it. Discovery enumerates active and archived rollouts and compares paths and lengths with SQLite. Matching rows need no rollout read; a mismatched offset projects only the unprojected suffix, and a missing row, changed path, or shortened file rebuilds that thread's projection. Concurrent initial discovery shares one reconciliation.

If SQLite listing or repair is unavailable, discovery returns summaries built from each readable rollout's latest `thread_opened`, name, and status records and its Turn-membership and rollback record envelopes and records a diagnostic warning; it never infers or migrates non-canonical paths. Rows whose rollout is absent are omitted but not deleted by read-repair. SQLite projections never participate in model-history reconstruction.

Each discovered thread summary returns `Id`, `Status`, `OriginChannel`, `ChannelContext`, `UserId`, `WorkspacePath`, `DisplayName`, `CreatedAt`, `LastActiveAt`, and `TurnCount`.

### 9.4.1 Persistence Authority Classification

| Category | Data | Recovery contract |
|---|---|---|
| Rollout session authority | Thread baseline and source, Turns, Items, queued inputs, model history, compaction | Replayed from the thread rollout |
| SQLite durable business authority | Goals, plans, spawn edges, subagent mailbox, visualization widget state | Not rebuilt by rollout read-repair |
| SQLite runtime continuity state | Context windows | May degrade to a new transient window when unavailable |
| SQLite disposable cache and sidecar | Context usage | May be discarded and recomputed |
| SQLite rebuildable projection | Threads, confirmed rollout offset, attachment references, thread read snapshots, Turns, Items | Rebuilt from readable rollout records |
| Diagnostics and accounting | Traces, token usage, dashboard usage | Independent diagnostic retention |
| Independent file assets | Attachment file bytes | Rollout preserves references only |

Thread rows store their rollout path relative to the data directory and record no workspace path. Read-repair updates only thread metadata and attachment references; it never touches business authority, continuity state, diagnostics, or unrelated tables. Losing `state.db` is partial data loss: conversations recover from rollouts, while goals, plans, graph edges, and mailbox state do not.

Thread metadata (Turn count, first user message, token totals) and attachment references are derived by the history projection from the records it applies, never from an in-memory Thread. Projection repair and every write update them and their offsets only after the rollout flush succeeds; one projection pass publishes history, the metadata row, attachment references, and their offsets in one transaction. Attachment projection uncertainty disables attachment garbage collection until a later successful repair. Each thread row records the attachment index version that produced its references, and repair reprojects older versions even when the offset matches, so references exist before the orphan sweep may delete anything.

### 9.4.2 Paged Thread History Projection

Persisted display history is read from a rebuildable SQLite projection. Display reads never materialize a complete Thread or replay a complete rollout to return a Thread header, a Turn page, or an Item page. Execution, resume, rollback, compaction, and fork read older Turns from this projection or model history through the bounded replayer; none replays the complete rollout.

The projection has one state row per Thread (rollout path, projected offset, next rollout ordinal, Thread snapshot without Turns, persisted runtime summary), one row per Turn (without Items), and one row per Item (keyed within its Turn, with its original and most recent rollout ordinal). All rows cascade from the owning thread row. Turns page by `(thread_id, rollout_ordinal)`; Items page by thread or by `(thread, turn)`. Process-local maintenance state is overlaid on the runtime summary at read time. These encodings are internal projections, not new authorities.

Ordinals are allocated monotonically per rollout. A Turn or Item keeps the ordinal from when it first became visible; updating an Item advances only its updated ordinal. Rollback removes the visible Turn tail and its Items without lowering the next ordinal, so positions are never reused.

Projection order, under the per-thread rollout write gate:

1. Append and flush the canonical rollout batch.
2. Obtain the confirmed byte offset.
3. Update the affected snapshot, Turn rows, and Item rows in one SQLite transaction.
4. Advance the projected offset and next ordinal last in that transaction.

An SQLite failure after a confirmed rollout write keeps the previous projection checkpoint; it never fails the canonical mutation retroactively or publishes a partial projection. Writers for different threads run concurrently.

Record application:

- Thread baseline, name, status, configuration, and queued-input records update the snapshot.
- `turn_started` and `turn_completed` insert or update Turn rows.
- `item_appended` inserts or replaces an Item id while keeping its original ordinal.
- `turn_state_replaced` replaces the Turn row and its exact Item set; absent Items are deleted.
- `thread_rolled_back` removes the visible Turn tail and its Items.
- Model-history, compaction, and provider-history records contribute no rows; only their outer envelope is read to advance the checkpoint, and provider-history item JSON is never decoded, stored, searched, logged, or exposed. Malformed provider history never makes display pages unavailable.

Persistent forks project their own rollout and share no rows with the source. Archive and unarchive update only the projected path. Permanent deletion cascades all rows. Ephemeral threads have no paged history.

Projection state is created and repaired on demand, not by a startup scan. A missing state row triggers a streaming rebuild; a valid checkpoint behind the file length scans only the complete suffix; a changed path, shortened file, invalid record boundary, or invalid projected JSON clears and rebuilds that thread. Repair runs under the per-thread gate, publishes one transaction, and retries the query.

There is no full-rollout fallback. A malformed record after the canonical header is skipped with a warning, as in replay. Canonical identity errors, unrecoverable projection errors, and failed query or repair return the stable error `ThreadHistoryUnavailable`. Deleting `state.db` or the history tables is supported because the next successful read rebuilds the projection.

Read-only query operations:

- `ReadThreadSnapshot(threadId)` — Thread header and persisted runtime.
- `ListThreadTurns(threadId, cursor, limit, direction)` — one ordered Turn page without Items.
- `ListThreadItems(threadId, turnId?, cursor, limit, direction)` — one ordered Item page with owning Turn ids.
- `ReadThreadItem(threadId, turnId, itemId)` — one Item, for actions on an older Item such as opening a View.

Projection telemetry contains no content, provider payload, or raw identifier: hits, incremental repairs, full rebuilds, failures by reason, query duration, row counts, repair bytes and records, and `ThreadHistoryUnavailable` counts. Healthy first and last pages over a large history must equal canonical replay, must not open the rollout, and use memory proportional to page size plus the largest single record.

### 9.5 Cross-Channel Resume Protocol

#### Default discovery (no `crossChannelOrigins`)

`FindThreads(identity, includeArchived, crossChannelOrigins: null)` searches the workspace's own store and matches on two identity fields; `ChannelName` is **not** a filter:

| Field | Behavior |
|---|---|
| `UserId` | Matched if non-null in identity; null skips this filter |
| `ChannelContext` | Null matches only threads with null `ChannelContext`; non-null matches exactly |

Cross-channel discovery is therefore natural for channels sharing an identity shape:

- **Local single-user channels** (a terminal, an editor protocol) use `UserId = "local"` and null `ChannelContext`, and see each other's threads.
- **Social channels** use per-user identities for private chats and group identities for group chats (`UserId = "group:{id}"` or `"chat:{chatId}"` with matching `ChannelContext`). Individual senders are recorded per Turn. Local and social threads are mutually invisible unless an origin is opted in.

#### Opt-in cross-context discovery (`crossChannelOrigins`)

When `crossChannelOrigins` is null or empty, discovery is the default. When non-empty, the result is the union of threads matching the default predicate and threads whose `OriginChannel` is in the list (case-insensitive), ignoring `ChannelContext` and `UserId` — so origins with per-run synthetic user ids (for example `automation:{runId}`) appear when opted in. The union is de-duplicated by id and ordered by `LastActiveAt` descending. This lets a client with a non-null context (such as `workspace:{path}`) list threads from origins with a different context when the user allows them.

#### Workspace-scoped discovery

Trusted workspace-owner clients may request workspace scope: every Thread in the store, with no `UserId`, `ChannelContext`, or `OriginChannel` filter; `crossChannelOrigins` is ignored. Callers that omit the scope keep identity rules. Archived, sub-agent, internal-thread, query, channel-name, and pagination filters remain independent. Workspace scope makes every non-internal thread in the workspace discoverable, including automation, App Binding, and unknown external origins.

#### Resume flow

The resume steps are in Section 5.5.

### 9.6 Persistence Failure Handling

- **Save failure**: the writer keeps the unconfirmed suffix and retries. If durability cannot be established, the Turn ends with a stable persistence error; Session Core never reports a durable terminal Turn or silently discards model history.
- **Load failure**: malformed history records are skipped with warnings. An unreadable file or an unsafe canonical header or source schema returns an error; the adapter informs the user and may offer a new Thread.
- **Discovery failure**: an unreadable thread file is skipped with diagnostics; one corrupt file never hides other threads.

### 9.7 Thread Rollback

`RollbackThread(threadId, numTurns)` removes `numTurns` (at least 1) Turns from the end of a non-archived Thread with no active Turn.

- Rollback appends a canonical rollback record and updates thread metadata. It does not revert files or other tool side effects.
- Model-visible history is rebuilt through the newest compaction checkpoint whose covered Turn survives; provider anchors and token trackers are invalidated and a replacement-history estimate is saved (from the generation-scoped estimator while a provider-native replacement is active). Pre-turn and turn-end checkpoints cover only terminal Turns, so editing and resending the newest Turn keeps its compacted history; a mid-turn or reactive checkpoint covering a removed Turn is discarded. Rollback never restores history that compaction had removed.
- Rollback resets the Thread's runtime state. It is not a Turn completion and produces no Turn-completion notification.
- Removed Turn sequence numbers are not reused: replay scans every valid `turn_started`, including rolled-back ones, before allocating a new id.
- Successful rollback also records a maintenance trace event; Dashboard de-duplicates it with the rollout-derived operation.

### 9.8 Loaded Thread Runtime

A loaded Thread keeps a bounded working set, independent of how long its history is:

- the Thread header, queued inputs, the persisted baseline, and counters for Turn count and the Turn sequence high watermark;
- the active Turn, if any, and the most recent terminal Turn, which answer queue pause, plan confirmation, and newest-terminal-Turn questions;
- after the first execution, the model history and world-state baseline, with provider-history records held by the thread store (Section 9.3).

Loading reads the projected header, counters, and the most recent Turns; it never replays the complete rollout. An operation that needs older Turns or Items reads them from the projection when it runs: fork copies the forked prefix, import compares against projected Turns, and actions on an older Item (such as opening a View or recording a late background result) address that Item through the projection and append their records without loading its Turn. History-wide aggregates (token totals, tool-call counts) are maintained by the projection and read from it.

## 10. Approval Flow Integration

### 10.1 Principle

Approvals are part of the Turn model, not an out-of-band concern of individual channels.

### 10.2 Normative Behavior

When a tool execution requires approval, Session Core emits an approval request tied to the active Turn, pauses that execution path until an adapter resolves it or the Turn ends (approvals have no timeout), records the outcome in Turn history, and resumes or rejects the operation.

When a Turn is cancelled, completes, or fails with approval or user-input requests pending, Session Core resolves each before the Turn's terminal event: an approval as `CancelTurn`, so its operation does not run, and a user-input request with an empty response. Each resolution creates its response Item and emits the resolved event. A later resolution of the same request is ignored, and a request raised after its Turn ended is rejected without prompting.

The adapter only presents the request and returns the decision. Approval UX is channel-specific, and non-interactive server-managed channels may auto-approve.

### 10.3 Model-Initiated User Input

`RequestUserInput` lets the agent ask one to three short structured questions before continuing. It is exposed only to main user threads, not SubAgents, and keeps the same schema in Agent and Plan modes.

Each `UserInputRequest` carries `isBlocking`, set by Session Core from the active mode: `true` in Plan mode, `false` in Agent mode. It is client-facing metadata, not a tool argument, and does not change the server-side wait: both modes pause until resolution or Turn end.

On invocation Session Core finalizes and persists preceding assistant and reasoning content, emits the `UserInputRequest` Item, pauses until resolution, cancellation, or unavailable-client fallback, records the `UserInputResponse`, and returns the answers to the tool.

Interactive adapters present the request natively. A client may auto-resolve a non-blocking request with an empty answer according to its own foreground and inactivity policy; blocking requests need an explicit answer or cancellation. Non-interactive or unsupported adapters resolve with an empty answer so the Turn continues. The model is instructed to treat an empty answer as no reply and continue with its best judgment, without asking again or waiting.

### 10.4 Asynchronous User Coordination

`SendUserMessageAsync` is exposed by default to top-level Agent and Plan threads (not Internal or SubAgent threads). It validates a non-empty message, persists and broadcasts an `AgentMessage` with `deliveryMode = "async"`, returns `{ "accepted": true }`, and lets the loop continue.

The `clock` namespace is exposed by default to ordinary agent threads, including native SubAgents but not Internal threads. `clock/CurrentTime` returns the current UTC time. `clock/Sleep` accepts `durationMs` from 1 ms to 12 hours and records a `Sleep` Item for the wait. It returns after the duration, Turn cancellation, or an active-Turn steer, mailbox, or task notification signal; the runtime subscribes before rechecking pending input so a signal at the boundary is not lost. The next drain then admits that input before the next sample.

When these tools are available, prompt composition describes the choice: `RequestUserInput` when a structured answer blocks further work, `SendUserMessageAsync` when independent authorized work can continue, and `clock/Sleep` after asking when no independent work remains. An asynchronous message is not repeated in the final answer. These tools never create a Goal or change Goal continuation.

## 11. Failure Model

### 11.1 Failure Classes

#### Turn-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Agent Exception** | Agent execution throws | Error Item; Turn `Failed`; `turn/failed`; partial state saved. |
| **Recoverable Stream Disconnect** | Provider stream disconnects, idles past its timeout, or ends before the request completes | `streamError` event and re-issue up to the provider retry limit (Section 6.3). Exhaustion becomes Agent Exception. |
| **Rate Limit, Quota, or Overload** | Provider reports throttling, exhausted plan or balance, or model capacity | Classified distinctly whether reported as HTTP status or in-stream error frame. Throttling is retryable and honors a server-advised delay; quota and capacity rejection are terminal. The failed Turn carries `providerError` and `httpStatus` when known. |
| **Tool Execution Error** | Dispatch returns or classifies a tool failure | The tool's result Item completes with stable failure fields; the failure text is returned to the model, which decides how to proceed. |
| **Incomplete Historical Tool Pair** | History has a tool call without an immediately following result | Request-local repair or filtering before provider submission (Section 5.3). |
| **Turn Timeout** | Turn exceeds its configured limit | Execution cancelled; Error Item; Turn `Failed`. |
| **Cancellation** | Adapter calls `CancelTurn` | Pending requests resolved (Section 10.2); Turn `Cancelled`; partial state saved. |
| **Prompt Hook Blocked** | PrePrompt hook blocks | Error Item with the reason; Turn `Failed`; no agent invocation. |

#### Thread-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Resume Failed (file missing)** | Thread file not found | Error returned to adapter. |
| **Resume Failed (unsafe header)** | Canonical header or source schema cannot be safely interpreted | Error returned; identity and authority never downgraded. |
| **Concurrent Turn** | `SubmitInput` while a Turn is active | Error returned; adapter queues or rejects. |

#### Persistence Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Save Failed** | Writer cannot confirm a terminal Turn batch | Suffix retained, later writes blocked, Turn ends with a persistence error (Section 9.3.1). |
| **Metadata Corrupt** | Thread projection unreadable or inconsistent | Filesystem read-repair; if SQLite stays unavailable, filesystem-derived summaries with a diagnostic warning. |
| **History Projection Unavailable** | Paged query cannot validate, repair, or rebuild | `ThreadHistoryUnavailable`; never a full-history response from JSONL. |
| **Disk Full** | No space for rollout or SQLite writes | Error on `CreateThread`/`SubmitInput`. |

#### Channel Disconnect Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Adapter Disconnects Mid-Turn** | Transport drops | The Turn runs to completion; events to the dead consumer are dropped; the adapter sees results after resume. |
| **Adapter Never Resolves Approval** | Channel disconnects while `WaitingApproval` | The approval stays pending; another adapter may resolve it, otherwise it resolves as `CancelTurn` when the Turn ends. |

### 11.2 Recovery Strategy

- Turn failures never corrupt Thread state; a failed Turn stays in history and the adapter may submit a new Turn.
- A persisted non-terminal Turn is terminated at the next cold load (Section 5.2).
- Canonical write failures are recoverable because the unconfirmed suffix is retained and retried.
- History projection failures recover from canonical JSONL through repair or rebuild (Section 9.4.2); a failed repair stays an explicit read failure.
- Channel disconnects are transparent to Session Core.

### 11.3 Error Reporting

Failures surface as an `Error` Item in the Turn (turn-level), an error returned to the adapter's call (thread-level and persistence), and a structured log entry with `threadId`, `turnId`, and error category.

## 12. Per-Session Agent Configuration

### 12.1 Principle

Thread configuration belongs to the thread model, not to any adapter.

### 12.2 Thread Configuration

Each thread may carry a `Configuration` object. It is thread-owned, persisted with the thread, and has the same shape across every host:

```
ThreadConfiguration
├── McpServers: McpServerConfig[]?                  // Per-thread MCP server connections
├── Mode: string                                    // "agent", "plan", ... (default "agent")
├── Extensions: string[]?                           // Active extension prefixes, e.g. ["_unity"]
├── CustomTools: string[]?                          // Additional tool names to enable
├── ProviderId: string?                             // Provider captured at thread creation
├── Model: string?                                  // Model captured at thread creation
├── Reasoning: ReasoningConfig?                     // Reasoning configuration
├── Speed: standard|fast?                           // Requested inference-speed mode
├── WorkspaceOverride: string?                      // Alternate workspace root
├── Cwd: string?                                    // Sticky working directory
├── RuntimeWorkspaceRoots: string[]?                // Sticky ordered runtime boundaries
├── ExecutionWorkspaceOverride: string?             // Execution root, typically a registered worktree
├── ToolProfile: string?                            // Named tool profile to inject
├── UseToolProfileOnly: bool                        // Use only the profile tools
├── AgentInstructions: string?                      // Extra system instructions
├── DeveloperInstructions: string?                  // Starting application's instructions, final prompt section
├── ApprovalPolicy: default|prompt|autoApprove|deny // Thread-scoped approval behavior
├── AutomationTaskDirectory: string?                // Local automation task directory
└── RequireApprovalOutsideWorkspace: bool?          // Outside-workspace file and shell boundary behavior
```

Approval fields:

- `default` uses the normal interactive approval path; `prompt` always uses it, even when the workspace default is `autoApprove`; `autoApprove` accepts approval-gated operations on the server; `deny` rejects them without prompting, the tool receives the rejection, and the Turn may continue. A persisted `interrupt` is read as `deny`.
- `RequireApprovalOutsideWorkspace`: `true` sends outside-workspace file and shell operations through approval; `false` rejects them without prompting; null uses the workspace file-tool default, which governs both file and shell operations.

When a thread is created or its configuration changes, Session Core rebuilds the effective agent and tool set; a Turn already running is never switched mid-flight, except for the tool location a worktree tool moves (§4.1.1.2). Configuration changes never create Turns, and channels may expose only the subset their UX supports.

Model preference:

- At creation, a server-managed thread captures the effective provider and its complete model preference (model, reasoning, speed). An explicit thread configuration wins; otherwise the preference comes from the workspace provider preferences. A provider without a complete preference is not a valid main-agent runtime.
- The main agent uses the captured values for every Turn, resume, and tool-planning operation; workspace changes affect only new threads.
- Native full-history SubAgents inherit the parent's complete captured preference and ignore other overrides. External CLI SubAgents do not use native preferences.
- Changing provider or preference on an existing thread atomically replaces those values and keeps the rest of the configuration.
- A server-managed thread also captures its memory scope and workspace memory switch at creation.

Context capacity is resolved per operation from the effective model's merged catalog (global and workspace catalogs override built-in entries). A positive `Compaction.MaxContextWindow` caps it; the default `-1` uses the catalog window. Running Turns keep their captured capacity; model changes affect queued and future work.

Workspace resolution:

- `WorkspacePath` is the state workspace for persistence and workspace-owned state.
- `WorkspaceOverride` is an alternate workspace root for the thread.
- `Cwd` is the sticky working directory for tool execution and relative paths, without moving state.
- `RuntimeWorkspaceRoots` is an ordered full replacement when present; null defaults to the ordinary cwd, and an empty array supplies no roots. A cwd-only update retargets the old cwd entry and keeps additional roots; duplicates are removed keeping first occurrence.
- `ExecutionWorkspaceOverride` wins for tool execution and first-party file and Git surfaces while state stays in `WorkspacePath`; it replaces the ordinary cwd root in the effective roots. Worktree handoff uses it, never `WorkspaceOverride`.
- A client worktree handoff on an existing thread is a configuration change on the same Thread: rejected while a Turn is active, and the effective agent and tool context is rebuilt before the next Turn. A move by the worktree tools updates the running Turn's tool location immediately and rebuilds the agent before the next Turn.
- Worktree handoffs, moves by the worktree tools, and worktree removal run one at a time across threads, so a worktree is bound to at most one thread.

### 12.3 Mode Switching

`SetThreadMode(threadId, mode)` changes `Configuration.Mode` and rebuilds the agent's tool set. It creates no Turn.

### 12.3.1 Mode-Stable Plan Tools

Plan tools keep the same schema across Agent/Plan switches to preserve prompt-cache stability. When plan persistence is available, `CreatePlan` and `TodoWrite` are always exposed and the mode decides which calls are valid:

| Mode | Allowed | Denied |
|------|---------|--------|
| `plan` | `CreatePlan` | `TodoWrite` |
| `agent` | `TodoWrite` | `CreatePlan` |

`CreatePlan` saves the proposed plan with its tasks as `pending`. `TodoWrite` takes the complete task list, each item a description and a status of `pending`, `in_progress`, or `completed`, and replaces the stored list while keeping the plan's title and body. Task ids are assigned by position (`todo-1`, `todo-2`, …); the model never supplies them.

Every host that exposes mode switching must provide per-thread plan persistence, and hook execution where lifecycle hooks are expected, so mode switching behaves identically across hosts; without plan persistence the plan tools are omitted, which is a host configuration error. A host may observe plan changes (each carrying `threadId` and the complete plan) to drive its own UX.

### 12.4 MCP Lifecycle

MCP connections are thread-scoped:

- **Connect**: when a thread with `McpServers` is created, or prepared for Turn execution after a cold load, Session Core connects the servers, waits for the startup attempt to settle, and adds ready tools before execution. Failed servers are reported through MCP status and do not block agent construction. Read-only operations (`GetThread`, discovery) never connect servers.
- **Disconnect**: archiving the thread or changing its MCP configuration disconnects the previous servers.
- Connections live while the thread is active. Null `McpServers` means workspace MCP configuration applies.

### 12.5 Connection-Scoped Extension Capabilities

Channel extension capabilities are connection-scoped at discovery time but may be recorded in `Configuration.Extensions` when they affect the thread's tool set. Channels without extensions leave it null.

## 13. Social Channel Patterns

### 13.1 Group Sessions (Multi-User)

Group sessions use the same Thread / Turn / Item model:

- A group thread's `UserId` is null or a group identifier (`group:12345`, `chat:chatId`), not an individual.
- Each Turn's input carries the sender fields of the `UserMessage` payload (Section 4.2).
- Sender context is appended to the current user message's runtime context, not the system prompt, so cacheable instructions stay stable across senders.
- The thread remains one execution context; sender identity is per Turn.

### 13.2 Permission and Role System

Permissions are an adapter concern. Roles (for example `Unauthorized`, `Whitelisted`, `Admin`) decide whether a user may chat, whether tools may write, whether the user may approve operations, and which slash commands are available. The adapter rejects unauthorized users before submitting input, supplies the user's role in the approval context, and filters commands by permission.

`SubmitInput` accepts an optional sender context — `SenderId`, `SenderName`, `SenderRole` (`admin` or `user`), and nullable `GroupId` — populated by the adapter. Session Core records it, appends it to the current user message's runtime context, and passes it to approval handling.

### 13.3 Slash Commands

Server-managed slash commands have one server-side registry, authoritative for discovery, permission hints, and routing. In-process adapters call it directly; out-of-process adapters reach it through the wire protocol. Adapters may provide native command UX but never fork command semantics.

| Command | Maps To |
|---------|---------|
| `/stop` | `CancelTurn` for the thread's active Turn |
| `/new` | Reset conversation: archive reusable threads and create a fresh thread |
| `/load`, `/sessions` | `FindThreads` + `ResumeThread` |
| `/help` | Managed command metadata listing |
| `/automate` | Automation management (host service) |
| `/debug` | Debug mode toggle (host service) |
| `/init` | Expands a repository-guidelines prompt into a normal agent Turn |
| Custom commands | Resolved by the command pipeline |

`/clear` is a client-local UI command, not a thread lifecycle command; client-local commands are outside the registry.

`/init` is workspace-conditional: discovery omits it when `AGENTS.override.md` or `AGENTS.md` already exists as a regular file at the thread's `WorkspacePath`. Its prompt asks the agent to inspect the workspace and create `AGENTS.md` with concise English project instructions, checking both filenames and never modifying an existing file. It runs as an ordinary Turn, so normal tools, approvals, and remote routing apply.

### 13.4 Active Run Cancellation

Session Core owns cancellation of the active run. `CancelTurn(turnId)` cancels the Turn's execution and transitions it to `Cancelled`. Foreground tool resource owners finish cancellation cleanup before the terminal Turn event. Explicitly detached background terminals are not owned by the Turn and are stopped through terminal control operations.

### 13.5 Turn Interruption History

With `AgentInterruptMessageEnabled` (default `true`), explicit Turn cancellation, including an approval `CancelTurn` decision, appends one model-visible `turn_aborted` context item after the interrupted Turn's partial history. Caller cancellation, thread deletion, recovery, and failures do not imply intentional interruption. The terminal Turn and its history are persisted before cancellation notifications are published.

- A regular fork that retains an active source Turn appends the same boundary only to the child. Completed-turn item cuts and forks excluding the active Turn add no marker.
- Existing markers survive fork and resume; ephemeral threads keep them in memory. Disabling the setting suppresses new markers without removing existing ones.
- Responses records the marker as provider-owned local input as well as neutral history, without replacing native compacted history. On cancellation, Responses keeps completed provider output and streamed assistant text whose output item had not completed before appending the boundary. Retried failed attempts are still discarded.
- Ephemeral history keeps the cancelled Turn's input even when cancellation precedes session initialization or no marker is generated.

## 14. Bidirectional Capabilities

The session model covers conversation state and Turn execution only. Transport-specific request/response features — IDE filesystem access, terminal control, extension calls, REST flows — are tool or channel concerns. Background terminals follow the same boundary: Session Core records the observable `CommandExecution` Item for the originating call, while the wire protocol exposes live terminal snapshots and output to terminal-capable clients. The model-facing shell surface stays minimal (`Exec`, `WriteStdin` where empty stdin polls, and `TaskStop`); terminal listing, direct reads, client stops, and cleanup are control-plane capabilities.

## 15. Thread Goals

Session Core owns persistent thread goals, their runtime accounting, and autonomous continuation. A thread has at most one current goal. Clients and adapters translate goal controls into Session Core operations rather than keeping their own goal state.

Goal continuation Turns are ordinary persisted Turns with system provenance. Session Core starts one only when an active goal exists, automatic continuation is enabled, the thread is idle and in a goal-compatible mode, and no user, approval, or plan-confirmation work is pending. The continuation input is model steering generated by Session Core, not a user message.

When a user interrupts a Turn pursuing an active goal, Session Core accounts the progress made and sets the goal to `paused`. Other cancellation may account progress but implies a pause only when its source represents an interrupt.

Goal objective text is user data: whenever it enters model-visible context it is escaped and marked as untrusted task context, not instructions.

## 16. Wire Protocol (Cross-Language SDK Support)

Out-of-process clients use a protocol projection of the Session API. The projection owns wire DTOs, connection capabilities, transport errors, and notification delivery; it must not introduce another Thread lifecycle or persist independent conversation state.
