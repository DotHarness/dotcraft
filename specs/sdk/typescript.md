# DotCraft TypeScript SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Related Specs** | [Unified SDK Specification](sdk.md), [AppServer Protocol](../protocols/appserver-protocol.md), [AppServer Protocol Contracts and SDK Generation](protocol-contract-generation.md), [Hub Architecture](../architecture/hub-architecture.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Session Core](../architecture/session-core.md), [Plugin Architecture](../architecture/plugin-architecture.md) |

Purpose: Define the TypeScript binding, package contract, Node.js runtime requirements, channel runtime, and compatibility strategy for `@dotcraft/sdk`.

## 1. Scope

This binding owns TypeScript packages, exports, Node.js requirements, high-level API types, event normalization, and hosted channel modules. Shared SDK semantics remain in [SDK](sdk.md) and must not be redefined here; wire fields and transport rules remain in [AppServer](../protocols/appserver-protocol.md), and local discovery follows [Hub architecture](../architecture/hub-architecture.md).

`@dotcraft/sdk` serves Node.js applications and protocol clients. `@dotcraft/channel` adds reusable platform-adapter lifecycle, queueing, stream reduction, and delivery hooks; platform integrations must not duplicate those responsibilities.

## 2. Package Contract

### 2.1 Packages

`@dotcraft/sdk` is the canonical TypeScript SDK package, published publicly on npm.

`@dotcraft/plugin` is a separate public package for authoring and building a DotCraft Plugin's Desktop module. It depends on the exact matching `@dotcraft/sdk` version so ordinary SDK applications do not acquire its React and build-tool dependencies.

### 2.2 Public Entry Points

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

### 2.3 Top-Level Exports

The top-level package exports `DotCraft`, `DotCraftThread`, `DotCraftRunResult`, `DotCraftRunEvent`, `DotCraftError`, `HubClientError`, `TurnInProgressError`, `parseAppBindingHandoff`, `appBindingToolError`, the input part helpers (§10.2), and common approval decision constants.

It does not expose Wire clients, Hub models, Channel APIs, or the complete generated DTO graph.

### 2.4 Contracts Exports

`@dotcraft/sdk/contracts` is the only home of generated protocol types: DTOs, the four direction method maps, discriminated method envelopes, method groups, and protocol information. It must not import Node.js APIs, transports, Hub helpers, or high-level SDK code.

### 2.5 Wire Exports

`@dotcraft/sdk/wire` exports JSON-RPC message models, `DotCraftWireClient`, `StdioTransport`, `WebSocketTransport`, transport errors, JSON-RPC code constants, typed request/notification/listener/server-request registration APIs, and explicit `requestRaw`, `notifyRaw`, unknown-notification, and unknown-server-request APIs. It does not re-export Contracts and exports no Run reducers or other high-level helpers.

Under the `react-native` and `browser` export conditions, `@dotcraft/sdk/wire` resolves to the same surface without `StdioTransport` and `WebSocketTransport`, so a React Native or browser bundle runs `DotCraftWireClient` over its own `Transport` with no Node.js module in the graph.

### 2.6 Hub Exports

`@dotcraft/sdk/hub` exports `HubClient`, `HubClientError`, `HubLockInfo`, `HubAppServerResponse`, `HubStatusResponse`, `HubEvent`, `findSseBoundary`, and typed request option interfaces.

### 2.7 Specialized Exports

`@dotcraft/sdk/app-binding` exports App Binding workflow helpers and `@dotcraft/sdk/dynamic-tools` exports Runtime Dynamic Tool authoring helpers. Both consume Contracts DTOs without defining synonymous wire models.

`@dotcraft/sdk/meta` exports `SDK_VERSION`, `CONTRACT_VERSION`, `APPSERVER_PROTOCOL_VERSION`, and `CONTRACT_SHA256` from package and generated metadata rather than duplicated literals.

### 2.8 Testing Exports

`@dotcraft/sdk/testing` exports fake/in-memory transport utilities and protocol fixtures. Channel conformance, manifest, config, queue, and reducer helpers belong to `@dotcraft/channel/testing`. Testing exports are public but not runtime-stable API.

## 3. Runtime Requirements

- The SDK and all first-party TypeScript channel packages target Node.js 20 or newer.
- The SDK is ESM-first: `"type": "module"`, an ESM `exports` map, and generated `.d.ts` files. CommonJS compatibility is not required.
- The SDK may depend on `ws` and Node built-in modules, and avoids framework dependencies. Channel packages may depend on platform SDKs.
- `@dotcraft/sdk/contracts` and, under the `browser` and `react-native` conditions, `@dotcraft/sdk/wire` also run in browsers and React Native over a caller-supplied `Transport`. The high-level clients, Hub discovery, and built-in transports are Node.js-only.

