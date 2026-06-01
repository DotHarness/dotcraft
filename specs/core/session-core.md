# DotCraft Session Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-18 |

Purpose: Define the current **server-managed** session model (Thread / Turn / Item) used by `DotCraft.Core`, including lifecycle, persistence, event semantics, approval semantics, and adapter boundaries.

## 1. Scope

This specification defines the **internal domain model and execution engine** for channels whose conversation state is owned by the server and executed through `ISessionService`.

For the external JSON-RPC API that projects these primitives to out-of-process clients, see the [DotCraft AppServer Protocol Specification](../protocols/appserver-protocol.md).

| Document | Defines |
|----------|---------|
| `session-core.md` | Domain model, lifecycle rules, event semantics, persistence layout, approval semantics, and adapter contracts inside `DotCraft.Core`. |
| `appserver-protocol.md` | JSON-RPC methods, notifications, transport rules, wire DTOs, error codes, and approval mechanics for out-of-process clients. |

### 1.1 In-Scope Channels

The Session Protocol is the active execution model for:

- CLI
- ACP
- QQ
- WeCom

These channels create and resume server-managed threads whose canonical JSONL history lives under `.craft/threads/active|archived/`, while queryable metadata and agent session blobs live in `.craft/state.db`. They submit turns through Session Core and consume `SessionEvent` streams through thin adapters.

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
- **Replacing `IChannelService`**: The channel module contract (`IChannelService`, `IDotCraftModule`) is unchanged. Session Core is a layer *inside* channel implementations, not a replacement of the module system.

## 3. System Overview

### 3.1 Main Components

1. **Session Core** (`DotCraft.Protocol`)
   - Owns Thread/Turn/Item lifecycle and state machines.
   - Wraps the agent execution pipeline (`AgentFactory` + `RunStreamingAsync`).
   - Emits a structured event stream consumed by adapters.
- Persists canonical thread history to JSONL under `.craft/threads/active|archived/` and stores thread metadata plus agent session state in `.craft/state.db`.
   - Enforces per-thread mutual exclusion.

2. **Channel adapters** (per in-scope channel, in module assemblies)
   - Translate between the channel's transport (stdio, HTTP, WebSocket, bot API) and Session Core API calls.
   - Subscribe to Session Core events and render them in channel-specific format.
   - Handle channel-specific concerns: authentication, message formatting, rate limiting.
   - Implement approval routing by translating `ApprovalRequest` Items into channel UX.

3. **Persistence Layer** (`DotCraft.Protocol`)
- Appends thread history to `.craft/threads/{active|archived}/{threadId}.jsonl`.
- Stores agent history in the SQLite `thread_sessions` table inside `.craft/state.db`.
   - Provides thread discovery and session reconstruction on resume.

4. **Event stream** (in-process, `DotCraft.Protocol`)
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
- Thread JSONL storage in `.craft/threads/active|archived/` plus metadata/session storage in `.craft/state.db`
   - SQLite-backed thread discovery
   - Agent session reconstruction on resume

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
| `AgentFactory` | Unchanged. Session Core calls `CreateAgentWithTools` and `CreateAgent` as before. |
| `SessionStore` | Removed. Its responsibilities are now split between thread/session file persistence and `ISessionService`. |
| `SessionGate` | Becomes an internal implementation detail of Session Core. Channels no longer call `AcquireAsync` directly. |
| `IApprovalService` | Remains the approval interface. Session Core delegates approval requests to the channel adapter, while the request and response are modeled as Items with explicit lifecycle. |
| `IChannelService` | Unchanged. A channel module still implements `IChannelService` for Gateway integration. The adapter is an internal component of the channel's `IChannelService` implementation. |
| `HookRunner` | Session Core invokes hooks (PrePrompt, Stop, PreToolUse, PostToolUse) at the appropriate points in the Turn lifecycle. Channels no longer invoke hooks directly. |
| `TraceCollector` | Session Core records trace events. Channels no longer interact with `TraceCollector` directly. |

### 3.5 External Dependencies

- **Microsoft.Extensions.AI**: `IChatClient`, `AITool`, `FunctionInvokingChatClient` — the agent execution pipeline.
- **Microsoft.Agents.AI**: `AIAgent`, `AgentSession`, `AgentResponseUpdate` — the agent session model.
- **Existing DotCraft.Core**: `AppConfig`, `SkillsLoader`, `MemoryStore`, `ToolProviderCollector` — workspace infrastructure.
- **Channel transports**: Each channel's transport library (NapCat for QQ, ASP.NET for WeCom, custom stdio for ACP).

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
- `Source` (ThreadSource)
  - Describes why this thread exists. `kind: "user"` is the default top-level conversation.
  - `kind: "subagent"` marks a child thread spawned from another thread turn and carries parent thread, parent turn, root thread, depth, nickname, role, profile, and runtime metadata.
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
  - Append-only. Turns are never removed from a Thread.
- `QueuedInputs` (ordered list of QueuedTurnInput)
  - FIFO inputs submitted while a Turn or blocking thread maintenance operation is active. The queue is part of canonical thread state and is persisted in the rollout file.
  - Clients may explicitly reorder the full queue. Reordering preserves queued input payloads and statuses, but updates the execution order for future queued turns.
  - When a running Turn completes successfully and no blocking thread maintenance is active, Session Core dequeues at most one queued input and starts it as the next Turn. Failed or cancelled Turns do not automatically consume queued inputs.
  - When blocking thread maintenance completes, skips, fails, or is cancelled, Session Core dequeues at most one queued input and starts it as the next Turn if the thread is otherwise idle.

#### 4.1.1.1 QueuedTurnInput

A QueuedTurnInput is a durable snapshot of user input waiting to become a future Turn.

#### 4.1.1.2 SubAgent Child Threads

Profile-backed SubAgents are represented as ordinary `SessionThread` instances with `Source.kind = "subagent"` and `OriginChannel = "subagent"`. Native profiles use the same turn, item, approval, persistence, and resume path as main agent threads. External CLI profiles persist synthetic turns containing the submitted prompt, final output or error, and token metadata when available.

