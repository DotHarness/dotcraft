# DotCraft TypeScript SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Living |
| **Date** | 2026-08-01 |
| **Related Specs** | [Unified SDK Specification](sdk.md), [AppServer Protocol](../protocols/appserver-protocol.md), [AppServer Protocol Contracts and SDK Generation](protocol-contract-generation.md), [Hub Architecture](../architecture/hub-architecture.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Session Core](../architecture/session-core.md), [.NET SDK Binding](dotnet.md), [Plugin Architecture](../architecture/plugin-architecture.md) |

Purpose: Define the TypeScript binding, package contract, Node.js runtime requirements, channel runtime, documentation model, and compatibility strategy for `@dotcraft/sdk`.

Shared SDK behavior is defined by [Unified SDK Specification](sdk.md). This language binding may add TypeScript-specific package structure, exports, and channel module details, but it must not redefine shared SDK semantics.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Design Principles](#2-design-principles)
- [3. Architecture](#3-architecture)
- [4. Package Contract](#4-package-contract)
- [5. Runtime Requirements](#5-runtime-requirements)
- [6. Connection Model](#6-connection-model)
- [7. Hub Client](#7-hub-client)
- [8. Wire Client](#8-wire-client)
- [9. High-Level Application API](#9-high-level-application-api)
- [10. Thread API](#10-thread-api)
- [11. Run API](#11-run-api)
- [12. Input Model](#12-input-model)
- [13. Streaming Event Model](#13-streaming-event-model)
- [14. Callback Capabilities](#14-callback-capabilities)
- [15. Error Model](#15-error-model)
- [16. Channel SDK](#16-channel-sdk)
- [17. TypeScript Channel Modules](#17-typescript-channel-modules)
- [18. Documentation and Examples](#18-documentation-and-examples)
- [19. Testing and Conformance](#19-testing-and-conformance)
- [20. Security](#20-security)
- [21. Versioning and Compatibility](#21-versioning-and-compatibility)
- [22. Repository Integration](#22-repository-integration)
- [23. Acceptance Contract](#23-acceptance-contract)
- [24. Future Work](#24-future-work)

---

## 1. Scope

### 1.1 What This Spec Defines

This specification defines the TypeScript SDK that application developers, channel module authors, and advanced protocol clients use to integrate with DotCraft.

It defines:

- The package identity and public entry points.
- The Node.js runtime baseline.
- The local Hub-managed connection flow.
- The remote AppServer WebSocket connection flow.
- The low-level AppServer JSON-RPC client surface.
- The high-level `DotCraft` / `Thread` / `Run` API.
- Streaming event normalization.
- Approval, user-input, and runtime dynamic tool callback contracts.
- The channel adapter SDK and first-party TypeScript channel module contract.
- Documentation, examples, testing, security, and compatibility requirements.

### 1.2 What This Spec Does Not Define

This specification does not define:

- The AppServer JSON-RPC wire protocol. That contract is defined by [AppServer Protocol](../protocols/appserver-protocol.md).
- Hub HTTP endpoint semantics. Those are defined by [Hub Architecture](../architecture/hub-architecture.md).
- External channel wire extensions. Those are defined by [External Channel Adapter](../protocols/external-channel-adapter.md).
- Session persistence, turn lifecycle semantics, item payload semantics, or agent execution internals. Those are defined by [Session Core](../architecture/session-core.md).
- Desktop-specific renderer behavior, Browser runtime behavior, or Chrome extension behavior.

### 1.3 Primary Audiences

The SDK serves three audiences:

| Audience | Need | SDK Surface |
|----------|------|-------------|
| Application developers | Start or connect to DotCraft and run agent work from Node.js. | `@dotcraft/sdk` |
| Advanced protocol clients | Access full AppServer JSON-RPC methods and notifications. | `@dotcraft/sdk/wire` |
| Channel authors | Build external channels that bridge social or messaging platforms to DotCraft. | `@dotcraft/channel` |

---

## 2. Design Principles

### 2.1 AppServer Is Authoritative

The SDK is a client library. It must not duplicate server-side state machines, permission decisions, approval policies, queue semantics, or persistence rules. It should provide ergonomic wrappers over server contracts while treating AppServer and Hub as the source of truth.

### 2.2 Hub Is the Default Local Bootstrap

Local TypeScript applications should not need to know how to manually allocate AppServer ports or avoid duplicate workspace runtimes. The SDK's local mode uses Hub by default:

```text
SDK -> Hub ensure -> AppServer WebSocket -> AppServer Protocol
```

After bootstrap, normal conversation traffic goes directly to AppServer. Hub is not on the turn execution hot path.

### 2.3 High-Level First, Raw Escape Hatch Always Available

The primary developer experience should be simple:

```ts
const dotcraft = await DotCraft.local({ workspacePath });
const thread = await dotcraft.threads.getOrCreate({ userId: "me" });
const result = await thread.run("Summarize this project.");
```

Advanced callers must still be able to use the raw wire client for methods that are not yet wrapped by the high-level API.

### 2.4 Thread Is the Core User Concept

DotCraft conversations are persistent threads. The SDK should expose this directly instead of hiding all state behind one-shot calls. One-shot helpers may exist, but the durable abstraction is `DotCraftThread`.

### 2.5 Streaming Must Be Structured

The SDK must expose normalized streaming events for common application cases and retain the raw JSON-RPC message for advanced rendering or diagnostics.

### 2.6 Channel Runtime Logic Should Be Shared

First-party TypeScript channel packages should not each reimplement thread lookup, turn queueing, stream reduction, delivery dispatch, tool dispatch, or module lifecycle logic. The SDK owns reusable channel runtime building blocks.

### 2.7 Protocol Changes Are Spec-First

If SDK implementation discovers a required change to AppServer Protocol, Hub Protocol, External Channel Adapter, or Session Core, the relevant protocol spec must be updated before implementing the server behavior.

---

## 3. Architecture

### 3.1 Layered Model

```text
┌───────────────────────────────────────────────────────────────┐
│ @dotcraft/sdk                                                  │
│   DotCraft.local/remote, Thread API, run/runStreamed, events   │
├───────────────────────────────────────────────────────────────┤
│ @dotcraft/channel                                              │
│   Channel authoring, runtime, media, testing, module lifecycle  │
├───────────────────────────────────────────────────────────────┤
│ @dotcraft/sdk/wire                 @dotcraft/sdk/hub           │
│   typed/raw JSON-RPC session        Hub discovery/management    │
├───────────────────────────────────────────────────────────────┤
│ @dotcraft/sdk/contracts                                        │
│   generated DTOs, method maps, unions, protocol metadata       │
├───────────────────────────────────────────────────────────────┤
│ DotCraft AppServer Protocol       DotCraft Hub Local API        │
└───────────────────────────────────────────────────────────────┘
```

### 3.2 Local Application Flow

```text
Node app
  -> DotCraft.local({ workspacePath })
  -> read ~/.craft/hub/hub.lock
  -> start dotcraft hub if needed
  -> POST /v1/appservers/ensure
  -> connect to endpoints.appServerWebSocket
  -> initialize / initialized
  -> thread/start or thread/resume
  -> turn/start or turn/enqueue
  -> stream notifications and server-initiated requests
```

### 3.3 Remote Application Flow

```text
Node app
  -> DotCraft.remote({ url, token })
  -> connect directly to AppServer WebSocket
  -> initialize / initialized
  -> normal AppServer Protocol
```

### 3.4 Channel Module Flow

```text
Channel platform event
  -> ChannelAdapter.handleMessage()
  -> ThreadResolver get/resume/create thread
  -> CommandRouter handles slash commands
  -> turn/start
  -> TurnStreamReducer consumes notifications
  -> platform-specific delivery hook sends response

Server initiated request
  -> approval / ext/channel/send / ext/channel/toolCall / heartbeat
  -> SDK dispatcher
  -> platform-specific hook
  -> JSON-RPC response
```

---

## 4. Package Contract

### 4.1 Package Name

The canonical TypeScript SDK package name is:

```text
@dotcraft/sdk
```

The package is published publicly on npm.

`@dotcraft/plugin` is a separate public package for authoring and building a DotCraft Plugin's Desktop module. It depends on the exact matching `@dotcraft/sdk` version so ordinary SDK applications do not acquire its React and build-tool dependencies.

### 4.2 Public Entry Points

| Entry point | Purpose |
|-------------|---------|
| `@dotcraft/sdk` | High-level application API. |
| `@dotcraft/sdk/contracts` | I/O-free generated AppServer contracts and protocol metadata. |
| `@dotcraft/sdk/wire` | Low-level AppServer JSON-RPC client, typed operations, and raw escape hatches. |
| `@dotcraft/sdk/hub` | Hub discovery, startup, management, and SSE helpers. |
| `@dotcraft/sdk/app-binding` | App Binding helpers. |
| `@dotcraft/sdk/dynamic-tools` | Runtime Dynamic Tool authoring helpers. |
| `@dotcraft/sdk/testing` | SDK transport and protocol test helpers. |
| `@dotcraft/sdk/meta` | SDK and generated protocol metadata. |

### 4.3 Top-Level Exports

The top-level package exports:

- `DotCraft`
- `DotCraftThread`
- `DotCraftRunResult`
- `DotCraftRunEvent`
- `DotCraftError`
- `HubClientError`
- `TurnInProgressError`
- `parseAppBindingHandoff`
- `appBindingToolError`
- `textPart`
- `imageDataUrlPart`
- `localImagePart`
- `skillRefPart`
- `commandRefPart`
- common approval decision constants

The top-level package does not expose Wire clients, Hub models, Channel APIs, or the complete generated DTO graph. Generated protocol types belong only under `@dotcraft/sdk/contracts`; `@dotcraft/sdk/wire` does not re-export them.

### 4.4 Contracts Exports

`@dotcraft/sdk/contracts` exports generated DTOs, client request and notification maps, server request and notification maps, discriminated method envelopes, method groups, and protocol information. It must not import Node.js APIs, transports, Hub helpers, or high-level SDK code.

### 4.5 Wire Exports

`@dotcraft/sdk/wire` exports:

- JSON-RPC message models.
- `DotCraftWireClient`.
- `StdioTransport`.
- `WebSocketTransport`.
- transport errors.
- typed request, notification, notification-listener, and server-request registration APIs;
- explicit `requestRaw`, `notifyRaw`, unknown-notification, and unknown-server-request APIs.

Known operations accept generated Contracts DTOs through generated typed methods. Run reducers and other high-level helpers are not exported here.

### 4.6 Hub Exports

`@dotcraft/sdk/hub` exports:

- `HubClient`
- `HubClientError`
- `HubLockInfo`
- `HubAppServerResponse`
- `HubStatusResponse`
- `HubEvent`
- `findSseBoundary`
- typed request option interfaces

### 4.7 Specialized Exports

`@dotcraft/sdk/app-binding` exports App Binding workflow helpers and `@dotcraft/sdk/dynamic-tools` exports Runtime Dynamic Tool authoring helpers. Both consume Contracts DTOs without defining synonymous wire models.

`@dotcraft/sdk/meta` exports `SDK_VERSION`, `CONTRACT_VERSION`, `APPSERVER_PROTOCOL_VERSION`, and `CONTRACT_SHA256` from package and generated metadata rather than duplicated literals.

### 4.8 Testing Exports

`@dotcraft/sdk/testing` exports SDK fake/in-memory transport utilities and protocol fixtures. Channel conformance, manifest, config, queue, and reducer helpers belong to `@dotcraft/channel/testing`.

Testing exports are public but not runtime-stable API for end-user applications.

---

## 5. Runtime Requirements

### 5.1 Node Version

The SDK targets Node.js 20 or newer.

All first-party TypeScript channel packages must use the same baseline.

### 5.2 Module Format

The SDK is ESM-first.

Required package properties:

- `"type": "module"`
- ESM `exports` map
- generated `.d.ts` files

CommonJS compatibility is not required in the initial SDK contract.

### 5.3 Required Runtime Dependencies

The SDK may depend on:

- `ws` for Node WebSocket support;
- Node built-in modules for file, process, path, stream, and child process behavior.

The high-level SDK should avoid framework dependencies.

Channel packages may depend on platform SDKs such as Telegram, Feishu, QQ, WeCom, or Weixin libraries.

### 5.4 Browser Support

The SDK is a Node.js SDK. Browser runtime support is out of scope unless a future spec introduces a browser-specific transport and security model.

---

## 6. Connection Model

### 6.1 Local Mode

`DotCraft.local()` establishes a Hub-managed local connection.

Options:

```ts
interface DotCraftLocalOptions {
  workspacePath: string;
  clientName?: string;
  clientVersion?: string;
  clientTitle?: string;
  executable?: string;
  expectedExecutable?: string;
  binaryMatchPolicy?: "ignore" | "restartIfMismatch" | "errorIfMismatch";
  hubStartupTimeoutMs?: number;
  approvalHandler?: ApprovalHandler;
  userInputHandler?: UserInputHandler;
  capabilities?: DotCraftClientCapabilityOptions;
}
```

Behavior:

1. Validate `workspacePath` is non-empty.
2. Discover live Hub from the current user's Hub lock file.
3. If no live Hub is available, start `dotcraft hub`.
4. Wait for Hub readiness until timeout.
5. Call `POST /v1/appservers/ensure` with `startIfMissing: true`.
6. Read `endpoints.appServerWebSocket`.
7. Connect to the AppServer WebSocket endpoint.
8. Perform `initialize`.
9. Send `initialized`.
10. Return a ready `DotCraft` client.

`DotCraft.local()` must not stop the Hub-managed AppServer when the SDK client closes. Closing the SDK client closes only its WebSocket connection.

`DotCraft.localChat()` follows the same flow after resolving and initializing the default Chat workspace (`~/.craft/workspaces/chats`). It calls the existing Hub AppServer ensure endpoint with that concrete `workspacePath`; it must not use an empty path or a separate Hub endpoint.

### 6.2 Remote Mode

`DotCraft.remote()` connects directly to an existing AppServer WebSocket endpoint.

Options:

```ts
interface DotCraftRemoteOptions {
  url: string;
  token?: string | null;
  clientName?: string;
  clientVersion?: string;
  clientTitle?: string;
  approvalHandler?: ApprovalHandler;
  userInputHandler?: UserInputHandler;
  capabilities?: DotCraftClientCapabilityOptions;
}
```

If `token` is provided separately, the WebSocket transport appends it as the `token` query parameter unless the URL already includes an explicit token.

### 6.3 Direct Stdio Mode

Direct stdio AppServer process management is not a high-level local default. It remains available through `@dotcraft/sdk/wire` for specialized subprocess clients and adapter scenarios.

### 6.4 Initialize Capabilities

High-level SDK clients send:

```json
{
  "approvalSupport": true,
  "requestUserInputSupport": true,
  "streamingSupport": true,
  "configChange": true
}
```

Advertising `approvalSupport` or `requestUserInputSupport` requires the matching handler before initialization. High-level connection fails with a stable configuration error rather than advertising an unsupported callback or inventing a response.

Channel adapters additionally send `capabilities.channelAdapter` as defined in [External Channel Adapter](../protocols/external-channel-adapter.md).

---

## 7. Hub Client

### 7.1 Responsibilities

The Hub client provides:

- Hub lock path resolution.
- Hub lock JSON parsing.
- Process liveness checks.
- Loopback URL validation.
- Hub status probing.
- Hub startup.
- AppServer ensure/restart/stop/list operations.
- Hub shutdown.
- SSE subscription and parsing.

### 7.2 Hub Lock

The SDK reads:

```text
~/.craft/hub/hub.lock
```

Expected shape:

```ts
interface HubLockInfo {
  pid: number;
  apiBaseUrl: string;
  token: string;
  startedAt?: string;
  version?: string;
  binaryPath?: string | null;
}
```

The lock is trusted only after:

1. It parses successfully.
2. `pid` appears live.
3. `apiBaseUrl` is loopback HTTP.
4. `GET /v1/status` succeeds.

### 7.3 Hub Startup

When Hub is not live, the SDK starts:

```text
dotcraft hub
```

Binary resolution order:

1. Explicit `executable`.
2. A host-provided resolver when available.
3. `dotcraft` on PATH.

The child process is detached, hidden on Windows, and not connected to the parent stdio streams.

### 7.4 AppServer Ensure

Request:

```json
{
  "workspacePath": "F:/examples/workspace",
  "client": {
    "name": "my-app",
    "version": "0.1.0"
  },
  "startIfMissing": true
}
```

The SDK requires `endpoints.appServerWebSocket` in the response. Missing endpoint is a typed Hub error.

### 7.5 SSE Events

The SDK supports `GET /v1/events` with bearer authorization.

SSE parsing must support both `\n\n` and `\r\n\r\n` frame boundaries.

Malformed event frames are ignored by default, but debug hooks may receive diagnostics.

---

## 8. Wire Client

### 8.1 JSON-RPC Responsibilities

The wire client handles:

- request id generation;
- response correlation;
- JSON-RPC error conversion;
- notification dispatch;
- server-initiated request dispatch;
- transport close handling;
- graceful shutdown;
- initialization gating, timeouts, lifecycle state, and opt-in reconnect;
- typed known-method dispatch and explicitly named raw escape hatches.

It does not expose Thread, Turn, Run, approval, user-input, Dynamic Tool, or Channel convenience behavior.

### 8.2 Transport Interface

The transport abstraction is message-oriented:

```ts
interface Transport {
  readMessage(): Promise<Record<string, unknown>>;
  writeMessage(message: Record<string, unknown>): Promise<void>;
  close(): Promise<void>;
}
```

### 8.3 Stdio Transport

Stdio transport uses newline-delimited JSON:

- stdin: server to SDK when SDK is spawned as client;
- stdout: SDK to server;
- diagnostics must go to stderr.

For adapters spawned by DotCraft, stdio is the channel between DotCraft and the adapter process.

### 8.4 WebSocket Transport

WebSocket transport uses one JSON-RPC message per text frame.

The transport must:

- support `ws://` and `wss://`;
- append bearer token as query parameter when provided separately;
- reject writes before connection is open;
- reject pending reads and writes on close;
- expose typed transport errors.

The transport represents one socket or stream and does not reconnect itself. `DotCraftWireClient` owns optional reconnect because it can repeat the protocol handshake without attempting to reconstruct application state. Raw clients default to reconnect disabled; Desktop and Channel profiles enable it explicitly.

### 8.5 Raw Request Escape Hatch

Known methods are compile-time constrained by generated maps:

```ts
await client.request("thread/read", params);
await client.notify("initialized", params);
```

Unknown extensions use separate methods:

```ts
await client.requestRaw<Result>("ext/vendor/method", params);
await client.notifyRaw("ext/vendor/event", params);
```

The typed methods do not accept arbitrary string overloads. Known and unknown notifications and server requests also use separate typed and raw registration APIs.

---

## 9. High-Level Application API

### 9.1 `DotCraft`

`DotCraft` represents one initialized AppServer connection.

Core methods and properties:

```ts
class DotCraft {
  static local(options: DotCraftLocalOptions): Promise<DotCraft>;
  static localChat(options?: Omit<DotCraftLocalOptions, "workspacePath">): Promise<DotCraft>;
  static remote(options: DotCraftRemoteOptions): Promise<DotCraft>;

  readonly serverInfo: ServerInfo;
  readonly capabilities: ServerCapabilities;
  readonly threads: ThreadManager;

  request<T>(method: string, params?: unknown): Promise<T>;
  on(event: string, handler: NotificationHandler): Unsubscribe;
  close(): Promise<void>;
}
```

The `request()` method is the high-level raw escape hatch. It delegates to the wire client.

### 9.2 Thread Manager

```ts
interface ThreadHistoryPageOptions {
  cursor?: string;
  limit?: number;
  sortDirection?: "ascending" | "descending";
}

interface ThreadItemPageOptions extends ThreadHistoryPageOptions {
  turnId?: string;
}

interface ThreadManager {
  getOrCreate(options: GetOrCreateThreadOptions): Promise<DotCraftThread>;
  start(options: StartThreadOptions): Promise<DotCraftThread>;
  resume(threadId: string, options?: ResumeThreadOptions): Promise<DotCraftThread>;
  list(options?: ListThreadOptions): Promise<ThreadSummary[]>;
  read(threadId: string): Promise<SessionThread>;
  listTurns(threadId: string, options?: ThreadHistoryPageOptions): Promise<ThreadTurnsListResult>;
  listItems(threadId: string, options?: ThreadItemPageOptions): Promise<ThreadItemsListResult>;
}
```

`getOrCreate()` should list reusable active or paused threads for the identity before creating a new thread. Paused threads are resumed before use.

### 9.3 Default Identity

When a caller omits identity fields, the SDK uses:

| Field | Default |
|-------|---------|
| `channelName` | `sdk` |
| `userId` | current OS username when available, otherwise `local-user` |
| `channelContext` | omitted |

Applications that persist cross-user sessions should specify `userId` explicitly.

---

## 10. Thread API

### 10.1 `DotCraftThread`

`DotCraftThread` represents one server-backed thread.

```ts
class DotCraftThread {
  readonly id: string;
  readonly identity: SessionIdentity;

  snapshot(): SessionThread;
  refresh(): Promise<SessionThread>;
  listTurns(options?: ThreadHistoryPageOptions): Promise<ThreadTurnsListResult>;
  listItems(options?: ThreadItemPageOptions): Promise<ThreadItemsListResult>;
  subscribe(options?: SubscribeOptions): Promise<ThreadSubscription>;
  unsubscribe(): Promise<void>;

  run(input: RunInput, options?: RunOptions): Promise<DotCraftRunResult>;
  runStreamed(input: RunInput, options?: RunOptions): AsyncIterable<DotCraftRunEvent>;
  enqueue(input: RunInput, options?: EnqueueOptions): Promise<QueuedInputResult>;
  interrupt(turnId: string): Promise<void>;

  setMode(mode: string): Promise<void>;
  archive(): Promise<void>;
  delete(): Promise<void>;

  onToolCall(namespace: string | null, name: string, handler: DynamicToolHandler): Unsubscribe;
}
```

### 10.2 Snapshot Semantics

The thread object caches the latest known thread snapshot. Methods that mutate or receive lifecycle notifications should update the cache when the server provides a new thread payload.

Callers can force a header refresh with `refresh()`. Persisted Turns and Items are read separately through bounded history pages.

### 10.3 Subscription Semantics

`subscribe()` maps to `thread/subscribe`.

The high-level `runStreamed()` may use a scoped subscription or a temporary event stream, but it must avoid duplicate event delivery when the connection already holds an active subscription for the target thread. This follows the AppServer Protocol at-most-once rule.

---

## 11. Run API

### 11.1 Run Options

```ts
interface RunOptions {
  sender?: SenderContext;
  collectRawEvents?: boolean;
  abortSignal?: AbortSignal;
  enqueueIfBusy?: boolean;
}
```

`enqueueIfBusy` is explicit. If omitted or false, a server `TurnInProgress` response becomes a `TurnInProgressError`.

### 11.2 Run Result

```ts
interface DotCraftRunResult {
  thread: Thread;
  turn: Turn;
  text: string;
  items: unknown[];
  usage?: TokenUsageInfo | null;
  rawEvents?: JsonRpcMessage[];
}
```

`text` is the canonical merged assistant reply. The SDK should use the same delta/snapshot merge semantics as the channel stream reducer so callers do not receive duplicated text when both deltas and final snapshots are present.

### 11.3 `run()`

`run()`:

1. Prepares input parts.
2. Subscribes or prepares event capture before `turn/start`.
3. Starts the turn.
4. Consumes events until terminal event.
5. Returns `DotCraftRunResult`.

Terminal events:

- `turn/completed`
- `turn/failed`
- `turn/cancelled`

`turn/failed` raises `TurnFailedError` unless the caller chooses an option that returns failed run results instead of throwing. The default is throw.

### 11.4 `runStreamed()`

`runStreamed()`:

1. Prepares event capture before `turn/start`.
2. Starts the turn.
3. Yields normalized events as they arrive.
4. Yields one terminal event containing final result or failure details.
5. Cleans up temporary handlers when iteration ends early.

If the caller stops iteration before the run is terminal, the SDK must unregister client-side handlers. It must not automatically interrupt the server turn unless the caller passes an abort signal or calls `interrupt()`.

### 11.5 Abort Semantics

When `abortSignal` is triggered after `turn/start` succeeds, the SDK should call `turn/interrupt`.

If the signal is triggered before `turn/start`, the SDK should reject without sending a request.

---

## 12. Input Model

### 12.1 Accepted Input

The SDK accepts:

```ts
type RunInput =
  | string
  | InputPart[]
  | {
      input: InputPart[];
      sender?: SenderContext;
    };
```

### 12.2 Input Part Helpers

The SDK exports:

```ts
textPart(text: string): TextPart
imageDataUrlPart(dataUrl: string): ImagePart
localImagePart(path: string, options?: LocalImageOptions): LocalImagePart
skillRefPart(name: string): SkillRefPart
commandRefPart(rawText: string): CommandRefPart
```

Helpers must produce the AppServer wire shape, using camelCase fields.

### 12.3 Slash Commands

The high-level application API does not automatically interpret user text that starts with `/`.

Applications that want built-in command semantics should call `command/execute` through the wire API or implement their own UI command layer.

Channel adapters keep their existing slash command behavior through `CommandRouter`.

### 12.4 Skill Mentions

High-level applications may use `skillRefPart()` directly.

The SDK does not parse `$skill` text into `skillRef` parts unless a future API explicitly enables that behavior.

---

## 13. Streaming Event Model

### 13.1 Normalized Event Shape

Every normalized event includes:

```ts
interface DotCraftRunEventBase {
  type: string;
  threadId: string;
  turnId?: string;
  raw: JsonRpcMessage;
}
```

### 13.2 Event Types

Required normalized event types:

| Type | Source |
|------|--------|
| `thread_started` | `thread/started` |
| `thread_resumed` | `thread/resumed` |
| `thread_status_changed` | `thread/statusChanged` |
| `thread_runtime_changed` | `thread/runtimeChanged` |
| `queue_updated` | `thread/queue/updated` |
| `turn_started` | `turn/started` |
| `item_started` | `item/started` |
| `item_completed` | `item/completed` |
| `agent_message_delta` | `item/agentMessage/delta` |
| `reasoning_delta` | `item/reasoning/delta` |
| `tool_arguments_delta` | `item/toolCall/argumentsDelta` |
| `approval_resolved` | `item/approval/resolved` |
| `usage_delta` | `item/usage/delta` |
| `subagent_progress` | `subagent/progress` |
| `plan_updated` | `plan/updated` |
| `system_event` | `system/event` |
| `completed` | `turn/completed` |
| `failed` | `turn/failed` |
| `cancelled` | `turn/cancelled` |
| `raw` | Any subscribed notification not otherwise normalized. |

Unknown notifications should be emitted as `raw` when they match the thread filter.

### 13.3 Text Merging

The SDK must handle servers that provide both streaming deltas and final item snapshots.

Rules:

- Deltas are accumulated per agent message item when possible.
- Final snapshots are preferred when they contain the complete text and are at least as long as deltas.
- The SDK must avoid duplicating text when both delta and snapshot contain overlapping content.
- Channel adapters and high-level runs should share the same reducer logic.
- Channel segment delivery is acknowledged by the adapter hook: `onSegmentCompleted` may return `false` to indicate the segment was not delivered. The reducer must not advance the delivered frontier for failed or thrown segment deliveries, so the final snapshot can resend the remaining text.

### 13.4 Segment Boundaries

For channel adapters, stream reducers may emit intermediate segments at meaningful item boundaries, such as before tool calls.

For high-level application runs, segment boundaries are optional. Applications receive raw delta events and a final merged result.

---

## 14. Callback Capabilities

### 14.1 Server-Initiated Request Dispatch

The SDK handles JSON-RPC requests sent from AppServer to the client.

Supported high-level request families:

- `item/approval/request`
- `item/tool/call`
- `item/tool/requestUserInput`
- `ext/channel/heartbeat`
- `ext/channel/send`
- `ext/channel/toolCall`

Channel-specific requests are handled by the channel SDK.

### 14.2 Approval Handler

```ts
type ApprovalHandler = (request: ApprovalRequest) => Promise<ApprovalDecision> | ApprovalDecision;
```

The SDK response shape:

```json
{ "decision": "accept" }
```

Allowed decisions:

- `accept`
- `acceptForSession`
- `acceptAlways`
- `decline`
- `cancel`

High-level initialization fails before connecting if `approvalSupport` is declared without an approval handler. The SDK never invents an approval decision.

### 14.3 Dynamic Tool Handler

Runtime dynamic tools are declared on `thread/start.dynamicTools` or `thread/resume.dynamicTools`. Clients may also pass thread-bound runtime app context through `additionalContext?: Record<string, { kind: "application"; value: string }>` on start/resume options when the server advertises `runtimeAdditionalContext`; an empty object on resume clears that context.

Handler:

```ts
type DynamicToolHandler = (request: DynamicToolCallRequest) =>
  Promise<DynamicToolCallResult> | DynamicToolCallResult;
```

Success:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Done." }
  ],
  "structuredContent": {}
}
```

Failure:

```json
{
  "success": false,
  "errorCode": "AdapterToolCallFailed",
  "errorMessage": "..."
}
```

If no handler is registered, the SDK returns:

```json
{
  "success": false,
  "errorCode": "UnsupportedTool",
  "errorMessage": "No handler registered for this dynamic tool."
}
```

### 14.4 User Input Handler

Plan Mode and tools may request structured user input.

Handler:

```ts
type UserInputHandler = (request: UserInputRequest) =>
  Promise<UserInputResponse> | UserInputResponse;
```

The high-level client registers this server-request method only when a handler is configured. Without a handler, the Wire layer returns JSON-RPC `-32601`; it never invents an answer.

### 14.5 Heartbeat

The SDK must always respond to `ext/channel/heartbeat` with `{}` when acting as a channel adapter.

Regular application clients may also respond with `{}` if the server sends the request unexpectedly.

---

## 15. Error Model

### 15.1 Base Error

All SDK-specific errors inherit from `DotCraftSdkError`:

```ts
class DotCraftSdkError extends Error {
  code: string;
  cause?: unknown;
}
```

### 15.2 JSON-RPC Errors

Server JSON-RPC errors are represented as:

```ts
class DotCraftError extends DotCraftSdkError {
  rpcCode: number;
  rpcMessage: string;
  data?: unknown;
}
```

The error message should prefer human-readable server detail when available in `error.data.detail`.

### 15.3 Typed Common Errors

Required typed errors:

| Error | Condition |
|-------|-----------|
| `HubClientError` | Hub discovery, startup, status, or request failure. |
| `TransportError` | Transport-level read/write/open failure. |
| `TransportClosed` | Transport closed while reads or writes were pending. |
| `InitializationError` | AppServer initialize handshake failed. |
| `TurnInProgressError` | Server rejected turn start due to active turn or maintenance. |
| `ThreadNotFoundError` | Server reports thread missing. |
| `ThreadNotActiveError` | Server reports thread cannot accept turns. |
| `TurnFailedError` | Agent execution failed after `turn/start` succeeded. |
| `TurnCancelledError` | Turn was cancelled before successful completion. |
| `ApprovalTimeoutError` | Server reports approval timeout. |

Raw JSON-RPC code constants remain available under `@dotcraft/sdk/wire`.

### 15.4 Error Stability

SDK error `code` strings are stable API. Error message text may evolve.

---

## 16. Channel Package

### 16.1 Purpose

The independent private `@dotcraft/channel` package lets TypeScript packages integrate messaging platforms with DotCraft as external channels. It depends on `@dotcraft/sdk`; the SDK does not depend on Channel.

It owns reusable behavior and leaves platform-specific concerns to subclasses.

### 16.2 Core Classes

`ChannelAdapter`:

- owns AppServer wire client;
- performs channel adapter initialize handshake;
- registers approval, delivery, channel tool, and heartbeat handlers;
- exposes `handleMessage()` for platform inbound events;
- provides hooks for platform-specific delivery, approval, streaming, and tools.

`ModuleChannelAdapter`:

- adds workspace context;
- loads module config;
- resolves state and temp paths;
- reports lifecycle status;
- supports hosted module startup via Desktop/AppServer module management.

### 16.3 Runtime Components

The channel SDK should factor reusable runtime pieces:

| Component | Responsibility |
|-----------|----------------|
| `ThreadResolver` | Resolve, resume, create, cache, and recover threads for channel identities. |
| `ChannelMessageQueue` | Serialize inbound messages per identity. |
| `CommandRouter` | Route slash commands through `command/execute` and apply reset results. |
| `TurnStreamReducer` | Consume turn notifications and merge text. |
| `SegmentBoundaryPolicy` | Decide when progressive channel delivery should flush partial agent text. |
| `DeliveryDispatcher` | Handle `ext/channel/send`. |
| `ChannelToolDispatcher` | Handle `ext/channel/toolCall`. |
| `ApprovalDispatcher` | Route `item/approval/request` to platform approval hooks. |
| `UserInputDispatcher` | Route `item/tool/requestUserInput` to platform question hooks. |
| `MediaSourcePreparer` | Normalize upload tool media sources into bytes, temporary files, URLs, or platform-ready upload references. |
| `ModuleConfigLoader` | Load and validate workspace config files. |
| `ModuleLifecycleState` | Track `stopped`, `starting`, `ready`, `configMissing`, `configInvalid`, `authRequired`, `authExpired`, and failure statuses. |

These may be exported or internal, but first-party channel packages should use them rather than duplicating equivalent logic.

### 16.4 Channel Adapter Hooks

Subclasses implement or override:

```ts
onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean>;
onApprovalRequest(request: Record<string, unknown>): Promise<ApprovalDecision>;
onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse>;
getDeliveryCapabilities(): Record<string, unknown> | null;
getChannelTools(): Record<string, unknown>[] | null;
onSend(target: string, message: Record<string, unknown>, metadata: Record<string, unknown>): Promise<Record<string, unknown>>;
onToolCall(request: Record<string, unknown>): Promise<Record<string, unknown>>;
onSegmentCompleted(threadId: string, turnId: string, segmentText: string, isFinal: boolean, channelContext: string): Promise<boolean | void>;
onTurnCompleted(threadId: string, turnId: string, replyText: string, channelContext: string, segmentsWereDelivered: boolean): Promise<void>;
onTurnFailed(threadId: string, turnId: string, error: string): Promise<void>;
onTurnCancelled(threadId: string, turnId: string): Promise<void>;
onThreadContextBound(threadId: string, channelContext: string): void;
onThreadsArchived(identityKey: string, archivedThreadIds: string[]): void;
getRuntimeAdditionalContext(): Record<string, RuntimeAdditionalContextEntry> | undefined;
```

Override `getRuntimeAdditionalContext` to bind compact adapter-owned application context through `thread/start.additionalContext`. The Channel runtime keeps the binding for active Thread reuse and restores it with one `thread/resume` after the AppServer connection is replaced.

First-party channel adapters must advertise `requestUserInputSupport` and resolve `item/tool/requestUserInput` requests. When a request contains multiple questions, chat-style adapters should ask them one at a time and aggregate the per-question answers into the protocol `UserInputResponse`. When a platform exposes stable native buttons, adapters should use them for single-question option prompts; otherwise they should display a numbered reply prompt and consume the matching inbound reply before it becomes a normal user turn. Current first-party behavior:

- Feishu/Lark: interactive card buttons for each non-secret option question; numbered/text replies for free-form and secret questions.
- Telegram: inline keyboard buttons for each non-secret option question; numbered/text replies for free-form and secret questions.
- QQ, WeCom, and Weixin: one numbered/text prompt per question using the existing text-message channel surface.

### 16.5 Channel Identity

Channel identity key:

```text
{userId}:{channelContext}
```

`userId` is the thread identity, not necessarily the physical sender. First-party group-capable adapters use the conversation as the thread identity for group/chat contexts, for example QQ `userId = "group:{groupId}"` and WeCom `userId = "chat:{chatId}"`. The physical sender is supplied separately through `sender`.

The `SessionIdentity.channelName` must match the adapter's declared channel name.

### 16.6 Sender Context

Adapters should provide per-turn sender context:

- `senderId`
- `senderName`
- `senderRole` when available
- `groupId` when the platform has a group/chat delivery target

If `groupId` is omitted, server-side delivery fallbacks may use `senderId`.
Sender context is appended to the current user message runtime context, not to the system prompt.

### 16.7 Channel Tools

Channel tools are declared during AppServer `initialize` under `capabilities.channelAdapter.channelTools`.

They are not configured in `ExternalChannels`.

Channel tool names should use PascalCase.

Display metadata may include:

- emoji `icon`;
- `title`;
- `subtitle`.

Approval metadata is descriptive and server-owned. The adapter does not make local approval policy decisions from descriptor metadata. For multi-source media tools, `approval.targetArgument` may point at an optional host-path argument: AppServer gates calls that provide that argument as a non-empty string, and skips that approval when the call uses another source such as URL, base64, or a platform file id.

### 16.8 Media Source Handling

`@dotcraft/channel/media` owns media source normalization for upload-capable channel tools.

This normalization keeps existing channel tool names and argument schemas stable. A tool may continue to expose an existing path, URL, base64, or platform-file identifier argument. The SDK converts that caller-provided source into the representation required by the target platform during `ext/channel/toolCall` handling.

When a channel tool can read a host path and can also accept URL, base64, or platform-file sources, the host path must have a dedicated argument that can be used as `approval.targetArgument`. Do not route host paths through the same overloaded argument that also accepts non-local sources, because server-side file approval is argument-based.

Media source handling uses these source categories:

- host path: a file path readable by the Node.js channel process;
- base64 data: decoded by the SDK before upload;
- URL: passed through only when the channel tool and platform allow URL sources;
- temporary file: materialized only when a platform SDK requires a local file path.

The preparer resolves the effective file name, media type, byte length, and byte content when bytes are needed. It rejects missing files, unreadable files, invalid base64 input, disallowed URL input, and sources exceeding the channel's configured size limit.

Upload-capable tools must not forward a host path to a downstream platform merely because that path exists in the DotCraft workspace. The SDK process is responsible for reading the path it can access and producing a platform-ready upload reference, byte payload, form-data body, or temporary file as appropriate.

Public helper names should describe media source or media upload preparation, not a single channel or downstream protocol. Platform-specific conversions may exist behind the helper boundary, but first-party channel packages should share the same source parsing, file-name inference, media-type inference, size checking, and error formatting.

Tool descriptions shown to agents should describe the source argument in product terms, such as a local file path, URL, or base64 payload. They should not mention adapter internals or deployment topology.

---

## 17. TypeScript Channel Modules

### 17.1 First-Party Modules

The first-party TypeScript channel packages are:

- `@dotcraft/channel-feishu`
- `@dotcraft/channel-weixin`
- `@dotcraft/channel-telegram`
- `@dotcraft/channel-qq`
- `@dotcraft/channel-wecom`

These packages depend on `@dotcraft/channel`, which depends on `@dotcraft/sdk`.

### 17.2 Module Manifest Contract

Channel modules export:

- `manifest`
- `createModule`
- optional `configDescriptors`
- optional `configGroups`
- platform-specific public utilities when useful

The module manifest includes:

- `moduleId`
- `channelName`
- `displayName`
- localized display metadata
- interface metadata
- package name
- config file name
- supported transports
- interactive setup requirement
- capability summary
- Channel contract version (`channelContractVersion`)
- supported Channel protocol versions (`supportedChannelProtocolVersions`)
- variant
- launcher descriptor

### 17.3 Configuration Descriptor Contract

`ConfigGroupDescriptor` defines an ordered configuration section with a stable, non-empty `id`, a default label, an optional description, and localized text. When `configGroups` is exported, its array order is the host display order. Group ids must be unique.

`ConfigDescriptor.group` assigns a field to a declared group. A host rejects a descriptor that references an undeclared group and omits declared groups that contain no fields. A field without `group` belongs to an implicit `Configuration` group. For compatibility, a field with `advanced: true` and no `group` belongs to an implicit `Advanced` group. Explicit `group` takes precedence over `advanced`.

Enum fields may declare structured `options`. Each `ConfigFieldOption` has a stable `value`, a default display label, optional localized display text and description, and an optional compact preview. `allowCustomValue: true` adds a host-provided custom-value path. Hosts prefer `options` over the compatibility-only `enumValues` array.

Hosts display `defaultValue` when the stored configuration omits a field. Rendering an effective default must not mutate configuration or trigger persistence; the host writes a value only after an explicit edit.

### 17.4 Workspace Context

Hosted modules receive:

```ts
interface WorkspaceContext {
  workspaceRoot: string;
  craftPath: string;
  channelName: string;
  moduleId: string;
  configOverridePath?: string;
}
```

State and temp paths are scoped to the workspace `.craft` directory.

### 17.5 Lifecycle Statuses

Supported statuses:

- `stopped`
- `starting`
- `ready`
- `configMissing`
- `configInvalid`
- `authRequired`
- `authExpired`

Failure statuses use `stopped` with a structured `ModuleError` unless a future lifecycle spec adds additional stable states.

### 17.6 Platform Behavior Preservation

Channel SDK refactors must preserve first-party behavior:

- Feishu card approvals and transcript card updates.
- Telegram long polling, commands, approval callbacks, and media tools.
- Weixin QR auth lifecycle and monitor loop.
- QQ OneBot reverse WebSocket behavior and permission checks.
- WeCom server/pusher behavior and approval routing.
- Existing structured delivery capability declarations.
- Existing channel tool names, schemas, and result shapes unless a protocol spec changes them.

---

## 18. Documentation and Examples

### 18.1 Documentation Locations

Documentation lives in VitePress:

- `docs/developing/sdk-typescript.md` (English root)
- `docs/zh/developing/sdk-typescript.md` (Chinese)
- SDK overview pages as needed
- related channel-specific SDK pages as needed

Docs must be bilingual: Chinese and English.

### 18.2 Documentation Structure

TypeScript SDK docs should include:

1. What the SDK is for.
2. Install or repository-local usage.
3. Local Hub-managed quickstart.
4. Remote WebSocket quickstart.
5. Thread API.
6. `run()` and `runStreamed()`.
7. Input parts.
8. Approval handling.
9. Runtime dynamic tools.
10. User input requests.
11. Raw wire API.
12. Channel adapter API.
13. First-party channel package map.
14. Troubleshooting.

### 18.3 Examples

At least one runnable Node.js example should demonstrate:

- local mode;
- remote mode;
- `runStreamed()`;
- approval handler;
- dynamic tool handler;
- user input handler;
- clean shutdown.

Examples should be small and copyable. Full application templates belong to a later release.

---

## 19. Testing and Conformance

### 19.1 SDK Unit Tests

Required tests:

- Hub lock parsing.
- Dead Hub lock rejection.
- Hub loopback URL validation.
- Hub startup command behavior.
- Hub ensure request and error parsing.
- SSE boundary parsing.
- stdio transport framing.
- WebSocket transport framing.
- JSON-RPC response correlation.
- JSON-RPC error conversion.
- notification registration and unregistration.
- server-initiated request dispatch.
- initialize handshake.
- raw request escape hatch.
- `run()` final result extraction.
- `runStreamed()` normalized event order.
- delta/snapshot text merge.
- explicit `turn/enqueue`.
- `TurnInProgressError`.
- approval callback.
- dynamic tool callback.
- user input callback.

### 19.2 Channel Tests

First-party channel package tests must continue to cover:

- config validation;
- module conformance;
- approval routing;
- delivery capabilities;
- channel tool descriptors;
- media tool behavior;
- media source normalization for host paths, base64 data, allowed and disallowed URLs, inferred file metadata, size limits, and platform-ready upload references;
- stream reducer behavior;
- platform-specific parsing and permission logic.

### 19.3 Workspace Validation Commands

TypeScript validation:

```bash
cd sdk/typescript
npm run typecheck:all
npm run test:all
npm run pack:verify
```

Documentation validation:

```bash
cd docs
npm run build
```

### 19.4 Conformance Helpers

`@dotcraft/sdk/testing` should provide reusable conformance suites for:

- module manifests;
- config descriptors;
- module lifecycle behavior;
- channel adapter startup failure behavior;
- delivery and tool dispatch shape.

---

## 20. Security

### 20.1 Hub Security

The SDK must only trust Hub lock files after validating:

- live process;
- loopback HTTP base URL;
- successful status probe.

Protected Hub requests include bearer authorization.

Hub tokens must not be logged by default.

### 20.2 AppServer WebSocket Tokens

When a WebSocket token is provided, it may appear in the URL query string required by AppServer WebSocket transport.

The SDK should avoid printing full WebSocket URLs with tokens in logs or error messages.

### 20.3 Approval Safety

The SDK must document that production applications should provide explicit approval handlers.

The Wire layer has no approval policy. High-level clients that advertise approval support require an explicit handler before initialization.

### 20.4 Dynamic Tools

Dynamic tool handlers execute inside the application process. The SDK should not sandbox them.

Tool authors are responsible for validating arguments, enforcing application-level authorization, and returning structured failures.

### 20.5 Channel Credentials

Channel modules own platform credentials and must keep secrets in workspace config or state according to existing channel docs. The SDK must not expose secrets through module manifests, status summaries, or logs.

---

## 21. Versioning and Compatibility

### 21.1 Channel Contract Version

The private Channel package exposes its contract version from `@dotcraft/channel/meta`:

```ts
export const CHANNEL_CONTRACT_VERSION = "1.0.0";
```

Hosted TypeScript channel modules and conformance tests use this value. SDK package and AppServer contract metadata remain available from `@dotcraft/sdk/meta`.

### 21.2 Protocol Version Compatibility

The SDK should inspect `initialize` result server info and capabilities.

High-level features must check capabilities before calling optional methods when the AppServer protocol marks them optional.

Examples:

- dynamic tool rebind requires `dynamicToolRebind`;
- command methods require `commandManagement`;
- workspace config methods require `workspaceConfigManagement`;
- model list requires `modelCatalogManagement`.

### 21.3 Breaking Package Rename

The canonical SDK name is `@dotcraft/sdk`.

`@dotcraft/sdk` and its documented subpaths define the TypeScript client SDK surface. `@dotcraft/plugin` is the separate Desktop module authoring package.

### 21.4 Stable and Unstable Surfaces

Stable:

- top-level `DotCraft` API;
- `@dotcraft/sdk/wire` transport and raw request APIs;
- channel adapter base class hooks;
- module manifest types;
- error codes;
- input part helpers.

Less stable:

- testing helper internals;
- internal stream reducer diagnostics;
- channel runtime component constructor details when they are not exported.

---

## 22. Repository Integration

### 22.1 Repository Consumers

Repository consumers import high-level APIs from `@dotcraft/sdk`, protocol-only APIs from `@dotcraft/sdk/wire`, generated types from `@dotcraft/sdk/contracts`, Hub operations from `@dotcraft/sdk/hub`, Desktop Plugin authoring contracts from `@dotcraft/plugin`, and Channel APIs from `@dotcraft/channel`. First-party packages must not import SDK, Plugin, or Channel source files through checkout-relative paths.

### 22.2 Desktop Integration

Desktop is the production reference consumer of `@dotcraft/sdk/wire`, `@dotcraft/sdk/contracts`, and `@dotcraft/sdk/hub`. It does not maintain a second JSON-RPC or Hub client.

Electron Main owns SDK connections and host policy. Preload exposes an end-to-end typed IPC boundary for known catalog methods; Renderer imports contract types only and never opens a transport. Dynamic extensions use a separately named raw IPC path that remains subject to Desktop authorization and scope checks.

The repository-local package is bundled into Electron Main and Preload output rather than externalized, so packaged applications do not depend on the checkout-relative package path.

### 22.3 Hosted Channel Module Ownership

TypeScript owns the first-party **hosted** channel module runtime (manifests, module lifecycle, Desktop-managed startup), which remains a TypeScript-only sub-profile.

---

## 23. Acceptance Contract

A complete implementation of this specification satisfies:

- `@dotcraft/sdk` is the canonical package name.
- `@dotcraft/sdk` and `@dotcraft/plugin` are public npm packages with the same version.
- Node.js 20 is the documented runtime baseline.
- Local mode starts or discovers Hub and connects to the ensured AppServer.
- Remote mode connects directly to AppServer WebSocket.
- High-level API exposes `DotCraft`, thread manager, `DotCraftThread`, `run()`, `runStreamed()`, and `enqueue()`.
- Streaming yields normalized events and preserves raw messages.
- Final run results merge delta and snapshot text without duplication.
- Approval, dynamic tool, and user-input callbacks work.
- Raw wire API remains available.
- `@dotcraft/sdk/contracts` is I/O-free and safe for type-only Renderer consumption.
- Known Wire methods are catalog-typed and unknown methods require explicit raw APIs.
- Desktop consumes the shared Wire and Hub clients without duplicate protocol implementations.
- Hub API is exported and independently testable.
- Channel SDK is factored into reusable runtime components.
- First-party TypeScript channel packages use `@dotcraft/sdk` imports.
- Existing channel behavior is preserved.
- AppServer wire DTOs, four-direction method maps, and method groups are generated from the shared Contract IR under `sdk/typescript/src/generated/appserver/`; handwritten transports and high-level APIs consume them while retaining raw fallbacks.
- TypeScript SDK and channel package test suites pass.
- Chinese and English TypeScript SDK docs are updated.
- A runnable TypeScript SDK example exists.
- The spec remains the source of truth for future SDK implementation work.

---

## 24. Future Work

Future specs or amendments may cover:

- Browser-compatible SDK build.
- Full typed wrappers for provider, model, MCP, plugin, skill, automation, memory, dreams, and workspace config management.
- Higher-level one-shot `dotcraft.run()` convenience API.
- Pluggable logger and telemetry hooks.
- Template generation for new channel modules.
