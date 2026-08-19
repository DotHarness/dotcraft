# DotCraft Session Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.7.2 |
| **Status** | Living |
| **Date** | 2026-08-12 |

Purpose: Define the current **server-managed** session model (Thread / Turn / Item) used by `DotCraft.Core`, including lifecycle, persistence, event semantics, approval semantics, and adapter boundaries.

## 1. Scope

This specification defines the **internal domain model and execution engine** for channels whose conversation state is owned by the server and executed through `ISessionService`.

For the external JSON-RPC API that projects these primitives to out-of-process clients, see the [DotCraft AppServer Protocol Specification](../protocols/appserver-protocol.md).

| Document | Defines |
|----------|---------|
| `session-core.md` | Domain model, lifecycle rules, event semantics, persistence layout, approval semantics, and adapter contracts inside `DotCraft.Core`. |
| `subagents.md` | Authoritative SubAgent runtime, fork, model resolution, policy, communication, and child lifecycle contract. |
| `appserver-protocol.md` | JSON-RPC methods, notifications, transport rules, wire DTOs, error codes, and approval mechanics for out-of-process clients. |

### 1.1 In-Scope Channels

The Session Protocol is the active execution model for:

- CLI
- ACP
- QQ
- WeCom

These channels create and resume server-managed threads whose canonical domain and model-visible history lives under `.craft/threads/active|archived/`, while queryable metadata lives in `.craft/state.db`. They submit turns through Session Core and consume `SessionEvent` streams through thin adapters.

### 1.2 Design Intent

The purpose of the Session Protocol is to unify the **server-managed** channels behind one core model:

- shared Thread / Turn / Item primitives
- shared execution path through `ISessionService`
- shared event semantics for adapters
- shared persistence and resume behavior where server-owned history exists

This boundary is intentional. DotCraft does **not** attempt to force client-owned channels into the same persistence model when that would conflict with their native architecture.

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Unified session primitives**: Define Thread, Turn, and Item as the shared model for all server-managed channels.
2. **Single server-side execution path**: In-scope channels invoke the agent through Session Core rather than channel-specific streaming loops.
3. **Unified event stream**: Session Core emits a structured event stream (item started, delta, completed) that server-managed channel adapters translate into transport-specific output.
4. **Cross-channel resume for compatible server-managed identities**: Threads can be discovered and resumed across channels that share the same identity shape.
5. **Thin adapters**: Channel-specific code should primarily translate transport messages and approvals, not own session orchestration logic.
6. **Approval flow unification**: HITL approval requests are modeled as Items within a Turn, with a defined lifecycle that in-scope adapters implement.

### 2.2 Non-Goals

- **Changing LLM/tool execution internals**: The Microsoft.Extensions.AI pipeline (`FunctionInvokingChatClient`, `TracingChatClient`, etc.) remains as-is. Session Core wraps it; it does not replace it.
- **Prescribing channel-specific UX**: How a QQ bot renders a diff versus how ACP renders it is an adapter concern. The protocol defines *what* happened, not *how* to display it.
- **Real-time cross-device sync**: Session Core does not push notifications to idle channels when a thread updates elsewhere. Channels discover thread state on resume.
- **Multi-user thread collaboration**: Collaborative editing of a thread (multiple users editing simultaneously) is not in scope. Sequential group input is supported as described in Section 17.
- **Standards-body compatibility**: This spec defines DotCraft's internal session model. It does not attempt to be an external public standard.
- **Channel integration**: Session Core is a layer *inside* channel implementations, not a replacement of the module system. Compiled modules use `IDotCraftModule` for registration and implement the capability-specific `IChannelServiceModule` and `ISessionChannelModule` facets when they provide managed channel behavior or session origins. Host composition remains outside Session Core.

## 3. System Overview

### 3.1 Main Components

1. **Session Core** (`DotCraft.Sessions`)
   - Owns Thread/Turn/Item lifecycle and state machines.
   - Wraps the agent execution pipeline (`AgentFactory` + `RunStreamingAsync`).
   - Emits a structured event stream consumed by adapters.
   - Persists canonical domain and model-visible history to JSONL under `.craft/threads/active|archived/`; SQLite contains explicitly classified durable business state, runtime continuity state, diagnostics, and rebuildable projections.
   - Enforces per-thread mutual exclusion.

2. **Channel adapters** (per in-scope channel, in module assemblies)
   - Translate between the channel's transport (stdio, HTTP, WebSocket, bot API) and Session Core API calls.
   - Subscribe to Session Core events and render them in channel-specific format.
   - Handle channel-specific concerns: authentication, message formatting, rate limiting.
   - Implement approval routing by translating `ApprovalRequest` Items into channel UX.

3. **Persistence Layer** (`DotCraft.Sessions`)
   - Appends domain transitions and model-history records to `.craft/threads/{active|archived}/{threadId}.jsonl`.
   - Replays JSONL to reconstruct both `SessionThread` and model-visible history.
   - Stores queryable projections and explicitly classified non-rollout state in SQLite.

4. **Event stream** (in-process, `DotCraft.Sessions`)
   - Delivers Session Core events to the active channel adapter.
   - Per-thread event stream: each Thread has one active consumer.
   - Delivery is decoupled from channel rendering.

### 3.2 Abstraction Layers

The server-managed session protocol is organized into five layers, ordered from closest to the user to closest to the model:

1. **Transport Layer** (per channel)
   - The raw communication mechanism: stdio JSON-RPC (ACP), WebSocket (QQ), HTTPS webhook (WeCom), in-process (CLI).
   - Each channel keeps its existing transport.

2. **Adapter Layer** (per channel)
   - Translates transport messages into Session Core calls: `CreateThread`, `ResumeThread`, `SubmitInput`, `ResolveApproval`.
   - Translates Session Core events into transport messages: text chunks, tool call notifications, approval prompts.
   - This is the only new layer that in-scope channel modules need to implement. It replaces channel-specific session orchestration logic.

3. **Session Core Layer** (`DotCraft.Core`, new)
   - Manages Thread/Turn/Item state machines.
   - Orchestrates a Turn: creates Items, invokes the agent, emits events, handles approval pauses.
   - Calls into the Agent Execution Layer and Persistence Layer.
   - This is the "one harness" shared by all server-managed channels.

4. **Agent Execution Layer** (`DotCraft.Core`, existing)
   - `AgentFactory.CreateAgentWithTools` — tool aggregation, pipeline construction.
   - `agent.RunStreamingAsync` — the Microsoft.Extensions.AI agent loop.
   - `FunctionInvokingChatClient` — tool call orchestration.
   - `TracingChatClient`, `DynamicToolInjectionChatClient` — pipeline middleware.
   - Unchanged by this spec. Session Core consumes its output.

5. **Persistence Layer** (`DotCraft.Core`)
   - Thread JSONL storage in `.craft/threads/active|archived/` plus metadata projections in `.craft/state.db`
   - SQLite-backed thread discovery
   - Rollout-backed model-history reconstruction on resume

### 3.3 Layer Diagram

```
Server-managed channels

  ACP      CLI      QQ      WeCom
   │        │        │         │
   └────────┴────────┴─────────┘
                         │
                    Adapter Layer
                         │
                    Session Core
       (Thread lifecycle, Turn orchestration, events)
                         │
                 Agent Execution Layer
                         │
                  Persistence Layer
(`.craft/threads/**/*.jsonl`, `.craft/state.db`)


```

### 3.4 Relationship to Existing Code

| Existing Component | Session Protocol Relationship |
|--------------------|-------------------------------|
| `AgentRunner` | Session Core subsumes its responsibilities. `AgentRunner.RunAsync` logic (session load, hook execution, streaming, save, compaction, consolidation) is now implemented on top of Session Core behavior. |
| `AgentFactory` | Builds immutable `ChatClientAgent` instances and their MEAI `IChatClient` pipelines. It does not create or own a second session model. |
| `SessionStore` | Removed. Its responsibilities are now split between thread/session file persistence and `ISessionService`. |
| `SessionGate` | Becomes an internal implementation detail of Session Core. Channels no longer call `AcquireAsync` directly. |
| `IApprovalService` | Remains the approval interface. Session Core delegates approval requests to the channel adapter, while the request and response are modeled as Items with explicit lifecycle. |
| `IChannelService` | Unchanged. AppServer uses `IChannelService` to manage in-process and external channel lifecycles. The adapter is an internal component of the channel's `IChannelService` implementation. |
| `HookRunner` | Session Core invokes hooks (PrePrompt, Stop, PreToolUse, PostToolUse) at the appropriate points in the Turn lifecycle. Channels no longer invoke hooks directly. |
| `TraceCollector` | Session Core records trace events. Channels no longer interact with `TraceCollector` directly. |

### 3.5 External Dependencies

- **Microsoft.Extensions.AI**: `IChatClient`, `AITool`, `FunctionInvokingChatClient` — the agent execution pipeline.
- **Existing DotCraft.Core**: `AppConfig`, `SkillsLoader`, `MemoryStore`, `ToolProviderCollector` — workspace infrastructure.
- **Channel transports**: Each channel's transport library (NapCat for QQ, ASP.NET for WeCom, custom stdio for ACP).

### 3.6 Runtime State Ownership

Session Core is the single authority for live state associated with threads and turns. This includes active execution, event delivery, approval and user-input pauses, maintenance work, background continuation, and transient state used to resume, cancel, or report work.

Each thread's live runtime state must have one ownership boundary. Related state must not be duplicated across independent owners that can drift apart or be cleaned up separately. Channel adapters, protocol projections, and background workers may request operations or observe events, but they must not maintain competing mutable thread runtime state.

Thread deletion is both a persistence operation and a runtime teardown. Deleting a thread must stop or invalidate outstanding work for that thread and clear all live state owned by Session Core. Background completion, cleanup, retry, or notification paths must not recreate runtime state for a thread that is being permanently deleted.

Any concurrent flow that touches thread runtime state must explicitly distinguish between reading existing state and creating or restoring state as part of a lifecycle operation. Incidental event delivery, cleanup, and delayed background continuations must use the existing-state path so they cannot resurrect a deleted or archived execution context.

### 3.6.1 Runtime Command Ownership

Every loaded Thread has one runtime command owner. External mutations of live Thread state -- Turn
admission and cancellation, approval and user-input resolution, queued-input changes, effective
configuration publication, maintenance admission, and lifecycle teardown -- are serialized by that
owner. Long-running model or maintenance work may execute outside the command dispatcher, but it
must return completion to the same owner before the active-work slot is cleared or follow-up work is
admitted.

The command dispatcher must remain responsive while a Turn is running so cancellation, approval,
user-input answers, steering, and configuration changes do not wait for model completion. A Thread
configuration change committed during an active Turn applies to the next Turn. It must not mutate
the active Turn's captured execution context.

Each runtime activation has a generation identity. Delayed completion from an earlier generation,
including completion after permanent deletion or reload, must not mutate, persist, publish, or
recreate live Thread state.

### 3.6.2 Turn Execution Ownership

Turn admission captures an immutable execution context containing the effective configuration,
Agent, workspace and runtime roots, client capabilities, and request origin used by that Turn. The
effective tool snapshot is frozen alongside it in mutable Turn runtime state; provider-history
identity is derived from the admitted Thread and Turn before the first provider request.
Caller-owned input collections, input snapshots, and client-managed history are copied at
admission. Runtime adapters may expose this context through ambient compatibility scopes, but
those scopes are not a second authority.

Mutable Turn state is owned separately from the immutable execution context. It contains
cancellation, pending approval and user-input waiters, tool projections, usage accounting, item
sequence allocation, and same-Turn steering. Turn execution is represented as a runtime task. The
Session Core task coordinator owns task start and generation-safe cleanup. The Thread command owner
requests cancellation through the mutable Turn state, while the task applies the existing terminal
success, cancellation, and failure policy. The Agent pipeline continues to own model sampling and
tool invocation.

Terminal persistence is owned by one Turn committer, which atomically commits terminal Turn state,
the model-history suffix, and any compaction checkpoint through the existing rollout contract. Live
Turn-state cleanup returns through the Thread command owner, and delayed cleanup is rejected when
its runtime generation is no longer current. Existing Session event ordering remains the protocol
contract; this ownership split does not introduce new events or reorder AppServer notifications.
Durable queued input continues to have priority over autonomous Goal continuation.

## 4. Core Domain Model

### 4.1 Entities

#### 4.1.1 Thread

A Thread is a persistent conversation between one user and one agent, tied to a workspace.

Fields:

- `Id` (string)
  - Globally unique identifier. Format: `thread_{timestamp}_{random}` (e.g., `thread_20260315_a3f2k9`).
  - Assigned by Session Core on creation. Immutable after creation.
- `WorkspacePath` (string)
  - Absolute path to the workspace this Thread belongs to.
- `UserId` (string, nullable)
  - Opaque user identifier from the originating channel. Used for thread discovery ("show me my threads").
  - Null for system-initiated threads (Cron, Heartbeat).
- `OriginChannel` (string)
  - Name of the channel that created this Thread (e.g., `"qq"`, `"acp"`, `"cli"`).
  - Informational only; does not restrict which channels can resume the Thread.
- `DisplayName` (string, nullable)
  - Human-readable label. Defaults to the first user message text (truncated). Can be set explicitly.
  - Forked threads use an explicit fork `displayName` when supplied. Otherwise they use the source thread's visible `DisplayName`, falling back to the first retained user message when the source has no display name.
- `Source` (ThreadSource)
  - Describes why this thread exists. `kind: "user"` is the default top-level conversation.
  - `kind: "subagent"` marks a child thread spawned from another thread turn and carries parent thread, parent turn, root thread, depth, nickname, role, profile, and runtime metadata.
  - The complete source and every subagent capability are part of the canonical thread baseline and must survive cold-start replay.
- `ForkedFromId` (string, nullable)
  - Source thread id when this thread was created by forking another conversation. Null for ordinary starts.
- `Ephemeral` (boolean)
  - True when the thread is process-local and is not persisted or listed by default.
- `Worktree` (ThreadWorktreeInfo, nullable)
  - DotCraft-managed Git worktree metadata bound to this thread. Null for ordinary local-workspace threads.
- `Status` (enum: `Active`, `Paused`, `Archived`)
  - See Section 5.1 for lifecycle rules.
- `CreatedAt` (UTC timestamp)
- `LastActiveAt` (UTC timestamp)
  - Updated when a Turn starts or completes.
- `Metadata` (dictionary, string → string)
  - Extensible key-value pairs for channel-specific data (e.g., QQ group ID, ACP workspace URI).
  - Session Core preserves but does not interpret Metadata.
- `Configuration` (ThreadConfiguration, nullable)
  - Per-thread agent configuration (MCP servers, mode, extensions). See Section 16. Null means workspace defaults apply.
- `Turns` (ordered list of Turn)
  - Canonical visible Turn history. Normal execution appends Turns; rollback removes a visible tail while the rollout retains the rollback and prior Turn records for audit and identity recovery.
- `QueuedInputs` (ordered list of QueuedTurnInput)
  - FIFO inputs submitted while a Turn or blocking thread maintenance operation is active. The queue is part of canonical thread state and is persisted in the rollout file.
  - Clients may explicitly reorder the full queue. Reordering preserves queued input payloads and statuses, but updates the execution order for future queued turns.
  - When a running Turn completes successfully and no blocking thread maintenance is active, Session Core dequeues at most one queued input and starts it as the next Turn. Failed or cancelled Turns do not automatically consume queued inputs.
  - When blocking thread maintenance completes, skips, fails, or is cancelled, Session Core dequeues at most one queued input and starts it as the next Turn if the thread is otherwise idle.

#### 4.1.1.1 Thread Forks

A thread fork creates a new conversation branch from a source thread while leaving the source unchanged.

Fork contract:

- A persistent fork receives a new thread id, persists its own rollout, and sets `ForkedFromId` to the source thread id.
- An ephemeral fork receives a new thread id and `ForkedFromId`, but stays process-local and is omitted from default thread lists.
- Fork history is copied, not linked. Future edits, archive/delete actions, rollback, or compaction on the source must not rewrite the fork.
- The fork copies the selected source turn/item prefix. A `forkPoint` may target a whole turn or an item boundary, with `"after"` including the target and `"before"` excluding it.
- A copied active or partial source turn must end at an explicit interrupted boundary so clients and model-history reconstruction do not treat partial output as a naturally completed response.
- Forks do not inherit queued inputs, pending approvals, active user-input requests, app bindings, active goals, or durable plan state. Transcript items already copied remain visible as history.
- Forks copy thread configuration, then apply request-level overrides.
- Forked threads include a persisted `SystemNotice` item with `kind = "forked"` and `sourceThreadId` at the boundary between inherited source history and fork-specific work. This marker is client-visible but not model-visible.
- A persistent fork must materialize its own optimized model-visible history when source history has already been compacted. If the selected fork prefix fully retains a source compaction checkpoint's covered turn, Session Core rebases that checkpoint into the fork rollout, appends the retained later model-history messages, and records an estimated context usage snapshot. If the fork point cuts inside or before the checkpoint-covered turn, that checkpoint is not compatible and an older compatible checkpoint may be used instead.
- Fork rebuilds must prefer fork-local compaction checkpoints and must not expand compacted source history back into the full pre-compaction transcript when a compatible checkpoint exists.

#### 4.1.1.2 Worktree Handoff

A worktree handoff composes Git worktree creation with thread fork.

Ownership rules:

- `WorkspacePath` remains the state workspace. It owns `.craft/threads`, `.craft/state.db`, memory, goals, plans, app bindings, skills, plugins, and thread metadata.
- The worktree path is the execution workspace for the forked thread. File tools, shell tools, Git operations, LSP, and first-party file surfaces use that execution root.
- `ordinaryCwd = Cwd ?? WorkspaceOverride ?? WorkspacePath`.
- `effectiveWorkspacePath = ExecutionWorkspaceOverride ?? ordinaryCwd`.
- `ExecutionWorkspaceOverride` changes runtime execution location only. It must not relocate rollout, memory, goals, plans, app bindings, or workspace configuration.
- `RuntimeWorkspaceRoots` is the ordered, sticky set of additional runtime boundaries. See [Multi-Folder Local Projects](../features/multi-folder-projects.md).
- Registered worktree roots under `.craft/worktrees` are allowed execution roots for their bound threads even though ordinary main-workspace browsing hides `.craft/worktrees/**`.
- Worktree handoff must not mutate the source thread or the source working tree. Dirty change handoff copies uncommitted source changes into the new worktree when requested.

#### 4.1.1.3 QueuedTurnInput

A QueuedTurnInput is a durable snapshot of user input waiting to become a future Turn.

Fields:

- `Id` (string)
  - Globally unique queued-input identifier.
- `ThreadId` (string)
  - Parent Thread ID.
- `NativeInputParts` (ordered list)
  - Transport-native input parts, such as text, file references, skill references, command references, or local image references.
- `MaterializedInputParts` (ordered list)
  - Model-visible input parts after materialization. This snapshot is used when the queued input is executed.
- `DisplayText` (string)
  - Human-readable queue label derived from the native snapshot.
- `Sender` (SenderContext, nullable)
  - Optional sender identity for group sessions.
- `Status` (string)
  - `"queued"` for normal FIFO execution or `"guidancePending"` after the user promotes the input into current-Turn guidance.
- `CreatedAt` (UTC timestamp)
- `ReadyAfterTurnId` (string, nullable)
  - Active Turn ID observed when the input was queued.
- `TriggerKind` (string, nullable)
  - Present when the queued input was synthesized by a server/app mechanism rather than typed by a human. Examples include `goal`, `heartbeat`, `cron`, `automation`, `app`, or `team`.
- `TriggerLabel` (string, nullable)
  - Optional human-readable source label.
- `TriggerRefId` (string, nullable)
  - Optional stable source id for client-side click-through or audit correlation.
- `DeliveryBindingId` (string, nullable)
  - Optional App Binding id that owns the default output delivery target for the future Turn.

When a queued input starts a future Turn, Session Core must copy trigger metadata, the queued input id, and any default delivery binding id into the persisted `UserMessagePayload`. When a queued input is promoted into current-turn guidance, the guidance `UserMessage` item must preserve the same trigger metadata. Before guidance is admitted into the active Turn, a client may change the queued input's desired status back to `"queued"` without changing its input payload, metadata, or queue position. Once admission atomically persists the guidance `UserMessage` and removes the queued input, the input is no longer retractable.