SubAgent child threads use normal session tool construction with a role-resolved tool policy. `agentRole` is a role selector, not just display metadata. The built-in `default` role disables DotCraft SubAgent control tools, `explorer` exposes a read-only exploration surface, and `worker` may expose write/shell/web tools plus Agent control when the depth policy allows it. Workspace configuration may override or add roles.

`SubAgent.MaxDepth` defaults to `1`. The first child spawned by a root thread has depth `1`; by default, that child cannot call `SpawnAgent` again even when its role would otherwise allow Agent control. Raising `SubAgent.MaxDepth` is the advanced opt-in for recursive SubAgent orchestration.

Session Core persists a `ThreadSpawnEdge` graph row for each parent/child relationship: `parentThreadId`, `childThreadId`, `parentTurnId`, `depth`, `agentNickname`, `agentRole`, `profileName`, `runtimeType`, `supportsSendInput`, `supportsResume`, `supportsClose`, `status` (`open` or `closed`), `createdAt`, and `updatedAt`.

Top-level thread discovery hides subagent threads by default. Callers that need a raw mixed list must request `includeSubAgents`; active lists still hide children whose parent is archived. Clients that render a background-agent widget should prefer the edge list for the active parent thread.

SubAgent child thread lifecycle is owned by the parent thread. Archiving, restoring, or permanently deleting a parent recursively applies to all descendant child threads. Direct archive/delete calls against a child thread are invalid; clients should close/resume children through the SubAgent control APIs or manage the parent thread.

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

When a queued input starts a future Turn, Session Core must copy trigger metadata into the persisted `UserMessagePayload`. When a queued input is promoted into current-turn guidance, the guidance `UserMessage` item must preserve the same trigger metadata.

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
  - `ToolCall` — Agent invokes a tool. Payload includes tool name and arguments.
  - `PluginFunctionCall` — Agent invokes a Plugin Function. Payload includes plugin identity, function name, arguments, content items, structured result, and success/failure metadata. Plugin-backed tools do not create companion `ToolResult` items.
  - `DynamicToolCall` — Agent invokes a runtime dynamic tool declared by an AppServer client. Payload includes optional namespace, tool name, arguments, content items, structured result, and success/failure metadata. Runtime dynamic tools do not create companion `ToolCall` / `ToolResult` items.
  - `ToolResult` — Result of a tool invocation. Payload includes result text and success/failure.
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
  "triggerKind": string,   // Optional automation trigger marker: "heartbeat" | "cron" | "automation"
  "triggerLabel": string,  // Optional human-readable source label (e.g. cron job name, task title)
  "triggerRefId": string   // Optional routing id for click-through (e.g. cron job id, task id)
}
```

`nativeInputParts` is authoritative for history rendering and editor rehydration when present. `materializedInputParts` captures the exact prompt/image snapshot that Session Core received after transport-side input materialization. `text` remains for compatibility and preview generation but is no longer the sole source of truth for user-message reconstruction.

The optional `triggerKind` trio is populated by Session Core when a turn is submitted inside a `TurnTriggerScope` (see `DotCraft.Protocol.TurnTriggerScope`). The automation-side runners set the scope so that heartbeat / cron (`AgentRunner`) and Automations (`AutomationSessionClient.SubmitTurnAsync`) synthesized messages carry a stable marker that clients can use to render an "automation-sourced" affordance and route click-through to the originating job/task. Goal continuation turns use `triggerKind = "goal"`, `triggerLabel = "Goal continuation"`, and `triggerRefId = goalId`. Fields are absent when the turn originates from a real user input.

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
  "toolName": string,     // Name of the tool being called
  "arguments": object,    // Tool arguments as key-value pairs
  "callId": string        // Correlation ID linking ToolCall to ToolResult
}
```

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

#### PluginFunctionCall

```
{
  "pluginId": string,       // Stable plugin identifier, e.g. "browser" or "external-channel:telegram"
  "namespace": string,      // Optional plugin function namespace
  "functionName": string,   // Model-visible function name
  "callId": string,         // Correlation ID for the plugin function invocation
  "arguments": object,      // Function arguments as key-value pairs
  "contentItems": [         // Optional rich result content
    {
      "type": string,       // "text" | "image"
      "text": string,       // Text content when type is "text"
      "mediaType": string,  // Image media type when type is "image"
      "dataBase64": string  // Base64 image data when type is "image"
    }
  ],
  "structuredResult": any,  // Optional structured JSON result
  "success": boolean,      // Whether the plugin function succeeded
  "errorCode": string,     // Optional machine-readable failure code
  "errorMessage": string   // Optional human-readable failure message
}
```

Plugin Function invocations are represented by this single item. Session Core does not emit a paired `ToolResult` item for the same call.

#### DynamicToolCall

```
{
  "namespace": string,      // Optional client-declared namespace
  "toolName": string,       // Runtime dynamic tool name
  "callId": string,         // Correlation ID for the runtime dynamic tool call
  "arguments": object,      // Tool arguments as key-value pairs
  "contentItems": [         // Optional rich result content
    {
      "type": string,       // "text" | "image"
      "text": string,       // Text content when type is "text"
      "mediaType": string,  // Image media type when type is "image"
      "dataBase64": string  // Base64 image data when type is "image"
    }
  ],
  "structuredResult": any,  // Optional structured JSON result
  "success": boolean,      // Whether the dynamic tool call succeeded
  "errorCode": string,     // Optional machine-readable failure code
  "errorMessage": string   // Optional human-readable failure message
}
```

Runtime Dynamic Tool invocations are represented by this single item. Session Core does not emit paired `ToolCall` / `ToolResult` items for the same call.

#### ToolResult

