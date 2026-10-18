# DotCraft External Channel Adapter Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.10 |
| **Status** | Living |
| **Date** | 2026-10-18 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) (Section 15) |

Purpose: Define the architecture, protocol extensions, configuration model, and behavioral contract that allow social channel adapters written in any language to integrate with DotCraft as first-class channels, preserving per-platform capabilities such as the Approval flow.

## Table of Contents

- [1. Scope](#1-scope)
- [4. Connection Modes](#4-connection-modes)
- [5. Protocol Extensions](#5-protocol-extensions)
- [6. Channel-Specific Server Methods](#6-channel-specific-server-methods)
- [7. Adapter host lifecycle](#7-adapter-host-lifecycle)
- [8. Workspace registration](#8-workspace-registration)
- [9. Configuration](#9-configuration)
- [10. Adapter Behavioral Contract](#10-adapter-behavioral-contract)
- [11. Approval Flow in External Channels](#11-approval-flow-in-external-channels)
- [12. Security](#12-security)

## 1. Scope

An external channel adapter is a full AppServer client. It maps platform conversations to thread identities, controls turns, receives events, and presents approval and user-input requests through the platform. AppServer owns session execution and workspace services; the adapter owns platform credentials and presentation.

[AppServer](appserver-protocol.md) owns framing, initialization, channel descriptors, delivery, and tool-call wire shapes. This specification owns adapter process/connection lifetime, configuration, and platform-facing obligations. The choice of transport is operational and does not limit which platform an adapter supports.

## 4. Connection Modes

### 4.1 Subprocess (managed stdio)

DotCraft spawns the adapter as a child process and communicates over the child's stdin/stdout using the standard JSONL Wire Protocol.

- AppServer controls the adapter's lifecycle (start with the workspace runtime, stop on shutdown).
- The adapter process does not need a network port.
- `stderr` from the adapter is forwarded to DotCraft's diagnostic log stream.
- Built-in TypeScript adapters may be configured with `builtinModule` instead of a persisted absolute `command`. In this case the AppServer expands the command at runtime using Hub-provided `DOTCRAFT_NODE_BIN`, `DOTCRAFT_NODE_RUN_AS_NODE`, and `DOTCRAFT_MODULES_DIR` environment variables.

### 4.2 WebSocket (external connect-out)

The adapter connects to DotCraft's existing AppServer WebSocket endpoint (appserver-protocol.md §15). The same `/ws` endpoint serves both regular AppServer clients (CLI, VS Code) and external channel adapters; the server distinguishes them by the presence of `channelAdapter` in the `initialize` handshake.

- The adapter manages its own lifecycle, deployment, and reconnection.
- DotCraft does not spawn the adapter; the adapter must be started separately.
- On connection, the adapter performs the `initialize` handshake with the `channelAdapter` capability (see §5). `AppServerHost` detects the `channelAdapter` capability and routes the connection to the corresponding `ExternalChannelHost` via `ExternalChannelRegistry`.
- No per-channel WebSocket port is needed. All external channel adapters share the AppServer WebSocket endpoint.

### 4.3 Managed WebSocket

DotCraft spawns the adapter as a child process but the adapter still connects back through the AppServer WebSocket endpoint. This keeps AppServer ownership of the adapter lifecycle while preserving the same WebSocket wire behavior used by externally managed adapters.

- DotCraft controls the adapter's lifecycle, restart policy, and diagnostic log capture.
- The adapter receives the current AppServer WebSocket endpoint through runtime environment variables.
- Adapter stdout and stderr are diagnostic logs; JSON-RPC traffic flows only through WebSocket.
- On process exit, connection loss, or heartbeat failure, DotCraft stops the child process and restarts it with backoff.

## 5. Protocol Extensions

### 5.1 `channelAdapter` Capability

External channel adapters extend the standard `initialize` params with a `channelAdapter` capability object. When this object is present, the server treats the connection as a channel adapter and registers it with `ExternalChannelHost`.

The descriptor and delivery-capability shapes are defined by [AppServer initialization](appserver-protocol.md#32-initialize). An adapter supplies its configured `channelName`, its platform `displayName`, its delivery capabilities, and optional connection-owned `channelTools`.

When `channelAdapter` is present, the server records the channel name on the connection. The server responds with the standard `initialize` result (see appserver-protocol.md §3.2). No additional fields are added to the response in v1.

If the `channelName` is not recognized in the server configuration, the server closes the connection after the `initialize` response with a `system/event` notification of kind `"channelRejected"`. This prevents unauthorized adapters from registering under arbitrary channel names.

### 5.2 Backward Compatibility

`channelAdapter` is an additive field. Existing clients that do not send it are treated as regular AppServer clients (e.g. CLI, VS Code extension) and are not registered as channel adapters.

Every adapter serves `ext/channel/send`. `channelTools` is optional; an adapter that omits it never receives `ext/channel/toolCall`.

## 6. Channel-Specific Server Methods

These are server-to-client extension methods (under the `ext/channel/` namespace, per appserver-protocol.md §11) used by DotCraft to push information to channel adapters.

### 6.1 `ext/channel/send`

Adapters implement the [AppServer structured delivery contract](appserver-protocol.md#1121-extchannelsend). Delivery capability negotiation applies to text and media; the server does not downgrade unsupported media to a text-only method. An adapter remembers recent `deliveryId` values and answers a repeated id with the first delivery's result without posting again.

### 6.2 `ext/channel/toolCall`

Adapters execute declared tools using the [AppServer channel tool-call contract](appserver-protocol.md#1122-extchanneltoolcall). They return structured success or failure and remain responsible for platform execution.

### 6.4 `ext/channel/heartbeat`

A JSON-RPC level health probe sent by DotCraft to verify the adapter's full message-processing pipeline is responsive. This is distinct from the transport-layer WebSocket ping/pong frames.

**Direction**: server → client (request, requires response)

**Params**: `{}`

**Result**: `{}`

If the adapter does not respond within the configured timeout, `ExternalChannelHost` marks the connection as unhealthy and initiates a reconnect cycle (subprocess mode: restart the process; WebSocket mode: close connection and wait for the adapter to reconnect).

## 7. Adapter host lifecycle

The workspace AppServer owns the adapter host. Every transport shares the same workspace Session, provider, command, binding, and extension services. Reconnecting an observing client does not restart or replace adapters.

The host routes delivery and tool calls, monitors heartbeat responsiveness, and owns subprocess startup/restart when configured. Scheduled results use the ordinary channel delivery path; the adapter does not own a scheduler.

### 7.2 Lifecycle

The host starts or accepts the configured transport and waits for `initialize` / `initialized` before publishing the adapter as ready. Stopping closes the connection and terminates only a process owned by the host.

An initialized adapter connection becomes the host's current tool binding after the adapter sends `initialized`. The binding owns the connection's immutable `channelTools` declarations and server-to-adapter tool-call route. Disconnecting clears that binding; a reconnect creates a new binding even when it attaches to the same `ExternalChannelHost`. While no connection is attached, threads keep the declarations of the last initialized connection in their tool lists, so an adapter restart does not change them; dispatch through those registrations fails as unavailable.

An adapter may implement a `channelTool` directly or delegate its execution to a bounded companion process that it owns. In both cases the declaration, route, approval metadata, configuration, and execution authority remain connection-owned. A companion child process does not become an AppServer service and cannot outlive or bypass the declaring connection lease.

A Turn freezes the binding selected when its effective tool snapshot is built. If that binding disconnects or the host is replaced, dispatch through the frozen snapshot fails as unavailable and must not be redirected to the replacement connection. The binding change invalidates cached thread agents so the next Turn can select the new ready connection. The server must not automatically retry a channel tool on another connection because the first connection may have performed side effects before disconnecting.

### 7.3 Restart Behavior (Subprocess Mode)

If the adapter process exits unexpectedly, `ExternalChannelHost` logs the exit code and restarts after a backoff delay. A start attempt succeeds only after the adapter completes the `initialize` / `initialized` handshake; transport EOF or process exit before that point is a failed start and must not reset the consecutive-failure count. After a configurable number of consecutive failed starts, the channel is marked permanently failed and removed from the active channel list. While the adapter is down, `DeliverAsync` is best-effort and returns structured failure results.

## 8. Workspace registration

The workspace creates hosts for enabled `ExternalChannels` entries. WebSocket connections attach through the existing AppServer `/ws` endpoint and match `channelAdapter.channelName` to a configured host. Native and external channels participate in the same workspace lifecycle.

## 9. Configuration

External channels are declared in `config.json` under the `"ExternalChannels"` key. Each property name under `"ExternalChannels"` is the canonical channel name.

### 9.1 Schema

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `enabled` | boolean | yes | Whether this channel is active. |
| `transport` | string | yes | `"subprocess"`, `"websocket"`, or `"managedWebsocket"`. |
| `command` | string | if `subprocess`/`managedWebsocket` and `builtinModule` is absent | Command to start the adapter process. |
| `builtinModule` | string | if `subprocess`/`managedWebsocket` and `command` is absent | Built-in TypeScript module directory name; DotCraft expands the runtime command from Hub-provided runtime hints. |
| `args` | string[] | no | Additional command-line arguments. |
| `workingDirectory` | string | no | Working directory for the subprocess. Defaults to workspace root. |
| `env` | object | no | Additional environment variables passed to the subprocess. |

> **WebSocket mode note**: WebSocket-mode channels reuse the existing AppServer WebSocket endpoint (configured under `"AppServer.WebSocket"`). The adapter connects to `ws://{host}:{port}/ws?token={token}` using the AppServer's host, port, and token settings. No per-channel port or token configuration is needed — the adapter is identified by `channelAdapter.channelName` during the `initialize` handshake.

> **Managed WebSocket mode note**: `managedWebsocket` uses the same AppServer WebSocket endpoint but DotCraft starts the adapter process. DotCraft injects the active endpoint into the adapter's process environment, so persisted adapter config must not be treated as the source of truth for the current AppServer port or token.

> **Runtime declaration note**: `ExternalChannels` configuration only tells DotCraft how to start or accept the adapter connection. Structured delivery capabilities and `channelTools` are declared by the adapter itself during `initialize`; they are not static config fields in `config.json`.

## 10. Adapter Behavioral Contract

This section defines the protocol-level obligations that any conforming external channel adapter must satisfy, regardless of implementation language or SDK.

### 10.1 Initialization

- The adapter **must** send `initialize` as the first message on connection, with `capabilities.channelAdapter` present.
- The adapter **must** send the `initialized` notification after receiving the `initialize` response before making any other requests.
- `channelAdapter.channelName` **must** match the channel name declared in server-side configuration.
- `channelAdapter.displayName` **must** name the platform as people know it.
- `channelAdapter.deliveryCapabilities` **must** declare `text` and `targets`, and a `media` entry for every attachment kind its `ext/channel/send` delivers. `targets` **must** describe only chats the adapter can actually reach.
- `channelAdapter.channelTools`, when present, **must** be declared during `initialize`; they are not loaded from server-side `ExternalChannels` configuration.
- A companion executable used by a `channelTool` **must** be resolved, configured, started, cancelled, and drained by the adapter; AppServer must not acquire product-specific process knowledge from the declaration.
- `capabilities.approvalSupport` **must** be `true` if the adapter will handle approval requests. If set to `false`, the server auto-resolves approvals using workspace defaults and the adapter will never receive `item/approval/request`.
- `channelTools[].approval`, when present, is a descriptive declaration of approval targets for server interception. It must not be used as an adapter-local approval policy source.

### 10.2 Thread and Turn Management

- The adapter is responsible for mapping platform identities to `SessionIdentity`. The `channelName` field in `SessionIdentity` **must** match the adapter's declared `channelName`.
- The adapter **must** use `thread/list` to locate existing threads for a given identity before creating a new one with `thread/start`. Creating duplicate threads for the same identity is a logical error.
- A thread's `channelContext` **must** be a target the adapter's `ext/channel/send` accepts: the chat the thread's messages come from, never a sender's direct chat when the messages come from a group.
- The adapter **must** send `chat` ([AppServer Protocol](appserver-protocol.md#4103-channel-chat)) when it starts or resumes a thread, with the chat's kind, its label when the platform provides one, and the thread's membership. It resumes a thread it reuses after connecting, so the binding exists on the current connection.
- Inbound input for a thread with an active regular Turn goes to that Turn with `turn/steer`, using the active Turn ID the adapter observed. When the active Turn cannot accept steering, or blocking thread maintenance is active, the adapter queues the input with `turn/enqueue`. Otherwise it starts a Turn with `turn/start`. When `turn/start` is rejected with `TurnInProgress` (`-32012`) because another Turn started first, the adapter steers the input into that Turn instead. The adapter does not retry or delay `turn/start` while a Turn runs.
- An inbound platform image is downloaded by the adapter, saved with a unique file name under the workspace's `.craft/attachments/images/`, and submitted as a `localImage` part, so the agent can reach the file by path after the Turn. The adapter never deletes it; Session Core owns the file's lifetime like any workspace-managed attachment. Platform image URLs are never forwarded.
- A platform reaction or similar acknowledgement only confirms that the inbound event was received; it does not indicate that a DotCraft turn was created.
- If request startup fails before `turn/start` returns a Turn ID, the adapter **must** send exactly one generic failure notification to the originating channel context. The notification must not contain the original server exception or other diagnostic details; those details belong only in channel logs. Once a Turn ID has been returned, the normal Turn event stream owns terminal status and the adapter must not emit this startup-failure notification.
- The adapter does not deliver `agentMessage` text, reasoning, plans, generated images or any other Item to the chat. People in the chat see only what the agent sends through `SendChannelMessage`.
- The adapter may show activity it owns: an acknowledgement reaction once an inbound message is accepted and, for a Turn it started, a typing indicator while the Turn runs and a status card. A status card is posted in the originating chat only when the Turn is still running 20 seconds after it started. It shows `working`, is patched without notification to `retrying` while the server retries the model stream (`system/event` with `kind = "streamError"`) and back to `working` when work resumes, and settles on `done`, `failed`, or `stopped` for a cancelled Turn, with the elapsed time, when the Turn ends. The card is never withdrawn. A Turn that completes or is cancelled within 20 seconds produces none; one that fails within 20 seconds posts the card directly as `failed`. Activity is never reported to the model.
- An adapter that shows no status card sends exactly one short generic notice to the originating chat when a Turn it started fails. The notice carries no diagnostic detail, and a cancelled Turn produces none.

### 10.3 Sender Context

- The adapter **must** populate `SenderContext` in `turn/start` with at minimum `senderId` and `senderName`. This enables correct attribution in the turn's `initiator` record and cross-channel audit logging.
- The adapter is responsible for permission checks before forwarding a message to DotCraft. DotCraft trusts the `SenderContext` presented by the adapter.
- The adapter sets `senderRole = "admin"` only for senders on its configured admin list and forwards every other permitted sender as `"user"`. An adapter whose platform lets only the bot's owner reach it has no admin list and forwards every sender as `"admin"`. It drops messages from senders it does not permit without forwarding them.
- The adapter **should** set `directTarget` to the sender's direct chat in its `ext/channel/send` target syntax whenever it can deliver there, including for messages that arrive in a group.
- The `groupId` field identifies the group a message came from and is omitted for direct chats. The default delivery target of a Turn, captured as the origin of an automation created during it, is the thread's `channelContext`.

### 10.4 Commands

- A message is a command only when its first word is the name or alias of a command `command/list` returns. Any other text, including text such as a path that starts with `/`, is ordinary input.
- The adapter runs a command with `command/execute` on the conversation's current thread as soon as it arrives. Commands bypass the adapter's per-conversation message queue, whether or not a Turn is active and whether or not the adapter has already resolved the thread; the adapter resolves the thread first when it has none cached. A command arriving while an approval is pending for the sender is executed, not answered with the approval reminder.
- The adapter handles each inbound message in this order: an answer to the sender's pending approval, including the slash replies (§11.2); then a command; then an answer to a pending user-input question; then ordinary input. A command never answers a pending question.
- When a result carries `thread`, the adapter makes it the conversation's current thread and resumes it with the conversation's `chat` before forwarding further input to it.
- When a result carries `expandedPrompt`, the adapter submits the sender's original text as a `commandRef` input like any other inbound message.
- The adapter shows `message`, followed by `choices` when present: with buttons or cards when the platform has interactive controls, where selecting one executes its `command` for the selecting sender, otherwise as a numbered list that shows each choice's `command`.
- An adapter whose platform has a native command menu fills it from `command/list`, showing commands with `requiresAdmin` only to admins. Where the platform scopes menus by chat or chat kind, the adapter uses those scopes; where it cannot scope a menu to the senders allowed to use a command, that command is left out of the menu.
- Adapter-local commands are limited to platform setup, such as `/bind` and adapter-owned account authorization. Conversation lifecycle commands such as `/new` are server commands.

### 10.5 Server-to-Client Requests

The adapter **must** handle the following server-initiated requests:

| Method | Required behavior |
|--------|-------------------|
| `item/approval/request` | Present platform-native approval UI; respond with `{ "decision": "..." }`. See §11. |
| `ext/channel/send` | Deliver a structured `message` payload to `target`; validate `message.kind` and source forms against the adapter's advertised capabilities; answer an unreachable or malformed `target` with `UnsupportedTarget`; answer a repeated `deliveryId` with the first result. |
| `ext/channel/toolCall` | Execute a previously declared `channelTools` entry after any server-side gating implied by descriptor metadata; return structured success/failure data without mutating the declared tool set. |
| `ext/channel/heartbeat` | Respond immediately with `{}`. |

The adapter **must not** ignore these requests. An unanswered approval stays pending until a client answers it or its turn ends ([AppServer Protocol](appserver-protocol.md) §7.6); an unanswered heartbeat marks the connection unhealthy.

### 10.6 Connection Lifecycle (WebSocket Mode)

- The adapter is responsible for reconnecting after a disconnection. It should use exponential backoff.
- After reconnection, the adapter **must** re-perform the full `initialize` / `initialized` handshake.
- Turns that were in progress at disconnection time keep running on the server, and their pending approvals stay pending. The adapter should not attempt to resume those turns; resuming or subscribing to their threads replays any approval still pending.
- A reconnect replaces the adapter's connection-owned `channelTools` binding. Tool snapshots bound to the previous connection are revoked immediately and are never dispatched through the replacement connection.

## 11. Approval Flow in External Channels

This section describes how the Wire Protocol's bidirectional `item/approval/request` (appserver-protocol.md §7) maps to platform-native approval UX in external channels.

### 11.1 Sequence

The adapter plays the client role in the [AppServer approval flow](appserver-protocol.md#7-approval-flow), translating the prompt and decision into the platform's interaction model.

### 11.2 Adapter Obligations

- The adapter **must** present an approval prompt to the user on the platform using platform-native mechanisms (buttons, reply prompts, etc.).
- The adapter **must** map the platform's callback identifier to the Wire Protocol `request.id` and send the JSON-RPC response when the user responds.
- Multiple approval requests may be in flight on different threads simultaneously. The callback-to-request mapping **must** be per-request, not global.
- The server has no approval timeout. When a request resolves without the adapter's answer, for example because its turn ended, the adapter receives `item/approval/resolved` and should clean up its pending approval UI.
- Every adapter accepts `/approve`, `/approve session` and `/deny` from the sender as answers to that sender's pending approval in the conversation, mapping them to `accept`, `acceptForSession` and `decline`, in addition to its buttons or reply keywords. These replies are not registry commands.
- An approval belongs to the sender who started the Turn that raised it. In a thread that holds one sender's messages, such as a direct chat or a group thread per sender, it belongs to that sender. The slash replies never answer another sender's approval; buttons answer the approval they were posted for.
- An adapter that collects decisions from plain-text replies accepts each reply keyword with or without a leading `/`. While an approval is pending for a sender in a conversation, that sender's other messages in the conversation are not forwarded, except commands (§10.4); the adapter answers each with a short reminder of the accepted replies. When the adapter's own approval wait expires, it responds `cancel` and tells the approver that the approval timed out.

### 11.3 Decision Values

The adapter must support the five `SessionApprovalDecision` values (appserver-protocol.md §7.3). Adapters that cannot present all five may offer a simplified subset (e.g., "Approve" = `accept`, "Stop" = `cancel`). The Wire Protocol does not require every decision value to be surfaced.

### 11.4 Channel Tool Approval Metadata

Adapters declare risk metadata using the [AppServer approval descriptor](appserver-protocol.md#32-initialize). AppServer applies thread/workspace policy before dispatch; adapters execute the call without creating a separate approval policy.

## 12. Security

### 12.1 Subprocess Mode

Security is provided by OS process isolation. Communication is over anonymous pipes, not network-accessible. No authentication token is needed. Adapter code runs with the same privileges as DotCraft; operators must only configure trusted adapter commands.

### 12.2 WebSocket Mode

Endpoint authentication follows [AppServer WebSocket authentication](appserver-protocol.md#154-authentication). The server accepts only configured, enabled channel names.

Adapters share the workspace Session authority rather than receiving a per-adapter isolation boundary. Operators requiring strict cross-channel isolation use separate DotCraft instances.
