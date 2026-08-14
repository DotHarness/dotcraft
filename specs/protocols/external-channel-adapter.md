# DotCraft External Channel Adapter Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.1 |
| **Status** | Living |
| **Date** | 2026-08-14 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) (Section 15) |

Purpose: Define the architecture, protocol extensions, configuration model, and behavioral contract that allow social channel adapters written in any language to integrate with DotCraft as first-class channels, preserving per-platform capabilities such as the Approval flow.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Prerequisites](#2-prerequisites)
- [3. Architecture](#3-architecture)
- [4. Connection Modes](#4-connection-modes)
- [5. Protocol Extensions](#5-protocol-extensions)
- [6. Channel-Specific Server Methods](#6-channel-specific-server-methods)
- [7. ExternalChannelHost](#7-externalchannelhost)
- [8. ExternalChannelManager](#8-externalchannelmanager)
- [9. Configuration](#9-configuration)
- [10. Adapter Behavioral Contract](#10-adapter-behavioral-contract)
- [11. Approval Flow in External Channels](#11-approval-flow-in-external-channels)
- [12. Security](#12-security)
- [13. Reference: Telegram Adapter](#13-reference-telegram-adapter)

---

## 1. Scope

### 1.1 What This Spec Defines

- The connection modes by which an out-of-process channel adapter communicates with a DotCraft server.
- The protocol extensions to the `initialize` handshake that identify a client as a channel adapter.
- The server-to-client extension methods for message delivery, runtime tool calls, and heartbeat.
- The server-side `ExternalChannelHost`, `ExternalChannelManager`, and `ChannelRunner` components that integrate external adapters into AppServer.
- The configuration schema for declaring external channels in `config.json`.
- The behavioral contract that any conforming channel adapter must satisfy.
- The Approval flow contract for external channels.

### 1.2 What This Spec Does Not Define

- The full AppServer wire protocol (message formats, thread/turn methods, event notifications). Those are defined in [appserver-protocol.md](appserver-protocol.md).
- Platform-specific UX (how each platform renders approval prompts, messages, or commands). Those are left entirely to the adapter implementation.
- The C# implementation of native channels (QQ, WeCom). Those are in-process channels that use `ISessionService` directly and are not affected by this spec.
- SDK implementation details. This spec defines the protocol-level contract; SDK authors are free to structure their implementations as they see fit.

### 1.3 Design Principle

Flattening platform messages through a central `MessageBus` loses platform-specific capabilities in transit. For DotCraft, the Approval flow — where each platform renders its own native UI (QQ reply, WeCom push, Telegram inline keyboard) — would become impossible to implement correctly under a flattened bus.

The External Channel Adapter pattern instead makes the adapter a **full Wire Protocol client**. The adapter controls the full thread and turn lifecycle, receives all session events including bidirectional approval requests, and remains responsible for platform-specific presentation. The Wire Protocol's JSON-RPC 2.0 framing is language-agnostic, so no C# binding is required.

This follows the same full-client pattern as `dotcraft exec`: the CLI maps a one-shot command to Session Core, while an external channel adapter maps a social platform to Session Core.

---

## 2. Prerequisites

This specification depends on the following:

| Dependency | Reference | Required For |
|------------|-----------|-------------|
| AppServer wire protocol (core) | [appserver-protocol.md §1–14](appserver-protocol.md) | All connection modes |
| WebSocket Transport | [appserver-protocol.md §15](appserver-protocol.md#15-websocket-transport) | WebSocket connection mode |
| `IChannelService` abstraction | `DotCraft.Core/Channels/IChannelService.cs` | ExternalChannelHost integration |
| AppServer workspace runtime | [appserver-protocol.md](appserver-protocol.md) | Lifecycle orchestration |

The WebSocket Transport (appserver-protocol.md §15) must be implemented before `ExternalChannelHost` can use the WebSocket connection mode. The stdio subprocess connection mode reuses the existing `StdioTransport` without additional prerequisites.

---

## 3. Architecture

### 3.1 Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  DotCraft Process (AppServer)                                               │
│                                                                             │
│  ┌──────────────────────────────────────────┐                              │
│  │  Native Channels (C#, in-process)        │                              │
│  │  WeComChannelService                     │──┐                           │
│  └──────────────────────────────────────────┘  │                           │
│                                                 ▼                           │
│  ┌──────────────────────────────────────────┐  ┌──────────────────────┐   │
│  │  ExternalChannelManager                  │  │   SessionService     │   │
│  │                                          │  │   (shared)           │   │
│  │  ExternalChannelHost "channel-A"         │──│                      │   │
│  │  (subprocess/stdio transport)    ────────┘  │  AgentExecution      │   │
│  │                                          │  │  Persistence         │   │
│  │  ExternalChannelHost "channel-B"  ───────┘  └──────────────────────┘   │
│  │  (WebSocket transport)                   │                              │
│  └──────────────────────────────────────────┘                              │
└─────────────────────────────────────────────────────────────────────────────┘
         │ stdio (subprocess)          │ WebSocket
         ▼                             ▼
┌─────────────────────┐    ┌───────────────────────┐
│  Any Adapter        │    │  Any Adapter           │
│  (any language)     │    │  (any language)        │
│                     │    │                        │
│  Wire Protocol      │    │  Wire Protocol         │
│  Client             │    │  Client                │
│                     │    │                        │
│  platform SDK       │    │  platform SDK          │
└─────────────────────┘    └───────────────────────┘
         │                             │
    Platform API                  Platform API
```

> The connection mode (subprocess vs WebSocket) is an **operational** choice, not a platform requirement. Any platform adapter can use either mode. See §4.3 for guidance on which mode to choose.

### 3.2 Comparison to Native Channels

| Aspect | Native Channel (QQ/WeCom) | External Channel Adapter |
|--------|--------------------------|--------------------------|
| Language | C# | Any (Python, TypeScript, Go, …) |
| `ISessionService` | In-process direct call | Wire Protocol client |
| `IChannelService` | Implemented directly | Wrapped by `ExternalChannelHost` |
| Approval flow | `QQApprovalService`, `WeComApprovalService` | Adapter-side via `item/approval/request` |
| Lifecycle managed by | AppServer `ChannelRunner` | AppServer `ChannelRunner` via `ExternalChannelHost` |
| Platform SDK | In-process channel client | Out-of-process (subprocess or networked) |

AppServer's `ChannelRunner` treats native channels and external channels as `IChannelService` instances with the same lifecycle contract.

---

## 4. Connection Modes

### 4.1 Subprocess (managed stdio)

DotCraft spawns the adapter as a child process and communicates over the child's stdin/stdout using the standard JSONL Wire Protocol.

```
AppServer workspace runtime
  └─ ExternalChannelHost (spawn)
       ├─ stdin  ──► adapter process stdin   (JSON-RPC requests/notifications)
       └─ stdout ◄── adapter process stdout  (JSON-RPC responses/notifications)
```

- AppServer controls the adapter's lifecycle (start with the workspace runtime, stop on shutdown).
- The adapter process does not need a network port.
- `ExternalChannelHost` reuses the existing `StdioTransport`.
- `stderr` from the adapter is forwarded to DotCraft's diagnostic log stream.
- Built-in TypeScript adapters may be configured with `builtinModule` instead of a persisted absolute `command`. In this case the AppServer expands the command at runtime using Hub-provided `DOTCRAFT_NODE_BIN`, `DOTCRAFT_NODE_RUN_AS_NODE`, and `DOTCRAFT_MODULES_DIR` environment variables.

Best for: adapters that are explicitly designed for stdio and do not need to share the AppServer WebSocket listener.

### 4.2 WebSocket (external connect-out)

The adapter connects to DotCraft's existing AppServer WebSocket endpoint (appserver-protocol.md §15). The same `/ws` endpoint serves both regular AppServer clients (CLI, VS Code) and external channel adapters; the server distinguishes them by the presence of `channelAdapter` in the `initialize` handshake.

```
Adapter process
  └─ Wire Protocol WebSocket client
       └─ connects to ws://{AppServer.WebSocket.Host}:{AppServer.WebSocket.Port}/ws?token={token}
            └─ AppServerHost routes to ExternalChannelHost (via ExternalChannelRegistry)
                 └─ WebSocket connection
```

- The adapter manages its own lifecycle, deployment, and reconnection.
- DotCraft does not spawn the adapter; the adapter must be started separately.
- On connection, the adapter performs the `initialize` handshake with the `channelAdapter` capability (see §5). `AppServerHost` detects the `channelAdapter` capability and routes the connection to the corresponding `ExternalChannelHost` via `ExternalChannelRegistry`.
- No per-channel WebSocket port is needed. All external channel adapters share the AppServer WebSocket endpoint.

Best for: distributed deployments, containerized adapters, adapters that need independent scaling.

### 4.3 Managed WebSocket

DotCraft spawns the adapter as a child process but the adapter still connects back through the AppServer WebSocket endpoint. This keeps AppServer ownership of the adapter lifecycle while preserving the same WebSocket wire behavior used by externally managed adapters.

```
AppServer workspace runtime
  └─ ExternalChannelHost (spawn)
       └─ Adapter process
            └─ WebSocket client
                 └─ connects to AppServer /ws and attaches by channelAdapter.channelName
```

- DotCraft controls the adapter's lifecycle, restart policy, and diagnostic log capture.
- The adapter receives the current AppServer WebSocket endpoint through runtime environment variables.
- Adapter stdout and stderr are diagnostic logs; JSON-RPC traffic flows only through WebSocket.
- On process exit, connection loss, or heartbeat failure, DotCraft stops the child process and restarts it with backoff.

Best for: bundled TypeScript social channels that should keep running with the workspace AppServer while using WebSocket adapter protocol.

### 4.4 Choosing a Mode

The connection mode is an **operational decision** driven by deployment topology, not by which social platform is being integrated. Any platform adapter can use either mode.

Use subprocess mode when:
- All components run on the same machine.
- You want DotCraft to own the adapter's lifecycle (start, restart, stop).
- You want a minimal operational footprint with no exposed network ports.

Use WebSocket mode when:
- The adapter runs in a separate container, VM, or region.
- You need to restart or redeploy the adapter independently without restarting DotCraft.
- You want to run multiple instances of the same adapter concurrently.

---

## 5. Protocol Extensions

### 5.1 `channelAdapter` Capability

External channel adapters extend the standard `initialize` params with a `channelAdapter` capability object. When this object is present, the server treats the connection as a channel adapter and registers it with `ExternalChannelHost`.

**Extended `initialize` params**:

```json
{
  "clientInfo": {
    "name": "telegram-adapter",
    "version": "1.0.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "channelAdapter": {
      "channelName": "telegram",
      "deliveryCapabilities": {
        "structuredDelivery": true,
        "media": {
          "file": {
            "supportsHostPath": false,
            "supportsUrl": false,
            "supportsBase64": true,
            "supportsCaption": true,
            "allowedMimeTypes": ["application/pdf"]
          }
        }
      },
      "channelTools": [
        {
          "name": "TelegramSendDocumentToCurrentChat",
          "description": "Send a document to the current Telegram chat.",
          "requiresChatContext": true,
          "approval": {
            "kind": "file",
            "targetArgument": "filePath",
            "operation": "read"
          },
          "inputSchema": {
            "type": "object",
            "properties": {
              "filePath": { "type": "string" }
            },
            "required": ["filePath"]
          }
        }
      ]
    }
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `capabilities.channelAdapter` | object | yes (for channel adapters) | Identifies this connection as a channel adapter. Omit for regular clients. |
| `channelAdapter.channelName` | string | yes | The canonical channel name (e.g., `"telegram"`). Must match the name declared in server-side configuration. |
| `channelAdapter.deliveryCapabilities` | object | no | Unified delivery capability descriptor for `ext/channel/send`. Text and media delivery both use this contract. |
| `channelAdapter.channelTools` | array | no | Runtime-declared channel tool descriptors exposed to matching-origin threads for the life of this connection. |

When `channelAdapter` is present, the server records the channel name on the connection. The server responds with the standard `initialize` result (see appserver-protocol.md §3.2). No additional fields are added to the response in v1.

If the `channelName` is not recognized in the server configuration, the server closes the connection after the `initialize` response with a `system/event` notification of kind `"channelRejected"`. This prevents unauthorized adapters from registering under arbitrary channel names.

### 5.2 Backward Compatibility

`channelAdapter` is an additive field. Existing clients that do not send it are treated as regular AppServer clients (e.g. CLI, VS Code extension) and are not registered as channel adapters.

`channelTools` is also additive. Adapters that omit it behave like delivery-only integrations and will never receive `ext/channel/toolCall`.

---

## 6. Channel-Specific Server Methods

These are server-to-client extension methods (under the `ext/channel/` namespace, per appserver-protocol.md §11) used by DotCraft to push information to channel adapters.

### 6.1 `ext/channel/send`

Structured delivery request for text and media payloads.

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `target` | string | Platform-specific delivery target. |
| `message` | object | Structured outbound payload. |
| `metadata` | object? | Optional channel-specific hints. |

Standard `message.kind` values:

- `text`
- `file`
- `audio`
- `image`
- `video`

Shared `message` fields:

- `kind: string`
- `text?: string`
- `caption?: string`
- `fileName?: string`
- `mediaType?: string`
- `source?: object`

`source.kind` may be:

- `hostPath`
- `url`
- `dataBase64`
- `artifactId`

Each media capability entry under `channelAdapter.deliveryCapabilities.media` supports:

- `maxBytes?: number`
- `allowedMimeTypes?: string[]`
- `allowedExtensions?: string[]`
- `supportsHostPath: boolean`
- `supportsUrl: boolean`
- `supportsBase64: boolean`
- `supportsCaption: boolean`

**Result**:

```json
{
  "delivered": true,
  "remoteMessageId": "abc123",
  "remoteMediaId": "media_xyz",
  "errorCode": null,
  "errorMessage": null
}
```

Runtime rules:

- `ext/channel/send` is the only active remote delivery method.
- Adapters must advertise `deliveryCapabilities.structuredDelivery = true` to participate in unified channel delivery.
- The server never downgrades `text`, `file`, `audio`, `image`, or `video` to a legacy text-only method.

If an adapter advertises `maxBytes` for a media kind, it should expect the server to reject sources it cannot validate against that limit. Remote `url` media is rejected when `maxBytes` is enforced because the server does not fetch remote bytes for size inspection.

### 6.2 `ext/channel/toolCall`

Runtime tool invocation for adapter-declared channel tools.

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread in which the tool is being executed. |
| `turnId` | string | Turn that owns the tool call. |
| `callId` | string | Server-generated tool call identifier. |
| `tool` | string | Declared tool name from `channelAdapter.channelTools`. |
| `arguments` | object | Validated tool arguments matching `inputSchema`. |
| `context` | object | Current channel context (`channelName`, `channelContext`, `senderId`, `groupId`). |

**Result**:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Sent report.pdf to the current chat." }
  ],
  "structuredContent": {
    "delivered": true,
    "fileName": "report.pdf"
  }
}
```

If the tool fails, the adapter returns `{ "success": false, "errorCode": "...", "errorMessage": "..." }`.

### 6.4 `ext/channel/heartbeat`

A JSON-RPC level health probe sent by DotCraft to verify the adapter's full message-processing pipeline is responsive. This is distinct from the transport-layer WebSocket ping/pong frames.

**Direction**: server → client (request, requires response)

**Params**: `{}`

**Result**: `{}`

If the adapter does not respond within the configured timeout, `ExternalChannelHost` marks the connection as unhealthy and initiates a reconnect cycle (subprocess mode: restart the process; WebSocket mode: close connection and wait for the adapter to reconnect).

---

## 7. ExternalChannelHost

`ExternalChannelHost` is the server-side bridge component. It implements `IChannelService` and wraps a Wire Protocol connection to an external adapter process.

### 7.1 Responsibilities

- Establishing and maintaining the transport connection to the adapter.
- Running the `AppServerRequestHandler` message loop for the adapter's connection, giving the adapter full access to `ISessionService`.
- Composing that request handler with the owning workspace runtime's shared `ChatClientRegistry` and `ModelProviderRegistry`. A session-capable adapter connection must never fall back to an empty provider registry.
- Implementing unified delivery via `ext/channel/send` for both text and media.
- Forwarding injected `HeartbeatService` and `CronService` delivery events to the adapter through the negotiated delivery path.
- Monitoring adapter responsiveness via `ext/channel/heartbeat` and triggering restarts when the adapter becomes unresponsive.
- Subprocess lifecycle management (subprocess mode only): spawning, monitoring exit, and restarting with backoff.

Subprocess, WebSocket, and managed WebSocket transports use the same request-handler composition rules. Transport selection may change connection and process ownership, but it must not change workspace services, protocol extensions, model providers, bindings, or other request-processing capabilities visible to the adapter.

### 7.2 Lifecycle

```
AppServerWorkspaceRuntimeFeature.StartAsync()
  └─ ChannelRunner.BeginChannelLoops()
       └─ ExternalChannelHost.StartAsync()
        ├─ [subprocess mode] Spawn adapter process, wait for initialize handshake
        ├─ [websocket mode]  Wait for adapter to connect and complete initialize
        ├─ [managedWebsocket mode] Spawn adapter process, then wait for WebSocket attach
        └─ Run AppServerRequestHandler message loop

AppServerWorkspaceRuntimeFeature.StopAsync()
  └─ ChannelRunner.DisposeAsync()
       └─ ExternalChannelHost.StopAsync()
        ├─ [subprocess mode] Terminate adapter process
        ├─ [websocket mode]  Close WebSocket connection
        └─ [managedWebsocket mode] Close WebSocket connection and terminate adapter process
```

### 7.3 Restart Behavior (Subprocess Mode)

If the adapter process exits unexpectedly, `ExternalChannelHost` logs the exit code and restarts after a backoff delay. After a configurable number of consecutive failed starts, the channel is marked permanently failed and removed from the active channel list. While the adapter is down, `DeliverAsync` is best-effort and returns structured failure results.

### 7.4 `IChannelService` Mapping

| `IChannelService` member | `ExternalChannelHost` behavior |
|--------------------------|-------------------------------|
| `Name` | Channel name from `channelAdapter.channelName` in `initialize`. |
| `StartAsync()` | Establishes transport, performs handshake, starts message loop. |
| `StopAsync()` | Closes transport, stops message loop. |
| `DeliverAsync(target, message, metadata)` | Structured delivery entry point used for text and media. |
| `ApprovalService` | `null` — approval is handled end-to-end by the adapter via Wire Protocol. |
| `ChannelClient` | `null` — platform client is out-of-process. |
| `HeartbeatService` | Injected by the AppServer workspace runtime; delivery results are forwarded through the negotiated delivery path. |
| `CronService` | Injected by the AppServer workspace runtime; job results are forwarded through the negotiated delivery path. |

---

## 8. ExternalChannelManager

`ExternalChannelManager` reads external channel configuration and creates the corresponding `ExternalChannelHost` instances for AppServer's `ChannelRunner`.

### 8.1 Responsibilities

- Load the `"ExternalChannels"` section from `config.json` via `AppConfig.GetSection<ExternalChannelsConfig>("ExternalChannels")`.
- For each enabled external channel entry, create an `ExternalChannelHost` with the appropriate transport.
- For WebSocket-mode channels, register the channel in `ExternalChannelRegistry`. The adapter connects to the existing AppServer WebSocket endpoint (`/ws`), and `AppServerHost` routes the connection to the correct `ExternalChannelHost` by matching `channelAdapter.channelName` from the `initialize` params (see §4.2).
- Provide the created `IChannelService` instances to `ChannelRunner` alongside native channel services.

### 8.2 AppServer integration

`ChannelRunner` invokes `ExternalChannelManager` while initializing the AppServer workspace runtime. It reads configuration, creates `ExternalChannelHost` instances, and merges them with native channels. WebSocket-mode channels reuse the AppServer WebSocket endpoint (`/ws`); `AppServerHost` routes incoming `channelAdapter` connections to the corresponding `ExternalChannelHost` through `ExternalChannelRegistry`.

---

## 9. Configuration

External channels are declared in `config.json` under the `"ExternalChannels"` key. Each property name under `"ExternalChannels"` is the canonical channel name. Configuration is loaded via `AppConfig.GetSection<ExternalChannelsConfig>("ExternalChannels")`, following the same pattern as other DotCraft modules.

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

### 9.2 Examples

**Subprocess mode** — DotCraft spawns and owns the adapter process:

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "subprocess",
      "command": "python",
      "args": ["-m", "dotcraft_telegram"],
      "workingDirectory": ".",
      "env": {
        "TELEGRAM_BOT_TOKEN": "your-token-here"
      }
    }
  }
}
```

**Built-in TypeScript subprocess mode** — DotCraft resolves the bundled adapter runtime:

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "subprocess",
      "builtinModule": "channel-telegram"
    }
  }
}
```

**Managed WebSocket mode** — DotCraft owns the adapter process while the adapter connects over WebSocket:

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "managedWebsocket",
      "builtinModule": "channel-telegram"
    }
  }
}
```

**WebSocket mode** — adapter connects independently (the same adapter, different deployment):

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

The adapter connects to `ws://127.0.0.1:{AppServer.WebSocket.Port}/ws?token={AppServer.WebSocket.Token}` and presents `channelAdapter.channelName = "telegram"` in its `initialize` params. The AppServer WebSocket endpoint is configured separately under the `"AppServer"` section (see appserver-protocol.md §15).

---

## 10. Adapter Behavioral Contract

This section defines the protocol-level obligations that any conforming external channel adapter must satisfy, regardless of implementation language or SDK.

### 10.1 Initialization

- The adapter **must** send `initialize` as the first message on connection, with `capabilities.channelAdapter` present.
- The adapter **must** send the `initialized` notification after receiving the `initialize` response before making any other requests.
- `channelAdapter.channelName` **must** match the channel name declared in server-side configuration.
- `channelAdapter.channelTools`, when present, **must** be declared during `initialize`; they are not loaded from server-side `ExternalChannels` configuration.
- `capabilities.approvalSupport` **must** be `true` if the adapter will handle approval requests. If set to `false`, the server auto-resolves approvals using workspace defaults and the adapter will never receive `item/approval/request`.
- `channelTools[].approval`, when present, is a descriptive declaration of approval targets for server interception. It must not be used as an adapter-local approval policy source.

### 10.2 Thread and Turn Management

- The adapter is responsible for mapping platform identities to `SessionIdentity`. The `channelName` field in `SessionIdentity` **must** match the adapter's declared `channelName`.
- The adapter **must** use `thread/list` to locate existing threads for a given identity before creating a new one with `thread/start`. Creating duplicate threads for the same identity is a logical error.
- A paused thread must be resumed via `thread/resume` before submitting a new turn.
- The adapter **must not** call `turn/start` on a thread that already has a running turn. The server rejects this with `-32012`. The adapter should serialize user messages per thread or inform the user that the agent is busy.
- A platform reaction or similar acknowledgement only confirms that the inbound event was received; it does not indicate that a DotCraft turn was created.
- If request startup fails before `turn/start` returns a Turn ID, the adapter **must** send exactly one generic failure notification to the originating channel context. The notification must not contain the original server exception or other diagnostic details; those details belong only in channel logs. Once a Turn ID has been returned, the normal Turn event stream owns terminal status and the adapter must not emit this startup-failure notification.

### 10.3 Sender Context

- The adapter **must** populate `SenderContext` in `turn/start` with at minimum `senderId` and `senderName`. This enables correct attribution in the turn's `initiator` record and cross-channel audit logging.
- The adapter is responsible for permission checks before forwarding a message to DotCraft. DotCraft trusts the `SenderContext` presented by the adapter.
- The `groupId` field **must** be set to the platform-specific delivery target for the current chat or group (e.g. the Telegram `chat_id`). The server uses this value as the default delivery target when a cron job is created during the turn: if the cron payload does not specify a `to` field, the server falls back to `SenderContext.groupId`. Adapters that participate in unified delivery must therefore ensure `groupId` contains a value that their `ext/channel/send` implementation can accept as `target`. If no meaningful group context exists, omit `groupId`; the server will fall back to `senderId` instead.

### 10.4 Server-to-Client Requests

The adapter **must** handle the following server-initiated requests:

| Method | Required behavior |
|--------|-------------------|
| `item/approval/request` | Present platform-native approval UI; respond with `{ "decision": "..." }`. See §11. |
| `ext/channel/send` | Deliver a structured `message` payload to `target`; validate `message.kind` and source forms against the adapter's advertised capabilities. |
| `ext/channel/toolCall` | Execute a previously declared `channelTools` entry after any server-side gating implied by descriptor metadata; return structured success/failure data without mutating the declared tool set. |
| `ext/channel/heartbeat` | Respond immediately with `{}`. |

The adapter **must not** ignore these requests. Failure to respond causes the server to time out (approval: `-32020` turn failure; heartbeat: connection marked unhealthy).

### 10.5 Connection Lifecycle (WebSocket Mode)

- The adapter is responsible for reconnecting after a disconnection. It should use exponential backoff.
- After reconnection, the adapter **must** re-perform the full `initialize` / `initialized` handshake.
- Any turns that were in progress at disconnection time will have failed on the server (approval timeout or turn cancellation). The adapter should not attempt to resume those turns.

---

## 11. Approval Flow in External Channels

This section describes how the Wire Protocol's bidirectional `item/approval/request` (appserver-protocol.md §7) maps to platform-native approval UX in external channels.

### 11.1 Sequence

The approval sequence for an external channel adapter is identical to the AppServer approval flow used by out-of-process clients. The adapter plays the client-side approval role:

```
Platform User           Adapter                  DotCraft (AppServer)
      |                    |                            |
      |                    | turn/start                 |
      |                    |--------------------------->|
      |                    |                            | (agent runs...)
      |                    | item/approval/request      |
      |                    |<---------------------------|
      |  [platform-native  |                            |
      |   approval prompt] |                            |
      |<-------------------|                            |
      |                    |                            |
      | user responds      |                            |
      |------------------>|                            |
      |                    | JSON-RPC response          |
      |                    | { decision: "accept" }     |
      |                    |--------------------------->|
      |                    |                            |
      |                    | item/approval/resolved     |
      |                    |<---------------------------|
      |                    |                            | (agent continues...)
```

### 11.2 Adapter Obligations

- The adapter **must** present an approval prompt to the user on the platform using platform-native mechanisms (buttons, reply prompts, etc.).
- The adapter **must** map the platform's callback identifier to the Wire Protocol `request.id` and send the JSON-RPC response when the user responds.
- Multiple approval requests may be in flight on different threads simultaneously. The callback-to-request mapping **must** be per-request, not global.
- If the user does not respond before the server's approval timeout (`-32020`), the turn fails. The adapter should clean up any pending approval UI on timeout.

### 11.3 Decision Values

The adapter must support the five `SessionApprovalDecision` values (appserver-protocol.md §7.3). Adapters that cannot present all five may offer a simplified subset (e.g., "Approve" = `accept`, "Stop" = `cancel`). The Wire Protocol does not require every decision value to be surfaced.

### 11.4 Channel Tool Approval Metadata

Adapter-declared `channelTools` may carry an `approval` object so the server can intercept sensitive tool calls before dispatch:

- `approval.kind` identifies the server approval category. Initial standard values are `file`, `shell`, and `remoteResource`. `remoteResource` is for non-local operations (e.g. SaaS documents, wiki nodes); the server asks the user once and does not run local path/command parsing for it.
- `approval.targetArgument` names the argument that contains the primary approval target. The server applies the approval only when the runtime call provides this argument as a non-empty string. If the argument is optional and absent/blank, the approval is skipped for that call; if the argument is listed in `inputSchema.required`, absent/blank still fails before dispatch.
- `approval.operation` is an optional static operation label.
- `approval.operationArgument` is an optional argument name whose runtime value supplies the operation label.

This metadata is descriptive only:

- It does not create a separate per-channel approval policy.
- It does not replace `item/approval/request`.
- It does not let the adapter override thread or workspace approval rules.

When the server supports pre-dispatch gating for a declared approval category, the server evaluates the tool call using the same approval policy sources already used by built-in tools:

- thread `approvalPolicy`
- thread `requireApprovalOutsideWorkspace` override when relevant
- workspace defaults in `AppConfig.Tools.*`

The adapter remains responsible only for declaration and execution. The approval decision stays server-owned.

---

## 12. Security

### 12.1 Subprocess Mode

Security is provided by OS process isolation. Communication is over anonymous pipes, not network-accessible. No authentication token is needed. Adapter code runs with the same privileges as DotCraft; operators must only configure trusted adapter commands.

### 12.2 WebSocket Mode

- **Shared AppServer endpoint**: WebSocket-mode external channels reuse the AppServer WebSocket endpoint (`/ws`), which is configured under the `"AppServer.WebSocket"` section of `config.json`. No per-channel endpoint is created.
- **Loopback-only binding** (default): The AppServer WebSocket endpoint binds to `127.0.0.1`. Remote adapters cannot connect without explicit configuration.
- **Bearer token**: Required when the endpoint is exposed beyond loopback. Passed as `?token=...` in the URL (see appserver-protocol.md §15.4). The token is shared across all AppServer clients (adapters and regular clients alike).
- **Channel name verification**: The server verifies that `channelAdapter.channelName` is registered in the `"ExternalChannels"` configuration. An adapter may not register under an unknown or disabled channel name.
- **No per-adapter identity isolation**: All connections to the same server process share `ISessionService`. An authorized adapter can operate on any thread matching its `SessionIdentity`. Operators requiring strict cross-channel isolation should use separate DotCraft instances.

---

## 13. Reference: Telegram Adapter

This section describes the design intent of the reference Telegram adapter (`sdk/python/examples/telegram/`). It is provided as guidance for adapter authors, not as a normative specification.

### 13.1 Design Goals

- Use the Telegram Bot API with long polling (no public IP or webhook required).
- Map each Telegram chat (private or group) to a DotCraft thread via `SessionIdentity`.
- Stream `item/agentMessage/delta` events to buffer the agent's reply, then send the final composed message to the chat.
- Present `item/approval/request` using Telegram inline keyboard buttons, mapping Telegram `callback_query` responses back to Wire Protocol approval decisions.
- Support `/new` and `/help` slash commands, mapping them to `thread/archive` + `thread/start` and local help text respectively.

### 13.2 Key Protocol Behaviors

The Telegram adapter demonstrates the following protocol obligations defined in §10:

- **Thread management**: On each incoming message, `thread/list` is called to find the active thread for the chat's `SessionIdentity`. If none exists, `thread/start` creates one.
- **SenderContext**: The Telegram user ID and display name are forwarded as `SenderContext` on every `turn/start`. The Telegram `chat_id` is also forwarded as `groupId`, which the server uses as the default delivery target for any cron jobs created during the session.
- **Approval**: The adapter intercepts `item/approval/request` mid-stream, presents a platform-native prompt, and sends the JSON-RPC response before resuming event consumption.
- **Delivery**: `ext/channel/send` is mapped to the adapter's message-send API for both text and structured media payloads, using the stored chat ID for the given `target`.
- **Channel tools**: tool descriptors are declared during `initialize`; if the adapter declares any, it must also implement `ext/channel/toolCall` for those tools.