```
{
  "callId": string,       // Matches the ToolCall.callId
  "result": string,       // Textual result
  "success": boolean      // Whether the tool execution succeeded
}
```

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
  "kind": string,              // Notice classifier. Known values: "compacted", "memoryConsolidated".
  "trigger": string,           // For kind="compacted": "auto" | "reactive" | "manual"
  "mode": string,              // For kind="compacted": "partial"; legacy persisted notices may contain "micro"
  "tokensBefore": number,      // Approximate input tokens right before compaction ran
  "tokensAfter": number,       // Approximate input tokens after compaction ran
  "percentLeftAfter": number,  // Fraction of EffectiveContextWindow still available (0.0 - 1.0)
  "clearedToolResults": number // Count of tool results cleared before summary (0 for partial-only compaction)
}
```

`SystemNotice` items are created by Session Core for summary-producing
maintenance and persisted via the normal rollout/`turn.Items` pipeline, so
they survive thread reload and round-trip through `thread/read`. Clients treat
them as inline dividers in the timeline rather than part of the model
conversation. Cold-cache tool-result clearing without a summary emits only the
transient `system/event` needed to refresh context usage; it must not create a
persistent timeline divider. Clients may encounter older `mode = "micro"`
compacted notices from previous releases and should hide them.
`memoryConsolidated` notices have no compaction-specific token fields.

### 4.3 Stable Identifiers and Normalization Rules

- **Thread ID**: Generated by Session Core. Format `thread_{yyyyMMdd}_{6-char-random}`. Must be unique within the workspace. Used as the primary key for persistence and cross-channel resume.
- **Turn ID**: Sequential within a Thread. Format `turn_{3-digit-sequence}`. Assigned by Session Core when a Turn starts.
- **Item ID**: Sequential within a Turn. Format `item_{3-digit-sequence}`. Assigned by Session Core when an Item is created.
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
  - Session Core loads the thread state from persistence, reconstructs the agent session, and sets status to Active.
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

### 5.2 Turn Lifecycle

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
  - Session Core runs post-turn operations: save session, run Stop hooks, compaction, consolidation.

- `Running` → `Failed`
  - An unrecoverable error occurs: agent exception, tool execution error, timeout.
  - Session Core creates an `Error` Item, sets `Turn.Error`, saves the partial Thread state, and runs cleanup.
  - For server-managed history, Session Core preserves the current optimized `AgentSession` when it is available, especially after a successful context compaction. Rebuilding from rollout is a recovery fallback only when the current optimized session cannot be saved or deserialized. The next Turn should include the failed Turn's completed user, assistant, and paired tool-call/tool-result Items that were durably recorded before the failure, but must not expand older history that had already been compacted out of the model-visible session.

- `Running`, `WaitingApproval`, or `WaitingInput` → `Cancelled`
- The adapter requests cancellation (e.g., user sends `/cancel`, channel disconnects).
- Session Core cancels the agent execution via `CancellationToken`, completes any currently streaming agent/reasoning Items with their accumulated text, saves partial state, and preserves the current optimized `AgentSession` when one exists. Rebuilding from rollout is allowed only as a fallback when the optimized session cannot be saved. A cancellation must not restore pre-compaction tool results or summaries that were no longer model-visible.

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
    - `PluginFunctionCall` starts when the plugin wrapper begins execution and completes with the plugin result payload.
    - `DynamicToolCall` starts when the runtime dynamic tool wrapper begins execution and completes with the AppServer client callback result payload.

**Invariants**:

- An Item's Status never moves backward.
- A Completed Item's payload is immutable.
- Items within a Turn are ordered by creation time. This order is the canonical sequence of events within the Turn.
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
3. [ToolCall → ToolResult | PluginFunctionCall | DynamicToolCall]* (zero or more tool invocations)
   3a. [ApprovalRequest → ApprovalResponse] (within a tool call, if approval needed)
   3b. [UserInputRequest → UserInputResponse] (Plan Mode only, if the agent needs a user decision before continuing)
4. AgentMessage (final response, streamed)
5. [Error] (if something went wrong)
```

The sequence may recurse: the agent may call tools, receive results, reason again, call more tools, and then respond. Session Core emits Items in the order they occur. The adapter renders them according to its channel's capabilities.

### 5.5 Cross-Channel Resume Semantics

When a channel adapter resumes a Thread that was created by a different channel:

1. The adapter calls `ResumeThread(threadId)`.
2. Session Core loads the Thread from persistence.
3. Session Core reconstructs the `AgentSession` (conversation history) by replaying the stored Items into the Microsoft.Agents.AI session format.
4. The Thread's `Status` is set to `Active`, `LastActiveAt` is updated.
5. The adapter can now call `SubmitInput` to start a new Turn.
6. The new Turn's Items are attributed to the resuming channel (recorded in Turn metadata).

The resumed agent has full context of previous Turns regardless of which channel originated them.