## 4. Connection Model

### 4.1 Local Mode

`DotCraft.local(options)` establishes a Hub-managed local connection. `DotCraftLocalOptions` takes `workspacePath`, client identity (`clientName`, `clientVersion`, `clientTitle`), `executable`, `expectedExecutable`, `binaryMatchPolicy` (`"ignore" | "restartIfMismatch" | "errorIfMismatch"`), `hubStartupTimeoutMs`, `approvalHandler`, `userInputHandler`, and `capabilities`.

Local bootstrap follows the [shared Hub profile](sdk.md#32-hub-bootstrap-profile). `DotCraft.localChat()` selects the default Chat workspace through the same flow. Closing the client closes its connection, not the Hub-managed AppServer.

### 4.2 Remote Mode

`DotCraft.remote(options)` connects directly to a running AppServer WebSocket endpoint. `DotCraftRemoteOptions` takes `url`, optional `token`, client identity, handlers, and `capabilities`. A separate `token` is appended as the `token` query parameter unless the URL already includes one.

### 4.3 Direct Stdio Mode

Direct stdio AppServer process management is not a high-level default. It remains available through `@dotcraft/sdk/wire` for specialized subprocess clients and adapters.

### 4.4 Initialize Capabilities

High-level clients send `approvalSupport`, `requestUserInputSupport`, `streamingSupport`, and `configChange`. Advertising `approvalSupport` or `requestUserInputSupport` requires the matching handler before initialization; otherwise connection fails with a stable configuration error.

Channel adapters additionally send `capabilities.channelAdapter` as defined in [External Channel Adapter](../protocols/external-channel-adapter.md).

## 5. Hub Client

The Hub client implements the [shared Hub bootstrap profile](sdk.md#32-hub-bootstrap-profile). Lock fields, discovery checks, authenticated endpoints, and ensure responses are owned by [Hub architecture](../architecture/hub-architecture.md).

When Hub is not live, the SDK starts `dotcraft hub`, resolving the binary from the explicit `executable`, then a host-provided resolver, then `dotcraft` on PATH. The child process is detached, hidden on Windows, and not connected to the parent stdio streams.

The SDK consumes `GET /v1/events` with bearer authorization. SSE parsing accepts both `\n\n` and `\r\n\r\n` frame boundaries; malformed frames are ignored, with optional debug diagnostics.

## 6. Wire Client

The Wire client follows the [shared connection contract](sdk.md#42-wire-client). It exposes generated typed operations and explicit raw APIs, with no Thread, Run, or callback policy.

The transport abstraction is message-oriented:

```ts
interface Transport {
  readMessage(): Promise<Record<string, unknown>>;
  writeMessage(message: Record<string, unknown>): Promise<void>;
  close(): Promise<void>;
  connect?(): Promise<void>;
}
```

A transport that implements `connect()` is opened by `DotCraftWireClient.connect()` and reopened by auto-reconnect; one without it is used as supplied and is not reconnected. Any read failure ends the connection: the client becomes `disconnected`, rejects requests already written, and starts auto-reconnect when enabled. `WebSocketTransport` accepts messages up to `maxPayload` (default 128 MiB); a larger message closes the socket.

Stdio and WebSocket implement [AppServer framing](../protocols/appserver-protocol.md#22-transports). A transport owns one socket or stream; `DotCraftWireClient` owns reconnect and protocol readiness. For adapters spawned by DotCraft, stdin receives server messages, stdout sends adapter messages, and diagnostics go to stderr.

Known methods are compile-time constrained by generated maps (`client.request("thread/read", params)`); unknown extensions use `requestRaw` / `notifyRaw`. Notifications and server requests likewise have separate typed and raw registration APIs.

## 7. High-Level Application API

### 7.1 `DotCraft`

`DotCraft` represents one initialized AppServer connection. It has static `local`, `localChat`, and `remote` constructors; `serverInfo`, `capabilities`, and `threads`; `request(method, params)` as the high-level raw escape hatch delegating to the wire client; `on(event, handler)`; and `close()`.

### 7.2 Thread Manager

`ThreadManager` provides `getOrCreate`, `start`, `resume`, `list`, `read` (returns `SessionThread`), `listTurns`, and `listItems`. History pages accept `cursor`, `limit`, and `sortDirection`; item pages also accept `turnId`.

`getOrCreate()` lists reusable active threads for the identity before creating a new thread.

### 7.3 Default Identity

When a caller omits identity fields, the SDK uses:

| Field | Default |
|-------|---------|
| `channelName` | `sdk` |
| `userId` | current OS username when available, otherwise `local-user` |
| `channelContext` | omitted |

Applications that persist cross-user sessions should specify `userId` explicitly.

## 8. Thread API

`DotCraftThread` represents one server-backed thread with `id` and `identity`, and provides `snapshot`, `refresh`, `listTurns`, `listItems`, `subscribe`, `unsubscribe`, `run`, `runStreamed`, `enqueue`, `interrupt`, `setMode`, `archive`, `delete`, and `onToolCall(namespace, name, handler)`.

The thread caches the latest known snapshot and updates it when a mutation or lifecycle notification provides a new thread payload. `refresh()` forces a header read; persisted Turns and Items are read separately through bounded history pages.

`subscribe()` maps to `thread/subscribe`. `runStreamed()` may use a scoped subscription or temporary event stream, but must not deliver duplicate events when the connection already holds an active subscription for the thread, per the AppServer at-most-once rule.

## 9. Run API

### 9.1 Options and Result

`RunOptions` takes `sender`, `collectRawEvents`, `abortSignal`, and `enqueueIfBusy`. Without `enqueueIfBusy`, a server `TurnInProgress` response becomes `TurnInProgressError`.

`DotCraftRunResult` contains `thread`, `turn`, `text`, `items`, optional `usage`, and optional `rawEvents`. `text` is the merged assistant reply (§11.3).

### 9.2 `run()`

`run()` prepares input, subscribes or prepares event capture before `turn/start`, starts the turn, consumes events until `turn/completed`, `turn/failed`, or `turn/cancelled`, and returns the result. `turn/failed` raises `TurnFailedError` by default.

### 9.3 `runStreamed()`

`runStreamed()` prepares event capture before `turn/start`, starts the turn, yields normalized events as they arrive, and yields one terminal event with the final result or failure. If the caller stops iterating before the run is terminal, the SDK unregisters its handlers but does not interrupt the server turn unless the caller aborts or calls `interrupt()`.

### 9.4 Abort Semantics

An `abortSignal` triggered after `turn/start` succeeds calls `turn/interrupt`. One triggered before `turn/start` rejects without sending a request.

## 10. Input Model

### 10.1 Accepted Input

```ts
type RunInput =
  | string
  | InputPart[]
  | { input: InputPart[]; sender?: SenderContext };
```

### 10.2 Input Part Helpers

`textPart(text)`, `imageDataUrlPart(dataUrl)`, `localImagePart(path, options?)`, `skillRefPart(name)`, and `commandRefPart(rawText)` produce the AppServer wire shape.

### 10.3 Slash Commands and Skills

The high-level API does not interpret text starting with `/` or parse `$skill` text into `skillRef` parts. Applications call `command/execute` through the wire API or their own command layer, and use `skillRefPart()` directly.

Channel adapters route slash commands through `CommandRouter`: only names and aliases from `command/list` are commands ([External Channel Adapter](../protocols/external-channel-adapter.md#74-commands)).

## 11. Streaming Event Model

### 11.1 Normalized Event Shape

Every normalized event includes `type`, `threadId`, optional `turnId`, and the `raw` JSON-RPC message.

### 11.2 Event Types

| Type | Source |
|------|--------|
| `thread_started` | `thread/started` |
| `thread_resumed` | `thread/resumed` |
| `thread_archived` | `thread/archived` |
| `thread_unarchived` | `thread/unarchived` |
| `thread_status_changed` | `thread/status/changed` |
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
| `raw` | Any subscribed notification matching the thread filter and not otherwise normalized. |

### 11.3 Text Merging

Deltas accumulate per agent message item. A final snapshot is preferred when it contains the complete text and is at least as long as the deltas. Overlapping delta and snapshot content is never duplicated. The channel stream reducer uses the same merge semantics.

High-level application runs do not require segment boundaries; applications receive raw delta events and a final merged result.

## 12. Callback Capabilities

### 12.1 Server-Initiated Request Dispatch

The high-level client handles `item/approval/request`, `item/tool/call`, and `item/tool/requestUserInput`. The channel package handles `ext/channel/heartbeat`, `ext/channel/send`, and `ext/channel/toolCall`.

### 12.2 Handlers

- `ApprovalHandler` maps an `ApprovalRequest` to an `ApprovalDecision`, following the [shared callback contract](sdk.md#45-callbacks).
- `UserInputHandler` maps a `UserInputRequest` to a `UserInputResponse`. The client registers this server-request method only when a handler is configured; otherwise the Wire layer returns JSON-RPC `-32601`.
- `DynamicToolHandler` maps a `DynamicToolCallRequest` to a `DynamicToolCallResult`, following [Runtime Dynamic Tools](sdk.md#44-runtime-dynamic-tools).

Handlers may return a value or a promise.

Start and resume options accept `additionalContext?: Record<string, { kind: "application"; value: string }>` for thread-bound runtime app context when the server advertises `runtimeAdditionalContext`; an empty object on resume clears it.

### 12.3 Heartbeat

Channel adapters always answer `ext/channel/heartbeat` with `{}`. Regular application clients may also answer `{}` if the server sends it.

## 13. Error Model

All SDK-specific errors inherit from `DotCraftError`, which carries a stable `code` and optional `cause`. Server JSON-RPC errors are `JsonRpcError` with `rpcCode`, `rpcMessage`, and optional `data`; its message prefers `error.data.detail` when available.

Required typed errors:

| Error | Condition |
|-------|-----------|
| `HubClientError` | Hub discovery, startup, status, or request failure. |
| `TransportError` | Transport-level read/write/open failure. |
| `TransportClosed` | Transport closed while reads or writes were pending. |
| `InitializationError` | AppServer initialize handshake failed. |
| `RequestTimeoutError` | A request exceeded its timeout before the server answered. |
| `ReconnectQueueFullError` | Requests queued during reconnect exceeded the queue bound. |
| `TurnInProgressError` | Server rejected turn start due to active turn or maintenance. |
| `ThreadNotFoundError` | Server reports thread missing. |
| `ThreadNotActiveError` | Server reports thread cannot accept turns. |
| `TurnFailedError` | Agent execution failed after `turn/start` succeeded. |
| `TurnCancelledError` | Turn was cancelled before successful completion. |

Error `code` strings are stable API; message text may evolve.

## 14. Channel Package

### 14.1 Purpose

`@dotcraft/channel` is a repository-internal package, not published to npm. It lets TypeScript packages integrate messaging platforms with DotCraft as external channels. It depends on `@dotcraft/sdk`; the SDK does not depend on it. It owns reusable behavior and leaves platform-specific concerns to subclasses.

| Entry point | Purpose |
|-------------|---------|
| `@dotcraft/channel` | Channel adapter base classes and module authoring contract. |
| `@dotcraft/channel/runtime` | Reusable channel runtime components. |
| `@dotcraft/channel/media` | Media source normalization for `ext/channel/send` delivery. |
| `@dotcraft/channel/testing` | Shared manifest, configuration, lifecycle, and dispatch conformance contract for first-party modules. |
| `@dotcraft/channel/meta` | Channel contract version metadata. |

### 14.2 Runtime Components

`ChannelAdapter` owns the AppServer wire client, performs the channel adapter initialize handshake, registers approval, delivery, channel tool, and heartbeat handlers, exposes `handleMessage()` for platform inbound events, and provides the hooks in §14.3.

`ModuleChannelAdapter` adds workspace context, module config loading, state and temp path resolution, lifecycle status reporting, and hosted module startup through Desktop/AppServer module management.

The Channel package owns thread resolution and caching, per-identity inbound serialization, slash command routing through `command/execute`, inbound routing to `turn/steer`, `turn/enqueue` or `turn/start` ([External Channel Adapter](../protocols/external-channel-adapter.md#72-thread-and-turn-management)), turn activity reduction, the generic Turn failure notice, `ext/channel/send` and `ext/channel/toolCall` dispatch, approval and user-input dispatch, plain-text approval reply parsing and pending-approval tracking, media source normalization, module config loading and validation, and module lifecycle state. First-party channel packages use these rather than duplicating equivalent logic.

### 14.3 Channel Adapter Hooks

Subclasses implement or override:

```ts
onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean>;
onApprovalRequest(request: Record<string, unknown>): Promise<ApprovalDecision>;
onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse>;
getDeliveryCapabilities(): Record<string, unknown> | null;
getChannelTools(): Record<string, unknown>[] | null;
onSend(target: string, message: Record<string, unknown>, metadata: Record<string, unknown>): Promise<Record<string, unknown>>;
onToolCall(request: Record<string, unknown>): Promise<Record<string, unknown>>;
onTurnStarted(turn: ChannelTurn): Promise<void>;
onActivity(turn: ChannelTurn, activity: TurnItemActivity): Promise<void>;
onTurnRetrying(turn: ChannelTurn): Promise<void>;
onTurnEnded(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void>;
onThreadContextBound(threadId: string, channelContext: string): void;
onCurrentThreadChanged(previousThreadId: string | undefined, threadId: string, channelContext: string): void;
renderCommandChoices(target: string, choices: CommandChoice[], context: CommandChoiceContext): Promise<void>;
getRuntimeAdditionalContext(): Record<string, RuntimeAdditionalContextEntry> | undefined;
```

`ChannelTurn` carries `threadId`, `turnId` and `channelContext`. The Turn hooks fire only for Turns the adapter started with `turn/start`; input steered or queued into another Turn produces none. They observe activity only: no hook receives `agentMessage` text, reasoning, plans or generated images. `onTurnRetrying` fires for `system/event` with `kind = "streamError"`. `onTurnEnded` fires once per started Turn with `completed`, `failed` (with the error, for logs only), `cancelled`, or `disconnected` when the event stream ends without a terminal event. The default `onTurnEnded` delivers one generic failure notice without diagnostic detail through `onDeliver` for a `failed` outcome; an adapter that shows a status card overrides it.

`onCurrentThreadChanged` fires when a command result switches the conversation to another thread. `renderCommandChoices` presents a command result's choices after its message; the default delivers a numbered list showing each choice's command through `onDeliver`, and adapters with buttons or cards override it and execute the selected choice with `executeCommandChoice`.

The runtime accepts `/approve`, `/approve session` and `/deny` as plain-text approval replies alongside the reply keywords. Adapters that collect approvals with buttons resolve the same replies through `parseApprovalReplyDecision` and answer only the approval that `approvalSender(request)` attributes to the replying sender: the sender of the Turn the adapter started, otherwise the only sender of a direct or per-sender thread.

`getRuntimeAdditionalContext` binds compact adapter-owned application context through `thread/start.additionalContext`. The runtime keeps the binding for active Thread reuse and restores it with one `thread/resume` after the AppServer connection is replaced.

First-party channel adapters advertise `requestUserInputSupport` and resolve `item/tool/requestUserInput`. Chat-style adapters ask multiple questions one at a time and aggregate the answers into the protocol `UserInputResponse`. Single-question option prompts use stable native buttons when the platform has them; otherwise a numbered reply prompt, whose matching inbound reply is consumed before it becomes a normal user turn. Secret questions always use a text reply.

### 14.4 Channel Identity

The channel identity key is `{userId}:{channelContext}`. `userId` is the thread identity, not necessarily the physical sender: first-party group-capable adapters use the conversation as the thread identity for group/chat contexts, for example QQ `userId = "group:{groupId}"` and WeCom `userId = "chat:{chatId}"`. `SessionIdentity.channelName` must match the adapter's declared channel name.

Adapters pass the platform sender separately through `sender`, following [External Channel Adapter](../protocols/external-channel-adapter.md#73-sender-context).

### 14.5 Channel Tools

Adapters declare tools through `capabilities.channelAdapter.channelTools` during initialization, following the [AppServer descriptor and approval contract](../protocols/appserver-protocol.md#32-initialize). These are connection capabilities, not `ExternalChannels` configuration. The SDK forwards declarations and returns callback results without making approval-policy decisions.

### 14.6 Media Source Handling

`@dotcraft/channel/media` implements the [shared media source contract](sdk.md#47-media-source-handling) in the Node.js process and may materialize temporary files when a platform SDK requires a local path.

`saveInboundImage(craftPath, bytes, mediaType)` persists an inbound platform image as the [workspace-managed attachment](../protocols/external-channel-adapter.md#72-thread-and-turn-management) the adapter submits as a `localImage` part.

A tool that accepts both host paths and non-local sources must expose the host path in a dedicated argument usable as `approval.targetArgument`; an overloaded path/URL argument cannot enforce the server's argument-based file approval.

## 15. TypeScript Channel Modules

### 15.1 First-Party Modules

The first-party channel packages are `@dotcraft/channel-feishu`, `@dotcraft/channel-weixin`, `@dotcraft/channel-telegram`, `@dotcraft/channel-qq`, and `@dotcraft/channel-wecom`. They depend on `@dotcraft/channel`.

### 15.2 Module Manifest Contract

Channel modules export `manifest`, `createModule`, optional `configDescriptors` and `configGroups`, and platform-specific public utilities when useful.

The manifest includes `moduleId`, `channelName`, `displayName`, localized display metadata, interface metadata, package name, config file name, supported transports, interactive setup requirement, capability summary, `channelContractVersion`, `supportedChannelProtocolVersions`, variant, and launcher descriptor.

### 15.3 Configuration Descriptor Contract

`ConfigGroupDescriptor` defines an ordered configuration section with a unique, non-empty `id`, a default label, an optional description, and localized text. When `configGroups` is exported, its array order is the host display order.

`ConfigDescriptor.group` assigns a field to a declared group. A host rejects a descriptor that references an undeclared group and omits declared groups with no fields. A field without `group` belongs to an implicit `Configuration` group, or to an implicit `Advanced` group when it sets `advanced: true`; explicit `group` takes precedence.

Enum fields may declare structured `options`. Each `ConfigFieldOption` has a stable `value`, a default display label, optional localized display text and description, and an optional compact preview. `allowCustomValue: true` adds a host-provided custom-value path. Hosts prefer `options` over the `enumValues` array.

List fields may also declare `options`; the stored value is then the array of chosen option values, and the host offers a choice among them instead of free text. The descriptor's `optionGroups` arrange them for display: each group has a unique `id`, a default label, an optional description, localized text, and the `values` of the options it holds. A value may belong to several groups, and choosing it in one chooses it in all; groups display in array order. A host drops saved values that no option names when the field is saved.

An adapter may report which option values the platform currently allows for a field by answering `ext/channel/configOptionAvailability` ([External Channel Adapter](../protocols/external-channel-adapter.md#44-extchannelconfigoptionavailability)). `ChannelAdapter.onConfigOptionAvailability(key)` returns the allowed values, or `null` when unknown; the default returns `null`.

Hosts display `defaultValue` when the stored configuration omits a field. Rendering an effective default must not mutate configuration; the host writes a value only after an explicit edit.

### 15.4 Workspace Context

Hosted modules receive a `WorkspaceContext` with `workspaceRoot`, `craftPath`, `channelName`, `moduleId`, and optional `configOverridePath`. State and temp paths are scoped to the workspace `.craft` directory.

### 15.5 Lifecycle Statuses

Supported statuses are `stopped`, `starting`, `ready`, `configMissing`, `configInvalid`, `authRequired`, and `authExpired`. Failure statuses use `stopped` with a structured `ModuleError`.

### 15.6 Platform Behavior Contract

Each first-party module owns platform behavior that the Channel runtime must not absorb or alter: Feishu card approvals and the Turn status card; Telegram long polling, commands, approval callbacks, and the typing indicator; Weixin QR auth lifecycle and monitor loop; QQ OneBot reverse WebSocket behavior and permission checks; WeCom server/pusher behavior and approval routing. Structured delivery capability declarations, channel tool names, schemas, and result shapes change only when a protocol spec changes them.

## 16. Security

The binding inherits [shared SDK security](sdk.md#8-security). Platform credentials remain channel-owned and must not appear in module manifests, status summaries, or logs. Runtime Dynamic Tool handlers execute with the host application's privileges.

## 17. Versioning and Compatibility

### 17.1 Channel Contract Version

`@dotcraft/channel/meta` exports `CHANNEL_CONTRACT_VERSION`, the Channel package's own contract version. Hosted modules declare it as `channelContractVersion` in their manifest, and the conformance suite asserts the match. SDK and AppServer contract metadata come from `@dotcraft/sdk/meta`.

### 17.2 Protocol Version Compatibility

The SDK inspects `initialize` server info and capabilities. High-level features check capabilities before calling methods the AppServer protocol marks optional, for example `dynamicToolRebind`, `commandManagement`, `workspaceConfigManagement`, and `modelCatalogManagement`.

### 17.3 Stable and Unstable Surfaces

Stable: the top-level `DotCraft` API, `@dotcraft/sdk/wire` transport and raw request APIs, channel adapter base class hooks, module manifest types, error codes, and input part helpers.

Less stable: testing helper internals, internal stream reducer diagnostics, and non-exported channel runtime component constructors.

## 18. Repository Integration

First-party packages import SDK, Plugin, and Channel APIs through their package entry points (§2.2, §14.1, `@dotcraft/plugin`), never through checkout-relative source paths.