Queued-input materialization is a local-only operation. Inline `image` parts contain base64 `data:image/...` URLs and are decoded without network access; `localImage` parts are read from their persisted local paths. Session Core must never dereference HTTP or HTTPS image URLs while starting a queued Turn or admitting current-Turn guidance. A legacy queued snapshot containing such a URL is materialized as the text `image content omitted because remote image URLs are not supported`, while the remaining parts continue normally.

#### 4.1.1.4 SubAgent Child Threads

The [SubAgent Core specification](../features/subagents.md) is authoritative for runtime selection,
fork modes, model fallback, role and profile precedence, communication, and child lifecycle. This
section records how those children participate in the broader Session Core domain.

Profile-backed SubAgents are represented as ordinary `SessionThread` instances with `Source.kind = "subagent"` and `OriginChannel = "subagent"`. Native profiles use the same turn, item, approval, persistence, and resume path as main agent threads. External CLI profiles persist synthetic turns containing the submitted prompt, final output or error, and token metadata when available.

SubAgent child threads use normal session tool construction with a role-resolved invocation policy. Their model-visible tool schema stays aligned with the parent; role restrictions are enforced when tools execute. `agentRole` is a role selector, not display metadata. The built-in `default` role denies DotCraft SubAgent control tools, `explorer` permits a read-only exploration subset including read-only shell observation, and `worker` may invoke write/shell/web tools plus Agent control when the depth policy allows it. Workspace configuration may override or add roles.

A role constrains shell tools along two independent axes that both must pass. The tool allow/deny lists decide whether a shell tool is reachable by name. The role's shell access level then decides what a reachable shell tool may run: `none` rejects shell tools outright, `readOnly` admits only commands classified as non-mutating and rejects standard-input writes, and `full` adds no further restriction. Roles that omit a shell access level default to `full`, so an existing role that restricts shell through its allow-list keeps that boundary. Read-only classification is a property of the command, not of the operational mode: a `readOnly` role must be able to observe repository state through commands such as `git diff`, and the denial reason must state which command was rejected.

A native full-history fork (`forkTurns=all`) materializes the parent's effective model context before the child's first sampling. It copies the parent's ordered system, developer, and user items verbatim, drops assistant, reasoning, and tool traffic, then appends the child role guidance and initial task. It also preserves the parent's stable reference-context pages and snapshots inheritable client-owned tool bindings from the direct parent. Fresh (`none`) and bounded forks rebuild context and inherit neither. A forked snapshot is not a live link: later parent changes do not reach the child.

Native children carry role instructions as a thread context item on every protocol, positioned after inherited history and before the initial task, as specified in [Prompt Composition](prompt-composition.md). Updating or clearing them creates an explicit history replacement boundary before the next sampling request. External runtimes receive role instructions through their runtime prompt.

Each path-addressable SubAgent has a stable `agentPath`, such as `/root/researcher`. The root agent path is `/root`. Child path segments are `taskName` values and must contain only lowercase ASCII letters, digits, or underscores. The segment values `root`, `.`, and `..` are reserved. Relative targets append valid path segments to the current agent path; absolute targets must begin with `/root`. Sibling SubAgents under the same parent must not share a `taskName`.

`agentPath` is the model-visible control identity and is immutable for the child relationship. `agentNickname` is optional display metadata provided at spawn time. `Thread.DisplayName` is initialized from `agentNickname` when present, otherwise from `taskName`; later thread rename operations may change `Thread.DisplayName` but must not change `agentPath` or `taskName`.

`SubAgent.MaxDepth` defaults to `1`. The first child spawned by a root thread has depth `1`; by default, that child cannot call `SpawnAgent` again even when its role would otherwise allow Agent control. Raising `SubAgent.MaxDepth` is the advanced opt-in for recursive SubAgent orchestration.

Session Core persists a `ThreadSpawnEdge` graph row for each parent/child relationship: `parentThreadId`, `childThreadId`, `parentTurnId`, `depth`, `agentPath`, `taskName`, `agentNickname`, `agentRole`, `profileName`, `runtimeType`, `supportsSendMessage`, `supportsFollowupTask`, `supportsClose`, `status` (`open` or `closed`), `createdAt`, and `updatedAt`.

Session Core represents SubAgent communication with one internal envelope containing `id`, `rootThreadId`, `authorAgentPath`, `recipientAgentPath`, `messageType`, `payload`, `parentTurnId`, and `createdAt`. `messageType` is one of `MESSAGE`, `NEW_TASK`, or `FINAL_ANSWER`. Its model-visible rendering is `Message Type`, recipient task path, sender path, and payload. The rendering remains user-role materialized input; it does not create a new public Item type or change Desktop projection.

Durable inter-agent mailbox entries preserve the envelope's message type and parent-Turn provenance alongside delivery `status` and `deliveredAt`. Schema initialization adds missing columns to existing mailbox tables; pre-existing rows receive `MESSAGE` with no parent-Turn provenance. Message types outside the three defined values are rejected. `SendMessage(target, message)` creates a pending `MESSAGE` entry and does not start a target turn. `FollowupTask(target, message, deliveryMode?)` renders a `NEW_TASK` envelope and starts a target turn when the target is idle. When the target has an active turn, `deliveryMode = "queue"` (the default) appends a FIFO queued input for the target thread, while `deliveryMode = "steer"` promotes the task into current-Turn guidance for a running native SubAgent. Running external SubAgents reject `deliveryMode = "steer"`; callers must use `"queue"`. Pending passive communications for the target are delivered as pre-task context with the submitted, queued, or steered task, then marked delivered only after that delivery is persisted successfully.

When a path-addressable child turn reaches a terminal state, Session Core writes a `FINAL_ANSWER` communication to the direct parent agent path mailbox. The communication includes the child `agentPath`, terminal status, final assistant text or error text when available, and the terminal child Turn as provenance. `WaitAgent(timeoutMs?)` waits for mailbox, SubAgent graph, or explicit steer activity scoped to the current root Agent tree; activity in another root tree cannot wake it. The result remains status plus timeout state and does not return child final text. `timeoutMs` is measured in milliseconds; omitting it uses `SubAgent.DefaultWaitTimeoutMs` (default `60000`). When supplied, it must be between `SubAgent.MinWaitTimeoutMs` (default `15000`) and `SubAgent.MaxWaitTimeoutMs` (default `3600000`); out-of-range values are rejected rather than clamped.

Mailbox delivery is serialized per `(rootThreadId, targetAgentPath)`, so sampling and follow-up delivery cannot materialize the same pending entry concurrently. Pending communications may be injected at model sampling or tool boundaries while the current Turn accepts mailbox input. Once the Turn emits its final answer, late passive communications remain pending for the next Turn. Explicit `guidancePending` steering reopens current-Turn mailbox delivery; goal-internal steering does not become a WaitAgent activity source. Completion communications remain model-visible context and audit records; clients should not render them as user-authored conversation bubbles or as the child agent's visible reply in the parent thread.

Open SubAgent identity, path, stored role/configuration, pending communication type, and parent-Turn provenance survive a cold workspace runtime restart. Resuming a root does not reopen closed children. Sending a follow-up to a persisted open child addresses that original child path and uses its stored configuration. Session Core does not promise exactly-once delivery across process failure and does not add mailbox claim/lease state.

`ListAgents(pathPrefix?)` reports open path-addressable agents plus `/root`. Its `status` value reflects the execution lifecycle of the latest relevant turn: active turns report `running`, `waitingapproval`, or `waitinginput`; terminal turns report `completed`, `failed`, or `cancelled`; agents without turns report `idle`; closed edges report `closed`.

Top-level thread discovery hides subagent threads by default. Callers that need a raw mixed list must request `includeSubAgents`; active lists still hide children whose parent is archived. Clients that render a background-agent widget should prefer the open edge list for the active parent thread, so explicitly closed child agents do not appear as active background activity.

SubAgent child thread lifecycle is owned by the parent thread. Archiving or permanently deleting a parent recursively applies to all descendant child threads. Restoring a parent restores only descendants whose parent/child edge is still open; children explicitly closed through `CloseAgent` remain archived. Direct archive/delete calls against a child thread are invalid; clients should close children through the SubAgent control APIs or manage the parent thread. `CloseAgent` marks the child edge closed, cancels any active child turn still owned by the server, and archives the target child subtree.

#### 4.1.1.5 Provider Context Windows

Session Core maintains an internal provider context-window record for each persistent thread. This is not part of the public Thread wire model; it exists so provider integrations such as ChatGPT OAuth Responses can describe the current model-visible history lineage using provider-compatible metadata.

The persisted record is keyed by `thread_id` and stores `first_window_id`, `previous_window_id`, `current_window_id`, `generation`, and `updated_at`. IDs are UUID strings generated by the runtime. A record is created when the first provider request on a thread needs it. `current_window_id` remains stable across normal turns, retries, tool loops, queued inputs, and resume from disk.