When reconstructing model history, provider reasoning metadata MUST be preserved on assistant messages that contain tool calls. If one sampling segment produced `ReasoningContent`, visible assistant text, and one or more `ToolCall` Items before their matching `ToolResult` Items, the reconstructed history represents them as one assistant `ChatMessage` containing reasoning content, visible text, and all function calls. This preserves OpenAI protocol providers such as DeepSeek whose thinking mode requires `reasoning_content` to be round-tripped on assistant tool-call messages.

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
  - Permanent removal is performed via `ISessionService.DeleteThreadPermanentlyAsync(threadId)`. Session Core removes in-memory state, persisted thread/session data, DB-backed plans, attachment reference rows, all tracing sessions/events bound to that thread, and dashboard usage rows associated with the thread or its bound trace sessions; it also best-effort deletes workspace-managed attachment files that are no longer referenced by any remaining thread. It does **not** enqueue a `SessionEvent` on the turn/event stream (there is no active turn for deletion).
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

  - **Defined `kind` values**:

    | Kind | Meaning | Timing |
    |------|---------|--------|
    | `compactWarning` | Token usage crossed `WarningThreshold` but not yet `ErrorThreshold`. Advisory only, no compaction is attempted. | Synchronous, post-turn (Step 5k), when threshold is above warning but below auto. |
    | `compactError` | Token usage crossed `ErrorThreshold`. Strong advisory; auto-compaction may trigger on the next turn. | Synchronous, post-turn (Step 5k), when threshold is above error but below auto. |
    | `compacting` | Auto-compaction is starting. `percentLeft`/`tokenCount` reflect the pre-compaction state. | Synchronous, before the `CompactionPipeline` runs. |
    | `compacted` | Compaction finished successfully. Token tracker has been reset and `percentLeft`/`tokenCount` reflect the post-compaction optimized model-visible state. Auto/reactive compaction may include estimated fixed request overhead for the current sampling request; idle manual compaction must report the compacted session estimate without carrying over the previous request's provider overhead. | Synchronous, immediately after the pipeline returns `Micro` or `Partial`. Only `Partial` creates a persisted compaction notice. |
    | `compactSkipped` | Compaction was evaluated but not executed (e.g. below threshold, nothing new to summarize, circuit breaker tripped). | Synchronous, immediately after the pipeline returns `Skipped`. |
    | `compactFailed` | Compaction attempted but failed (LLM error). The circuit breaker may trip after several consecutive failures. | Synchronous, immediately after the pipeline returns `Failed`. |
    | `compactCancelled` | Thread-scoped manual compaction was interrupted by the user. | Asynchronous/thread-scoped, when the maintenance cancellation token is signalled. |
    | `consolidating` | Memory consolidation is starting. Consolidation is driven by Session Core after every configured number of successful Turns, independent from compaction. | Reserved for asynchronous memory-maintenance notifications. |
    | `consolidated` | Memory consolidation completed successfully. MEMORY.md and HISTORY.md have been updated. | Reserved for asynchronous memory-maintenance notifications. |
    | `consolidationSkipped` | Memory consolidation completed without writing MEMORY.md or HISTORY.md (for example, no `save_memory` call or no valid changes). UIs should dismiss any active consolidation status and should not show a success marker. | Asynchronous, after the background consolidation task returns no changes. |
    | `consolidationFailed` | Memory consolidation failed (LLM error, provider error, or persistence failure). UIs should dismiss any active consolidation status. | Asynchronous, after the background consolidation task throws. |
    | `consolidationCancelled` | Memory consolidation was interrupted by the user. UIs should dismiss any active consolidation status. | Asynchronous, after the maintenance cancellation token is signalled. |
    | `streamError` | A provider streaming response disconnected, timed out while idle, or otherwise ended before the sampling request completed; Session Core is retrying the same sampling request. `message` uses the compact form `Reconnecting... x/y`. | Turn-scoped, during agent execution, before the retry delay. |

  - **Emission rules**:
    - System events are emitted during the Turn's post-processing phase (after agent execution completes, before `turn/completed`), except when raised reactively (see below).
    - The threshold advisory events (`compactWarning`, `compactError`) carry `percentLeft` and `tokenCount` so UIs can render a "context almost full" warning bar without needing a separate usage request.
    - Successful `compacted` events carry `contextUsage` when available. Clients should prefer this full snapshot over `tokenCount` / `percentLeft` because it includes thresholds needed to seed context-window UI after manual compaction timeouts or missed thread snapshots.
    - Auto-compaction events (`compacting`, `compacted`, `compactSkipped`, `compactFailed`) are synchronous within Step 5k and always fire in the order `compacting` -> one terminal event (`compacted` / `compactSkipped` / `compactFailed`). Count-based tool-result clearing is not part of the hot auto-threshold path because it rewrites the prompt prefix and can break prompt-cache reuse. When a prompt request snapshot is available and the cache is not known to be cold, auto-compaction must enter the summary-producing partial/fork path with the original history so snapshot-prefix matching can succeed. A lightweight tool-result clearing pass may run only after a provider-aware idle gap indicates the relevant prompt cache is cold; if that pass does not bring the estimated request below the auto threshold, Session Core must continue to summary compaction using the cleared history.
    - Manual compaction uses `ISessionService.CompactThreadAsync(threadId)` and is exposed to AppServer clients as `thread/compact/start`. It is allowed only for Active, server-managed threads with existing history and no `Running` / `WaitingApproval` turn or active thread maintenance. It registers thread maintenance with `maintenanceKind = "compacting"`, emits the same `compacting` -> terminal `system/event` sequence through the thread event broker, and prevents new turns from starting until the terminal event. It does not run a microcompact pre-pass; it first tries partial compaction, and if no older prefix exists, or the partial attempt cannot produce a summary, it falls back to full-history compaction. On success, Session Core saves the compacted agent session, updates context usage from the compacted session estimate, invalidates any prompt request snapshot captured before the replacement history boundary, and appends a persisted `SystemNotice` with `kind = "compacted"` and `trigger = "manual"` to the latest completed turn. On cancellation it emits `compactCancelled` and does not append a notice.
    - The pipeline may also be invoked **reactively** from the Turn's error path when the model rejects a request with `prompt_too_long`, `context_length_exceeded`, or another conservatively classified context-overflow equivalent. In that case the Turn still fails, but `compacting` followed by `compacted` / `compactFailed` is emitted first so UIs know the history was repaired before the user retries.
    - Automatic memory consolidation is a non-blocking background task scheduled by Session Core after a configured number of successful Turns and after the baseline thread/session persistence attempt for that Turn has finished. It is not spawned by the compaction pipeline, and Turn completion is **not** deferred for consolidation. Its start event (`consolidating`) is emitted through the turn-scoped `SessionEventChannel`; its terminal events (`consolidated` / `consolidationSkipped` / `consolidationFailed` / `consolidationCancelled`) are emitted through the thread event broker with `turnId = null`. Automatic consolidation does not register thread maintenance, does not set `maintenanceKind = "consolidating"`, and does not prevent the next user input from starting a Turn immediately. Session Core serializes automatic consolidation per thread; if another automatic trigger arrives while one is active, at most one follow-up attempt is scheduled after the active attempt completes. On `consolidated`, Session Core persists a `SystemNotice` item with `kind = "memoryConsolidated"` into the completed Turn and broadcasts `item/started` + `item/completed` through the thread event broker. Manual consolidation uses `ISessionService.ConsolidateThreadMemoryAsync(threadId)` and is exposed to AppServer clients as `thread/memory/consolidate/start`. It is allowed only for Active, server-managed, idle threads with at least one completed Turn, no active thread maintenance, and non-empty model-visible history; it bypasses `Memory.AutoConsolidateEnabled`, registers thread maintenance with `maintenanceKind = "consolidating"`, emits thread-scoped `consolidating` → terminal `system/event`, awaits the maintenance result, and appends the same persistent notice on success. See [Memory Consolidation](memory-consolidation.md) for the design contract.
    - `ISessionService.CancelThreadMaintenanceAsync(threadId)` interrupts active thread maintenance. AppServer exposes this as `thread/maintenance/interrupt`.
    - Turn-scoped system events are emitted through the turn-scoped `SessionEventChannel`, so they are guaranteed to arrive before `turn/completed`. Thread-scoped maintenance events may arrive later.
    - The protocol is language-neutral. System events carry `messageKey`, optional `params`, and an English `fallbackText`; `message` is a compatibility alias for `fallbackText`. Clients that support UI localization translate `messageKey` locally and fall back to `fallbackText`. User text, model output, and raw tool output remain original text and are not translated by Session Core.
    - Provider stream retry events (`streamError`) are transient and must not create a persistent `SystemNotice`. They are emitted only when the failed sampling attempt has not yet produced a visible item or item delta. Once visible assistant text, reasoning, tool-call, or tool-argument preview output has been emitted for an attempt, a later stream failure is treated as a normal agent exception so the partial Turn can be preserved without inventing delta rollback semantics. Idle-timeout detection must surface the retry or failure promptly; cleanup of the failed provider stream is best-effort and must not indefinitely delay the retry notification or terminal failure.
  - **Adapters**: Adapters that display session maintenance status (e.g., CLI spinner for consolidation, status text for compaction) should consume `system/event` notifications. Adapters that do not need maintenance status may ignore this event type or opt out via `optOutNotificationMethods`.

#### Context Compaction Summary Contract

Context compaction is a short-term context-window optimization. It is not long-term memory consolidation and must not attempt to preserve every historical detail.

- The compact summary is a handoff for the next model-visible history. It should preserve only the current task, key decisions, important files read or changed, critical errors/fixes, constraints, and concrete next steps needed to continue.
- Summary prompts must target about 4,000-6,000 output tokens and must not request an unbounded chronological analysis of every message.
- Summary prompts must not require a separate `<analysis>` drafting block. Implementations may still strip `<analysis>` if a provider returns it, for compatibility with older summaries.
- Summary prompts must not require listing all user messages or embedding full code snippets by default. They may ask for the smallest necessary excerpt only when exact text is required to continue the task.
- Non-cache or legacy compaction requests must use a compact-specific `MaxOutputTokens` budget. The default budget is 12,000 tokens and must not inherit the normal Anthropic 64,000-token turn budget.
- Cache-sharing snapshot forks should keep cache-sensitive request parameters stable when possible, but a snapshot is usable only while its captured messages remain a prefix of the current canonical model-visible history. Any successful history replacement (auto, reactive, or manual compaction; rollback; deletion) invalidates older snapshots. Maintenance forks should attempt the provider request first so prompt-cache-aware providers can reuse the captured prefix. If the provider rejects the snapshot request with a conservatively classified prompt-too-long / context-overflow error, the fork returns `maintenance_snapshot_too_large` and falls back to a trimmed non-cache path when one exists. Other provider, authentication, rate-limit, model, or request-shape errors must not be reclassified as context overflow.
- Snapshot forks enforce summary length through the prompt and by validating the returned summary. A summary that exceeds the compact-specific hard budget is treated as `compact_summary_too_long` and must fall back to a non-cache path or report `compactFailed`.
- Compaction model-call cancellation, provider/network timeout, and overlong summaries must be observable in trace storage with a terminal maintenance-fork response. Manual compaction maps user cancellation to `compactCancelled`; provider timeout and overlong/invalid summary map to `compactFailed` with machine-readable `message` values.
- Successful history replacement must persist a recovery checkpoint containing the replacement model-visible history and the newest covered Turn. Later recovery and rollback rebuilds use the newest checkpoint whose covered Turn still survives in the canonical Thread.

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

The `content` parameter accepts multimodal input (text, images, etc.) as a list of `AIContent` parts. When the transport provides native input metadata (for example native command, skill, or file-reference parts), Session Core persists both the transport-native snapshot and the materialized `AIContent` snapshot on `UserMessagePayload`, derives `UserMessagePayload.Text` from the native snapshot for compatibility/display, and passes the full multimodal materialized content to the agent via `ChatMessage`. A convenience extension method `SubmitInputAsync(string threadId, string text, ...)` wraps plain text into `[new TextContent(text)]` for text-only callers.

`UserMessagePayload.DeliveryMode` is optional and indicates how the user message entered the conversation: `"normal"` (or omitted) for a direct Turn start, `"queued"` for a queued input that later became a Turn, and `"guidance"` for a user request appended to an active Turn.

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
Task<TurnSteerResult> SteerTurnAsync(
    string threadId,
    string expectedTurnId,
    string queuedInputId,
    CancellationToken ct = default,
    SenderContext? sender = null)
```

Marks the referenced queued input as `guidancePending` after validating that `expectedTurnId` still matches the current active Turn. The active execution loop drains pending guidance only at safe model/tool boundaries, appends a `UserMessage` item with `DeliveryMode = "guidance"` at insertion time, removes the queued input, and injects the input into the current model history. If the Turn ends before insertion, pending guidance is restored to `queued`.

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
- execute the agent against the persisted server-managed session
- translate agent output into typed items and events
- collect token usage and other turn-level metadata
- persist updated thread/session state
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
│   │   ├── {threadId}.jsonl     # Canonical rollout history for active threads
│   │   └── ...
│   ├── archived/
│   │   ├── {threadId}.jsonl     # Canonical rollout history for archived threads
│   │   └── ...
├── state.db                     # SQLite metadata, agent sessions, tracing, token usage
├── attachments/images/          # Workspace-managed local image blobs referenced by thread history
├── cache/                       # Rebuildable cache files; not part of thread lifecycle
```

### 9.2 Thread File Format

Each thread is stored as an append-only JSONL rollout. Every line is a `ThreadRolloutRecord` describing one state transition:

```json
{ "kind": "thread_opened", "timestamp": "2026-03-15T10:00:00Z", "threadOpened": { ... } }
{ "kind": "turn_started", "timestamp": "2026-03-15T10:00:01Z", "turnStarted": { ... } }
{ "kind": "item_appended", "timestamp": "2026-03-15T10:00:01Z", "itemAppended": { ... } }
{ "kind": "turn_completed", "timestamp": "2026-03-15T10:02:30Z", "turnCompleted": { ... } }
{ "kind": "queued_input_added", "timestamp": "2026-03-15T10:02:31Z", "queuedInputAdded": { ... } }
{ "kind": "queued_input_removed", "timestamp": "2026-03-15T10:02:32Z", "queuedInputRemoved": { ... } }
{ "kind": "queued_input_reordered", "timestamp": "2026-03-15T10:02:33Z", "queuedInputReordered": { ... } }
```