When a compaction succeeds and replaces neutral or provider-native model-visible history (`micro` or `partial` outcomes from auto, reactive, or manual compaction), Session Core advances the provider context window from the durable replacement boundary. A provider-native rollout replacement carries the exact next window identity and is authoritative if the derived `thread_context_windows` projection is stale. The previous `current_window_id` becomes `previous_window_id`, the committed replacement supplies a new `current_window_id`, and `generation` increments. Skipped, failed, cancelled, or diagnostic-only compaction attempts must not advance the window. See [Context Compaction](context-compaction.md#replacement-installation) for commit and recovery ordering.

The window record is internal routing metadata. It must not be rendered as conversation content,
included in AppServer Thread DTOs, or used as the provider prompt-cache key. For Responses,
`prompt_cache_key` and OAuth `session-id` use the root cache-session Thread id, while OAuth
`thread-id` and `x-client-request-id` use the current executing Thread id. Root Threads and
ordinary user forks are their own cache-session roots; subagents retain the persisted
`Source.SubAgent.RootThreadId`.

#### 4.1.2 Turn

A Turn is one unit of agent work initiated by user input. A Turn starts when the user submits a message and ends when the agent finishes responding (or fails, or is cancelled).

Fields:

- `Id` (string)
  - Unique within the Thread. Format: `turn_{sequence}` (e.g., `turn_001`).
- `ThreadId` (string)
  - Reference to the parent Thread.
- `Status` (enum: `Running`, `Completed`, `WaitingApproval`, `Failed`, `Cancelled`)
  - See Section 5.2 for lifecycle rules.
- `Input` (Item)
  - The user's input Item that initiated this Turn. Always of type `UserMessage`.
- `Items` (ordered list of Item)
  - All Items produced during this Turn, including the Input. Append-only.
- `StartedAt` (UTC timestamp)
- `CompletedAt` (UTC timestamp, nullable)
  - Set when Status transitions to a terminal state (`Completed`, `Failed`, `Cancelled`).
- `TokenUsage` (object, nullable)
  - `InputTokens` (long): cumulative billing input across all LLM requests in the Turn.
  - `OutputTokens` (long)
  - `CachedInputTokens` (long): cache-hit/cache-read input tokens.
  - `CacheWriteInputTokens` (long): cache-creation/cache-write input tokens.
  - `FreshInputTokens` (long, derived): `max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens)`.
  - `NonCachedInputTokens` (long, derived): `max(0, InputTokens - CachedInputTokens)`.
  - `ReasoningOutputTokens` (long)
  - `TotalTokens` (long)
  - Accumulated across every model request in the Turn from `UsageContent` in the streaming response. This is billing usage, not context-window occupancy.
- `Error` (string, nullable)
  - Human-readable error description when Status is `Failed`.

#### 4.1.3 Item

An Item is the atomic unit of input/output within a Turn. Every piece of information exchanged between the user, agent, and tools is represented as an Item with a typed payload and an explicit lifecycle.

Fields:

- `Id` (string)
  - Unique within the Turn. Format: `item_{sequence}` (e.g., `item_001`).
- `TurnId` (string)
  - Reference to the parent Turn.
- `Type` (enum)
  - `UserMessage` — User's input text.
  - `AgentMessage` — Agent's response text (may be streamed incrementally).
  - `ReasoningContent` — Agent's internal reasoning/thinking (if exposed by the model).
  - `CommandExecution` — Server-observed shell execution projection for `Exec`-style tools. Payload includes command metadata and aggregated output for persistence, history summaries, and non-terminal-capable fallback rendering.
  - `ToolExecution` — Server-observed runtime lifecycle for a normal tool invocation. Payload includes call id, tool name, status, duration, and optional preview/error text.
  - `ImageGeneration` — Hosted image generation lifecycle. Payload includes provider call id, in-progress/completed/failed status, revised prompt, generated image bytes when available, saved path, and error text.
  - `ToolCall` — Agent invokes a native or plugin tool, including managed social tools. Payload includes canonical namespace/name, arguments, call id, definition identity, and safe provenance.
  - `McpToolCall` — MCP invocation lifecycle item preserving raw MCP result fields under audience rules plus separately normalized model content.
  - `DynamicToolCall` — Runtime Dynamic callback lifecycle item with canonical namespace/name, separate call/item ids, status, duration, normalized content, structured content, and stable failure data.
  - `ToolResult` — Result paired with a standard `ToolCall`, including model fallback content and audience-separated client/host data.
  - `ApprovalRequest` — Agent requests user approval for a sensitive operation.
  - `ApprovalResponse` — User's approval decision (approved/rejected).
  - `UserInputRequest` — Plan Mode agent asks the client to collect structured user input.
  - `UserInputResponse` — User's answer to a Plan Mode input request.
  - `Error` — An error occurred during the Turn.
  - `SystemNotice` — Persistent system-level marker in the conversation timeline (e.g. context compaction point). Emits `item/started` + `item/completed` back-to-back; no streaming phase.
- Thread maintenance — A thread-level busy state for long-running blocking maintenance outside the normal Turn stream, currently manual context compaction and manual memory consolidation. While active, new input is accepted only through the queued-input path and starts after the maintenance terminal event. Automatic memory consolidation is non-blocking background work and does not make the thread maintenance-busy.
- `Status` (enum: `Started`, `Streaming`, `Completed`)
  - `Started` — Item has been created, payload may be partial or empty.
  - `Streaming` — Item is receiving incremental updates (deltas). Valid for `AgentMessage`, `ReasoningContent`, runtime-projected `CommandExecution`, and AppServer-projected streamed `ToolCall` argument previews.
  - `Completed` — Item is finalized, payload is complete.
- `Payload` (type-specific object)
  - See Section 4.2 for payload schemas per Item type.
- `CreatedAt` (UTC timestamp)
- `CompletedAt` (UTC timestamp, nullable)

#### 4.1.4 SessionIdentity

A SessionIdentity maps a channel-specific user context to a Thread. It is used for thread discovery and creation.

Fields:

- `ChannelName` (string)
  - The channel requesting the operation (e.g., `"qq"`, `"acp"`).
- `UserId` (string, nullable)
  - Channel-specific user identifier.
- `ChannelContext` (string, nullable)
  - Channel-specific context key (e.g., QQ group ID, ACP workspace URI). Allows multiple threads per user within the same channel.
- `WorkspacePath` (string)
  - The workspace this identity operates in.

Thread discovery uses `SessionIdentity` to find existing threads:
- `FindThreads(identity)` → returns threads matching the workspace, user, and optionally channel context.
- The adapter decides whether to resume an existing thread or create a new one.

### 4.2 Item Payload Schemas

Each Item type has a specific payload structure:

#### UserMessage

```
{
  "text": string,          // Compatibility/display text derived from nativeInputParts
  "nativeInputParts": [    // Optional native input snapshot persisted as the source of truth
    InputPart
  ],
  "materializedInputParts": [ // Optional model-visible input snapshot after server-side materialization
    InputPart
  ],
  "senderId": string,      // Individual sender within a group session (nullable, see Section 17.1)
  "senderName": string,    // Display name of the sender (nullable)
  "senderRole": string,    // Sender role when available from channel adapter (nullable)
  "channelName": string,   // Originating channel for this user message (nullable)
  "channelContext": string,// Channel-specific context for this message (nullable)
  "groupId": string,       // Group/chat identifier (nullable)
  "images": [              // Optional local image metadata for UI rehydration
    {
      "path": string,      // Absolute attachment path on host
      "mimeType": string,  // Optional MIME hint
      "fileName": string   // Optional original filename
    }
  ],
  "triggerKind": string,   // Optional trigger marker: "heartbeat" | "cron" | "automation" | "goal" | "app" | "mcpApp" | "team" | "subagentFollowupTask" | "subagentMailbox" | "subagentInput"
  "triggerLabel": string,  // Optional human-readable source label (e.g. cron job name, task title, agent label)
  "triggerRefId": string   // Optional routing/audit id for click-through when supported (e.g. cron job id, task id, agent path)
}
```

`nativeInputParts` is authoritative for history rendering and editor rehydration when present. `materializedInputParts` captures the exact prompt/image snapshot that Session Core received after transport-side input materialization. `text` remains for compatibility and preview generation but is no longer the sole source of truth for user-message reconstruction.

The optional `triggerKind` trio is populated by Session Core when a turn is submitted inside a `TurnTriggerScope` (see `DotCraft.Sessions.TurnTriggerScope`). The automation-side runners set the scope so that heartbeat / cron (`AgentRunner`) and Automations (`AutomationSessionClient.SubmitTurnAsync`) synthesized messages carry a stable marker that clients can use to render an "automation-sourced" affordance and route click-through to the originating job/task. Goal continuation turns use `triggerKind = "goal"`, `triggerLabel = "Goal continuation"`, and `triggerRefId = internal goal id`. Session-backed SubAgent turns set `triggerKind = "subagentFollowupTask"` for follow-up task turns, `triggerKind = "subagentInput"` for direct/resumable external input, and mailbox drain items use `triggerKind = "subagentMailbox"`; mailbox drain items are internal/model-visible notifications rather than user-authored bubbles, and their `triggerRefId` is an agent path for audit/display and is not necessarily a client-navigable thread id. Fields are absent when the turn originates from a real user input.

#### AgentMessage

```
{
  "text": string          // Accumulated response text (final value after streaming)
}
```

Delta payload (during streaming):

```
{
  "textDelta": string     // Incremental text chunk
}
```

#### ReasoningContent

```
{
  "text": string          // The reasoning/thinking text
}
```

#### ToolCall

```
{
  "namespace": string,       // Optional canonical namespace; absent/null for top-level tools
  "toolName": string,        // Canonical local tool name
  "providerFlatName": string,// Deterministic flat-provider alias persisted from the snapshot
  "definitionId": string,    // Source-qualified ToolDefinitionId
  "runtimeBindingId": string,// Live RuntimeBindingId at invocation time
  "bindingRevision": string, // Runtime binding revision at invocation time
  "snapshotRevision": string,// EffectiveToolSnapshot revision at invocation time
  "sourceKind": string,      // Safe source kind
  "sourceToolId": string,    // Optional safe raw source identity
  "sourceProvenance": object,// Optional sanitized source provenance
  "presentation": object,    // Optional trusted PresentationId + bounded options
  "pluginId": string,        // Optional plugin provenance
  "functionId": string,      // Optional plugin function provenance
  "arguments": object,       // Tool arguments as key-value pairs
  "callId": string           // Provider/model correlation id linking ToolCall to ToolResult
}
```

The tool snapshot and the live invocation have distinct lifetimes. Snapshot planning does not provide the execution Turn identity. At the model callback boundary, Session Core captures the active thread and Turn in an immutable invocation context and uses that context for dispatch and projection. An invocation with no Turn may execute for an authorized host audience but MUST NOT create or update a Turn item.

For a registered call, streamed arguments and dispatcher lifecycle events are coordinated by `(threadId, turnId, callId, projectionShape)`. They MUST atomically upsert the same `ToolCall`; neither arrival order may create a generic duplicate. Canonical identity, source provenance, and presentation come from the matched frozen registration and MUST NOT be inferred from `toolName`, arguments, or result content.

While arguments are still being streamed, the started projection omits `arguments` (or represents it
as null); an empty object means the final argument set is known to be empty. The completed projection
always carries the authoritative argument object.

#### ToolExecution

```
{
  "callId": string,        // Matches the ToolCall.callId
  "toolName": string,      // Name of the tool being executed
  "status": string,        // "inProgress", "completed", "failed", or "cancelled"
  "success": boolean,      // Whether execution succeeded (nullable until completion)
  "durationMs": number,    // Wall-clock duration when available
  "resultPreview": string, // Optional sanitized/truncated UI preview
  "errorMessage": string   // Optional human-readable failure/cancellation message
}
```

`ToolExecution` is a runtime projection for UIs. It does not replace `ToolCall` or `ToolResult`: `ToolCall` remains the model-request/final-arguments item, and `ToolResult` remains the complete model-visible result.

#### ImageGeneration

```
{
  "callId": string,        // Provider image generation call id
  "status": string,        // "inProgress", "completed", or "failed"
  "revisedPrompt": string, // Optional provider-revised prompt
  "result": string,        // Base64 image bytes when completed with inline image data
  "mediaType": string,     // Image media type; defaults to "image/png"
  "savedPath": string,     // Optional local path under .craft/generated_images/{threadId}/{callId}.png
  "errorMessage": string   // Optional human-readable failure or unsupported-result message
}
```

`ImageGeneration` represents hosted image generation as a first-class Session item. It is a provider-hosted capability, not a local tool and not an `IToolRuntime` invocation. Snapshot planning records it in a separate provider-capability plan; the provider adapter creates and completes this item from provider call/result content. If the result arrives before the call, Session Core creates and immediately completes a single `ImageGeneration` item. Completed inline image bytes are persisted under `.craft/generated_images/{threadId}/{callId}.png`; the payload also carries base64 bytes for wire clients.

#### McpToolCall

```
{
  "namespace": string,          // Canonical MCP server namespace
  "toolName": string,           // Canonical local name
  "providerFlatName": string,   // Deterministic flat-provider alias persisted from the snapshot
  "server": string,             // Runtime MCP server name
  "origin": string,             // "workspace" | "plugin" | "thread" | "binding"
  "sourceToolId": string,       // Original MCP tool name
  "definitionId": string,       // Source-qualified ToolDefinitionId
  "runtimeBindingId": string,   // Live MCP binding identity
  "bindingRevision": string,    // Binding/session generation revision
  "snapshotRevision": string,   // EffectiveToolSnapshot revision
  "sourceProvenance": object,   // Sanitized origin provenance
  "presentation": object,       // Optional trusted local presentation descriptor
  "callId": string,             // Provider/model call id; differs from Item.id
  "arguments": object,
  "status": string,             // "inProgress" | "completed" | "failed"
  "durationMs": number,
  "contentItems": array,        // Normalized model/history content
  "structuredContent": any,     // Client/view-only structured result
  "_meta": object,              // Sanitized host/view-only MCP metadata
  "rawContent": array,          // Raw MCP content retained for clients, not model history
  "mcpAppResourceUri": string,  // Optional normalized ui:// association from the invoked definition
  "isError": boolean,
  "success": boolean,           // Absent/null while in progress
  "errorCode": string,
  "errorMessage": string
}
```

`McpToolCall` is one lifecycle item and has no companion `ToolResult`. History reconstruction uses only `contentItems`; it MUST NOT insert `structuredContent`, `_meta`, or `rawContent` into model context.

`mcpAppResourceUri` and the bounded tool result are the only persisted MCP App presentation inputs. Availability is recalculated against the current MCP registration and authority when the Item is projected. `viewHandle`, UI resource HTML/blob, CSP, iframe/AppBridge state, pending context, credentials, and live authority state are runtime data and MUST NOT be persisted. When current availability cannot be established, a persisted or replayed `McpToolCall` uses the generic result presentation.

#### DynamicToolCall

```
{
  "namespace": string,      // Optional canonical namespace; absent/null for a top-level Function
  "toolName": string,       // Runtime dynamic tool name
  "providerFlatName": string,// Deterministic flat-provider alias persisted from the snapshot
  "callId": string,         // Provider/model call id; differs from Item.id
  "arguments": object,      // Tool arguments as key-value pairs
  "status": string,         // "inProgress" | "completed" | "failed"
  "durationMs": number,     // Present on terminal payloads
  "contentItems": [         // Optional rich result content
    {
      "type": string,       // "text" | "image"
      "text": string,       // Text content when type is "text"
      "mediaType": string,  // Image media type when type is "image"
      "dataBase64": string  // Base64 image data when type is "image"
    }
  ],
  "structuredContent": any, // Client-only structured JSON result
  "success": boolean,       // Absent/null while in progress
  "errorCode": string,      // Optional stable failure code
  "errorMessage": string    // Optional English fallback failure message
}
```

Runtime Dynamic Tool invocations are represented by this single item. Session Core does not emit paired `ToolCall` / `ToolResult` items for the same call.

#### ToolResult

```
{
  "callId": string,          // Matches the ToolCall.callId
  "namespace": string,       // Optional canonical namespace; absent/null for top-level tools
  "toolName": string,        // Canonical local tool name
  "providerFlatName": string,// Deterministic flat-provider alias persisted from the snapshot
  "definitionId": string,    // Source-qualified ToolDefinitionId
  "runtimeBindingId": string,// RuntimeBindingId used for the call
  "bindingRevision": string, // Binding revision used for the call
  "snapshotRevision": string,// EffectiveToolSnapshot revision used for the call
  "sourceKind": string,      // Safe source kind
  "sourceToolId": string,    // Optional safe raw source identity
  "sourceProvenance": object,// Optional sanitized source provenance
  "presentation": object,    // Optional trusted PresentationId + bounded options
  "pluginId": string,        // Optional plugin provenance
  "functionId": string,      // Optional plugin function provenance
  "durationMs": number,      // Wall-clock duration of the invocation
  "result": string,          // Model/history-safe textual fallback
  "contentItems": [          // Optional model-safe rich content
    {
      "type": string,     // "text" | "image"
      "text": string,     // Text content when type is "text"
      "mediaType": string,// Image media type when type is "image"
      "dataBase64": string// Base64 image data when type is "image"
    }
  ],
  "structuredContent": any, // Optional client-only structured result
  "_meta": object,           // Optional sanitized host-only metadata
  "success": boolean,        // Whether the tool execution succeeded
  "errorCode": string,       // Optional stable failure code
  "errorMessage": string     // Optional English fallback failure message
}
```

For ordinary tools that return non-text `AIContent`, `result` remains the
model/history-safe text fallback. Clients may use `contentItems` for richer
presentation, such as showing image output from a file read. History
reconstruction may restore a validated image item as structured model media,
but it must never serialize the item's data URL or base64 payload as ordinary
text or JSON. Provider-specific compatibility projection may replace historical
tool media with the textual fallback. It never serializes `structuredContent`
or `_meta` into provider history.

`ToolResult` repeats the complete immutable invocation identity used by its matching `ToolCall`; clients do not join against current registry state to recover provenance or presentation. Standard tool projection appends exactly one terminal `ToolResult` for a call. Specialized `McpToolCall` and `DynamicToolCall` projections instead update their single item to exactly one terminal state and do not create `ToolResult`. All projections use one atomic terminal guard across completion, rejection, cancellation, timeout, and failure races. If an untrusted provider callback names an invalid, unknown, or unavailable function, the result is a recoverable tool failure with `success = false` and stable `errorCode = "tool_not_found"`; the same failure is returned to the model without escalating it to a Turn-level exception.

#### CommandExecution

```
{
  "callId": string,               // Correlates to the underlying Exec-style tool call
  "command": string,              // Shell command text
  "workingDirectory": string,     // Effective working directory
  "source": string,               // "host" or "sandbox"
  "status": string,               // "inProgress", "completed", "failed", "cancelled", "backgrounded", "killed", or "lost"
  "aggregatedOutput": string,     // Full accumulated output shown to the user
  "sessionId": string | null,     // Background terminal id when the command continues after tool return
  "outputPath": string | null,    // Host-local output log for background terminal sessions
  "originalOutputChars": number | null,
  "truncated": boolean | null,
  "backgroundReason": string | null,
  "exitCode": number | null,      // Process exit code when available
  "durationMs": number | null     // Total wall-clock duration when available
}
```

When an `Exec`-style tool returns while its process is still alive, the
`CommandExecution` Item is completed with `status = "backgrounded"`. Later
process lifecycle changes are represented by the background terminal runtime,
not by appending deltas to an already completed Turn.

An `Exec` process that is still owned by the active tool invocation is foreground
work. Cancelling the Turn stops its process tree, completes the
`CommandExecution` Item with `status = "cancelled"`, and only then completes Turn
cancellation. A process explicitly started with `runInBackground = true` transfers
ownership to the background terminal runtime once it starts; Turn cancellation
does not stop it, including while the tool is waiting for its initial yield.

Terminal-capable AppServer clients consume live shell process output from
`terminal/*` notifications. `CommandExecution` remains the persisted observable
summary and compatibility projection; clients that consume both paths merge by
`callId` and avoid rendering duplicate output.

#### ApprovalRequest

```
{
  "approvalType": string, // "file" or "shell"
  "operation": string,    // For file: "read", "write", "edit", "list". For shell: the command.
  "target": string,       // For file: the path. For shell: the working directory.
  "requestId": string     // Unique ID for correlating with ApprovalResponse
}
```

#### ApprovalResponse

```
{
  "requestId": string,    // Matches the ApprovalRequest.requestId
  "approved": boolean     // User's decision
}
```

#### UserInputRequest

```
{
  "requestId": string,
  "questions": [
    {
      "id": string,       // Stable answer key, e.g. "provider_id_handling"
      "header": string,   // Short UI label
      "question": string, // User-facing prompt
      "isOther": boolean, // Whether the client may offer a free-form response
      "isSecret": boolean,
      "options": [
        { "label": string, "description": string }
      ]
    }
  ]
}
```

#### UserInputResponse

```
{
  "requestId": string,
  "response": {
    "answers": {
      "<questionId>": {
        "answers": [string]
      }
    }
  }
}
```

#### Error

```
{
  "message": string,      // Human-readable error description
  "code": string,         // Machine-readable error code (e.g., "agent_error", "timeout")
  "fatal": boolean        // Whether the error terminates the Turn
}
```

#### SystemNotice

```
{
  "kind": string,              // Notice classifier. Known values: "compacted", "memoryConsolidated", "forked".
  "trigger": string,           // For kind="compacted": "auto" | "reactive" | "manual"
  "mode": string,              // For kind="compacted": "partial"; legacy persisted notices may contain "micro"
  "tokensBefore": number,      // Approximate input tokens right before compaction ran
  "tokensAfter": number,       // Approximate input tokens after compaction ran
  "percentLeftAfter": number,  // Fraction of EffectiveContextWindow still available (0.0 - 1.0)
  "clearedToolResults": number,// Count of tool results cleared before summary (0 for partial-only compaction)
  "sourceThreadId": string     // For kind="forked": source thread id
}
```

`SystemNotice` items are created by Session Core for partial neutral or
provider-native compaction and persisted via the normal rollout/`turn.Items`
pipeline, so they survive thread reload and round-trip through paged Item history.
Clients treat them as inline dividers in the timeline rather than part of the
model conversation. Cold-cache tool-result clearing without a replacement
emits only the transient `system/event` needed to refresh context usage; it
must not create a persistent timeline divider. Clients may encounter older
`mode = "micro"` compacted notices from previous releases and should hide them.
`memoryConsolidated` notices have no compaction-specific token fields.
`forked` notices mark the boundary between copied source history and new
fork-specific work. They carry `sourceThreadId`, are not model-visible, and
must not mutate the source thread.

### 4.3 Stable Identifiers and Normalization Rules

- **Thread ID**: Generated by Session Core. Format `thread_{yyyyMMdd}_{6-char-random}`. Must be unique within the workspace. Used as the primary key for persistence and cross-channel resume.
- **Turn ID**: Sequential and never reused within a Thread. Format `turn_{3-digit-sequence}`. Assigned by Session Core when a Turn starts. Session Core maintains a historical sequence high-water mark across rollback, restart, fork initialization, subagent execution, and ordinary Turn creation; removing a visible Turn never lowers that mark.
- **Item ID**: Sequential within a Turn. Format `item_{3-digit-sequence}`. Assigned by Session Core when an Item is created. Continuing an existing Turn resumes after its highest allocated sequence. Lifecycle updates may reuse the same id only for the same logical Item; a different type, call, or source MUST NOT overwrite it.
- **Item reference**: An Item is addressed across Thread-level protocols by `(turnId, itemId)`. `itemId` alone is not a Thread-wide identity.
- **Canonical tool identity**: The persisted pair `(namespace, toolName)` is the exact case-sensitive `ToolName` selected by the Turn snapshot. It is not parsed from a provider string. Each present component matches `^[A-Za-z0-9_]+$` and its deterministic flat form fits within 64 ASCII bytes.
- **Flat provider alias**: `providerFlatName` is the exact deterministic alias selected by the same snapshot for providers that cannot represent namespaces. It is required on every new tool invocation payload, remains distinct from the canonical pair, and is never parsed to recover canonical or source-routing identity.
- **UserId Normalization**: Session Core stores `UserId` as-is from the adapter. Cross-channel user identity resolution (is QQ user X the same as ACP user Y?) is out of scope for this spec.

## 5. Session Lifecycle Specification

### 5.1 Thread Lifecycle

```
                    ┌──────────┐
     CreateThread   │          │
    ─────────────►  │  Active  │ ◄──── ResumeThread
                    │          │
                    └────┬─────┘
                         │
              ┌──────────┼──────────┐
              │                     │
              ▼                     ▼
        ┌──────────┐         ┌───────────┐
        │  Paused  │         │ Archived  │
        └────┬─────┘         └───────────┘
             │
             │ ResumeThread
             ▼
        ┌──────────┐
        │  Active  │
        └──────────┘
```

**Transitions**:

- `CreateThread(identity)` → `Active`
  - Session Core generates a Thread ID, sets `CreatedAt` and `LastActiveAt` to now.
  - The adapter provides `SessionIdentity` with channel name, user ID, and context.

- `Active` → `Paused`
  - Triggered by explicit adapter request or by inactivity timeout (configurable, default: none).
  - A Paused thread can be resumed by any compatible server-managed channel.
  - No Turn may be started on a Paused thread without first resuming it.

- `Paused` → `Active` (via `ResumeThread`)
  - Any compatible in-scope adapter can resume a Paused thread by calling `ResumeThread(threadId)`.
  - Session Core loads the thread state and model history from rollout, constructs a runtime agent session, and sets status to Active.
  - `LastActiveAt` is updated.

- `Active` → `Archived`
  - Triggered by explicit adapter request or by archival policy (e.g., "archive threads inactive for 30 days").
  - Archived threads are read-only. They can be listed and inspected but not resumed.
  - Archiving a parent thread recursively archives its SubAgent child subtree.

- `Archived` → `Active` (via `UnarchiveThread`)
  - Triggered by explicit restore request.
  - Restoring a parent thread recursively restores its SubAgent child subtree.

**Invariants**:

- At most one Turn may be `Running`, `WaitingApproval`, or `WaitingInput` on a Thread at any time.
- At most one thread maintenance operation may be active on a Thread at any time.
- A Thread may have Turns from different channels (cross-channel resume). Each Turn records which channel originated it.

### 5.1.1 Thread-Owned Runtime Artifacts

Session Core distinguishes canonical thread history from thread-owned runtime artifacts. Artifacts are not a second source of conversation authority, but their ownership and cleanup are part of the thread lifecycle:

- Background terminal metadata and output logs are owned by the originating Thread. A terminal session is identified by its persisted terminal metadata and remains addressable after process completion until its owner is permanently deleted or retention removes the artifact.
- Oversized tool-result spill files are owned by the originating Thread. The model-visible message contains a bounded preview and a workspace-relative reference; the complete spill file remains independent of the preview and is not reconstructed from the rollout.
- Archive is not deletion. `Active → Archived` stops or invalidates active terminal processes for the archived thread, but preserves completed/lost terminal metadata, output logs, and tool-result spill files so historical inspection, context export, and tool-result reads continue to work.
- Permanent deletion is deletion of the complete owned subtree. It stops or invalidates active terminal processes, removes terminal metadata/output and tool-result spill artifacts for the thread and all descendant SubAgent threads, then removes the thread's durable state. Cleanup is idempotent and may be retried independently when an individual filesystem operation fails.
- Parent lifecycle operations apply to the complete SubAgent subtree. A child artifact must not survive permanent deletion of its owning parent solely because the child is not visible in the default thread list.
- Compaction may replace or clear model-visible tool-result content, but must not delete a spill file while a current rollout or retained historical view may reference it. Spill cleanup is a retention/orphan-maintenance concern, not a compaction side effect.
- Graceful process shutdown stops active terminal processes, flushes terminal metadata/rollout writers, and releases handles. It does not delete archived history or persistent terminal/tool-result artifacts merely because the process exits. A later startup pass marks persisted `running` terminals as `lost` before retention processing.

### 5.1.2 Session Recovery Snapshots

Session Core can export and restore a versioned JSON recovery snapshot for a server-managed, non-ephemeral Thread. The snapshot preserves the executable state required to continue with a later Turn.

- Export accepts only a Thread ID. Session Core first loads the persisted Thread into its runtime, admits thread maintenance through the same runtime command owner used by `SubmitInput`, flushes durable state, and captures only while the Thread is idle and its newest Turn is `Completed`, `Failed`, or `Cancelled`. The result reports the Turn boundary actually captured so an adapter can compare it with its own Run binding.
- Version 1 is one JSON document containing the original Thread ID and normalized workspace, execution configuration, source and metadata, latest terminal Turn identity and state, the Turn sequence high-watermark, model Session encoded by `ModelHistoryCodec`, and provider continuity state.
- Restore decodes model history with `ModelHistoryCodec` and replays provider state with `ProviderHistoryReplayer` before installation.
- Export and restore exchange snapshot files only through a restricted staging directory under the workspace `.craft` path. Callers cannot provide an arbitrary filesystem path. Staged snapshots are caller-cleaned after use and may also be removed by age-based DotCraft maintenance.
- Restore requires the current workspace and original Thread ID to match and is allowed only when the target Thread does not exist.
- Restore materializes the Thread header, captured terminal boundary, Session checkpoint, and provider checkpoint. It preserves the Turn sequence high-watermark.
- The snapshot is validated before the restored Thread becomes visible, and a failed restore removes state created by that attempt.
- Normal `ResumeThread` remains the only way to create execution resources after restore. Recovery itself returns only the restored Thread ID and emits no synthetic Turn or Item.

### 5.2 Turn Lifecycle

Creating a Running Turn is an atomic Thread-local transition. Validation that the Thread is active
and has no Running, WaitingApproval, or WaitingInput Turn, sequence reservation, Turn insertion,
and runtime registration occur under the same Thread-local start gate. Concurrent submissions may
create at most one Running Turn; all other contenders fail with the existing turn-in-progress
error and must not append input Items or emit start events.

```
SubmitInput
    │
    ▼
Running ──approval request──► WaitingApproval ──resolved──► Running
Running ──input request─────► WaitingInput    ──resolved──► Running
Running ────────────────────► Completed | Failed | Cancelled
WaitingApproval/WaitingInput ──────────► Cancelled
```

**Transitions**:

- `SubmitInput(threadId, content)` → `Running`
  - Session Core creates a new Turn, creates a `UserMessage` Item from the input, invokes the agent.
  - Precondition: Thread status is `Active` and no other Turn is `Running`, `WaitingApproval`, or `WaitingInput`, and no thread maintenance is active.

- `Running` → `WaitingApproval`
  - The agent's tool execution encounters a sensitive operation requiring user approval.
  - Session Core creates an `ApprovalRequest` Item and pauses agent execution.
  - The adapter presents the approval request to the user.

- `WaitingApproval` → `Running`
  - The adapter calls `ResolveApproval(turnId, requestId, approved)`.
  - Session Core creates an `ApprovalResponse` Item and resumes agent execution.

- `Running` → `WaitingInput`
  - A root-thread agent calls `RequestUserInput` to ask the user a short structured question.
  - Session Core creates a `UserInputRequest` Item and pauses agent execution.
  - The adapter presents the question request to the user.

- `WaitingInput` → `Running`
  - The adapter resolves the request with a `UserInputResponse`.
  - Session Core records the response Item and resumes agent execution.

- `Running` → `Completed`
  - The agent finishes its response. The final `AgentMessage` Item is marked Completed.
  - Session Core commits the terminal rollout batch, runs Stop hooks, and schedules eligible background consolidation.

- `Running` → `Failed`
  - An unrecoverable error occurs: agent exception, tool execution error, timeout.
  - Session Core creates an `Error` Item, sets `Turn.Error`, saves the partial Thread state, and runs cleanup.
  - For server-managed history, Session Core appends completed model messages and any compaction replacement before finishing failure handling. The next Turn includes the failed Turn's completed user, assistant, and paired tool-call/tool-result history, but must not expand older history that a surviving compaction checkpoint replaced.

- `Running`, `WaitingApproval`, or `WaitingInput` → `Cancelled`
- The adapter requests cancellation (e.g., user sends `/cancel`, channel disconnects).
- Session Core cancels the agent execution via `CancellationToken`, completes any currently streaming agent/reasoning Items with their accumulated text, and appends partial domain and model history to rollout. A cancellation must not restore pre-compaction tool results or summaries that were no longer model-visible.

**Terminal states**: `Completed`, `Failed`, `Cancelled`. A Turn in a terminal state cannot transition.

### 5.3 Item Lifecycle

```
    ┌─────────┐      ┌───────────┐      ┌───────────┐
    │ Started │ ───► │ Streaming │ ───► │ Completed │
    └─────────┘      └───────────┘      └───────────┘
         │                                     ▲
         └─────────────────────────────────────┘
              (non-streaming items skip Streaming)
```

**Transitions**:

- `Started` → `Streaming` (optional, for `AgentMessage`, `ReasoningContent`, and runtime-projected `CommandExecution`)
  - Session Core begins receiving incremental content from the agent.
  - Each delta emits an `item/delta` event with the incremental payload.
  - For streamed tool arguments, hosts such as AppServer may project these deltas as tool-call argument preview notifications on the wire while the canonical persisted `ToolCall` payload is finalized at completion.

- `Streaming` → `Completed`
  - The agent finishes producing content for this Item.
  - The Item's payload contains the final accumulated value.

- `Started` → `Completed` (for non-streaming items)
    - Items like `ToolResult`, `ApprovalRequest`, `ApprovalResponse`, `UserInputRequest`, `UserInputResponse`, `Error` are created with their full payload and immediately completed.
    - `ToolCall` is usually completed directly, but hosts may expose an intermediate streaming preview of argument construction before the final completed payload is persisted.
    - `ImageGeneration` starts when hosted image generation begins and completes with inline image data or a visible failure/unsupported-result payload.
    - `McpToolCall` and `DynamicToolCall` start with `status = "inProgress"` and nullable/absent `success`, then complete once with a terminal completed/failed payload.

**Invariants**:

- An Item's Status never moves backward.
- A Completed Item's payload is immutable.
- A tool invocation has exactly one source-declared projection shape. Streaming observation and dispatcher recording atomically upsert its call item by Turn, call id, and shape.
- Each invocation publishes at most one terminal projection. Competing completion, rejection, timeout, cancellation, and failure paths converge on the same terminal guard.
- Items within a Turn are ordered by creation time. This order is the canonical sequence of events within the Turn.
- A tool-call boundary finalizes any preceding streaming `AgentMessage` and `ReasoningContent` before the tool lifecycle begins. Their accumulated payloads are persisted and their `item/completed` events are emitted before any approval or user-input request created by that tool can pause execution.
- Before model-visible history is submitted to a provider, Session Core MUST ensure
  that each assistant tool call has an immediately following tool result message.
  If persisted or in-memory history contains an incomplete historical tool call,
  the provider request is repaired or filtered at the request boundary rather
  than sending a provider-invalid message sequence.

### 5.4 Turn Item Sequence (Normative)

A typical Turn produces Items in this order:

```
1. UserMessage (input)
2. [ReasoningContent] (if model exposes thinking)
3. [ToolCall → ToolResult | ImageGeneration | McpToolCall | DynamicToolCall]* (zero or more tool/hosted invocations)
   3a. [ApprovalRequest → ApprovalResponse] (within a tool call, if approval needed)
   3b. [UserInputRequest → UserInputResponse] (Plan Mode only, if the agent needs a user decision before continuing)
4. AgentMessage (final response, streamed)
5. [Error] (if something went wrong)
```

The sequence may recurse: the agent may call tools, receive results, reason again, call more tools, and then respond. Session Core emits Items in the order they occur. The adapter renders them according to its channel's capabilities.

### 5.4.1 MCP App direct actions and transient context

An MCP App view action does not belong to the completed Turn that produced its source result.

- A view `tools/call` uses the common dispatcher with null Turn id. It creates no Turn, `ToolCall`, `ToolResult`, or `McpToolCall`, and never enters provider history. Safe tracing records `ToolInvocationOrigin(kind: "mcpApp", sourceItemId)` independently from Session items.
- An accepted `ui/message` is the only view action that creates conversational input. It starts a Turn immediately when the thread is idle or uses `QueuedTurnInput` while another Turn is active. The resulting user input is marked with `triggerKind = "mcpApp"` and safe server/tool/source-item provenance; the opaque view handle and UI resource data are not persisted.
- Each live view owns one last-write-wins pending context. A View message atomically consumes only its originating value. An ordinary user Turn atomically consumes all live pending values for the thread. Consumption happens at the central Turn-start boundary, including the queued-input path, and each value is injected at most once.
- Pending context is bounded, explicitly marked untrusted, and transient. It is not an Item, rollout record, thread configuration, or provider-history message by itself. It cannot change system policy or tool authority. Teardown, revoke, disconnect, archive/delete, or MCP generation replacement discards unconsumed values.
- If an accepted View message is persisted in the ordinary input queue and the process restarts, the queued message may survive but its transient pending context does not.

Unknown context content shapes reject the entire update. Text and image content are materialized through the normal safe input path; structured content may be injected only as bounded JSON.

### 5.5 Cross-Channel Resume Semantics

When a channel adapter resumes a Thread that was created by a different channel:

1. The adapter calls `ResumeThread(threadId)`.
2. Session Core loads the Thread from persistence.
3. Session Core reconstructs a request-local `List<ChatMessage>` from the rollout's exact model-history records and latest usable compaction checkpoint. Domain Items are used only as a finite fallback when an exact record is absent.
4. The Thread's `Status` is set to `Active`, `LastActiveAt` is updated.
5. The adapter can now call `SubmitInput` to start a new Turn.
6. The new Turn's Items are attributed to the resuming channel (recorded in Turn metadata).

The resumed agent has full context of previous Turns regardless of which channel originated them.

When reconstructing model history, provider reasoning metadata MUST be preserved on assistant messages that contain tool calls. If one sampling segment produced `ReasoningContent`, visible assistant text, and one or more `ToolCall` Items before their matching `ToolResult` Items, the reconstructed history represents them as one assistant `ChatMessage` containing reasoning content, visible text, and all function calls. This preserves OpenAI protocol providers such as DeepSeek whose thinking mode requires `reasoning_content` to be round-tripped on assistant tool-call messages.

Tool history reconstruction is source-neutral. It expands a valid standard `ToolCall`/`ToolResult` pair or a terminal `McpToolCall`/`DynamicToolCall` into the provider's function-call/function-result sequence while preserving the original provider `callId`. A namespace-capable provider receives the persisted `(namespace, toolName)` tuple; a flat-only provider receives the persisted `providerFlatName`. Reconstruction MUST NOT consult the current tool inventory, parse a flat alias, regenerate one from current naming rules, or substitute MCP `server`/`sourceToolId` for model identity. Orphaned, duplicated, incomplete, or identity-incomplete calls are diagnosed and skipped or rejected at the request boundary; Session Core MUST NOT guess pairings. Only normalized model content participates in provider history. `structuredContent`, `_meta`, and raw MCP result fields never do.

Validated tool-result images remain structured media until history compaction.
Summary-producing partial or full compaction replaces tool-result images in the
replacement history with their bounded textual description. Images attached to
a user message remain only when that user message is part of the preserved tail;
images covered by the summarized prefix are not copied into replacement history.

## 6. Event Model

### 6.1 Overview

Session Core emits a structured event stream during Turn execution. The event stream is the **contract between Session Core and channel adapters**: adapters consume events and translate them to their transport format. This replaces channel-specific `agent.RunStreamingAsync` consumption loops.

Events are delivered in-process via a callback or async enumerable pattern. There is no network transport for events — adapters run in the same process as Session Core.

### 6.2 Event Envelope

Every event carries a common envelope:

```
SessionEvent
├── EventId: string           // Unique event ID, monotonically increasing within a Turn
├── EventType: string         // One of the types defined in Section 6.3
├── ThreadId: string          // Parent Thread
├── TurnId: string            // Parent Turn (null for thread-level events)
├── ItemId: string            // Related Item (null for turn/thread-level events)
├── Timestamp: UTC timestamp  // When the event was emitted
└── Payload: object           // Event-type-specific data
```

### 6.3 Event Types

#### Thread Events

- **`thread/created`**
  - Emitted when a new Thread is created.
  - Payload: `{ thread: Thread }` (full Thread object with initial state).

- **`thread/resumed`**
  - Emitted when a Paused or previously inactive Thread is resumed.
  - Payload: `{ thread: Thread, resumedBy: string }` (channel name that resumed it).

- **`thread/statusChanged`**
  - Emitted when Thread status changes (Active → Paused, Active → Archived).
  - Payload: `{ previousStatus: string, newStatus: string }`.

- **`thread/renamed` (Wire Protocol only; not a `SessionEvent`)**
  - Display name changes are applied in Session Core via `ISessionService.RenameThreadAsync` or when the first user message on a turn sets `Thread.DisplayName` (see turn input handling and `Thread.DisplayName` in this specification). Session Core does **not** enqueue a `SessionEvent` on the turn/event stream for rename-only updates (there is no separate thread-level event type consumed by in-process adapters the same way as `thread/created`).
  - Hosts that multiplex **multiple Wire clients** onto the same Session Core process (e.g. DotCraft AppServer) **SHOULD** broadcast a `thread/renamed` notification on the AppServer Wire Protocol after the display name is updated, including when the change originates from another channel or from automatic titling, so clients such as DotCraft Desktop can refresh thread titles **without** relying on `turn/completed` (which may not be delivered to connections that did not subscribe to that thread). See [AppServer Protocol §4.11 `thread/rename`](../protocols/appserver-protocol.md#411-threadrename) and [§6.1 `thread/renamed`](../protocols/appserver-protocol.md#61-thread-notifications).

- **`thread/deleted` (Wire Protocol only; not a `SessionEvent`)**
  - Permanent removal is performed via `ISessionService.DeleteThreadPermanentlyAsync(threadId)`. Session Core first closes the thread recorder, then deletes the canonical rollout, then removes DB-backed state and attachment references, and finally best-effort deletes workspace-managed attachment files that are no longer referenced by any remaining thread. Failure to delete the rollout leaves database state and attachment assets intact. It does **not** enqueue a `SessionEvent` on the turn/event stream (there is no active turn for deletion).
  - Hosts that multiplex **multiple Wire clients** onto the same Session Core process (e.g. DotCraft AppServer) **SHOULD** broadcast a `thread/deleted` notification on the AppServer Wire Protocol after deletion completes, including when deletion is initiated outside Wire (e.g. DashBoard HTTP `DELETE` on `/dashboard/api/sessions/{sessionKey}`), so UIs stay consistent. See [AppServer Protocol §4.9 `thread/delete`](../protocols/appserver-protocol.md#49-threaddelete) and [§6.1 Thread Notifications](../protocols/appserver-protocol.md#61-thread-notifications).

#### Turn Events

- **`turn/started`**
  - Emitted when a new Turn begins.
  - Payload: `{ turn: Turn }` (Turn object with `Status = Running`, Input Item included).

- **`turn/completed`**
  - Emitted when a Turn finishes successfully.
  - Provider output-limit termination (for example `finish_reason = "length"` or Anthropic `max_tokens`) is not successful completion.
  - Payload: `{ turn: Turn }` (final Turn state with all Items, TokenUsage).

- **`turn/failed`**
  - Emitted when a Turn fails.
  - Also emitted when the model response is truncated by the provider output token limit, so clients can retry or surface the incomplete generation instead of accepting it as success.
  - Payload: `{ turn: Turn, error: string }`.

- **`turn/cancelled`**
  - Emitted when a Turn is cancelled.
  - Payload: `{ turn: Turn, reason: string }`.

#### Item Events

- **`item/started`**
  - Emitted when an Item is created.
  - Payload: `{ item: Item }` (Item with `Status = Started`, payload may be partial).
  - Adapters should begin rendering immediately (e.g., show a "typing" indicator for AgentMessage, show tool name for ToolCall).

- **`item/delta`**
  - Emitted for incremental content updates on streaming Items (`AgentMessage`, `ReasoningContent`, `CommandExecution`, and streamed `ToolCall` argument previews).
  - Payload: the delta-specific payload (e.g., `{ textDelta: "chunk of text" }`).
  - May be emitted many times per Item. Adapters that support streaming should forward these to the user progressively.
  - Adapters that do not support streaming may ignore deltas and wait for `item/completed`.
  - Persistence still uses the final completed Item payload as source of truth; intermediate `ToolCall` argument preview deltas are for progressive rendering.

- **`item/completed`**
  - Emitted when an Item is finalized.
  - Payload: `{ item: Item }` (Item with `Status = Completed`, full payload).

#### Approval Events

- **`approval/requested`**
  - Emitted when the agent requires user approval. Equivalent to `item/started` for an `ApprovalRequest` Item, but distinguished as a separate event type because it requires adapter action (the adapter must present the request and return a response).
  - Payload: `{ item: Item }` (the `ApprovalRequest` Item).
  - The Turn enters `WaitingApproval` status.

- **`approval/resolved`**
  - Emitted when the user resolves an approval request.
  - Payload: `{ item: Item }` (the `ApprovalResponse` Item).
  - The Turn returns to `Running` status.

#### User Input Request Events

- **`userInput/requested`**
  - Emitted when a root-thread agent calls `RequestUserInput`.
  - Payload: `{ item: Item }` (the `UserInputRequest` Item).
  - The Turn enters `WaitingInput` status.

- **`userInput/resolved`**
  - Emitted when the adapter returns answers for a user input request.
  - Payload: `{ item: Item }` (the `UserInputResponse` Item).
  - The Turn returns to `Running` status.

#### SubAgent Progress Events

- **`subagent/progress`**
  - Emitted periodically (~200ms) during Turn execution when one or more SubAgent tool calls (`SpawnAgent`) are active.
  - Provides a snapshot of all active SubAgents' real-time execution progress, including the tool currently being executed, cumulative token consumption, and completion status.
  - Payload:

    ```
    {
      "entries": [
        {
          "label": string,          // SubAgent identifier/label (matches the agentNickname argument passed to SpawnAgent)
          "currentTool": string,    // Name of the tool the SubAgent is currently executing (nullable, null when thinking)
          "inputTokens": long,      // Cumulative input token consumption
          "outputTokens": long,     // Cumulative output token consumption
          "isCompleted": boolean    // Whether the SubAgent has finished execution
        }
      ]
    }
    ```

  - **Emission rules**:
    - The event is emitted by a periodic aggregator (~200ms interval) that snapshots the in-process `SubAgentProgressBridge` state.
    - The aggregator starts when the first `SpawnAgent` tool call begins within a Turn, and stops when the Turn ends or all tracked SubAgents have completed.
    - Each notification contains the **complete snapshot** of all tracked SubAgents (not incremental deltas), so clients can replace their local state on each receipt.
    - The event is injected into the Turn's event stream as a sideband signal — it may interleave with `item/started`, `item/delta`, and `item/completed` events. This is expected behavior.
  - **Relationship to Item events**: SubAgent execution is triggered by `SpawnAgent` tool calls, which appear as `item/started` (type `toolCall`, toolName `SpawnAgent`) and `item/completed` (type `toolResult`) events. The `subagent/progress` event provides fine-grained intermediate progress that is not captured by the standard Item lifecycle.
  - **Adapters**: Adapters that render SubAgent progress (e.g., CLI Live Table) should consume `subagent/progress` events to update their UI. Adapters that do not need SubAgent progress may ignore this event type or opt out via `optOutNotificationMethods`.

#### System Events

- **`system/event`**
  - Emitted by Session Core when a system-level maintenance operation occurs during a Turn's post-processing phase. These operations are not part of the agent's conversational output but affect the session's internal state.
  - Payload:

    ```
    {
      "kind": string,          // One of: "compactWarning", "compactError",
                                //         "compacting", "compacted", "compactSkipped", "compactFailed",
                                //         "consolidating", "consolidated", "consolidationSkipped",
                                //         "consolidationFailed", "compactCancelled",
                                //         "streamError",
                                //         "consolidationCancelled"
      "messageKey": string,    // Stable client-localization key (nullable)
      "params": object,        // Optional interpolation params (nullable)
      "fallbackText": string,  // English fallback text (nullable)
      "message": string,       // Compatibility alias for fallbackText (nullable)
      "percentLeft": double,   // Fraction of the effective context window still unused (nullable; 0.0-1.0)
      "tokenCount": long,      // Current estimated prompt token usage (nullable)
      "contextUsage": object   // Full ContextUsageSnapshot on successful compaction terminal events (nullable)
    }
    ```

    The effective context window is evaluated for the thread's effective model when `Compaction.ContextWindow` is inferred from the model catalog; a thread-level model override therefore changes compaction thresholds for that thread.

  - **Context usage accounting**:
    - `ContextUsageSnapshot.tokens` is server-authoritative active context-window occupancy, separate from cumulative billing usage. After each provider usage snapshot, Session Core records the latest request's provider-visible input plus the output generated by that request; this value is the post-response active context count, not the Turn's cumulative `TokenUsage`.
    - Session Core chooses sources in this order: a valid provider usage anchor plus model-visible messages appended after the anchored request; a prefix-adjusted anchor when the anchored message prefix and non-instruction request shape still match; the latest provider active-context snapshot only while it belongs to the current request boundary; persisted provider-context display fallback for UI only; and finally full model-visible history estimate.
    - Strict prompt request fingerprints are for prompt-cache and maintenance-fork safety. `PromptDriftDetected`, including drift caused by re-reading dirty memory pages, must not by itself force context usage to fall back to full-history estimation. If only base instructions changed, Session Core adjusts the anchored count by the estimated base-instructions token delta.
    - Auto-compaction may trigger from provider context only when the provider anchor or request-boundary snapshot is still valid for the current model-visible history. After rollback, compaction, deletion, or another history replacement, Session Core must invalidate provider anchors and token trackers, save a replacement-history estimate, and allow that estimate to drive the next auto-compaction decision until a new provider usage snapshot arrives. A provider-native replacement uses its generation-scoped estimator instead of expanding the neutral transcript. Persisted provider display values without a valid anchor remain UI/diagnostic fallbacks and must not by themselves trigger auto-compaction. See [Context Compaction](context-compaction.md).

  - **Defined `kind` values**:

    | Kind | Meaning | Timing |
    |------|---------|--------|
    | `compactWarning` | Token usage crossed `WarningThreshold` but not yet `ErrorThreshold`. Advisory only, no compaction is attempted. | Synchronous, post-turn (Step 5k), when threshold is above warning but below auto. |
    | `compactError` | Token usage crossed `ErrorThreshold`. Strong advisory; auto-compaction may trigger on the next turn. | Synchronous, post-turn (Step 5k), when threshold is above error but below auto. |
    | `compacting` | Auto-compaction is starting. `percentLeft`/`tokenCount` reflect the projected pre-compaction threshold hint, not a stable active context snapshot. | Synchronous, before the selected compaction backend runs. |
    | `compacted` | Compaction finished successfully. Token tracker has been reset and `percentLeft`/`tokenCount` reflect the post-compaction optimized model-visible state. Auto/reactive compaction may include estimated fixed request overhead for the current sampling request; idle manual compaction must report the compacted replacement estimate without carrying over the previous request's provider overhead. | Synchronous, immediately after the coordinator returns `Micro` or `Partial`. Only `Partial` creates a persisted compaction notice. |
    | `compactSkipped` | Compaction was evaluated but not executed (e.g. below threshold, nothing new to summarize, circuit breaker tripped). | Synchronous, immediately after the coordinator returns `Skipped`. |
    | `compactFailed` | Compaction attempted but failed (backend, validation, transport, or persistence error). The selected backend's circuit breaker may trip after several consecutive failures. | Synchronous, immediately after the coordinator returns `Failed`. |
    | `compactCancelled` | Thread-scoped manual compaction was interrupted by the user. | Asynchronous/thread-scoped, when the maintenance cancellation token is signalled. |
    | `consolidating` | Memory consolidation is starting. Consolidation is driven by Session Core after every configured number of successful Turns, independent from compaction. | Reserved for asynchronous memory-maintenance notifications. |
    | `consolidated` | Memory consolidation completed successfully. MEMORY.md and HISTORY.md have been updated. | Reserved for asynchronous memory-maintenance notifications. |
    | `consolidationSkipped` | Memory consolidation completed without writing MEMORY.md or HISTORY.md (for example, no `save_memory` call or no valid changes). UIs should dismiss any active consolidation status and should not show a success marker. | Asynchronous, after the background consolidation task returns no changes. |
    | `consolidationFailed` | Memory consolidation failed (LLM error, provider error, or persistence failure). UIs should dismiss any active consolidation status. | Asynchronous, after the background consolidation task throws. |
    | `consolidationCancelled` | Memory consolidation was interrupted by the user. UIs should dismiss any active consolidation status. | Asynchronous, after the maintenance cancellation token is signalled. |
    | `streamError` | A provider streaming response disconnected, timed out while idle, or otherwise ended before the sampling request completed; Session Core is retrying the same sampling request. `message` uses the compact form `Reconnecting... x/y`. | Turn-scoped, during agent execution, before the retry delay. |

  - **Emission rules**:
    - System events are emitted during the Turn's post-processing phase (after agent execution completes, before `turn/completed`), except when raised reactively (see below).
    - The threshold advisory events (`compactWarning`, `compactError`) carry `percentLeft`, `tokenCount`, and `contextUsage` when available so UIs can render the same context-pressure value Session Core used for threshold evaluation.
    - `compacting` start events do not carry `contextUsage`; clients must not update context-window UI from their projected `tokenCount` / `percentLeft`. Successful `compacted` events carry `contextUsage` when available. Clients should prefer this full snapshot over `tokenCount` / `percentLeft` because it includes thresholds and source metadata needed to seed context-window UI after manual compaction timeouts or missed thread snapshots.
    - Auto-compaction events (`compacting`, `compacted`, `compactSkipped`, `compactFailed`) are synchronous within Step 5k and always fire in the order `compacting` -> one terminal event (`compacted` / `compactSkipped` / `compactFailed`). The coordinator selects one backend according to [Context Compaction](context-compaction.md). The local backend retains the existing cache-aware micro/partial rules: count-based tool-result clearing is not part of the hot auto-threshold path, and a lightweight clearing pass may run only after a provider-aware idle gap indicates that the relevant prompt cache is cold.
    - Manual compaction uses `ISessionService.CompactThreadAsync(threadId)` and is exposed to AppServer clients as `thread/compact/start`. It is allowed only for Active, server-managed threads with existing history and no `Running` / `WaitingApproval` turn or active thread maintenance. It registers thread maintenance with `maintenanceKind = "compacting"`, emits the same `compacting` -> terminal `system/event` sequence through the thread event broker, and prevents new turns from starting until the terminal event. The selected backend does not run a microcompact pre-pass. A local backend first tries partial compaction and may fall back to full-history compaction. A provider-native backend replaces only its native generation and leaves neutral model history unchanged. On success, Session Core persists the replacement domain, updates context usage, invalidates request-boundary anchors, and appends a `SystemNotice` with `kind = "compacted"` and `trigger = "manual"` to the latest completed turn. On cancellation it emits `compactCancelled` and installs nothing.
    - The pipeline may also be invoked **reactively** from the Turn's error path when the model rejects a request with `prompt_too_long`, `context_length_exceeded`, or another conservatively classified context-overflow equivalent. In that case the Turn still fails, but `compacting` followed by `compacted` / `compactFailed` is emitted first so UIs know the history was repaired before the user retries.
    - Automatic memory consolidation is a non-blocking background task scheduled by Session Core after a configured number of successful Turns and after the terminal rollout commit for that Turn has finished. It is not spawned by the compaction pipeline, and Turn completion is **not** deferred for consolidation. Its start event (`consolidating`) is emitted through the turn-scoped `SessionEventChannel`; its terminal events (`consolidated` / `consolidationSkipped` / `consolidationFailed` / `consolidationCancelled`) are emitted through the thread event broker with `turnId = null`. Automatic consolidation does not register thread maintenance, does not set `maintenanceKind = "consolidating"`, and does not prevent the next user input from starting a Turn immediately. Session Core serializes automatic consolidation per thread; if another automatic trigger arrives while one is active, at most one follow-up attempt is scheduled after the active attempt completes. On `consolidated`, Session Core persists a `SystemNotice` item with `kind = "memoryConsolidated"` into the completed Turn and broadcasts `item/started` + `item/completed` through the thread event broker. Manual consolidation uses `ISessionService.ConsolidateThreadMemoryAsync(threadId)` and is exposed to AppServer clients as `thread/memory/consolidate/start`. It is allowed only for Active, server-managed, idle threads with at least one completed Turn, no active thread maintenance, and non-empty model-visible history; it bypasses `Memory.AutoConsolidateEnabled`, registers thread maintenance with `maintenanceKind = "consolidating"`, emits thread-scoped `consolidating` → terminal `system/event`, awaits the maintenance result, and appends the same persistent notice on success. See [Memory Consolidation](../features/memory-consolidation.md) for the design contract.
    - `ISessionService.CancelThreadMaintenanceAsync(threadId)` interrupts active thread maintenance. AppServer exposes this as `thread/maintenance/interrupt`.
    - Turn-scoped system events are emitted through the turn-scoped `SessionEventChannel`, so they are guaranteed to arrive before `turn/completed`. Thread-scoped maintenance events may arrive later.
    - The protocol is language-neutral. System events carry `messageKey`, optional `params`, and an English `fallbackText`; `message` is a compatibility alias for `fallbackText`. Clients that support UI localization translate `messageKey` locally and fall back to `fallbackText`. User text, model output, and raw tool output remain original text and are not translated by Session Core.
    - Provider stream retry events (`streamError`) are transient and must not create a persistent `SystemNotice`. They are emitted only when the failed sampling attempt has not yet produced a visible item or item delta. Visible output is assistant text, reasoning text, a tool call, or a tool-argument delta. Usage metadata, provider error frames, and `FunctionResultContent` echoed from request input are non-visible updates; the retry layer buffers them before the first visible update and discards the failed attempt's buffer when retrying. Once visible output has been emitted for an attempt, a later stream failure is treated as a normal agent exception so the partial Turn can be preserved without inventing delta rollback semantics. Idle-timeout detection must surface the retry or failure promptly; cleanup of the failed provider stream is best-effort and must not indefinitely delay the retry notification or terminal failure.
  - **Adapters**: Adapters that display session maintenance status (e.g., CLI spinner for consolidation, status text for compaction) should consume `system/event` notifications. Adapters that do not need maintenance status may ignore this event type or opt out via `optOutNotificationMethods`.

#### Local Summary Compaction Contract

These requirements apply to the local summary backend. The backend-neutral orchestration,
provider-native replacement contract, and failure policy are defined in
[Context Compaction](context-compaction.md). Context compaction is a short-term context-window
optimization. It is not long-term memory consolidation and must not attempt to preserve every
historical detail.

- The compact summary is a handoff for the next model-visible history. It should preserve only the current task, key decisions, important files read or changed, critical errors/fixes, constraints, and concrete next steps needed to continue.
- Summary prompts must target about 4,000-6,000 output tokens and must not request an unbounded chronological analysis of every message.
- Summary prompts must not require a separate `<analysis>` drafting block. Implementations may still strip `<analysis>` if a provider returns it, for compatibility with older summaries.
- Summary prompts must not require listing all user messages or embedding full code snippets by default. They may ask for the smallest necessary excerpt only when exact text is required to continue the task.
- Non-cache or legacy compaction requests must use a compact-specific `MaxOutputTokens` budget. The default budget is 12,000 tokens and must not inherit the normal Anthropic 64,000-token turn budget. Snapshot compaction forks must also cap their requested output to the compact-specific budget even when preserving the cache-sensitive input prefix, so a maintenance summary cannot inherit a normal Turn's larger output allowance.
- Cache-sharing snapshot forks and context-usage anchors should keep cache-sensitive request parameters stable when possible, but a snapshot or anchor is usable only while its captured messages remain a prefix of the current canonical model-visible history and its request-shape fingerprint still matches. Any successful history replacement (auto, reactive, or manual compaction; rollback; deletion) invalidates older snapshots and anchors. Maintenance forks should attempt the provider request first so prompt-cache-aware providers can reuse the captured prefix only when the estimated snapshot request fits the maintenance input budget. If the snapshot estimate is over budget, or if the provider rejects the snapshot request with a conservatively classified prompt-too-long / context-overflow error, the fork returns `maintenance_snapshot_too_large` and falls back to a trimmed non-cache path when one exists. An otherwise empty response containing provider error content returns a terminal maintenance-fork response with `maintenance_empty_error_response` and must also fall back to the trimmed non-cache path for compaction. Other provider, authentication, rate-limit, model, or request-shape errors must not be reclassified as context overflow.
- If automatic pre-sampling compaction fails while the original context estimate is still over the blocking limit, Session Core must fail the Turn explicitly with a stable `agent_context_compaction_failed` error instead of continuing to the main provider request. This prevents a too-large context from producing a silent `turn_completed` after a failed maintenance fork.
- Snapshot forks enforce summary length through the prompt and by validating the returned summary. A summary that exceeds the compact-specific hard budget is treated as `compact_summary_too_long` and must fall back to a non-cache path or report `compactFailed`.
- Compaction model-call cancellation, provider/network timeout, and overlong summaries must be observable in trace storage with a terminal maintenance-fork response. Manual compaction maps user cancellation to `compactCancelled`; provider timeout and overlong/invalid summary map to `compactFailed` with machine-readable `message` values.
- A successful neutral history replacement must persist a recovery checkpoint containing the replacement model-visible history and the newest covered Turn. A provider-native replacement instead persists `provider_history_replaced` and leaves neutral model history unchanged. Later recovery and rollback select the newest replacement in the relevant domain whose covered Turn still survives in the canonical Thread.

#### Usage Events

- **`usage/delta`**
  - Emitted each time the agent completes an LLM iteration and a `UsageContent` is received from the streaming response. Carries the **incremental** token consumption for that single iteration.
  - Payload:

    ```
    {
      "inputTokens": long,      // Input tokens consumed in this iteration
      "outputTokens": long,     // Output tokens consumed in this iteration
      "cachedInputTokens": long,
      "cacheWriteInputTokens": long,
      "freshInputTokens": long,
      "reasoningOutputTokens": long,
      "llmCallDelta": long,
      "contextInputTokens": long,
      "turnInputTokens": long,
      "turnOutputTokens": long,
      "turnLlmCalls": long
    }
    ```

  - **Emission rules**:
    - The event is emitted by Session Core immediately after processing a `UsageContent` from the agent's streaming output, provided the token counts are non-zero.
    - Each emission carries only the delta for the current LLM request, not cumulative totals. `turnInputTokens`, `turnOutputTokens`, and `turnLlmCalls` are optional cumulative billing totals emitted for convenience.
    - `contextInputTokens` is the latest main-agent request input snapshot used for context-window occupancy. It is not the sum of Turn input usage.
    - `contextUsage.tokens`, when present, is the active context-window value after processing the same provider usage snapshot. It may be greater than `contextInputTokens` because it includes the current request's output tokens and, when rebased from an anchor, estimated model-visible messages appended after the anchored request.
    - Example: request snapshots `12000 | 20000 | 41000` produce `turnInputTokens = 73000` and `contextInputTokens = 41000`.
    - Cache-hit totals follow the same request-sum rule, so dashboards can show how much of the cumulative input was cache-read input for billing verification.
    - At most one `usage/delta` event is emitted per LLM iteration (the `UsageContent` is emitted once at the end of each iteration by the provider, not per token).
    - The event is a sideband signal — it may interleave with `item/started`, `item/delta`, and `item/completed` events. This is expected behavior.
  - **Relationship to Turn.TokenUsage**: The sum of all `usage/delta` events for a Turn's main agent equals the main-agent portion of `Turn.TokenUsage`. SubAgent tokens are reported separately via `subagent/progress` and are added to `Turn.TokenUsage` at turn completion.
  - **Adapters**: Adapters that display real-time token consumption (e.g., CLI Thinking/Tool spinners) should consume `usage/delta` events to maintain a running total. Adapters that only need final totals may ignore this event type or opt out via `optOutNotificationMethods`.

### 6.4 Event Delivery Semantics

- **Ordering**: Events within a Turn are emitted in causal order. `item/started` always precedes `item/delta` and `item/completed` for the same Item. `turn/started` always precedes all item events for that Turn. `turn/completed` (or `turn/failed`, `turn/cancelled`) is always the last event for a Turn.

- **At-most-once delivery**: Events are not durably queued. If the adapter is not listening (e.g., channel disconnected mid-turn), events are lost. The adapter can reconstruct state from the persisted Thread on reconnection.

- **Decoupled emission**: Session Core writes events into a per-turn in-memory channel and does not synchronously wait for channel rendering. The event stream is authoritative for the active turn, but it is not a durable queue.

- **Single consumer per Turn**: At most one adapter is actively consuming events for a Turn. This is enforced by the Thread invariant (one Running Turn at a time, started by one adapter).

### 6.5 Event Subscription API

Session Core does not expose a standalone `SubscribeToTurn` API. Instead, the turn-scoped event stream is returned directly from `SubmitInputAsync(...)`:

```
IAsyncEnumerable<SessionEvent> SubmitInputAsync(
    string threadId,
    IList<AIContent> content,
    SenderContext? sender = null,
    ...)
```

The `content` parameter accepts multimodal input (text, images, etc.) as a list of `AIContent` parts. When the transport provides native input metadata (for example native command, skill, or file-reference parts), the transport validates and materializes those parts before submission. Session Core persists both the transport-native snapshot and the materialized `AIContent` snapshot on `UserMessagePayload`, derives `UserMessagePayload.Text` from the native snapshot for compatibility/display, and passes the full multimodal materialized content to the agent via `ChatMessage`. A convenience extension method `SubmitInputAsync(string threadId, string text, ...)` wraps plain text into `[new TextContent(text)]` for text-only callers.

`UserMessagePayload.DeliveryMode` is optional and indicates how the user message entered the conversation: `"normal"` (or omitted) for a direct Turn start, `"queued"` for a queued input that later became a Turn, and `"guidance"` for a user request appended to an active Turn.

`UserMessagePayload.QueuedInputId` stores the queue record that produced the message when available. `UserMessagePayload.DeliveryBindingId` stores the App Binding id that should receive the default assistant output for that Turn; direct Desktop-originated input leaves it empty.

```
Task<QueuedTurnInput> EnqueueTurnInputAsync(
    string threadId,
    IList<AIContent> content,
    SenderContext? sender = null,
    CancellationToken ct = default,
    SessionInputSnapshot? inputSnapshot = null)
```

Enqueues user input while another Turn may be running. The queue is persisted as append-only rollout records. On successful Turn completion, Session Core automatically dequeues the first input and invokes `SubmitInputAsync` with `DeliveryMode = "queued"`.

```
Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
    string threadId,
    string queuedInputId,
    CancellationToken ct = default)
```

Removes a queued input without starting a Turn.

```
Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
    string threadId,
    string queuedInputId,
    string expectedTurnId,
    string status,
    CancellationToken ct = default)
```

Sets the referenced queued input's desired status to `"queued"` or `"guidancePending"`. Promotion to `guidancePending` validates that `expectedTurnId` matches the active Turn. Restoring `queued` validates the queued input's bound Turn but does not require that Turn to remain active. Setting the current status again is an idempotent success. The active execution loop drains pending guidance only at safe model/tool boundaries, and the final status recheck, `UserMessage` persistence, and queue removal occur under the same per-thread queue lock used by update and removal operations. If update-to-queued wins the lock, admission is skipped; if admission wins, the queued input no longer exists and the later update fails. If the Turn ends before admission, pending guidance is restored to `queued`.

The adapter starts a turn and immediately consumes the returned async stream. Callback-style consumption is a helper-layer concern (for example, wrapping the stream in a local event handler), not part of the `ISessionService` contract.

## 7. Channel Adapter Contract

### 7.1 Role

A channel adapter is the boundary between a transport and Session Core. It is responsible for:

- turning inbound user actions into `ISessionService` calls
- turning `SessionEvent` output into channel-specific UX
- routing approval decisions back into the active turn
- exposing thread discovery and resume in whatever UX the channel supports

The adapter is not a new public framework interface. It is an internal part of a channel's existing `IChannelService`.

### 7.2 Contract

The normative contract is intentionally small:

- `CreateThread` / `ResumeThread` / `FindThreads` define thread lifecycle at the transport boundary.
- `GetThread` returns persisted thread state and may load the thread into the in-process cache **without** rebuilding execution resources (e.g. per-thread MCP connections).
- `EnsureThreadLoaded` (or an equivalent internal step before turn execution) loads the thread like `GetThread` and, when `Thread.Configuration` is non-null, ensures the effective agent for that thread matches the persisted configuration. It does **not** change thread status or emit `thread/resumed`. Session Core uses this on turn execution paths when the thread may exist only on disk or was cached without agent hydration (e.g. after host restart).
- `SubmitInput` starts a turn and returns the authoritative event stream for that turn. Before running the agent, Session Core must ensure per-thread configuration (mode, MCP, etc.) has been applied when `Configuration` is present—same outcome as loading via `ResumeThread` from disk.
- `ResolveApproval` and `CancelTurn` let the adapter participate in interactive control flow.
- `SetThreadMode` and `UpdateThreadConfiguration` support per-thread behavior where a channel exposes it.
- `ExportThreadRecovery` and `RestoreThreadRecovery` expose the restricted JSON recovery lifecycle to trusted adapters.

### 7.3 Design Constraints

- Adapters may choose different UX patterns (streaming text, buffered messages, structured UI, non-interactive execution).
- Adapters must not own persistence, agent lifecycle, or hook orchestration.

## 8. Agent Execution Integration

### 8.1 Principle

Session Core wraps the existing agent pipeline rather than redefining it. Agent creation, tool invocation, tracing, and middleware remain existing responsibilities; Session Core standardizes how their output is represented as Threads, Turns, Items, and `SessionEvent`s.

### 8.2 Normative Behavior

For each submitted turn, Session Core must:

- validate thread state and mutual exclusion
- create the Turn and its initial user input item
- execute the agent against a runtime session hydrated from persisted rollout history
- translate agent output into typed items and events
- collect token usage and other turn-level metadata
- persist updated domain and model history to rollout
- emit terminal completion or failure events

### 8.3 Compatibility Boundary

- Session Core owns orchestration.
- Adapters own presentation.
- `AgentRunner` may remain as a compatibility entry point, but it is no longer a separate session model.

## 9. Persistence Specification

### 9.1 Storage Layout

Thread data is stored under the workspace's `.craft/` directory:

```
.craft/
├── threads/
│   ├── active/
│   │   ├── {threadId}.jsonl     # Canonical rollout history when threadId is a safe file name
│   │   ├── thread-{sha256(threadId)}.jsonl # Canonical name when threadId contains invalid file-name characters
│   │   └── ...
│   ├── archived/
│   │   ├── {threadId}.jsonl     # Canonical rollout history when threadId is a safe file name
│   │   ├── thread-{sha256(threadId)}.jsonl # Canonical name when threadId contains invalid file-name characters
│   │   └── ...
├── state.db                     # Classified SQLite state, projections, diagnostics, usage
├── attachments/images/          # Workspace-managed local image blobs referenced by thread history
├── cache/                       # Rebuildable cache files; not part of thread lifecycle
```

### 9.2 Thread File Format

Each thread is stored as an append-only JSONL rollout. Every line is a `ThreadRolloutRecord` describing one state transition:

```json
{ "kind": "thread_opened", "timestamp": "2026-03-15T10:00:00Z", "threadOpened": { ... } }
{ "kind": "turn_started", "timestamp": "2026-03-15T10:00:01Z", "turnStarted": { ... } }
{ "kind": "item_appended", "timestamp": "2026-03-15T10:00:01Z", "itemAppended": { ... } }
{ "kind": "model_history_messages_appended", "timestamp": "2026-03-15T10:00:02Z", "modelHistoryMessagesAppended": { "turnId": "...", "messages": [ ... ] } }
{ "kind": "provider_history_items_appended", "timestamp": "2026-03-15T10:00:03Z", "providerHistoryItemsAppended": { "schemaVersion": 1, "protocol": "openai-responses", "turnId": "...", "entries": [ ... ] } }
{ "kind": "provider_history_replaced", "timestamp": "2026-03-15T10:00:04Z", "providerHistoryReplaced": { "schemaVersion": 1, "protocol": "openai-responses", "coveredThroughTurnId": "...", "entries": [ ... ] } }
{ "kind": "provider_history_attempt_aborted", "timestamp": "2026-03-15T10:00:05Z", "providerHistoryAttemptAborted": { "schemaVersion": 1, "protocol": "openai-responses", "turnId": "...", "attemptId": "..." } }
{ "kind": "turn_state_replaced", "timestamp": "2026-03-15T10:02:30Z", "turnStateReplaced": { ... } }
{ "kind": "turn_completed", "timestamp": "2026-03-15T10:02:30Z", "turnCompleted": { ... } }
{ "kind": "queued_input_added", "timestamp": "2026-03-15T10:02:31Z", "queuedInputAdded": { ... } }
{ "kind": "queued_input_removed", "timestamp": "2026-03-15T10:02:32Z", "queuedInputRemoved": { ... } }
{ "kind": "queued_input_reordered", "timestamp": "2026-03-15T10:02:33Z", "queuedInputReordered": { ... } }
```

Session Core reconstructs a `SessionThread` by replaying the rollout file in order. Incremental `turn_started`, `item_appended`, and `turn_completed` records remain readable. The normal terminal write path commits a complete Turn mutation as one `turn_state_replaced` record. Replay replaces the same Turn id atomically, including its ordered Items, terminal state, input, token usage, and error. A Turn writes at most one terminal replacement; streaming deltas and other transient lifecycle updates are not rollout records.

`thread_opened.source` is a DotCraft-owned versioned union rather than the serialized CLR shape of the public `ThreadSource` type. New records use `schemaVersion = 1`. The user variant stores `kind` and optional `spawnedFromThreadId`. The subagent variant additionally stores parent thread id, root thread id, parent turn id, parent call id, depth, path, task, nickname, role, profile, runtime, and all send, resume, message, follow-up, and close capabilities. A source change is a baseline change and appends a new `thread_opened` record. Replay uses the latest baseline.

Every current rollout begins with a canonical `thread_opened` record containing the versioned source union. An unknown source schema version or a structurally invalid canonical source is a thread identity safety error and stops thread replay; it must not be downgraded to an ordinary user thread.

### 9.3 Model-History Storage and Runtime Execution

The rollout is the sole authority for both the Session Protocol domain model and optimized model-visible history. Session Core replays the rollout into DotCraft-owned model-history messages, converts them to a request-local `List<ChatMessage>`, and passes that list explicitly to `ChatClientAgent` for execution. The request-local agent object and its middleware pipeline are not persistence records.

A successful context replacement has one recoverable rollout commit boundary. The authoritative
compaction checkpoint and replacement model history are committed before derived context-usage,
provider-history, context-window, or cache projections are published. Recovery replays the
checkpoint and repairs or replaces those derived projections; it must never expose a new provider
window with pre-compaction model history, nor compacted model history with an unrelated provider
prefix. In-memory history and UI notifications become observable only after the authoritative
commit succeeds.

Per-thread plans are stored in SQLite `thread_plans`. Plans follow the thread lifecycle: archive/unarchive keeps them, and permanent thread deletion cascades through the database.

Workspace-managed local images referenced by persisted `localImage` input parts are indexed in SQLite `thread_attachments`. The image file is an independent asset: rollout stores its reference but cannot reconstruct its bytes. The file remains available for history rendering while at least one active or archived thread references it. Permanent thread deletion removes that thread's references and best-effort deletes now-unreferenced managed files. Unsent draft images that never enter thread history are cleaned as unreferenced attachments after the configured TTL.

#### 9.3.0 Thread Artifact Storage and Maintenance

The current artifact layout is workspace-relative and thread-owned:

```text
.craft/
├── terminals/{canonicalThreadDirectory}/
│   ├── {terminalSessionId}.json   # terminal metadata
│   └── {terminalSessionId}.log    # complete terminal output
└── tool-results/{canonicalThreadDirectory}/
    └── {stableToolCallArtifact}.txt
```

The canonical thread directory name is the same for all thread-owned artifact stores: a safe current Thread ID may remain readable; an ID containing invalid filename characters uses the current `thread-{sha256(threadId)}` form. Runtime code must resolve the final path under the workspace's `.craft/terminals` or `.craft/tool-results` root and must not read or migrate older replacement-based sanitized directories.

Artifact rules:

- A terminal output log is complete persistent output. UI/API previews may impose independent character, line, or width bounds and must not be interpreted as permission to truncate or delete the log.
- A tool-result spill filename is stable for the originating tool call (prefer the persisted tool call ID; otherwise use a deterministic current-protocol artifact key). Reprocessing or replaying the same call must be idempotent: an existing matching file is a valid result and must not create a second random spill file or overwrite a different call's file.
- A spill file is referenced from a bounded model-visible preview. The full file is retained through archive and compaction unless permanent deletion or a retention janitor can prove that the owning thread and all current rollout references are gone.
- The terminal `OutputRetentionDays` setting governs completed and lost terminal artifact retention. Running terminals are never removed by TTL cleanup. Tool-result retention may share the workspace retention policy, but cleanup must be conservative when reference status cannot be established.
- Startup and long-lived-process maintenance may run a throttled janitor. It must use a workspace/process lock or equivalent coordination, avoid deleting active owners, continue after individual failures, and report a retryable maintenance result. A marker may suppress duplicate scans but must not suppress a later retry after an incomplete scan.
- Thread deletion is the authoritative synchronous cleanup path; janitor cleanup is best effort and must not be required to make a deleted Thread unavailable. If database deletion succeeds while filesystem cleanup fails, the failure is observable and the orphan remains eligible for a later janitor retry.
- Normal process shutdown is not an artifact retention boundary. Shutdown cleanup handles active processes and open streams; it does not remove persistent terminal logs or tool-result spill files.

`model_history_messages_appended` atomically stores one Turn-local ordered batch of versioned `ModelHistoryMessage` values. Each message carries `schemaVersion`, `turnId`, role, optional message identity/author/timestamp, and ordered contents. DotCraft's `kind` field is the content discriminator. The storage contract is independent of runtime CLR type envelopes and serializer-specific property shapes. The codec encodes MEAI semantic fields into DotCraft-owned DTOs and explicitly constructs new MEAI values when decoding.

Every model-history content value has the shape `{ kind, payload }`. The version 1 payload union is owned by DotCraft and contains only the following durable fields, plus content-level `additionalProperties`:

| Kind | Durable fields |
|---|---|
| `text` | `text` |
| `reasoning` | `text`, `protectedData` |
| `data` | `base64Data`, `mediaType`, `name` |
| `function_call` | `callId`, `name`, `arguments`, `informationalOnly`, `namespace`, `providerFlatName` |
| `function_result` | `callId`, versioned result union |
| `hosted_image_generation` | `id`, `status`, `revisedPrompt`, `imageBase64`, `mediaType`, `errorMessage` |
| `image_generation_tool_call` | `callId` |
| `image_generation_tool_result` | `callId`, ordered `outputs` |
| `error` | `message`, `errorCode`, `details` |
| `uri` | `uri`, `mediaType` |
| `usage` | standard token counts and `additionalCounts` |

Function results use a versioned union. A `json` result stores any JSON-compatible scalar, object, array, or null. A `contents` result recursively stores an ordered list from the same DotCraft-owned content union. Image-generation outputs use the same recursive union. Binary data and hosted image bytes are stored once as base64; derived data URIs are not duplicated.

Function-call namespace and provider-flat-name values are persisted as strong fields even when the same values also occur in `AdditionalProperties`. On decode, the strong fields restore the standard runtime metadata keys. A conflict between a strong field and its extension value makes that content invalid; the model-history replayer rejects the containing record and applies its normal whole-Turn fallback rather than choosing one value silently.

Message-level and content-level `AdditionalProperties` are persisted in full using JSON semantics. Arbitrary nested objects, arrays, numbers, booleans, strings, and nulls must round-trip as JSON; runtime CLR object identity is not a persistence guarantee and complex values may hydrate as `JsonElement`. `RawRepresentation` and runtime exception objects on function calls or results are never persisted. Core content fields remain strongly typed even when equivalent provider metadata also exists in `AdditionalProperties`.

Streaming tool-call argument deltas are transient presentation state. They are removed when model history is encoded and never appear in a rollout model-history record. Any other unsupported runtime content fails the write explicitly; the writer must not silently omit an unknown semantic content type. On read, an unknown kind, malformed owned payload, or metadata conflict follows the tolerant replay contract in section 9.3.2.

Session Core manages the mapping:

- **Append**: Every successful mutation of server-managed model history appends the corresponding model-history records to the same thread rollout before the mutation is considered durable.
- **Load**: Resume materializes the request-local MEAI history produced by rollout replay and supplies it directly to `ChatClientAgent`.
- **Fork**: A persistent fork writes its materialized model history into the fork rollout. It does not create a separate runtime-session blob.

The domain history and exact model history are intentionally distinct durable representations. `turn_state_replaced` preserves client-visible Turn and Item lifecycle state. `model_history_messages_appended` preserves the exact provider-facing message sequence. Neither representation is required to losslessly derive the other, and arbitrary model metadata must not be copied into client-visible Items merely to remove duplicate text.

Current rollouts require `thread_opened.providerHistorySchemaVersion = 1` and may additionally
persist a protocol-native history. For OpenAI Responses, that history is the source of the future wire
`input` array while generic model history remains the Session-owned cross-provider
representation. Its append, replacement, retry, rollback, fork, privacy, and compatibility
contracts are defined in
[Canonical OpenAI Responses Provider History](responses-provider-history.md).

`context_compacted` is an internal model-history replacement record, not a Session Item and not a client event. Its replacement history uses the same versioned DotCraft model-history schema. During replay, the newest checkpoint whose covered turn survives establishes a new history baseline; earlier model-history records are discarded and only later surviving records are appended. Rollback that removes the covered turn invalidates that checkpoint.

### 9.3.1 Ordered Rollout Writer

All records for a thread pass through one workspace-lifetime recorder keyed by thread id. Each recorder has one bounded queue with capacity 256 and owns its pending suffix and any currently open append stream. Enqueue order is append order. A normal terminal Turn mutation enqueues the Turn replacement, any compaction replacement, and its model-history suffix as one batch, then crosses one flush barrier. The file handle may be released after a barrier so standard filesystem readers can inspect the rollout concurrently; recorder lifetime is independent from file-handle lifetime.

A flush receipt identifies the confirmed byte offset and serialized record sizes. SQLite projections advance only after that barrier succeeds. Normal shutdown flushes and closes every recorder. Writers for different threads may operate concurrently, but one thread has exactly one active append order.

Records leave the pending suffix only after their complete JSONL lines have been written. On I/O failure the recorder drops the stream, retains the unwritten suffix, reopens the file, and retries without rewriting confirmed complete lines. If a failed write leaves a non-newline-terminated tail, reopen terminates that invalid tail before retrying the unconfirmed record so forward readers can skip the damaged line. Exhausted retries block later writes for that thread and fail the affected Turn with a persistence error; a later flush or shutdown may retry the retained suffix.

### 9.3.2 Bounded Model-History Replay

A shared, read-only replayer is the canonical selector for runtime hydration and current-context export. It receives a rollout path, ordered surviving Turns, an optional excluded current Turn, and the expected Thread id. The rollout path must resolve under the workspace's `.craft/threads/{active,archived}` roots, and the canonical `thread_opened` identity plus every model-history/checkpoint payload must match the expected Thread id. For ordinary uncompressed JSONL, it scans backward in bounded blocks. It accumulates the surviving suffix and stops at the newest decodable `context_compacted` record whose covered Turn still survives. The reconstructed history is the checkpoint replacement followed by later model-history messages in physical rollout order, including later messages associated with the covered Turn itself.

Ordinary malformed JSONL lines, rollout records, model batches, and model contents are rejected individually. Replay skips them, increments a rejected-record count, emits a structured warning without protected or extension payloads, and continues recovering other Turns. If a rejected record can be associated with a Turn, that Turn is marked exact-history-incomplete and its entire model-visible contribution is rebuilt from Session Items; valid exact batches from the same Turn must not be mixed with that fallback. Other Turns continue using exact history.

An invalid checkpoint is discarded as a whole and scanning continues to an earlier checkpoint. If no usable checkpoint exists, the same reverse scan reaches the beginning and recovers every decodable surviving record; it must not first materialize a second complete domain thread. A surviving tail Turn with no exact records also uses the lossy SessionItem projection. Canonical `thread_opened` source-schema errors remain fail-fast because continuing would change thread identity or authority.

The replay result contains ordered messages, warnings, rejected-record count, fallback Turn ids, bytes read, and decoded-record count. Rollout observability records rejected resume records in `dotcraft.rollout.resume.records.rejected` without thread ids or payload data. Full forward domain replay remains available to execution and lifecycle operations that require complete canonical state. UI history reads use the paged projection in section 9.4.2, while current-context export consumes the shared model replay result and applies its own presentation redaction.

### 9.3.3 Rollout Observability and Search Safety

Rollout diagnostics measure record count and serialized bytes by record kind, flush duration and outcome, and resume bytes, decoded-record count, and rejected-record count. Metric dimensions must remain low-cardinality and must not contain thread ids, message contents, `ProtectedData`, or arbitrary `AdditionalProperties`.

Context search treats exact model-history, provider-history, and compaction replacement records as internal recovery state. It must not index or preview `model_history_messages_appended`, `provider_history_items_appended`, `provider_history_replaced`, `provider_history_attempt_aborted`, `context_compacted`, `ProtectedData`, arbitrary `AdditionalProperties`, system prompts, raw trace event JSON, channel context, or tool arguments/results. Searchable rollout evidence is extracted from explicitly displayable domain fields rather than raw JSONL lines, and a domain value duplicated in model history contributes only one rollout match. Tool arguments and result payloads are omitted or replaced with `[redacted]` at the presentation boundary.

Diagnostic readers apply the same domain replay semantics as Session Core: later `turn_state_replaced` records replace the same Turn, rollback removes the visible tail, and only surviving terminal Items contribute errors and tool summaries. Exact model batches may expose counts, Turn ids, schema versions, content kinds, and rejection status, but never model payloads, `ProtectedData`, or extension properties. Compaction diagnostics expose only checkpoint boundaries and decode status.

Current-context handoff uses the canonical shared model-history replayer. Its Markdown presentation excludes reasoning, `ProtectedData`, `AdditionalProperties`, internal provider metadata, and the free-form `SessionThread.Metadata` dictionary. Thread metadata may contain channel credentials, external CLI session identifiers, private paths, or future extension values and therefore is not eligible for blacklist-based redaction. Export uses an explicit allowlist of strong thread fields such as display name, status, timestamps, origin channel, history mode, and Turn count, and propagates sanitized replay warnings through the existing export warning surface.

### 9.4 Thread Discovery

Thread discovery uses a filesystem-first read-repair pass followed by the SQLite `threads` metadata projection. Rollout files remain the canonical conversation history, while `ThreadSummary` rows are derived metadata used by `FindThreadsAsync`.

Each metadata row records the confirmed rollout byte offset from the flush that produced it. Discovery enumerates active and archived rollout files and compares their paths and lengths with SQLite. Matching rows require no rollout-body read. A missing row, changed path, shortened file, or mismatched offset causes only that rollout to be replayed and its projection rebuilt before the list is returned. Concurrent initial discovery shares one reconciliation operation.

If SQLite listing or repair is unavailable, discovery reports the metadata read failure; it does not infer or migrate non-canonical rollout paths. Rows whose current canonical rollout files are absent are omitted but are not destructively deleted by read-repair. SQLite projections never participate in model-history reconstruction.

### 9.4.1 Persistence Authority Classification

| Category | Data | Recovery contract |
|---|---|---|
| Rollout session authority | Thread baseline and source, Turns, Items, queued inputs, model history, compaction | Replayed from the thread rollout |
| SQLite durable business authority | Goals, plans, spawn edges, subagent mailbox | Not rebuilt by rollout read-repair |
| SQLite runtime continuity state | Context windows | May explicitly degrade to a new transient window when unavailable |
| SQLite disposable cache and sidecar | Context usage, item widget state | May be discarded and recomputed or restarted |
| SQLite rebuildable projection | Threads, confirmed rollout offset, attachment references, thread read snapshots, Turns, Items | Rebuilt from readable rollout records |
| Diagnostics and accounting | Traces, token usage, dashboard usage | Independent diagnostic retention rules apply |
| Independent file assets | Attachment file bytes | Rollout preserves references only |

Filesystem read-repair updates only thread metadata and attachment references. It must not clear or overwrite durable business authority, runtime continuity, diagnostics, or unrelated sidecar tables. Loss of `state.db` is partial data loss: conversation state remains recoverable from rollout, while goals, plans, graph edges, and mailbox state are not recovered.

A projection repair or terminal Turn commit updates thread metadata, attachment references, and their confirmed rollout offsets only after the corresponding rollout flush succeeds. Each projection advances its own offset in the transaction that publishes its complete derived state. Attachment projection uncertainty disables attachment garbage collection until a later successful repair; retaining an orphan is preferable to deleting a referenced asset.

`ThreadSummary` fields returned for each discovered thread:
- `Id`, `Status`, `OriginChannel`, `ChannelContext`
- `UserId`, `WorkspacePath`, `DisplayName`
- `CreatedAt`, `LastActiveAt`
- `TurnCount`

### 9.4.2 Paged Thread History Projection

Persisted Thread display history is queried from a rebuildable SQLite projection. AppServer reads must not materialize a complete `SessionThread` or replay a complete rollout merely to return a Thread header, a Turn page, or an Item page. Execution, resume, rollback, compaction, and fork materialization continue to use canonical rollout replay when they require complete domain or model-visible history.

The projection consists of three tables:

```sql
thread_history_projection_state (
    thread_id                TEXT PRIMARY KEY,
    rollout_path             TEXT NOT NULL,
    projected_rollout_offset INTEGER NOT NULL,
    next_rollout_ordinal     INTEGER NOT NULL,
    thread_snapshot_json     TEXT NOT NULL,
    persisted_runtime_json   TEXT NOT NULL,
    FOREIGN KEY (thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
)

thread_turns (
    thread_id                TEXT NOT NULL,
    turn_id                  TEXT NOT NULL,
    rollout_ordinal          INTEGER NOT NULL,
    turn_json                TEXT NOT NULL,
    PRIMARY KEY (thread_id, turn_id),
    FOREIGN KEY (thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
)

thread_items (
    thread_id                TEXT NOT NULL,
    turn_id                  TEXT NOT NULL,
    item_id                  TEXT NOT NULL,
    rollout_ordinal          INTEGER NOT NULL,
    updated_rollout_ordinal  INTEGER NOT NULL,
    item_json                TEXT NOT NULL,
    PRIMARY KEY (thread_id, turn_id, item_id),
    FOREIGN KEY (thread_id, turn_id)
        REFERENCES thread_turns(thread_id, turn_id) ON DELETE CASCADE
)
```

All three tables reference the owning `threads` row directly or transitively with `ON DELETE CASCADE`. Turn paging uses a `(thread_id, rollout_ordinal)` index. Item paging uses `(thread_id, rollout_ordinal)` and `(thread_id, turn_id, rollout_ordinal)` indexes.

`thread_snapshot_json` is the provider-neutral `SessionThread` state with an empty `Turns` collection. It includes configuration, queued inputs, source, worktree, metadata, and other current Thread fields required by read-only clients. `persisted_runtime_json` is the runtime summary derived from canonical domain state at the same projection boundary; process-local maintenance state is overlaid at read time. `turn_json` stores a `SessionTurn` without Items. `item_json` stores one complete provider-neutral `SessionItem`. These JSON values are internal projection encodings, not new persistence authorities or framework session blobs.

Projection ordinals are monotonically allocated per rollout. A Turn or Item keeps the ordinal assigned when it first becomes visible. Updating the same Item advances `updated_rollout_ordinal` without changing its paging position. Rollback removes the visible Turn tail and its Items but does not lower `next_rollout_ordinal`; later Turns therefore cannot reuse positions removed by rollback.

The per-thread rollout writer and `ThreadRolloutWriteGate` define projection order:

1. Append and flush the canonical rollout batch.
2. Obtain the confirmed byte offset.
3. Update the affected snapshot, Turn rows, and Item rows in one SQLite transaction.
4. Advance `projected_rollout_offset` and `next_rollout_ordinal` last in that transaction.

An SQLite failure after a confirmed rollout write leaves the prior projection checkpoint intact. It must not make the canonical mutation fail retroactively or advance a partial projection. Writers for different Threads may use SQLite WAL concurrently; Session Core does not add a workspace-global history writer.

Projection applies domain records as follows:

- Thread baseline, name, status, configuration, and queued-input records update the Thread snapshot.
- `turn_started` and `turn_completed` insert or update Turn metadata.
- `item_appended` inserts or replaces the stable Item id while retaining its original paging ordinal.
- `turn_state_replaced` atomically replaces the Turn metadata and exact Item set; Items absent from the replacement are deleted.
- `thread_rolled_back` removes the selected visible Turn tail and cascades its Items.
- Model-history, compaction, and provider-history records do not contribute display rows. The projector reads only their outer record envelope and advances the confirmed checkpoint without decoding their internal payloads.

Persistent forks write and project their own copied rollout; they never share history rows or lineage with the source. Archive and unarchive update the projected rollout path without rewriting history rows. Permanent deletion cascades all history projection rows. Ephemeral Threads have no SQLite owner and do not support paged history queries.

Projection state is created and repaired on demand rather than through a workspace-wide startup scan. A missing state row causes a streaming rebuild from the canonical rollout. A valid checkpoint behind the current file length scans only the complete suffix. A changed path, shortened file, invalid record boundary, or invalid projected JSON clears that Thread's projection and performs a streaming rebuild. Repair runs under the same per-thread gate, publishes one complete SQLite transaction, and retries the requested query after success.

The projector parses one JSONL line at a time and decodes only domain payloads. It must not deserialize, store, search, log, or expose the item JSON inside `provider_history_items_appended`, `provider_history_replaced`, or `provider_history_attempt_aborted`. Malformed provider-history state remains isolated to provider-history recovery and must not make provider-neutral Thread, Turn, or Item pages unavailable. Canonical identity errors and unrecoverable domain projection errors fail the query with stable code `ThreadHistoryUnavailable`.

There is no full-rollout response fallback. If SQLite query or repair fails, the caller receives `ThreadHistoryUnavailable`; Session Core must not replay JSONL into a complete response. Removing `state.db` or the history tables is supported because the next successful paged read rebuilds the projection. Existing `thread_sessions` tables remain ignored: runtime code does not read, update, migrate, or delete them.

Session Core exposes this projection through read-only query DTOs rather than returning mutable execution aggregates:

- `ReadThreadSnapshotAsync(threadId)` returns the provider-neutral Thread header and persisted runtime, with process-local maintenance state overlaid by the caller.
- `ListThreadTurnsAsync(threadId, cursor, limit, direction)` returns one ordered Turn metadata page without Items.
- `ListThreadItemsAsync(threadId, optionalTurnId, cursor, limit, direction)` returns one ordered Item page with its owning Turn id.

`GetThreadAsync` remains the internal lifecycle path for execution, resume, rollback, compaction, and fork operations that genuinely require complete canonical state. AppServer display-history reads must not call it.

History projection telemetry contains no Thread content, provider payload, or raw identifier. It records projection hits, incremental repairs, full rebuilds, repair failures and reason categories, page-query duration, returned row count, repair bytes read, repair record count, and `ThreadHistoryUnavailable` counts. Performance acceptance uses a fixture with 10,000 Turns and 100,000 Items: healthy first and last pages must equal canonical replay, must not open the rollout file, and must allocate `O(page size + largest single rollout record)` server memory rather than memory proportional to total history.

### 9.5 Cross-Channel Resume Protocol

#### Default discovery (no `crossChannelOrigins`)

`FindThreadsAsync(identity, includeArchived, crossChannelOrigins: null)` uses identity-scoped discovery by default and matches threads by three fields. `ChannelName` on the identity is **not** used as a filter:

| Field | Behavior |
|---|---|
| `WorkspacePath` | Required exact match (case-insensitive) |
| `UserId` | Matched if non-null in identity; null identity field skips this filter |
| `ChannelContext` | `null` identity matches only threads with `ChannelContext = null`; non-null matches exactly |

This means cross-channel discovery is **natural for channels that share the same identity shape**:

- **CLI and ACP** both use `UserId = "local"` and `ChannelContext = null`. They discover each other's threads automatically. A thread created in CLI appears in ACP's session list, and vice versa. This is by design — both are local, single-user channels on the same machine.
- **QQ, WeCom, and Feishu** use social conversation identities. Private chats use a per-user identity; group chats use a group/chat identity (`UserId = "group:{id}"` or `"chat:{chatId}"`, with matching `ChannelContext`). Individual senders are recorded per turn through `SenderContext`. CLI and ACP cannot see social-channel threads and vice versa unless an opt-in cross-channel origin is supplied.

#### Opt-in cross-context discovery (`crossChannelOrigins`)

`FindThreadsAsync` accepts an optional fourth parameter: `crossChannelOrigins` (`IReadOnlyList<string>?`, default `null`).

- When `crossChannelOrigins` is **null** or **empty**, behavior is identical to the default discovery above (no extra threads).
- When **non-empty**, the result set is the union of:
  1. Threads that satisfy the default identity predicate (`WorkspacePath` + `UserId` + `ChannelContext` as in the table above), **and**
  2. Threads that match `WorkspacePath` (case-insensitive) + `OriginChannel` contained in `crossChannelOrigins` (case-insensitive string match), **ignoring `ChannelContext`**. This branch does **not** require `UserId` to match the request identity, so channels that use per-job or per-session synthetic user IDs (e.g. `cron:{jobId}`) still appear when the user opts in to that origin.

The union is deduplicated by thread ID and ordered by `LastActiveAt` descending.

This opt-in path exists so clients such as **DotCraft Desktop** (which uses a non-null `ChannelContext` such as `workspace:{path}`) can still list threads created by channels with a different context (e.g. CLI with `ChannelContext = null`) when the user explicitly allows those origin channels.

#### Workspace-scoped discovery

Trusted workspace-owner clients may request workspace-scoped discovery. This mode still requires an exact case-insensitive `WorkspacePath` match, but does not filter by `UserId`, `ChannelContext`, or `OriginChannel`. `crossChannelOrigins` is redundant and ignored in this mode.

Workspace-scoped discovery does not weaken the default identity scope. Clients and adapters that omit the scope continue to use the identity rules above. Archived, sub-agent, internal-thread, query, channel-name, and pagination controls remain independent filters. DotCraft Desktop uses workspace scope so every non-internal thread in the current workspace is discoverable, including cron, heartbeat, App Binding origins, and previously unknown external origins.

#### Resume flow

1. **Discovery**: Adapter calls `FindThreadsAsync(identity, includeArchived, crossChannelOrigins)`. Returns threads matching the combined predicate when `crossChannelOrigins` is set, otherwise the default predicate only.
2. **Selection**: The adapter presents the list to the user, or auto-selects the most recently active thread.
3. **Resume**: Adapter calls `ResumeThreadAsync(threadId)`. Session Core sets status to `Active` and updates `LastActiveAt`.
4. **Session Load**: Session Core replays model history from the rollout and materializes the request-local `List<ChatMessage>` supplied to `ChatClientAgent`.
5. **Ready**: Adapter calls `SubmitInputAsync` to start a new Turn. The Turn's `OriginChannel` records the resuming channel's name.

### 9.6 Legacy Compatibility Policy

Session Core does not implement compatibility paths for older snapshot layouts such as `.craft/sessions/{key}.json`, `.craft/threads/{threadId}.json`, or `.craft/threads/{threadId}.session.json`.

The supported persistence contract is:

- Canonical thread history in `.craft/threads/active|archived/*.jsonl`
- Classified durable state, diagnostics, and rebuildable projections in `.craft/state.db`

Dashboard trace-session deletion follows the same persistence contract. Deleting one trace session removes that session's trace rows and associated dashboard usage rows; if the session is bound to a thread, deletion cascades through permanent thread deletion. Clearing all trace sessions deletes the selected trace/thread state and associated usage rows, but preserves global usage rows that have no `thread_id` or `session_key`. Bulk trace clearing may run SQLite maintenance (`wal_checkpoint(TRUNCATE)` and conditional `VACUUM`) after deletion to reclaim WAL/free-page space.

Dashboard trace event reads are paged from the durable trace store. The first page returns at most the newest 1000 events for the selected session or all sessions; clients fetch older events with an opaque `beforeCursor` when the user scrolls upward. Maintenance envelope events are filterable as maintenance events and are counted separately from normal LLM request/response totals, while detailed collector events and token usage remain in the same trace session for correlation.

Dashboard may also project read-only thread operations from canonical thread JSONL. Rollback visibility is derived from `thread_rolled_back` records and exposed as operation metadata (`type = rollback`, `threadId`, timestamp, removed Turn count, and source). Hidden recovery records such as compaction checkpoints remain internal and must not be exposed through Dashboard operation APIs or trace views.

### 9.7 Persistence Failure Handling

- **Save failure**: The writer retains the unconfirmed suffix and retries transient failures. If durability cannot be established, the affected Turn ends with a stable persistence error; Session Core must not report a successfully durable terminal Turn or silently discard model history.
- **Load failure**: Ordinary malformed history records are skipped with structured warnings. If Session Core cannot read the file or cannot safely interpret the canonical thread header or source schema, it returns an error to the adapter. The adapter should inform the user and offer to create a new Thread.
- **Discovery failure**: If a thread file is unreadable during `FindThreadsAsync`, it is skipped with diagnostic evidence. SQLite failure falls back to readable filesystem rollouts, and one corrupt file does not prevent other threads from being discovered.

## 10. Approval Flow Integration

### 10.1 Principle

Approvals are part of the turn model, not an out-of-band concern owned by individual channels.

### 10.2 Normative Behavior

When a tool execution requires approval, Session Core must:

- emit an approval request event tied to the active turn
- pause the affected execution path until resolution or timeout
- record the approval outcome in the turn history
- resume or reject the operation accordingly

The adapter is responsible only for presenting the request and returning the decision.

### 10.3 Constraints

- Approval UX remains channel-specific.
- Non-interactive server-managed channels may auto-approve.

### 10.4 Model-Initiated User Input

`RequestUserInput` is a model tool that lets the agent ask one to three short structured questions before continuing. It is exposed only to main user threads, not SubAgents, and remains schema-stable across Agent and Plan modes.

When the tool is invoked, Session Core must:

- finalize and persist any assistant or reasoning content emitted before the tool call
- emit a `UserInputRequest` Item tied to the active turn
- pause the affected execution path until resolution, turn cancellation, or transport/unavailable-client fallback
- record the returned `UserInputResponse` Item
- resume execution with the answer payload returned to the tool

Interactive adapters present the request in their native UI. Non-interactive or unsupported adapters resolve the request with an empty answer object so the turn can continue.

## 11. Implementation Status

### 11.1 Adopted Scope

The Session Protocol is now the active execution path for all **server-managed** channels:

- CLI
- ACP
- QQ
- WeCom

These channels create threads, submit turns through `ISessionService`, consume `SessionEvent`s, and persist canonical state via rollout files with rebuildable projections in `.craft/state.db`.

### 11.2 Cross-Channel Resume Status

Cross-channel resume works for channels that share the same identity shape:

- **CLI ↔ ACP** share `UserId = "local"` and `ChannelContext = null`, so they naturally share one thread pool.
- **QQ**, **WeCom**, and **Feishu** remain isolated by social conversation `ChannelContext`, while multiple users in the same group/chat share that conversation's thread.

### 11.3 Artifact Maintenance Status

The current implementation exposes explicit terminal artifact operations for stop-only thread cleanup, permanent thread-directory deletion, and retention cleanup. Construction of the background terminal service performs a best-effort startup retention pass using `Tools.Shell.Background.OutputRetentionDays`; normal service disposal remains stop-only. The current host registration does not yet provide a separate long-lived periodic janitor for tool-result spill directories, so tool-result permanent deletion and any future TTL/orphan scan must remain explicit maintenance work rather than an assumed background service behavior.

The lifecycle contract in Section 5.1.1 and storage contract in Section 9.3.0 are normative even where a host has not yet wired every maintenance trigger. Implementations must not introduce legacy sanitized-directory discovery or delete tool-result spill files during compaction to compensate for a missing janitor.

## 12. Failure Model

### 12.1 Failure Classes

#### Turn-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Agent Exception** | `RunStreamingAsync` throws | Create Error Item. Set Turn status = Failed. Emit `turn/failed`. Save partial state. |
| **Recoverable Provider Stream Disconnect** | The provider stream disconnects, becomes idle past `StreamIdleTimeoutMs`, or ends before the sampling request completes before visible output is emitted | Emit `system/event` with `kind = "streamError"` and retry the same sampling request up to the provider's `StreamMaxRetries`. Exhaustion falls through to Agent Exception behavior. Cleanup of the failed provider stream is best-effort and must not indefinitely block retry or failure delivery. |
| **Tool Execution Error** | Unified dispatch returns or classifies a tool failure | Native/plugin calls complete `ToolResult` with stable failure fields; MCP and Runtime Dynamic calls complete their specialized lifecycle item as failed. The normalized textual failure is returned to the model, which decides whether to retry or stop. |
| **Incomplete Historical Tool Pair** | Persisted or in-memory model history contains a `tool_use`/function call without an immediately following `tool_result`/function result | Repair or filter the model request before provider submission so strict providers can accept the history. The repair is request-local and does not silently mutate rollout evidence. |
| **Approval Timeout** | Adapter does not resolve approval within timeout | Reject the approval. Create Error Item noting timeout. Tool receives rejection. Agent may continue or fail. |
| **Turn Timeout** | Turn exceeds configurable time limit | Cancel the `CancellationToken`. Create Error Item. Set Turn status = Failed. |
| **Cancellation** | Adapter calls `CancelTurn` | Cancel the `CancellationToken`. Set Turn status = Cancelled. Save partial state. |
| **Prompt Hook Blocked** | PrePrompt hook returns `Blocked = true` | Create Error Item with block reason. Set Turn status = Failed. No agent invocation occurs. |

#### Thread-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Resume Failed (file missing)** | Thread file not found on resume | Return error to adapter. Adapter informs user. |
| **Resume Failed (unsafe header)** | Canonical thread header or source schema cannot be safely interpreted | Return error to adapter. Do not downgrade thread identity or authority. |
| **Concurrent Turn** | Adapter calls `SubmitInput` while a Turn is Running | Return error to adapter. Adapter should queue or reject the message. |

#### Persistence Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Save Failed** | Rollout writer cannot confirm a terminal Turn batch | Retain the unconfirmed suffix, block later writes for that thread, and end the Turn with a persistence error. A later flush may retry. |
| **Metadata Corrupt** | `threads` projection is unreadable or inconsistent | Attempt filesystem read-repair; if SQLite remains unavailable, return filesystem-derived summaries with a diagnostic warning. |
| **History Projection Unavailable** | A paged history query cannot validate, incrementally repair, or rebuild the thread history projection | Return the stable `ThreadHistoryUnavailable` error. Do not assemble or return a full-history response from JSONL. |
| **Disk Full** | No space for new rollout records or SQLite writes | Return error on CreateThread/SubmitInput. Adapter informs user. |

#### Channel Disconnect Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Adapter Disconnects Mid-Turn** | QQ WebSocket drops, ACP stdio closes | Turn continues to completion. Events are emitted to a dead consumer (buffered and eventually dropped). On reconnect, the adapter can resume the Thread and see the completed Turn's results. |
| **Adapter Never Resolves Approval** | Channel disconnects while WaitingApproval | Approval timeout fires. Approval is rejected. Turn continues. |

### 12.2 Recovery Strategy

- **Turn failures** do not corrupt Thread state. A failed Turn is recorded in the Thread's Turn history. The adapter can submit a new Turn to retry.
- **Canonical write failures** are recoverable because Session Core retains the unconfirmed in-memory suffix and retries on the next operation.
- **History projection failures** are recoverable from canonical JSONL through the incremental repair or full rebuild rules in §9.4.2. A failed repair remains an explicit read failure; it is never converted into an unbounded JSONL response.

### 9.8 Thread Rollback

`RollbackThread(threadId, numTurns)` removes `numTurns` turns from the end of a non-archived Thread. `numTurns` must be at least 1 and no turn in the Thread may be `Running` or `WaitingApproval`.

Rollback appends a canonical rollback record to thread JSONL and updates thread metadata; it does not revert files or other workspace side effects created by tools. After rollback, Session Core first tries to trim the removed Turn tail from the optimized request-local model history. If the removed Turns are no longer present as a plain model-visible suffix, Session Core rebuilds through the newest surviving compaction checkpoint before falling back to full canonical history. Rollback must not silently restore model-visible history that had already been compacted out.
Rollback does not release the historical Turn sequence numbers of removed Turns. Replay scans all valid `turn_started` records, including Turns later removed by rollback, before allocating a new Turn ID.
Successful rollback also records a maintenance trace event for live Dashboard visibility. Dashboard must de-duplicate that live event with the canonical rollout-derived operation when both are available.

- **Channel disconnects** are transparent to Session Core. Turns run to completion regardless of adapter state. Results are persisted and available on reconnect.

### 12.3 Error Reporting

All failures surface as:
1. An `Error` Item within the Turn (for turn-level failures)
2. An exception returned to the adapter's `SubmitInput`/`ResumeThread` call (for thread-level and persistence failures)
3. A log entry with structured context (`threadId`, `turnId`, error category)

## 13. Test and Validation Matrix

### 13.1 Validation Profiles

- **Core Conformance**: Tests for Session Core types, lifecycle, event emission, persistence. Required for any implementation.
- **Adapter Conformance**: Tests for each channel adapter's integration with Session Core. Required per channel.
- **Cross-Channel Conformance**: Tests for shared thread discovery and resume across compatible identities.

### 13.2 Core Conformance Tests

#### Types and Serialization

- Thread serialization/deserialization round-trip preserves all fields
- Turn serialization preserves Item order
- Item payload schemas validate correctly for each Item type
- Thread ID generation produces unique IDs
- Turn ID sequential numbering is correct within a Thread
- Item ID sequential numbering is correct within a Turn

#### Thread Lifecycle

- `CreateThread` sets correct initial state (Active, timestamps, generated ID)
- `PauseThread` transitions Active → Paused
- `ResumeThread` transitions Paused → Active, updates `LastActiveAt`
- `ArchiveThread` transitions Active → Archived
- `ArchiveThread` on Paused thread succeeds
- `ResumeThread` on Archived thread fails
- `SubmitInput` on Paused thread fails
- `SubmitInput` on Archived thread fails
- Archiving a thread stops active terminals but preserves completed/lost terminal artifacts and tool-result spill files
- Permanent deletion removes terminal and tool-result artifacts for the complete SubAgent subtree
- Permanent deletion remains successful and retryable when one artifact deletion fails, with an observable cleanup warning
- Graceful shutdown stops active terminals without deleting persistent artifacts
- Startup retention marks stale running terminals as lost before removing only eligible completed/lost artifacts

#### Turn Lifecycle

- `SubmitInput` creates Turn with Running status, UserMessage Item
- Turn completes with Completed status after agent finishes
- Turn fails with Failed status on agent exception
- `CancelTurn` sets Cancelled status
- `SubmitInput` while Turn is Running returns error
- Turn with approval: Running → WaitingApproval → Running → Completed
- Approval timeout: WaitingApproval → approval rejected → Turn continues

#### Event Emission

- `turn/started` emitted on `SubmitInput`
- `item/started` emitted for each Item creation
- `item/delta` emitted for streaming AgentMessage content
- `item/delta` emitted for CommandExecution compatibility output when shell streaming is enabled
- `item/completed` emitted when Item is finalized
- `turn/completed` emitted after all Items are complete
- `turn/failed` emitted on error
- `approval/requested` emitted when approval needed
- `approval/resolved` emitted when approval resolved
- Event ordering is causal (started before delta before completed); a tool call and its interactive requests occur only after preceding streamed assistant/reasoning Items have completed
- `turn/completed` is always the last event for a Turn

#### Persistence

- Thread file written after Turn completes
- Thread file loaded correctly on resume
- Exact model history and supported content round-trip through rollout
- Malformed ordinary history records are rejected observably without hiding recoverable Turns
- Unknown canonical source schemas fail explicitly
- Thread index updated on create, resume, pause, archive
- Thread and attachment projections rebuilt from files when missing without changing SQLite business authority
- Thread snapshots and Turn/Item pages rebuilt on demand without returning full-rollout fallback responses
- Healthy paged reads do not open rollout files and allocate in proportion to the requested page plus one rollout record
- Turn and Item cursors page in both directions without duplicates and reject mismatched Thread or scope
- Rollback, fork, archive, unarchive, and delete preserve the history projection lifecycle contract
- Provider-history records and protected provider payloads never appear in display-history projection rows or telemetry
- Tool-result spill writes are stable and idempotent for the same tool call identity
- Canonical artifact directory naming does not collide for distinct invalid Thread IDs and remains contained under the artifact root
- Terminal retention does not remove running sessions and is safe to repeat after a partial failure
- Thread artifact deletion is idempotent for terminals and tool-result spill directories

### 13.3 Adapter Conformance Tests (Per-Channel)

For each migrated channel:

- User message → Turn created → response delivered to user
- Streaming deltas delivered incrementally (for channels that support it)
- Tool calls visible to user (for channels that render them)
- Approval request presented to user (for channels with interactive approval)
- Approval response routed back to Session Core
- Thread created with correct `OriginChannel` and `UserId`
- Thread resumed correctly (conversation context preserved)
- Cancellation works (user can cancel a running Turn)
- Channel disconnect does not crash Session Core

### 13.4 Cross-Channel Conformance Tests

- Thread created by Channel A is discoverable by Channel B
- Thread resumed by Channel B has full conversation context from Channel A
- New Turn on resumed Thread produces correct Items
- Thread metadata from Channel A is preserved after Channel B Turn

### 13.5 Per-Session Configuration Conformance Tests (Section 16)

- Thread created with MCP servers connects them and adds tools to agent
- Mode switch via `SetThreadMode` rebuilds agent tool set
- Thread archive disconnects per-thread MCP servers
- Thread without configuration uses workspace defaults
- ACP extensions recorded in `Thread.Configuration.Extensions`
- Simulated host restart (new Session Core instance, same persistence): a thread with non-null `Thread.Configuration` is loaded from disk; `EnsureThreadLoaded` (or turn start) hydrates the per-thread agent so turns do not fall back to workspace-default agent-only behavior

### 13.6 Social Channel Conformance Tests (Section 17)

- Group session Thread allows different `SenderContext` per Turn
- Permission check at adapter level rejects unauthorized users before `SubmitInput`
- `/stop` command maps to `CancelTurn` and cancels running Turn
- `/new` command archives current Thread and creates new one
- Slash commands not exposed as Items (adapter-local operations)

### 13.7 MCP Apps Session Conformance Tests

- App direct tool dispatch uses null Turn id and creates no Session invocation item or provider-history entry.
- `ui/message` starts immediately or uses the normal queued-input path and persists only safe MCP App trigger provenance.
- Per-view pending context is last-write-wins, consumed exactly once at the central Turn-start boundary, and discarded on view/thread teardown.
- Ordinary user Turns consume all pending thread contexts while View messages consume only their originating context.
- Persisted and replayed `McpToolCall` items contain invocation provenance, the normalized UI resource association, and audience-separated results, but never availability, handles, HTML, CSP, or pending context.

## 14. Validation Priorities

This specification no longer tracks implementation phases or completed checklists. The remaining validation work is:

- Expand automated **Core Conformance** coverage for lifecycle, persistence, and failure handling.
- Add per channel **Adapter Conformance** coverage for CLI, ACP, QQ, and WeCom.
- Add **Cross-Channel Conformance** coverage for the CLI ↔ ACP shared thread pool.
- Add **Per-Session Configuration** coverage for ACP-specific mode and MCP behavior.
- Add **Social Channel Conformance** coverage for sender context, approval routing, and slash commands.
- Add **MCP Apps Session Conformance** coverage for direct calls, queued View messages, one-shot context, and persisted audience isolation.

The purpose of this section is to define ongoing verification targets, not to duplicate project-management to-do lists.

---

## 15. Channel-Specific History Boundaries

The Session Protocol applies to server-managed channels:

- CLI
- ACP
- QQ
- WeCom

For these channels, Session Core loads persisted rollout state, constructs a runtime session, executes the turn, emits `SessionEvent`s, and persists updated domain and model history afterward.

### 15.1 Resume Semantics

Cross-channel resume applies only to **server-managed** threads.

- **CLI ↔ ACP** resume works because both participate in Session Core and share the same identity shape.
- **QQ**, **WeCom**, and **Feishu** remain isolated by social conversation `ChannelContext`.

---

## 16. Per-Session Agent Configuration

### 16.1 Principle

Thread configuration belongs to the thread model rather than to any individual adapter.

### 16.2 Thread Configuration

Each thread may carry a `Configuration` object. This is a thread-owned model, not channel-owned state, and the same shape applies across CLI, AppServer, external adapters, and other hosts.

```
ThreadConfiguration
├── McpServers: McpServerConfig[]?               // Per-thread MCP server connections
├── Mode: string                                 // Agent mode: "agent", "plan", etc. (default: "agent")
├── Extensions: string[]?                        // Active extension prefixes, e.g. ["_unity"]
├── CustomTools: string[]?                       // Additional tool names to enable
├── ProviderId: string?                          // Per-thread provider id captured at thread creation
├── Model: string?                               // Per-thread model captured from ProviderPreferences at thread creation
├── Reasoning: ReasoningConfig?                  // Per-thread reasoning configuration
├── Speed: standard|fast?                        // Per-thread requested inference-speed mode
├── ContextWindow: { mode: "default"|"max" }?    // Per-thread context-window mode
├── WorkspaceOverride: string?                   // Alternate workspace root for this thread
├── Cwd: string?                                 // Sticky working directory; relative paths resolve here
├── RuntimeWorkspaceRoots: string[]?             // Sticky ordered runtime boundaries; null defaults to cwd
├── ExecutionWorkspaceOverride: string?          // Runtime execution root, typically a registered worktree
├── ToolProfile: string?                         // Named tool profile to inject
├── UseToolProfileOnly: bool                     // Use only the profile tools when true
├── AgentInstructions: string?                   // Optional extra system instructions
├── ApprovalPolicy: default|autoApprove|interrupt// Thread-scoped approval behavior
├── AutomationTaskDirectory: string?             // Local automation task directory
└── RequireApprovalOutsideWorkspace: bool?       // Overrides workspace file/shell boundary behavior
```

Approval-related fields are normative:

- `ApprovalPolicy = default` means the thread uses the normal interactive approval path when a tool requests approval.
- `ApprovalPolicy = autoApprove` means approval-gated operations on that thread are auto-accepted by the server.
- `ApprovalPolicy = interrupt` means any approval-gated operation is rejected without prompting; the active tool receives the rejection and the turn may continue.
- `RequireApprovalOutsideWorkspace = true` allows outside-workspace file or shell operations to proceed through the approval service.
- `RequireApprovalOutsideWorkspace = false` rejects outside-workspace file or shell operations without prompting.
- `RequireApprovalOutsideWorkspace = null` falls back to the workspace-level default in `AppConfig.Tools.File`, which governs both file and shell operations.

When a thread is created or its configuration changes, Session Core recreates the effective agent/tool set from that configuration.

Model preference resolution is thread-aware:

- when a server-managed thread is created, Session Core captures the effective provider and its complete `ModelPreference`: model, reasoning, speed, and context-window mode
- an explicit thread configuration wins; otherwise the preference resolves from `AppConfig.ProviderPreferences[ProviderId]`
- a selected provider without a complete preference is not a valid MainAgent runtime
- the MainAgent uses the captured thread configuration for every turn, resume, and tool-planning operation; workspace defaults never replace values on an existing thread
- native full-history SubAgents inherit the parent thread's complete captured preference and ignore default, role, and invocation-specific overrides
- fresh and bounded native SubAgents apply the default, role, invocation-specific override, and capability-normalization precedence defined by the [SubAgent Core specification](../features/subagents.md#6-native-model-resolution)
- external CLI SubAgents do not consume native model preferences
- changing provider or preference on an existing thread atomically replaces the corresponding provider/model/reasoning/speed/context values while preserving the rest of `ThreadConfiguration`; workspace changes affect Welcome and future threads only
- provider and SubAgent preference changes invalidate cached agents, but an already-running turn is never switched mid-flight

Context-window resolution is thread-aware:

- `ThreadConfiguration.ContextWindow` is an optional object `{ mode: "default" | "max" }`; omitted or null means `default`.
- `default` preserves today's compaction behavior: explicit `Compaction.ContextWindow` wins, otherwise the model catalog is inferred and capped by `Compaction.MaxContextWindow`.
- `max` is valid only when the model-context catalog has an explicit match for the thread's effective model and that catalog window is greater than the configured default window. When valid, Session Core sets the effective compaction `ContextWindow` to the raw catalog window and bypasses `Compaction.MaxContextWindow`.
- New threads capture the context-window mode from the selected provider preference. Unsupported `max` selections are normalized to `default`.
- Forks copy the source thread's context-window configuration unless the fork request supplies a replacement `ThreadConfiguration`.
- `UpdateThreadConfiguration` validates explicit `max`, rebuilds the thread agent and compaction pipeline before the next turn, persists the new configuration, and emits `thread/updated`.

Workspace resolution is thread-aware:

- `WorkspacePath` is the state workspace for thread persistence and workspace-owned state.
- `WorkspaceOverride`, when present, is the traditional alternate workspace root for a thread.
- `Cwd`, when present, is the sticky working directory for tool execution and relative path resolution without moving state from `WorkspacePath`.
- `RuntimeWorkspaceRoots` is an ordered full replacement when present. Null defaults to the ordinary cwd; an empty array explicitly supplies no runtime roots.
- A cwd-only update retargets the old cwd entry while preserving additional roots. Duplicate roots are removed without changing first-occurrence order.
- `ExecutionWorkspaceOverride`, when present, wins for tool execution and first-party file/Git surfaces while keeping state in `WorkspacePath`; it replaces the ordinary cwd root in the effective roots and preserves additional roots.
- Git worktree handoff uses `ExecutionWorkspaceOverride`; it must not use `WorkspaceOverride` to move state into the worktree.
- Existing-thread worktree handoff is a metadata/configuration change on the same Thread. It must be rejected while the Thread has running or waiting turn work, and it must rebuild the effective agent/tool context before the next turn.
- The complete multi-folder contract is specified in [Multi-Folder Local Projects](../features/multi-folder-projects.md).

### 16.3 Mode Switching

Mode switching is a thread-level operation:

```
ISessionService.SetThreadMode(threadId: string, mode: string) → void
```

- Changes `Thread.Configuration.Mode`.
- Session Core recreates the agent with the new mode's tool set.
- No Turn is created. This is a metadata operation.
- Emits `thread/statusChanged` event with mode information.

### 16.3.1 Mode-Stable Plan Tools

Plan-related tools remain schema-stable across ordinary Agent/Plan mode switches to preserve prompt-cache stability. When `PlanStore` is available, `AgentFactory` exposes `CreatePlan`, `UpdateTodos`, and `TodoWrite`; `ModeToolPolicy` enforces which calls are valid for the current mode:

| Mode | Allowed Plan Tools | Denied Plan Tools |
|------|--------------------|-------------------|
| `plan` | `CreatePlan` | `UpdateTodos`, `TodoWrite` |
| `agent` | `UpdateTodos`, `TodoWrite` | `CreatePlan` |

**`PlanStore` as a Required Dependency**: `PlanStore` provides per-session plan persistence and is required for plan-related tool injection. All hosts that support mode switching **must** supply a `PlanStore` instance to `AgentFactory`. When `PlanStore` is `null`, plan-related tools are silently omitted regardless of the requested mode — this is considered a host configuration error, not a graceful degradation.

**`onPlanUpdated` Callback**: Hosts may optionally supply a plan-update callback to propagate plan state changes to their UX layer (e.g., CLI status panel, ACP notification, Wire notification). The callback receives the source `threadId` plus the complete plan snapshot. The absence of this callback does not affect tool injection; it only disables real-time plan status updates to the client.

**Host Equivalence Requirement**: Every host that exposes `ISessionService` (and therefore mode switching) must construct `AgentFactory` with equivalent mode-critical dependencies. The minimum set is:

- `PlanStore` — required for plan/agent mode tools
- `HookRunner` — optional but recommended for lifecycle hooks

### 16.4 MCP Lifecycle

MCP server connections are thread-scoped, not turn-scoped:

- **Connect**: When a thread is created with `McpServers`, Session Core connects those servers, waits for the current startup attempt to settle, and adds ready tools before turn execution. Failed servers are reflected through MCP status and do not prevent agent construction unless the caller cancels. The same applies when a thread with persisted `McpServers` is prepared for turn execution after a cold load (e.g. via `ResumeThread` from disk or `EnsureThreadLoaded` before `SubmitInput`). Purely read-only operations (`GetThread`, thread discovery) must not connect MCP servers solely because thread metadata was loaded.
- **Disconnect**: When a thread is archived or its MCP configuration changes, Session Core disconnects the previous servers.
- **Lifecycle**: MCP connections live as long as the thread remains active.

When `Thread.Configuration.McpServers` is null, workspace-level MCP configuration applies.

### 16.5 ACP Extension Capabilities

ACP-specific capabilities such as extension prefixes are connection-scoped at discovery time but may be recorded in `Thread.Configuration.Extensions` when they affect the thread's effective tool set.

For channels that do not use extension capabilities, `Thread.Configuration.Extensions` is null.

### 16.6 Design Constraints

- Configuration changes do not implicitly create turns.
- Configuration must be persisted with the thread.
- Channels may expose only the subset of configuration that their UX supports.

---

## 17. Social Channel Patterns

### 17.1 Group Sessions (Multi-User)

QQ-style group sessions are supported without changing the Thread / Turn / Item model:

- `Thread.UserId` for group sessions is **null or a group identifier** (e.g., `group:12345` or `chat:chatId`), not an individual user.
- Each Turn's `Input` Item can carry per-message sender information in its payload.
- Sender context is appended to the current user message runtime context, not to the thread system prompt, so prompt-cacheable system instructions remain stable across different group senders.

Add to `UserMessage` payload:

```
{
  "text": string,
  "nativeInputParts": [InputPart],
  "materializedInputParts": [InputPart],
  "senderId": string,          // Individual sender within a group session (nullable)
  "senderName": string,        // Display name of the sender (nullable)
  "images": [                  // Optional local image metadata for UI rehydration
    {
      "path": string,
      "mimeType": string,
      "fileName": string
    }
  ]
}
```

Session Core still treats the thread as a single execution context; sender identity is carried at the turn level.

### 17.2 Permission and Role System

Permissions are an adapter-level concern. Typical roles include `Unauthorized`, `Whitelisted`, and `Admin`. They affect:

- Whether a user can chat at all
- Whether tools can write to the workspace
- Whether a user can approve operations
- Whether a user can use slash commands like `/stop`, `/new`

The adapter's responsibilities:
1. Check permissions before calling `SubmitInput` (reject unauthorized users).
2. Set `ApprovalContext` with the user's role so that `SessionApprovalService` can route appropriately.
3. Filter slash commands by permission level before executing them.

Add `SenderContext` to `SubmitInput`:

```
SubmitInput(threadId, text, senderContext?: SenderContext)

SenderContext
├── SenderId: string           // Individual user ID
├── SenderName: string         // Display name
├── SenderRole: string         // "admin", "whitelisted", "unauthorized"
└── GroupId: string            // Group/chat ID for group sessions (nullable)
```

Session Core records `SenderContext`, appends it to the current user message runtime context, and passes it through to approval handling. The adapter is responsible for populating it.

### 17.3 Slash Commands

Slash commands are modeled as a managed subsystem with a single server-side command registry for **server-managed commands**:

- Built-in in-process adapters (CLI, QQ, WeCom) call the registry directly.
- Out-of-process adapters use AppServer wire methods (`command/list`, `command/execute`).
- Both paths resolve against the same server-managed command set and permission metadata.

| Command | Maps To | Scope |
|---------|---------|-------|
| `/stop` | `ISessionService.CancelTurn(turnId)` | Session Core |
| `/new` | `ISessionService.ResetConversation(identity)` (archive reusable threads + create fresh thread) | Session Core |
| `/load`, `/sessions` | `ISessionService.FindThreads(identity)` + `ResumeThread` | Session Core |
| `/help` | Managed command metadata listing | Session Core + Adapter rendering |
| `/heartbeat` | `HeartbeatService.TriggerNowAsync()` | AppServer-hosted service |
| `/cron` | Cron management operations | AppServer-hosted service |
| `/debug` | Debug mode toggle operation | AppServer-hosted service |
| `/init` | Expand a repository-guidelines prompt into a normal agent turn | Session Core command pipeline |
| Custom commands | `CustomCommandLoader.TryResolve` | Session Core command pipeline |

The registry is authoritative for command discovery, permission hints, and execution routing.
Adapters may still provide platform-specific UX (for example native command menus), but they must not fork server-managed command semantics.
`/clear` is intentionally excluded from Session Core semantics and should be treated as a client-local UI command (clear screen) rather than a thread lifecycle command.
Client-local commands are outside `command/list` and `command/execute`.

`/init` is a workspace-conditional server-managed built-in command. Command discovery omits it when `.craft/AGENTS.md` is already a regular file, while direct execution remains accepted for compatibility and stale-client safety. It returns an expanded prompt that asks the agent to inspect the active workspace and create `.craft/AGENTS.md` with concise English project instructions grounded in the repository. The agent must check the target in the workspace that owns the thread and must not overwrite or modify an existing file. Clients execute the expanded prompt through the ordinary turn pipeline, so normal workspace tools, approvals, and remote-workspace routing continue to apply.

### 17.4 Active Run Cancellation

Session Core owns active-run cancellation:

- Session Core tracks the `CancellationTokenSource` for each Running Turn internally.
- `CancelTurn(turnId)` cancels the token and transitions the Turn to `Cancelled`.
- Foreground tool resource owners observe that token and finish their cancellation cleanup before the terminal Turn event is emitted.
- Explicitly detached background terminals are not owned by the active Turn and require terminal control-plane stop or cleanup operations.
- The adapter maps `/stop` to `CancelTurn` for the current Thread's active Turn.

---

## 18. Bidirectional Capabilities

Bidirectional capabilities are outside the session model.

The Session Protocol models conversation state and turn execution. It does not model transport-specific request/response features such as IDE filesystem access, terminal control, extension calls, or API-specific REST flows. Those remain tool- or channel-level concerns. Background terminals follow the same boundary: Session Core records the observable `CommandExecution` Item for the originating tool call, while AppServer exposes live terminal snapshots and output deltas to terminal-capable clients. The model-facing shell surface stays minimal (`Exec` plus `WriteStdin`, where empty stdin polls output); terminal listing, direct reads, stopping, and cleanup are AppServer/control-plane capabilities.

The design rule is simple:

- Session Core models conversation semantics.
- Adapters and tool providers model transport capabilities.

---

## 19. Thread Goals

> **Status**: Runtime implementation. See [Goal Design](../features/goal.md) for the full contract.

Session Core owns persistent thread goals, their runtime accounting, and autonomous continuation. A thread has at most one current goal. Clients and adapters may expose controls, but must translate them to Session Core or AppServer goal operations instead of maintaining independent goal state.

Goal continuation turns are ordinary persisted turns with system provenance. Session Core starts them only when an active goal exists, automatic continuation is enabled, the thread is idle, the thread is in a goal-compatible mode, and no user/approval/plan-confirmation work is pending. The continuation input is generated by Session Core as model steering; it is not a user-authored message.

When a user interrupts a turn that is pursuing an active goal, Session Core accounts progress made so far and changes the goal to `paused`. Non-user cancellation may account progress but must not imply user intent to pause unless the cancellation source explicitly represents an interrupt.

Goal objective text is user-provided data. Whenever it is injected into model-visible context, it must be escaped and marked as untrusted task context rather than higher-priority instructions.

---

## 20. Wire Protocol (Cross-Language SDK Support)

> **Status**: Specified. See the [DotCraft AppServer Protocol Specification](../protocols/appserver-protocol.md) for the full definition.

### 20.1 Goal

Expose Session Core over a language-neutral protocol so that non-C# adapters (IDE extensions, web frontends, third-party integrations) can participate in the same server-managed thread model without linking DotCraft.Core directly.

The AppServer wire protocol is specified in [appserver-protocol.md](../protocols/appserver-protocol.md). That document defines the transport, JSON-RPC message shapes, method surface, event notifications, error handling, and approval request/response mechanics that project this Session Core model to external clients.

### 20.2 External Channel Adapters

The wire protocol also enables out-of-process social channel adapters written in any language. By implementing a Wire Protocol client, a channel adapter gains the full session model — thread lifecycle, streaming events, bidirectional approval — without any C# binding.

This is specified in the [External Channel Adapter Specification](../protocols/external-channel-adapter.md) (Draft). The key prerequisite for external channels is the WebSocket transport defined in [appserver-protocol.md §15](../protocols/appserver-protocol.md#15-websocket-transport).

### 20.3 Relationship to AppServer Protocol

The AppServer protocol is the server-managed entry point for persistent threads and structured events.