Session Core reconstructs a `SessionThread` by replaying the rollout file in order.

### 9.3 Agent Session Storage

Serialized `AgentSession` state is stored in the SQLite `thread_sessions` table inside `.craft/state.db`.

Per-thread plans are stored in SQLite `thread_plans`. Plans follow the thread lifecycle: archive/unarchive keeps them, and permanent thread deletion cascades through the database. Legacy `.craft/plans/{threadId}.json|md` files are not a runtime plan source.

Workspace-managed local images referenced by persisted `localImage` input parts are indexed in SQLite `thread_attachments`. The image file remains available for history rendering while at least one active or archived thread references it. Permanent thread deletion removes that thread's references and best-effort deletes now-unreferenced managed files. Unsent draft images that never enter thread history are cleaned as unreferenced attachments after the configured TTL.

Session Core manages the mapping:
- **Save**: After each successful compaction and after each terminal Turn when an optimized session is available, serialize the `AgentSession` and upsert `thread_sessions.session_json`.
- **Load**: On Thread resume, deserialize `thread_sessions.session_json` via `agent.DeserializeSessionAsync`.
- **Rebuild**: Rebuild from rollout only when the optimized session row is missing, malformed, or cannot be saved/deserialized. Rebuilds must first apply the newest surviving compaction checkpoint and then replay only later surviving Turns. Full rollout rebuild is a legacy fallback when no usable checkpoint exists. Rebuilds must be treated as recovery and should not overwrite a usable optimized session after compaction.

The separation between rollout history and agent session state is intentional:
- The rollout JSONL files are the source of truth for the Session Protocol UI/domain model.
- The `thread_sessions` table is the source of truth for optimized LLM conversation history. It may contain compacted summaries, cleared tool-result markers, or other model-visible projections that intentionally differ from the full UI rollout.
- Rollout may also contain internal compaction checkpoints. These records store replacement model-visible history for recovery only; they are not Session Items and are not projected to clients.

### 9.4 Thread Discovery

Thread discovery is implemented by querying the SQLite `threads` metadata table in `.craft/state.db`. Rollout files remain the canonical conversation history, while `ThreadSummary` rows are derived metadata used by `FindThreadsAsync`.

This database-backed approach avoids replaying every rollout file during discovery while keeping rollout files as the canonical history.

`ThreadSummary` fields returned for each discovered thread:
- `Id`, `Status`, `OriginChannel`, `ChannelContext`
- `UserId`, `WorkspacePath`, `DisplayName`
- `CreatedAt`, `LastActiveAt`
- `TurnCount`

### 9.5 Cross-Channel Resume Protocol

#### Default discovery (no `crossChannelOrigins`)

`FindThreadsAsync(identity, includeArchived, crossChannelOrigins: null)` matches threads by three fields. `ChannelName` on the identity is **not** used as a filter:

| Field | Behavior |
|---|---|
| `WorkspacePath` | Required exact match (case-insensitive) |
| `UserId` | Matched if non-null in identity; null identity field skips this filter |
| `ChannelContext` | `null` identity matches only threads with `ChannelContext = null`; non-null matches exactly |

This means cross-channel discovery is **natural for channels that share the same identity shape**:

- **CLI and ACP** both use `UserId = "local"` and `ChannelContext = null`. They discover each other's threads automatically. A thread created in CLI appears in ACP's session list, and vice versa. This is by design — both are local, single-user channels on the same machine.
- **QQ and WeCom** use per-user, per-context identifiers (`ChannelContext = "group:{id}"`, `"chat:{chatId}"`). Each conversation context has its own isolated thread pool. CLI and ACP cannot see QQ/WeCom threads and vice versa. This is also by design — social channel threads are scoped to their originating context.

#### Opt-in cross-context discovery (`crossChannelOrigins`)

`FindThreadsAsync` accepts an optional fourth parameter: `crossChannelOrigins` (`IReadOnlyList<string>?`, default `null`).

- When `crossChannelOrigins` is **null** or **empty**, behavior is identical to the default discovery above (no extra threads).
- When **non-empty**, the result set is the union of:
  1. Threads that satisfy the default identity predicate (`WorkspacePath` + `UserId` + `ChannelContext` as in the table above), **and**
  2. Threads that match `WorkspacePath` (case-insensitive) + `OriginChannel` contained in `crossChannelOrigins` (case-insensitive string match), **ignoring `ChannelContext`**. This branch does **not** require `UserId` to match the request identity, so channels that use per-job or per-session synthetic user IDs (e.g. `cron:{jobId}`) still appear when the user opts in to that origin.

The union is deduplicated by thread ID and ordered by `LastActiveAt` descending.

This opt-in path exists so clients such as **DotCraft Desktop** (which uses a non-null `ChannelContext` such as `workspace:{path}`) can still list threads created by channels with a different context (e.g. CLI with `ChannelContext = null`) when the user explicitly allows those origin channels.

#### Resume flow

1. **Discovery**: Adapter calls `FindThreadsAsync(identity, includeArchived, crossChannelOrigins)`. Returns threads matching the combined predicate when `crossChannelOrigins` is set, otherwise the default predicate only.
2. **Selection**: The adapter presents the list to the user, or auto-selects the most recently active thread.
3. **Resume**: Adapter calls `ResumeThreadAsync(threadId)`. Session Core sets status to `Active` and updates `LastActiveAt`.
4. **Session Load**: Session Core loads `thread_sessions.session_json`, reconstructing the LLM context.
5. **Ready**: Adapter calls `SubmitInputAsync` to start a new Turn. The Turn's `OriginChannel` records the resuming channel's name.

### 9.6 Legacy Compatibility Policy

Session Core does not implement compatibility paths for older snapshot layouts such as `.craft/sessions/{key}.json`, `.craft/threads/{threadId}.json`, or `.craft/threads/{threadId}.session.json`.

The supported persistence contract is:

- Canonical thread history in `.craft/threads/active|archived/*.jsonl`
- Queryable metadata and serialized agent sessions in `.craft/state.db`

Dashboard trace-session deletion follows the same persistence contract. Deleting one trace session removes that session's trace rows and associated dashboard usage rows; if the session is bound to a thread, deletion cascades through permanent thread deletion. Clearing all trace sessions deletes the selected trace/thread state and associated usage rows, but preserves global usage rows that have no `thread_id` or `session_key`. Bulk trace clearing may run SQLite maintenance (`wal_checkpoint(TRUNCATE)` and conditional `VACUUM`) after deletion to reclaim WAL/free-page space.

Dashboard trace event reads are paged from the durable trace store. The first page returns at most the newest 1000 events for the selected session or all sessions; clients fetch older events with an opaque `beforeCursor` when the user scrolls upward. Maintenance envelope events are filterable as maintenance events and are counted separately from normal LLM request/response totals, while detailed collector events and token usage remain in the same trace session for correlation.

Dashboard may also project read-only thread operations from canonical thread JSONL. Rollback visibility is derived from `thread_rolled_back` records and exposed as operation metadata (`type = rollback`, `threadId`, timestamp, removed Turn count, and source). Hidden recovery records such as compaction checkpoints remain internal and must not be exposed through Dashboard operation APIs or trace views.

### 9.7 Persistence Failure Handling

- **Save failure**: If Session Core cannot write to disk after a Turn completes, the Turn's result is still delivered to the adapter (events were already emitted). The error is logged. The in-memory Thread state is preserved. The next save attempt retries.
- **Load failure**: If Session Core cannot read a Thread file on resume, it returns an error to the adapter. The adapter should inform the user and offer to create a new Thread.
- **Discovery failure**: If a thread file is unreadable during `FindThreadsAsync`, it is silently skipped. Corrupt files do not prevent other threads from being discovered.

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

These channels create threads, submit turns through `ISessionService`, consume `SessionEvent`s, and persist state via rollout files plus `.craft/state.db`.

### 11.2 Cross-Channel Resume Status

Cross-channel resume works for channels that share the same identity shape:

- **CLI ↔ ACP** share `UserId = "local"` and `ChannelContext = null`, so they naturally share one thread pool.
- **QQ** and **WeCom** remain isolated by `ChannelContext`, which is the intended behavior for social conversations.

## 12. Failure Model

### 12.1 Failure Classes

#### Turn-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Agent Exception** | `RunStreamingAsync` throws | Create Error Item. Set Turn status = Failed. Emit `turn/failed`. Save partial state. |
| **Recoverable Provider Stream Disconnect** | The provider stream disconnects, becomes idle past `StreamIdleTimeoutMs`, or ends before the sampling request completes before visible output is emitted | Emit `system/event` with `kind = "streamError"` and retry the same sampling request up to the provider's `StreamMaxRetries`. Exhaustion falls through to Agent Exception behavior. Cleanup of the failed provider stream is best-effort and must not indefinitely block retry or failure delivery. |
| **Tool Execution Error** | A tool throws during `FunctionInvokingChatClient` processing | The error is captured by `FunctionInvokingChatClient` as a `FunctionResultContent` with error. A `ToolResult` Item is created with `success = false`. The agent decides whether to retry or fail. |
| **Incomplete Historical Tool Pair** | Persisted or in-memory model history contains a `tool_use`/function call without an immediately following `tool_result`/function result | Repair or filter the model request before provider submission so strict providers can accept the history. The repair is request-local and does not silently mutate rollout evidence. |
| **Approval Timeout** | Adapter does not resolve approval within timeout | Reject the approval. Create Error Item noting timeout. Tool receives rejection. Agent may continue or fail. |
| **Turn Timeout** | Turn exceeds configurable time limit | Cancel the `CancellationToken`. Create Error Item. Set Turn status = Failed. |
| **Cancellation** | Adapter calls `CancelTurn` | Cancel the `CancellationToken`. Set Turn status = Cancelled. Save partial state. |
| **Prompt Hook Blocked** | PrePrompt hook returns `Blocked = true` | Create Error Item with block reason. Set Turn status = Failed. No agent invocation occurs. |

#### Thread-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Resume Failed (file missing)** | Thread file not found on resume | Return error to adapter. Adapter informs user. |
| **Resume Failed (session corrupt)** | Agent Session file cannot be deserialized | Return error to adapter. Offer to start a new Thread. |
| **Concurrent Turn** | Adapter calls `SubmitInput` while a Turn is Running | Return error to adapter. Adapter should queue or reject the message. |

#### Persistence Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Save Failed** | Disk write error after Turn completes | Log error. In-memory state preserved. Next operation retries save. Turn result is still delivered to adapter (events already emitted). |
| **Metadata Corrupt** | `threads` metadata table unreadable or inconsistent | Return error for discovery operations and log warning. |
| **Disk Full** | No space for new rollout records or SQLite writes | Return error on CreateThread/SubmitInput. Adapter informs user. |

#### Channel Disconnect Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Adapter Disconnects Mid-Turn** | QQ WebSocket drops, ACP stdio closes | Turn continues to completion. Events are emitted to a dead consumer (buffered and eventually dropped). On reconnect, the adapter can resume the Thread and see the completed Turn's results. |
| **Adapter Never Resolves Approval** | Channel disconnects while WaitingApproval | Approval timeout fires. Approval is rejected. Turn continues. |

### 12.2 Recovery Strategy

- **Turn failures** do not corrupt Thread state. A failed Turn is recorded in the Thread's Turn history. The adapter can submit a new Turn to retry.
- **Persistence failures** are recoverable because Session Core maintains in-memory state and retries on next operation.

### 9.8 Thread Rollback

`RollbackThread(threadId, numTurns)` removes `numTurns` turns from the end of a non-archived Thread. `numTurns` must be at least 1 and no turn in the Thread may be `Running` or `WaitingApproval`.

Rollback appends a canonical rollback record to thread JSONL and updates thread metadata; it does not revert files or other workspace side effects created by tools. After rollback, Session Core first tries to trim the removed Turn tail from the optimized `AgentSession`. If the removed Turns are no longer present as a plain model-visible suffix, Session Core rebuilds through the newest surviving compaction checkpoint before falling back to full canonical history. Rollback must not silently restore model-visible history that had already been compacted out.
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
- Event ordering is causal (started before delta before completed)
- `turn/completed` is always the last event for a Turn

#### Persistence

- Thread file written after Turn completes
- Thread file loaded correctly on resume
- Agent Session file round-trip preserves conversation history
- Thread index updated on create, resume, pause, archive
- Thread index rebuilt from files when missing

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

## 14. Validation Priorities

This specification no longer tracks implementation phases or completed checklists. The remaining validation work is:

- Expand automated **Core Conformance** coverage for lifecycle, persistence, and failure handling.
- Add per channel **Adapter Conformance** coverage for CLI, ACP, QQ, and WeCom.
- Add **Cross-Channel Conformance** coverage for the CLI ↔ ACP shared thread pool.
- Add **Per-Session Configuration** coverage for ACP-specific mode and MCP behavior.
- Add **Social Channel Conformance** coverage for sender context, approval routing, and slash commands.

The purpose of this section is to define ongoing verification targets, not to duplicate project-management to-do lists.

---

## 15. Channel-Specific History Boundaries

The Session Protocol applies to server-managed channels:

- CLI
- ACP
- QQ
- WeCom

For these channels, Session Core loads persisted session state, executes the turn, emits `SessionEvent`s, and persists updated thread/session state afterward.

### 15.1 Resume Semantics

Cross-channel resume applies only to **server-managed** threads.

- **CLI ↔ ACP** resume works because both participate in Session Core and share the same identity shape.
- **QQ** and **WeCom** remain isolated by `ChannelContext`.

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
├── Model: string?                               // Per-thread model; defaults to the effective workspace model at thread creation
├── WorkspaceOverride: string?                   // Alternate workspace root for this thread
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
- `RequireApprovalOutsideWorkspace = null` falls back to the workspace-level defaults in `AppConfig.Tools.File` and `AppConfig.Tools.Shell`.

When a thread is created or its configuration changes, Session Core recreates the effective agent/tool set from that configuration.

Model resolution is thread-aware:

- when a server-managed thread is created, Session Core captures the current effective workspace `AppConfig.Model` into `Thread.Configuration.Model` unless the caller supplied an explicit model
- the MainAgent uses `Thread.Configuration.Model`; workspace `AppConfig.Model` is a creation-time default for new threads, not a dynamic fallback for already-created threads
- DotCraft-managed native SubAgents use workspace `AppConfig.SubAgent.Model` when set
- when `AppConfig.SubAgent.Model` is empty, native SubAgents inherit the thread's effective MainAgent model
- workspace `model`, `apiKey`, `endpoint`, and `subagent` configuration changes invalidate cached thread agents so the next turn uses freshly resolved clients; existing threads keep their captured model unless their thread configuration is explicitly changed, and an already-running turn is not switched mid-flight

### 16.3 Mode Switching

Mode switching is a thread-level operation:

```
ISessionService.SetThreadMode(threadId: string, mode: string) → void
```

- Changes `Thread.Configuration.Mode`.
- Session Core recreates the agent with the new mode's tool set.
- No Turn is created. This is a metadata operation.
- Emits `thread/statusChanged` event with mode information.

### 16.3.1 Mode-Specific Tool Injection

Each agent mode defines a **mode-specific tool set** that is injected (or removed) when the agent is created for that mode. The `AgentFactory` is responsible for assembling the correct tools based on the mode:

| Mode | Injected Tools | Required Dependency | Removed Tools |
|------|---------------|---------------------|---------------|
| `plan` | `CreatePlan` | `PlanStore` | Tools in the plan-mode deny list (e.g., `TodoWrite`, `UpdateTodos`) |
| `agent` | `UpdateTodos`, `TodoWrite` | `PlanStore` | _(none beyond global deny list)_ |

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

- `Thread.UserId` for group sessions is **null or a group identifier** (e.g., `qq_group:12345`), not an individual user.
- Each Turn's `Input` Item can carry per-message sender information in its payload.
- The adapter may inject sender context into the prompt.

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

Session Core records `SenderContext` and passes it through to approval handling. The adapter is responsible for populating it.

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
| Custom commands | `CustomCommandLoader.TryResolve` | Session Core command pipeline |

The registry is authoritative for command discovery, permission hints, and execution routing.
Adapters may still provide platform-specific UX (for example native command menus), but they must not fork server-managed command semantics.
`/clear` is intentionally excluded from Session Core semantics and should be treated as a client-local UI command (clear screen) rather than a thread lifecycle command.
Client-local commands are outside `command/list` and `command/execute`.

### 17.4 Active Run Cancellation

QQ and WeCom use `ActiveRunRegistry` to track and cancel in-flight runs. Under Session Core, this is replaced by:

- Session Core tracks the `CancellationTokenSource` for each Running Turn internally.
- `CancelTurn(turnId)` cancels the token and transitions the Turn to `Cancelled`.
- The adapter maps `/stop` to `CancelTurn` for the current Thread's active Turn.
- `ActiveRunRegistry` is no longer needed — Session Core owns the cancellation lifecycle.

---

## 18. Bidirectional Capabilities

Bidirectional capabilities are outside the session model.

The Session Protocol models conversation state and turn execution. It does not model transport-specific request/response features such as IDE filesystem access, terminal control, extension calls, or API-specific REST flows. Those remain tool- or channel-level concerns. Background terminals follow the same boundary: Session Core records the observable `CommandExecution` Item for the originating tool call, while AppServer exposes live terminal snapshots and output deltas to terminal-capable clients. The model-facing shell surface stays minimal (`Exec` plus `WriteStdin`, where empty stdin polls output); terminal listing, direct reads, stopping, and cleanup are AppServer/control-plane capabilities.

The design rule is simple:

- Session Core models conversation semantics.
- Adapters and tool providers model transport capabilities.

---

## 19. Thread Goals

> **Status**: Runtime implementation. See [Goal Design](goal-design.md) for the full contract.

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

