# DotCraft AppServer Protocol Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.15 |
| **Status** | Living |
| **Date** | 2026-06-01 |
| **Parent Spec** | [Session Core](../core/session-core.md) (Section 20) |
| **Related Specs** | [Interactive Tool UI](tool-result-presentation.md) |

Purpose: Define a language-neutral JSON-RPC wire protocol that exposes Session Core (`ISessionService`) and related AppServer capabilities to out-of-process clients, enabling them to create and resume threads, submit turns, stream events, participate in approval flows, and call server-level management methods through one transport-stable contract.

## Table of Contents

- [1. Scope](#1-scope)
- [1.4 V1 Contract Snapshot](#14-v1-contract-snapshot)
- [2. Protocol Fundamentals](#2-protocol-fundamentals)
- [3. Initialization](#3-initialization)
- [4. Thread Methods](#4-thread-methods)
  - [4.15 Thread Goal Methods](#415-thread-goal-methods)
  - [4.19 Worktree Methods](#419-worktree-methods)
- [5. Turn Methods](#5-turn-methods)
  - [5.4 `welcome/suggestions`](#54-welcomesuggestions)
- [6. Event Notifications](#6-event-notifications)
  - [6.5 SubAgent Notifications](#65-subagent-notifications)
  - [6.6 Usage Notifications](#66-usage-notifications)
  - [6.7 System Notifications](#67-system-notifications)
- [6.8 Plan Notifications](#68-plan-notifications)
- [6.10 Notification Delivery Guarantees](#610-notification-delivery-guarantees)
- [7. Approval Flow](#7-approval-flow)
- [8. Error Handling](#8-error-handling)
- [9. Backpressure](#9-backpressure)
- [10. Notification Opt-Out](#10-notification-opt-out)
- [11. Extension Methods](#11-extension-methods)
- [12. Versioning and Compatibility](#12-versioning-and-compatibility)
- [13. Full Turn Example](#13-full-turn-example)
  - [13.1 ACP client turn (extension proxy)](#131-acp-client-turn-extension-proxy)
  - [13.2 Standard wire turn (no ACP)](#132-standard-wire-turn-no-acp)
- [15. WebSocket Transport](#15-websocket-transport)
- [16. Cron Management Methods](#16-cron-management-methods)
- [17. Heartbeat Management Methods](#17-heartbeat-management-methods)
- [18. Skills Management Methods](#18-skills-management-methods)
- [19. Command Management Methods](#19-command-management-methods)
- [20. Channel Status Methods](#20-channel-status-methods)
- [21. Model Catalog Methods](#21-model-catalog-methods)
- [22. MCP Management Methods](#22-mcp-management-methods)
- [23. External Channel Management Methods](#23-external-channel-management-methods)
- [24. SubAgent Profile Management Methods](#24-subagent-profile-management-methods)
- [25. Workspace Config Methods](#25-workspace-config-methods)
- [26. Memory Management Methods](#26-memory-management-methods)
- [27. Dreams Management Methods](#27-dreams-management-methods)
- [27A. Usage Telemetry Methods](#27a-usage-telemetry-methods)
- [28. Protocol Ownership](#28-protocol-ownership)

---

## 1. Scope

### 1.1 What This Spec Defines

This specification defines the wire protocol — message formats, methods, notifications, and transport rules — that a DotCraft server exposes to external clients over stdio or WebSocket. It is primarily the network-facing projection of the Session Core `ISessionService` API, and additionally covers server-level management operations that are exposed on the same JSON-RPC surface.

### 1.2 What This Spec Does Not Define

- **Domain model semantics**: Thread, Turn, and Item lifecycle rules, persistence layout, and state machine invariants are defined in the [Session Core Specification](../core/session-core.md). This spec references them but does not redefine them.
- **Agent execution internals**: Model orchestration, tool invocation internals, hook execution, and other host-side implementation details are not part of this wire protocol.
- **Channel-specific UX**: How a client renders events, approvals, or status is a client concern.
- **Host implementation patterns**: In-process adapter wiring, dependency injection structure, persistence layout, and runtime service composition are internal to the server and not part of this wire protocol.

### 1.3 Protocol Ownership

This protocol is DotCraft's language-neutral JSON-RPC contract for projecting Session Core to out-of-process clients. The Thread/Turn/Item primitives, event streaming, and bidirectional approval flow are defined by this specification and the Session Core specification.

### 1.4 V1 Contract Snapshot

The current v1 contract is based on the refactored Session Core, not on the earlier draft assumptions. For implementation planning, features fall into three buckets:

| Bucket | V1 Items |
|-------|----------|
| **Guaranteed in v1** | Rich approval decisions (`accept`, `acceptForSession`, `acceptAlways`, `decline`, `cancel`), thread-scoped event subscription, accurate per-turn origin/initiator metadata, strict `historyMode` rules, separate wire DTO serialization with camelCase enums and lossless delta typing. Cron management methods (`cron/list`, `cron/remove`, `cron/enable`, `cron/run`) with the `cronManagement` server capability flag. Heartbeat trigger method (`heartbeat/trigger`) with the `heartbeatManagement` capability flag. Skills management methods (`skills/list`, `skills/read`, `skills/view`, `skills/restoreOriginal`, `skills/setEnabled`, `skills/uninstall`) with the `skillsManagement` / `skillVariants` capability flags. Command management methods (`command/list`, `command/execute`) with the `commandManagement` capability flag. Channel status method (`channel/status`) with the `channelStatus` capability flag. Provider management methods (`provider/list`, `provider/create`, `provider/update`, `provider/delete`, `provider/test`) with the `providerManagement` capability flag. Model catalog method (`model/list`) with the `modelCatalogManagement` capability flag. MCP management methods (`mcp/list`, `mcp/get`, `mcp/upsert`, `mcp/remove`, `mcp/status/list`, `mcp/test`) with the `mcpManagement` / `mcpStatus` capability flags. External channel management methods (`externalChannel/list`, `externalChannel/get`, `externalChannel/upsert`, `externalChannel/remove`) with the `externalChannelManagement` capability flag. SubAgent profile management methods (`subagent/profiles/list`, `subagent/settings/update`, `subagent/profiles/setEnabled`, `subagent/profiles/upsert`, `subagent/profiles/remove`) with the `subAgentManagement` capability flag. Session-backed SubAgent child-thread listing, mailbox send, follow-up task, and close methods with the `subAgentSessions` capability flag. Workspace config update method (`workspace/config/update`) with the `workspaceConfigManagement` capability flag. Dreams workspace memory methods (`dreams/status`, `dreams/run`, `dreams/create`, `dreams/get`, `dreams/list`, `dreams/cancel`, `dreams/apply`, `dreams/discard`, `dreams/archive`) with the `dreams` capability flag. |
| **Guaranteed with narrowed semantics** | `thread/list` is deterministic and supports optional cursor pagination; archived threads are excluded by default and included only via an explicit filter. `thread/read` supports optional cursor pagination for turn history while preserving full-history reads for legacy clients. |
| **Deferred from v1** | Structured extension capability registry beyond a flat namespace advertisement. Clients must treat extension namespaces as optional and discoverable, not required for core Session behavior. |

**Multi-client thread lists**: In deployments with multiple concurrent connections, server-broadcast notifications in [Section 6.1](#61-thread-notifications) include `thread/started`, `thread/deleted`, `thread/renamed`, and `thread/runtimeChanged` so clients can keep both thread lists and per-thread activity indicators (running, waiting-on-approval, waiting-on-plan-confirmation) synchronized without polling or subscribing to every thread's event stream.

---

## 2. Protocol Fundamentals

### 2.1 JSON-RPC 2.0

The wire protocol uses **JSON-RPC 2.0** with the `"jsonrpc": "2.0"` header included on every message.

Three message kinds:

| Kind | Has `id` | Has `method` | Direction |
|------|----------|--------------|-----------|
| Request | yes | yes | either |
| Response | yes | no | reply to request |
| Notification | no | yes | either |

- **Client-to-server requests**: thread and turn lifecycle operations.
- **Server-to-client notifications**: event stream (thread/turn/item events).
- **Server-to-client requests**: approval prompts that require a client response.
- **Client-to-server notifications**: `initialized` handshake acknowledgement.

### 2.2 Transports

| Transport | Wire Format | Status |
|-----------|-------------|--------|
| stdio | Newline-delimited JSON (JSONL): one complete JSON-RPC message per line, UTF-8 encoded, over stdin (client→server) and stdout (server→client). | Primary |
| WebSocket | One JSON-RPC message per WebSocket text frame. | Experimental |

**stdio transport**: The server reads JSON-RPC requests from `stdin` and writes responses/notifications to `stdout`. Diagnostic and log output goes to `stderr`. Stdio is a 1:1 transport — exactly one client per server process.

**WebSocket transport**: When listening on `ws://HOST:PORT/ws`, the server supports multiple concurrent client connections. Each connection is fully independent and maintains its own initialization state and thread subscriptions. Full behavior is specified in [Section 15](#15-websocket-transport).

### 2.3 Serialization Rules

- All JSON property names use **camelCase** (e.g., `threadId`, `tokenUsage`, `approvalType`).
- Timestamps are **ISO 8601 UTC** strings (e.g., `"2026-03-15T10:00:00Z"`).
- Enums are serialized as **camelCase strings** (e.g., `"active"`, `"running"`, `"toolCall"`, `"waitingApproval"`).
- Nullable fields are omitted from the JSON when `null`, unless explicitly stated otherwise.
- Wire DTOs are distinct from the on-disk persistence models. Persisted thread JSON may keep internal compatibility quirks; the wire contract must remain lossless and transport-stable.
- AppServer projects Session Core `item/delta` events to specific wire methods (`item/agentMessage/delta`, `item/reasoning/delta`, `item/toolCall/argumentsDelta`, `item/commandExecution/outputDelta`). Delta notifications that can represent multiple logical kinds carry `deltaKind`.
- `id` fields in JSON-RPC messages may be strings or integers. The server preserves the type and value when responding.

---

## 3. Initialization

### 3.1 Handshake

The client must send an `initialize` request as the very first message on a new connection. Any other method sent before initialization is rejected with error code `-32002` (`"Not initialized"`). Repeated `initialize` calls on the same connection are rejected with error code `-32003` (`"Already initialized"`).

After receiving the `initialize` response, the client must send an `initialized` notification to signal readiness. The server may begin sending notifications (e.g., for in-progress threads) after receiving `initialized`.

```
Client                              Server
  |                                   |
  | initialize (request, id: 0)      |
  |---------------------------------->|
  |                                   |
  | (response, id: 0)                |
  |<----------------------------------|
  |                                   |
  | initialized (notification)        |
  |---------------------------------->|
  |                                   |
  | (protocol ready, both directions) |
```

### 3.2 `initialize`

**Direction**: client → server (request)

**Params**:

```json
{
  "clientInfo": {
    "name": "dotcraft-client",
    "title": "DotCraft Client",
    "version": "1.0.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "configChange": true,
    "optOutNotificationMethods": [],
    "acpExtensions": {
      "fsReadTextFile": true,
      "fsWriteTextFile": true,
      "terminalCreate": true,
      "extensions": ["_unity"]
    },
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
          "display": {
            "icon": "📎",
            "title": "Send document to current Telegram chat"
          },
          "inputSchema": {
            "type": "object",
            "properties": {
              "fileName": { "type": "string" }
            },
            "required": ["fileName"]
          },
          "deferLoading": true
        }
      ]
    }
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `clientInfo.name` | string | yes | Machine-readable client identifier. |
| `clientInfo.title` | string | no | Human-readable client name. |
| `clientInfo.version` | string | yes | Client version string. |
| `capabilities.approvalSupport` | boolean | no | Whether the client can handle server-initiated approval requests. Default `true`. |
| `capabilities.requestUserInputSupport` | boolean | no | Whether the client can handle model-initiated Plan Mode question requests (`item/tool/requestUserInput`). Default `false`. |
| `capabilities.streamingSupport` | boolean | no | Whether the client can consume `item/*/delta` notifications. Default `true`. |
| `capabilities.commandExecutionStreaming` | boolean | no | Whether the client can consume `commandExecution` items and `item/commandExecution/outputDelta` fallback notifications. Default `false`. |
| `capabilities.toolExecutionLifecycle` | boolean | no | Whether the client can consume `toolExecution` lifecycle items for per-call runtime completion. Default `false`. |
| `capabilities.backgroundTerminals` | boolean | no | Whether the client can consume `terminal/*` terminal notifications for server-managed shell processes. Default `false`. |
| `capabilities.configChange` | boolean | no | Whether the client wants `workspace/configChanged` notifications. Default `true`. |
| `capabilities.interactiveToolUi` | boolean | no | Whether the client can render Interactive Tool UI (MCP Apps): a tool's `ui://` resource in a sandboxed iframe driven by the `ui/*` bridge. Default `false`. When not declared, the server does not honor `ui/*` host methods (`ui/resource/read`, `ui/tool/call`, `ui/open-link`, `ui/update-model-context`, `item/widget-state/set`) for the connection, and the client receives the tool-result text fallback. See [tool-result-presentation.md](tool-result-presentation.md). |
| `capabilities.optOutNotificationMethods` | string[] | no | Exact notification method names to suppress for this connection. See [Section 10](#10-notification-opt-out). |
| `capabilities.channelAdapter` | object | no | External channel adapter metadata. When present, the connection is treated as the remote backend for one unified channel runtime. See [external-channel-adapter.md](external-channel-adapter.md). |
| `capabilities.acpExtensions` | object | no | ACP tool proxy capabilities. When present, the client can handle server-initiated `ext/acp/*` requests. See [Section 11.2](#112-acp-tool-proxy). Default omitted (no ACP support). |
| `capabilities.nodeRepl` | object | no | Persistent Node REPL capability. When present with `browserUse`, the client can handle server-initiated `ext/nodeRepl/*` requests for thread-bound local browser automation. Default omitted (no browser automation support). |
| `capabilities.browserUse` | object | no | Browser automation capability. When present with `nodeRepl`, the Node REPL is backed by one or more client browser backends such as Desktop embedded browser tabs or the Chrome extension backend. Default omitted (no browser automation support). |

`capabilities.configChange` is an opt-out capability. When omitted, the server treats it as `true` and may push `workspace/configChanged` notifications. Modern clients should declare it explicitly for clarity, even when using the default behavior.

**`acpExtensions` object** (when present):

| Field | Type | Description |
|-------|------|-------------|
| `fsReadTextFile` | boolean | Client can handle `ext/acp/fs/readTextFile`. |
| `fsWriteTextFile` | boolean | Client can handle `ext/acp/fs/writeTextFile`. |
| `terminalCreate` | boolean | Client can handle `ext/acp/terminal/*` methods. |
| `extensions` | string[] | Custom extension families the client implements (e.g. `["_unity"]`). Server may send `ext/acp/<family>/<method>` for each advertised family. |

**`nodeRepl` object** (when present):

| Field | Type | Description |
|-------|------|-------------|
| `backend` | string | Client runtime identifier, currently `desktop-node`. |

**`browserUse` object** (when present):

| Field | Type | Description |
|-------|------|-------------|
| `backend` | string | Preferred single client browser backend identifier, currently `desktop-iab`. Clients that send `backends` should keep this field set to their preferred backend. |
| `backends` | string[] | Optional list of client browser backends. Current values include `desktop-iab`; Chrome extension clients use `chrome-extension`. When omitted, servers treat `backend` as the only backend. |
| `protocolVersion` | number | Browser IAB protocol version. Current value is `2`. |
| `supportsCancel` | boolean | Optional. When `true`, the client handles `ext/nodeRepl/cancel` for in-flight evaluations. |
| `browserSessionProtocolVersion` | number | Optional. Browser session metadata protocol version supported by the client. Chrome M2/M3 clients use `1`. |
| `supportsCommandCancel` | boolean | Optional. When `true`, browser commands carry command ids and can be cooperatively cancelled independently of the outer Node REPL request. |
| `maxBrowserResultBytes` | number | Optional. Maximum serialized browser command result bytes before the client rejects oversized results. |
| `defaultCommandTimeoutMs` | number | Optional. Default browser command timeout used when a command omits `timeoutMs`. |
| `maxCommandTimeoutMs` | number | Optional. Maximum accepted browser command timeout after clamping. |
| `supportsTypedFinalize` | boolean | Optional. When `true`, `browser.tabs.finalize({ keep })` requires typed keep entries with `handoff` or `deliverable` status. |
| `supportsChromeDiagnostics` | boolean | Optional. When `true`, the Chrome backend can surface safe setup, discovery, command, and cancellation diagnostic summaries. |

**`channelAdapter` object** (when present):

| Field | Type | Description |
|-------|------|-------------|
| `channelName` | string | Canonical external channel name (for example `telegram`, `feishu`). |
| `deliveryCapabilities` | object | Structured delivery capability descriptor for the remote backend. |
| `channelTools` | array | Optional channel tool descriptors declared by the adapter during `initialize`. These descriptors are the wire projection of the unified channel tool model. |

**`deliveryCapabilities` object**:

| Field | Type | Description |
|-------|------|-------------|
| `structuredDelivery` | boolean | Whether the adapter can receive `ext/channel/send`. |
| `media` | object | Optional media capability map keyed by delivery kind (`file`, `audio`, `image`, `video`). |

Each media capability entry supports:

- `maxBytes?: number`
- `allowedMimeTypes?: string[]`
- `allowedExtensions?: string[]`
- `supportsHostPath: boolean`
- `supportsUrl: boolean`
- `supportsBase64: boolean`
- `supportsCaption: boolean`

Each `channelTools` descriptor supports:

- `name: string`
- `description: string`
- `inputSchema: object`
- `outputSchema?: object`
- `display?: { icon?: string, title?: string, subtitle?: string }`
- `requiresChatContext: boolean`
- `approval?: { kind: string, targetArgument: string, operation?: string, operationArgument?: string }`
- `deferLoading?: boolean`

Channel tool names should use PascalCase. For cross-runtime icon support, adapters should prefer declaring emoji icons via `channelTools[].display.icon`.

`deferLoading` requests lazy tool exposure. When the active provider supports native deferred tool loading, such as the Responses API with `nativeDeferredToolLoading`, the server may omit the tool from the top-level model tool list and expose it later through the native `tool_search` flow. Otherwise the server may simulate deferred loading with DotCraft's ordinary local tool-search mechanism.

When `approval` is present, it is a descriptive risk declaration rather than an adapter-owned policy block:

- `approval.kind` identifies the server approval category. Initial standard values are `file`, `shell`, and `remoteResource`. `remoteResource` targets non-local resources (e.g. third-party SaaS documents or wiki nodes); the server asks the user once and does not run path/command parsing for it.
- `approval.targetArgument` names the tool argument that contains the primary approval target, such as `filePath` or `workingDirectory`.
- `approval.operation` is an optional static label forwarded to the server approval layer.
- `approval.operationArgument` is an optional argument name whose value is forwarded as the operation string.
- Policy resolution remains server-owned. The adapter must not treat descriptor metadata as a private approval configuration source.

### 3.2.1 Unified Channel Model

DotCraft internally models built-in channels and external adapters through the same runtime concepts:

- `ChannelDeliveryCapabilities`
- `ChannelToolDescriptor`
- `ChannelOutboundMessage`
- `ExtChannelToolCallContext` (unified channel execution context)
- `ExtChannelToolCallResult` (unified channel tool result)

Built-in channels do not negotiate these capabilities over `initialize`; they provide equivalent runtime objects in-process. External adapters expose the same model through `capabilities.channelAdapter`, `ext/channel/send`, and `ext/channel/toolCall`.

**Result**:

```json
{
  "serverInfo": {
    "name": "dotcraft",
    "version": "0.2.0",
    "protocolVersion": "1",
    "extensions": ["acp"]
  },
  "capabilities": {
    "threadManagement": true,
    "threadFork": true,
    "threadSubscriptions": true,
    "approvalFlow": true,
    "requestUserInput": true,
    "modeSwitch": true,
    "configOverride": true,
    "cronManagement": true,
    "heartbeatManagement": true,
    "skillsManagement": true,
    "pluginManagement": true,
    "skillVariants": true,
    "runtimeAdditionalContext": true,
    "gitWorktrees": true,
    "appBinding": true,
    "appContextBlocks": true,
    "commandManagement": true,
    "modelCatalogManagement": true,
    "workspaceConfigManagement": true,
    "mcpManagement": true,
    "mcpServerOrigins": true,
    "externalChannelManagement": true,
    "mcpStatus": true,
    "extensions": {
      "welcomeSuggestions": true
    }
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `serverInfo.name` | string | Always `"dotcraft"`. |
| `serverInfo.version` | string | DotCraft server version. |
| `serverInfo.protocolVersion` | string | Wire protocol version. Currently `"1"`. |
| `serverInfo.extensions` | string[] | Optional flat list of available extension namespaces. Structured extension capability metadata is deferred from v1. |
| `capabilities.threadManagement` | boolean | Server supports thread CRUD operations. |
| `capabilities.threadFork` | boolean | Server supports creating conversation branches with `thread/fork`. |
| `capabilities.threadSubscriptions` | boolean | Server supports passive `thread/subscribe` observers independent from `turn/start`. |
| `capabilities.threadGoals` | boolean | Server supports the complete thread goal runtime contract: `thread/goal/*` control methods, goal notifications, prompt-visible goal context, usage accounting, budget transitions, and model goal tools. Automatic idle continuation still depends on server config. |
| `capabilities.manualCompaction` | boolean | Server supports manual context compaction with `thread/compact/start`. |
| `capabilities.manualMemoryConsolidation` | boolean | Server supports manual long-term memory consolidation with `thread/memory/consolidate/start`. |
| `capabilities.dynamicToolRebind` | boolean | Server supports rebinding Runtime Dynamic Tools to the current client connection via `thread/resume.dynamicTools`. |
| `capabilities.runtimeAdditionalContext` | boolean | Server supports thread-bound runtime context supplied by the AppServer client through `thread/start.additionalContext` and `thread/resume.additionalContext`. |
| `capabilities.gitWorktrees` | boolean | Server supports DotCraft-managed Git worktree methods (`worktree/createAndFork`, `worktree/createAndStart`, `thread/worktree/handoff`, `worktree/list`, `worktree/status`). |
| `capabilities.appBinding` | boolean | Server supports App Binding methods (`app/*` and `thread/appBindings/*`). |
| `capabilities.appContextBlocks` | boolean | Server supports App Binding context block methods (`app/binding/context/upsert`, `app/binding/context/remove`, and `thread/appContextBlocks/list`). |
| `capabilities.appThreadInputEnqueue` | boolean | Server supports App Binding-safe app-triggered queued input via `app/threadInput/enqueue`. |
| `capabilities.approvalFlow` | boolean | Server may send approval requests. |
| `capabilities.requestUserInput` | boolean | Server may expose the root-thread `RequestUserInput` tool and send `item/tool/requestUserInput` requests to capable clients. |
| `capabilities.modeSwitch` | boolean | Server supports `thread/mode/set`. |
| `capabilities.configOverride` | boolean | Server supports `thread/config/update`. |
| `capabilities.cronManagement` | boolean | Server supports cron job management methods (`cron/list`, `cron/remove`, `cron/enable`, `cron/run`). Absent or `false` when the cron service is not configured. |
| `capabilities.heartbeatManagement` | boolean | Server supports heartbeat management methods (`heartbeat/trigger`). Absent or `false` when the heartbeat service is not configured. |
| `capabilities.skillsManagement` | boolean | Server supports skills management methods (`skills/list`, `skills/read`, `skills/view`, `skills/restoreOriginal`, `skills/setEnabled`, `skills/uninstall`). |
| `capabilities.pluginManagement` | boolean | Server supports plugin management methods (`plugin/list`, `plugin/view`, `plugin/install`, `plugin/remove`, `plugin/setEnabled`). |
| `capabilities.skillVariants` | boolean | Server has skill variants enabled for the current runtime. Clients may use effective skill views and restore source-skill behavior (`skills/view`, `skills/restoreOriginal`) without exposing variant internals. |
| `capabilities.commandManagement` | boolean | Server supports command management methods (`command/list`, `command/execute`). |
| `capabilities.providerManagement` | boolean | Server supports personal model provider management methods (`provider/list`, `provider/create`, `provider/update`, `provider/delete`, `provider/test`). |
| `capabilities.modelCatalogManagement` | boolean | Server supports model catalog methods (`model/list`). |
| `capabilities.workspaceConfigManagement` | boolean | Server supports workspace configuration methods (`workspace/config/schema`, `workspace/config/update`). |
| `capabilities.memoryManagement` | boolean | Server supports workspace memory management methods (`memory/reset`). |
| `capabilities.dreams` | boolean | Server supports workspace Dreams status, manual/create run requests, review lifecycle, and Dreams settings. |
| `capabilities.mcpManagement` | boolean | Server supports MCP configuration management methods (`mcp/list`, `mcp/get`, `mcp/upsert`, `mcp/remove`). |
| `capabilities.mcpServerOrigins` | boolean | Server annotates MCP config/status DTOs with `origin` and `readOnly` so clients can show plugin-bundled MCP servers as read-only runtime entries. |
| `capabilities.externalChannelManagement` | boolean | Server supports external channel configuration management methods (`externalChannel/list`, `externalChannel/get`, `externalChannel/upsert`, `externalChannel/remove`). |
| `capabilities.subAgentManagement` | boolean | Server supports SubAgent profile management methods (`subagent/profiles/list`, `subagent/settings/update`, `subagent/profiles/setEnabled`, `subagent/profiles/upsert`, `subagent/profiles/remove`). |
| `capabilities.mcpStatus` | boolean | Server supports MCP runtime status methods and notifications (`mcp/status/list`, `mcp/status/updated`, `mcp/test`). |
| `capabilities.usageTelemetry` | boolean | Server supports the aggregate usage telemetry method (`usage/summary`). Absent or `false` when tracing is disabled (no trace store is available). |
| `capabilities.extensions` | object | Optional module capability registry keyed by extension name. Each value is extension-defined metadata; boolean `true` means the extension methods are available. Example: `capabilities.extensions.welcomeSuggestions = true` advertises support for `welcome/suggestions`. |

### 3.3 `initialized`

**Direction**: client → server (notification, no `id`)

**Params**: `{}` (empty object)

No response. Signals the client is ready to receive notifications.

---

## 4. Thread Methods

Thread methods correspond to `ISessionService` thread lifecycle operations defined in the [Session Core Specification, Section 5.1](../core/session-core.md#51-thread-lifecycle).

### 4.1 `thread/start`

Create a new thread. The server generates a Thread ID and persists initial state.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | yes | Channel identity for thread ownership. See [Session Core, Section 4.1.4](../core/session-core.md#414-sessionidentity). |
| `config` | ThreadConfiguration | no | Per-thread agent configuration. Null means workspace defaults. |
| `dynamicTools` | DynamicToolSpec[] | no | Thread-scoped runtime tools implemented by the AppServer client that creates or resumes the thread. |
| `additionalContext` | RuntimeAdditionalContext | no | Thread-bound runtime context supplied by the AppServer client. Requires `capabilities.runtimeAdditionalContext`. |
| `historyMode` | string | no | `"server"` (default) or `"client"`. |
| `displayName` | string | no | Explicit thread display name. |
| `spawnedFromThreadId` | string | no | Id of the thread that started this thread on the user's behalf (e.g. the Desktop `CreateThread` tool invoked from another thread). The server records it as a non-subagent origin on the new thread's `ThreadSource` (`kind` stays `"user"`) and mirrors it into thread metadata as `spawnedFromThreadId`, so the new thread stays an ordinary sibling thread (it does not become a subagent and does not enter the SubAgent dock) while its first user message can link back to the source thread. Self-references are ignored. |

#### 4.1.0 Runtime Dynamic Tools

`dynamicTools` lets an AppServer client expose thread-scoped callback tools to the agent. The tools are bound to the connection that creates the thread, or to the connection that later resumes the thread with `thread/resume.dynamicTools`, and are invoked through the server-to-client `item/tool/call` request. They are not plugin manifest tools and are not a general remote tool transport; external reusable services should use MCP.

Dynamic tool spec:

```json
{
  "namespace": "oratorio",
  "name": "SubmitReviewDraft",
  "description": "Submit a structured code review draft.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "body": { "type": "string" }
    },
    "required": ["body"]
  },
  "outputSchema": {
    "type": "object",
    "properties": {
      "draftId": { "type": "string" }
    }
  },
  "deferLoading": false,
  "display": {
    "title": "Submit review draft",
    "subtitle": "Oratorio"
  },
  "_meta": {
    "ui": { "resourceUri": "ui://oratorio/review-draft", "visibility": ["model", "app"] }
  },
  "approval": {
    "kind": "remoteResource",
    "targetArgument": "body",
    "operation": "submit-review-draft"
  }
}
```

Rules:

- `name` and `description` are required.
- `name` should follow DotCraft's model-visible tool naming convention: PascalCase operation names such as `CreatePlan`, `RequestUserInput`, and `ListThreads`.
- `namespace` is optional. When present it must be non-empty after trimming.
- `inputSchema` is required and must be a valid JSON Schema object.
- `outputSchema`, when present, describes the structured result returned by the tool.
- `display`, when present, is optional user-facing metadata for clients that render tool activity.
- `_meta.ui`, when present, declares an interactive UI for the tool (UI resource `resourceUri`, `visibility`, CSP/permissions). See [Interactive Tool UI](tool-result-presentation.md).
- `display` and `_meta.ui` are client-facing metadata and MUST NOT be included in the model-visible tool description.
- `(namespace, name)` pairs must be unique within a `thread/start` request.
- `approval`, when present, uses the same descriptive approval metadata as channel tools: `file`, `shell`, or `remoteResource`. DotCraft evaluates approval before dispatching `item/tool/call`.
- If the bound connection closes, dynamic tools bound to that thread become unavailable and calls fail with a structured failed `dynamicToolCall` item until a capable client resumes the thread with replacement `dynamicTools`.

#### 4.1.0.1 Desktop Thread Management Runtime Tool Profile

DotCraft Desktop may expose a standard thread-management profile as Runtime Dynamic Tools. This profile is client-owned: AppServer does not add native model tools for cross-thread management and does not define additional JSON-RPC methods for this profile. Desktop declares the tools through `thread/start.dynamicTools` and, when supported, rebinds them through `thread/resume.dynamicTools`; AppServer invokes them only through `item/tool/call`.

Tool identity:

- `namespace`: `desktop`
- `name`: PascalCase, following DotCraft model-visible tool naming. Clients must expose `CreateThread`, not `create_thread`.
- `deferLoading`: `true` by default for every tool in this profile. Clients may expose the tools directly only when the active model/runtime has no deferred-tool discovery path.
- The profile must remain schema-stable across ordinary Agent/Plan mode switches. Availability, target validation, and policy constraints are enforced by the Desktop handler result rather than by adding/removing individual tools per mode.
- When this profile is deferred, Desktop supplies concise thread-coordination guidance through `additionalContext["desktop.threadCoordination"]`. AppServer must not infer or hardcode this guidance from the Desktop tool names.

Standard tools:

| Tool | Backing AppServer methods | Required arguments | Summary |
|------|---------------------------|--------------------|---------|
| `CreateThread` | `thread/start`, then `turn/start` for the initial prompt | `prompt` | Creates a server-managed thread in the current Desktop workspace/identity and starts the initial turn. |
| `ListThreads` | `thread/list` | none | Returns a cursor-paged list of recent thread summaries for the current Desktop workspace/identity. |
| `ReadThread` | `thread/read` | `threadId` | Reads status, queued input summaries, and cursor-paged recent turn summaries for one thread without opening it in the Desktop UI. |
| `SendMessageToThread` | optional `thread/read` + `thread/config/update`, then `turn/start` or `turn/enqueue` | `threadId`, `prompt` | Sends a follow-up prompt to an existing thread without changing the user's active Desktop selection. |
| `SetThreadTitle` | `thread/rename` | `threadId`, `title` | Renames a thread. |
| `SetThreadArchived` | `thread/archive` or `thread/unarchive` | `threadId`, `archived` | Archives or restores a thread. |
| `SetThreadPinned` | Desktop settings only | `threadId`, `pinned` | Pins or unpins a top-level non-archived thread in the current Desktop workspace. |

Pinned-thread state is Desktop-local. AppServer does not define a pinned-thread JSON-RPC method or store pinned state in Session Core.

#### 4.1.0.2 Runtime Additional Context

`additionalContext` lets an AppServer client attach compact thread-bound runtime context alongside client-owned capabilities such as Runtime Dynamic Tools. The server renders this context into the model-visible System prompt using DotCraft's App Context tag semantics.

Wire shape:

```json
{
  "desktop.threadCoordination": {
    "kind": "application",
    "value": "When the user asks to create, inspect, continue, pin, archive, rename, or otherwise manage DotCraft threads in the background, search for the relevant thread tool first: CreateThread, ListThreads, ReadThread, SendMessageToThread, SetThreadTitle, SetThreadArchived, SetThreadPinned."
  }
}
```

Rules:

- Keys are client-owned stable source identifiers. They must be non-empty, at most 128 characters, and contain only letters, digits, `.`, `_`, or `-`.
- `kind` must be `"application"` in this version.
- `value` is required, model-visible text with a maximum length of 16 KiB.
- Runtime additional context is bound to the requesting client runtime for the thread. It does not create Turns, Items, thread rollout records, or `ThreadConfiguration` updates.
- The server renders each entry inside `<app-context>...</app-context>` in a System prompt section. Clients must not rely on a separate developer role being available.

Argument conventions:

- `CreateThread.prompt` and `SendMessageToThread.prompt` are plain user prompts encoded as `InputPart` text when calling `turn/start` or `turn/enqueue`.
- `CreateThread.displayName` is optional and maps to `thread/start.displayName` when present.
- When `CreateThread` is invoked from within a thread (the tool call carries the originating `threadId`), Desktop sets `thread/start.spawnedFromThreadId` to that originating thread id. The created thread stays a normal sibling thread; its origin is recorded only as a non-subagent `ThreadSource`/metadata marker so the client can show a "from another thread" affordance on the new thread's first user message. This must not turn the created thread into a subagent.
- `CreateThread.reasoningEffort` and `SendMessageToThread.reasoningEffort` are optional values in `low`, `medium`, `high`, or `extraHigh`. Desktop maps them to persistent thread reasoning configuration. When `SendMessageToThread` sets reasoning effort, the running turn is not changed; future and queued turns use the updated thread configuration.
- `CreateThread.model` and `SendMessageToThread.model`, when supported by the client, map to thread configuration or a turn-scoped override only through explicit AppServer protocol support. A client that cannot apply the override must return `success = false` with `errorCode = "UnsupportedOption"` rather than silently ignoring it.
- `ListThreads.query`, `ListThreads.limit`, `ListThreads.cursor`, and `ListThreads.includeArchived` map to `thread/list` filtering and cursor pagination. Desktop defaults `limit` to 20 and caps it at 100.
- `ReadThread.includeOutputs` and `ReadThread.maxOutputCharsPerItem` are presentation controls for the client-produced summary. `ReadThread.turnLimit` and `ReadThread.cursor` map to `thread/read` turn pagination. Desktop defaults `turnLimit` to 10 and caps it at 50.
- `ReadThread` summaries must be payload-aware: clients should extract model-useful previews from item `payload` / `payloadKind`, bound all text and output fields, and never dump raw media data, full tool results, or full command output unless explicitly requested through `includeOutputs` and still capped by `maxOutputCharsPerItem`.
- `ReadThread` summaries must include a bounded `queuedInputs` summary with stable fields (`id`, `status`, `displayText`, `createdAt`, `sender`, `triggerLabel`, and `readyAfterTurnId`) plus `queuedInputCount`.
- `SetThreadPinned` is a Desktop-only settings mutation. Pinning a thread must reject archived threads and subagent child threads; unpinning may remove a missing id from local settings without reading the thread.

Result conventions:

- `contentItems` should contain a concise text summary suitable for the model.
- `structuredResult` should reuse AppServer wire DTOs or stable summaries derived from them. Examples include `thread`, `threads`, `turn`, `queuedInput`, `started`, `queued`, and `archived`.
- `CreateThread` returns the created `thread` and, when the initial prompt is accepted, the started `turn` or queued input state.
- `SendMessageToThread` returns whether the prompt was started immediately or queued.
- `ReadThread` must not resume execution or subscribe the UI to that thread; it is a read-only projection.

Failure conventions:

- Desktop returns `success = false` with stable `errorCode` and English `errorMessage`.
- Standard errors are `UnsupportedTool`, `UnsupportedOption`, `InvalidArguments`, `ThreadNotFound`, `ThreadArchived`, `ThreadBusy`, `ThreadManagementUnavailable`, `TargetUnsupported`, and `AppServerRequestFailed`.
- If the target thread is busy, `SendMessageToThread` should use `turn/enqueue` when available. If queuing is not available or rejected, the handler returns `ThreadBusy`.
- If the Desktop transport that owns the tools is disconnected, AppServer handles the call as an unavailable dynamic tool using the Runtime Dynamic Tools failure rules above.

Thread-management tools are dynamic client callbacks, while thread lifecycle, storage, turn execution, and broadcasts remain owned by the AppServer `thread/*` and `turn/*` protocol.

#### 4.1.1 `ThreadConfiguration` Wire Shape

`ThreadConfiguration` is the canonical thread-scoped configuration object on the wire:

```json
{
  "mcpServers": [],
  "mode": "agent",
  "extensions": ["_unity"],
  "customTools": ["SomeTool"],
  "model": "gpt-4.1",
  "workspaceOverride": "/path/to/alt/workspace",
  "executionWorkspaceOverride": "/path/to/runtime/workspace",
  "toolProfile": "commit-message",
  "useToolProfileOnly": false,
  "agentInstructions": "Focus on concise commit messages.",
  "approvalPolicy": "default",
  "automationTaskDirectory": "/path/to/task",
  "reasoning": {
    "enabled": true,
    "effort": "high",
    "output": "full"
  },
  "requireApprovalOutsideWorkspace": true
}
```

Fields:

| Field | Type | Description |
|-------|------|-------------|
| `mcpServers` | `McpServerConfig[]` | Optional per-thread MCP server configuration. |
| `mode` | string | Agent mode for the thread. Default `agent`. |
| `extensions` | string[] | Optional active ACP extension prefixes. |
| `customTools` | string[] | Optional extra tool names enabled for the thread. |
| `model` | string | Optional per-thread model override. |
| `workspaceOverride` | string | Optional alternate workspace root for the thread. |
| `executionWorkspaceOverride` | string | Optional runtime execution root. It is used for worktree-bound execution and does not move thread state. |
| `toolProfile` | string | Optional named tool profile. |
| `useToolProfileOnly` | boolean | When `true`, use only the tools from `toolProfile`. |
| `agentInstructions` | string | Optional additional system instructions. |
| `approvalPolicy` | string | Thread-scoped approval mode: `default`, `autoApprove`, or `interrupt`. `default` means the thread consults the workspace default approval policy. |
| `automationTaskDirectory` | string | Optional local automation task directory. |
| `reasoning` | object | Optional per-thread reasoning configuration. When absent, old threads fall back to current workspace defaults. Uses camelCase wire enum values such as `low`, `medium`, `high`, `extraHigh` and output values such as `none`, `summary`, or `full`. |
| `requireApprovalOutsideWorkspace` | boolean | Optional override for the workspace file/shell outside-boundary behavior. |

Approval semantics:

- `approvalPolicy = default` uses the workspace default approval policy. If the workspace default is also `default` or unset, the server uses the normal interactive approval flow when the client supports approvals.
- `approvalPolicy = autoApprove` auto-accepts approval-gated operations for that thread.
- `approvalPolicy = interrupt` cancels the turn when an approval-gated operation is encountered.
- `requireApprovalOutsideWorkspace = true` allows outside-workspace file/shell operations to proceed through the approval service.
- `requireApprovalOutsideWorkspace = false` rejects outside-workspace file/shell operations without prompting.
- `requireApprovalOutsideWorkspace` omitted means the server uses workspace defaults.

`SessionIdentity` on the wire:

```json
{
  "channelName": "vscode",
  "userId": "user-123",
  "channelContext": "workspace:/path/to/project",
  "workspacePath": "/path/to/project"
}
```

**Result**:

```json
{
  "thread": {
    "id": "thread_20260316_x7k2m4",
    "workspacePath": "/path/to/project",
    "userId": "user-123",
    "originChannel": "vscode",
    "displayName": null,
    "forkedFromId": null,
    "ephemeral": false,
    "worktree": null,
    "effectiveWorkspacePath": "/path/to/project",
    "status": "active",
    "createdAt": "2026-03-16T10:00:00Z",
    "lastActiveAt": "2026-03-16T10:00:00Z",
    "metadata": {},
    "turns": []
  }
}
```

The server also emits a `thread/started` notification after the response.

Thread objects may include `forkedFromId`, `ephemeral`, `worktree`, and `effectiveWorkspacePath`. `forkedFromId` is lineage metadata. `effectiveWorkspacePath` is the root clients should use for file, shell, Git, and editor surfaces for that thread.

In a shared Session Core process (typical AppServer mode), when **any** channel creates a thread (not only via `thread/start` on this connection), the server **broadcasts** the same `thread/started` notification to connected clients. For ordinary `thread/start` RPCs, the initiating client may receive the post-response notification from the request handler instead of the shared broadcast and should dedupe by thread id. Session-backed SubAgent child threads are always broadcast to the current connection as well, because their creation happens inside a parent turn/tool call and has no direct `thread/start` response.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/start", "id": 1, "params": {
    "identity": {
      "channelName": "vscode",
      "userId": "user-123",
      "channelContext": "workspace:/home/dev/myproject",
      "workspacePath": "/home/dev/myproject"
    },
    "historyMode": "server"
} }

{ "jsonrpc": "2.0", "id": 1, "result": {
    "thread": {
      "id": "thread_20260316_x7k2m4",
      "status": "active",
      "workspacePath": "/home/dev/myproject",
      "createdAt": "2026-03-16T10:00:00Z",
      "lastActiveAt": "2026-03-16T10:00:00Z",
      "turns": []
    }
} }

{ "jsonrpc": "2.0", "method": "thread/started", "params": {
    "thread": { "id": "thread_20260316_x7k2m4", "status": "active" }
} }
```

### 4.2 `thread/resume`

Resume a paused or previously loaded thread. Session Core loads the thread from persistence, reconstructs the agent session, and sets status to Active.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to resume. |
| `dynamicTools` | DynamicToolSpec[] | no | Replacement Runtime Dynamic Tools bound to this resume request's client connection. Requires `capabilities.dynamicToolRebind`. Omitted or empty keeps the existing binding. |
| `additionalContext` | RuntimeAdditionalContext | no | Replacement runtime additional context bound to this resume request's client connection. Requires `capabilities.runtimeAdditionalContext`. Omitted keeps the existing binding; `{}` clears it. |

**Result**: `{ "thread": Thread }` — the resumed thread object.

The server emits a `thread/resumed` notification.

When `dynamicTools` is non-empty, the server validates the specs using the same rules as `thread/start.dynamicTools`, replaces any existing dynamic-tool binding for the thread with the current transport connection, and refreshes the thread agent before the next turn can use the tools. This lets reconnecting or batch clients resume a persisted thread while taking over its thread-scoped callback tools from an older closed connection.

If the resumed thread contains unresolved interactive requests in a `waitingApproval` or `waitingInput` turn, the server must re-deliver the corresponding server-to-client requests (`item/approval/request` or `item/tool/requestUserInput`) to the resuming connection when that connection declared the required capability. This replay uses the original logical `requestId`; the JSON-RPC request envelope may receive a fresh transport `id`. Replaying a pending request is idempotent per connection and must not create duplicate prompts for the same `method + threadId + turnId + requestId`. When replaying multiple unresolved approval requests for the same thread, the server must start them serially: the next `item/approval/request` is sent only after the previous replayed approval request has resolved or fallen back, so the per-request reply timeout does not elapse while a prompt is only queued behind another approval in the client UI.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/resume", "id": 2, "params": {
    "threadId": "thread_20260316_x7k2m4"
} }

{ "jsonrpc": "2.0", "id": 2, "result": {
    "thread": { "id": "thread_20260316_x7k2m4", "status": "active" }
} }
```

### 4.2.1 `thread/fork`

Create a new thread from a source thread's persisted history. Clients must check `capabilities.threadFork` before offering this action.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Source thread id. |
| `path` | string | no | Restricted diagnostic source rollout path. When present, it must resolve under the workspace `.craft/threads` tree and match `threadId`. |
| `forkPoint` | object | no | Optional history prefix selector with `turnId`, optional `itemId`, and optional `position` (`after` by default, or `before`). |
| `identity` | SessionIdentity | no | Replacement identity for the forked thread. Omitted fields inherit from the source thread. |
| `config` | ThreadConfiguration | no | Thread configuration overrides applied after copying the source configuration. |
| `displayName` | string | no | Explicit display name for the forked thread. When omitted, the fork uses the source thread's visible display name, or the first retained user message when the source has no display name. |
| `ephemeral` | boolean | no | When true, create a process-local fork omitted from default lists. Defaults to false. |
| `excludeTurns` | boolean | no | When true, omit copied turns from the response; clients can call `thread/read` to load history. |

**Result**: `{ "thread": Thread }`

Semantics:

- The source thread is not mutated.
- The fork has a new thread id and `forkedFromId = threadId`.
- The fork copies the selected source history prefix and retargets copied turns to the new thread id.
- Copied active or partial turns are terminated with an interrupted boundary.
- Forks do not inherit queued inputs, pending approvals, active user-input requests, app bindings, active goals, or durable plan state.
- A persistent `systemNotice` item with `kind = "forked"` and `sourceThreadId` marks the copied-history boundary when turns are included.
- After a successful persistent fork, the server emits `thread/started` for the new thread. The broadcast uses the compact thread shape and does not include copied turns.

### 4.3 `thread/list`

List threads matching a given identity.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | yes | Identity to filter by. |
| `includeArchived` | boolean | no | Default `false`. When `true`, archived threads are included in the result set alongside non-archived threads. |
| `includeSubAgents` | boolean | no | Default `false`. When `true`, session-backed subagent child threads may be included in the mixed result set. Children whose parent is archived are still hidden unless `includeArchived` is also true. Widget-style clients should prefer `subagent/children/list` for a parent thread. |
| `includeInternal` | boolean | no | Default `false`. When `false`, DotCraft-owned helper threads marked with `dotcraft.internal` metadata or known internal origins are excluded. This should only be enabled by diagnostics. |
| `crossChannelOrigins` | string[] \| null | no | When **omitted** or JSON `null`, no cross-channel origin list is applied. When present as an array (possibly empty), non-empty values additionally return threads whose `originChannel` is in the list with the same `workspacePath` and `userId` as `identity`, ignoring `channelContext`. See [Session Core §9.5](../core/session-core.md#95-cross-channel-resume-protocol). |
| `channelName` | string | no | When set, post-filters results to threads whose persisted `originChannel` matches (case-insensitive). Same as existing filter. |
| `query` | string | no | Optional case-insensitive text filter applied to thread id, display name, origin channel, status, and channel context before pagination. |
| `limit` | number | no | Optional page size. Must be positive and at most 100. If omitted with `cursor`, the server uses 50. If both `limit` and `cursor` are omitted, the server returns the full compatible list. |
| `cursor` | string | no | Opaque cursor returned by a previous `thread/list` call. Invalid cursors return `InvalidParams`. |

**Result**:

```json
{
  "data": [
    {
      "id": "thread_20260316_x7k2m4",
      "displayName": "Fix login bug",
      "status": "active",
      "originChannel": "vscode",
      "createdAt": "2026-03-16T10:00:00Z",
      "lastActiveAt": "2026-03-16T10:05:00Z",
      "runtime": {
        "running": true,
        "waitingOnApproval": false,
        "waitingOnPlanConfirmation": false
      },
      "originApp": {
        "appId": "com.dotharness.oratorio",
        "displayName": "Oratorio",
        "icon": "data:image/svg+xml;base64,..."
      }
    }
  ],
  "nextCursor": "opaque_cursor_or_null",
  "totalMatched": 42
}
```

Results are ordered by `lastActiveAt` descending. Filtering is applied before pagination. `nextCursor` is `null` or omitted when no further page exists. `totalMatched` is the number of threads after all filters and before pagination. Cursors are opaque and clients must not parse them. Older clients that omit both `limit` and `cursor` keep receiving the complete list for compatibility.

Each `ThreadSummary` may include an optional `runtime` snapshot with the same shape as `thread/runtimeChanged`. This snapshot is best-effort process-local state intended to hydrate thread-list activity indicators after reconnect. Clients should apply it as initial list state and continue to consume `thread/runtimeChanged` as the incremental source of truth. Older servers may omit `runtime`, and clients must treat omission as unknown rather than as an idle thread.

Each `ThreadSummary` may also include an optional `originApp` object `{ appId, displayName, icon?, memberId? }`. The server populates it only when the summary's `originChannel` matches the declared `originChannel` of an installed App Binding app (see [App Binding] §5.1), attributing the thread's origin to that app so clients can render the app's icon + name as the origin badge. When the app also declares `originMembers` and the summary's `channelContext` matches one, `displayName`/`icon` carry the matched member's branding and `memberId` is set (the matched key) so clients can present it as a per-member origin; `appId` still identifies the owning app. `icon` is an optional data URL or safe URL (same contract as app icons). Clients must fall back to the generic origin-channel badge when `originApp` is absent or its `icon` is missing. The same `originApp` attribution (identical shape and contract) is attached to the full thread object delivered by the thread lifecycle methods — `thread/read`, `thread/started`, `thread/updated`, `thread/resumed`, and `thread/rollback` — so threads that reach the client only through the event stream (e.g. threads created server-side by a managed runtime) carry the same origin badge without waiting for a `thread/list` refresh.

### 4.3.1 `channel/list`

Lists discoverable **origin channel** names that may appear in thread metadata. No Session Core query is required; this is server-derived discovery metadata.

**Direction**: client → server (request)

**Params**: `{}` (empty object) or omitted — no required fields.

**Result**:

```json
{
  "channels": [
    { "name": "cli", "category": "builtin" },
    { "name": "qq", "category": "social" },
    { "name": "telegram", "category": "external" }
  ]
}
```

| Field | Description |
|-------|-------------|
| `name` | Canonical `originChannel` string (case as stored). |
| `category` | `builtin`, `social`, `system`, or `external`. |

**Semantics**:

- The result contains server-known origin channels that may appear on persisted threads or be accepted by related APIs.
- Server-defined channels may be categorized as `builtin`, `social`, or `system`; externally configured channels may be categorized as `external`.
- Internal-only origins that are not intended for cross-channel discovery may be omitted.
- Results are sorted by category order (builtin → social → system → external), then by `name` (ordinal case-insensitive).

### 4.4 `thread/read`

Read a thread by ID without resuming it. Optionally includes turn history.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to read. |
| `includeTurns` | boolean | no | If `true`, include the full `turns` array. Default `false`. |
| `turnLimit` | number | no | Optional turn page size. Must be positive and at most 100. When present, `includeTurns` is treated as `true` and the first page contains the most recent turns. |
| `cursor` | string | no | Opaque cursor returned by a previous paged `thread/read` call. Invalid cursors return `InvalidParams`. When present, `includeTurns` is treated as `true`. |

**Result**: `{ "thread": Thread, "turnPage"?: TurnPage }` — the thread object, with `turns` populated if requested or paged.

When `turnLimit` or `cursor` is supplied, `turnPage` has this shape:

```json
{
  "order": "oldestFirst",
  "limit": 10,
  "totalTurns": 42,
  "startOrdinal": 33,
  "endOrdinal": 42,
  "nextCursor": "opaque_cursor_or_null",
  "hasMore": true
}
```

**Semantics**: `thread/read` is a **read-only** operation. It does not by itself resume execution, start background services, or apply execution-time thread configuration.

The `Thread` wire object includes `queuedInputs?: QueuedTurnInput[]`. This queue is returned regardless of `includeTurns`, because it is current thread state rather than historical turn detail.

When paged, the first `thread/read` page returns the most recent `turnLimit` turns, but the returned page remains oldest-first within that page so clients can render it like ordinary history. `nextCursor` points to the next older page. When `includeTurns = true` is supplied without `turnLimit` or `cursor`, the server returns the full historical `turns` array and omits `turnPage` for compatibility.

The `Thread` wire object may include `plan?: PlanSnapshot | null`. When present, it is the current persisted plan for that exact thread from `thread_plans`, using the same `title`, `overview`, `content`, and `todos` shape as `plan/updated`. Clients should use this field to restore plan/todo UI after switching threads.

**`contextUsage` field**: When the server has persisted context-window occupancy for the thread, the returned `Thread` carries an optional `contextUsage` snapshot for the desktop token ring. This snapshot is not billing usage and must not be derived from cumulative `Turn.tokenUsage` totals. Its token count is server-authoritative and follows Session Core's context usage accounting order: provider context, anchored post-request delta, prefix-adjusted anchor for base-instruction drift, persisted provider fallback, then full-history estimate when no provider lineage is available. After compaction, rollback, or another history replacement, old anchors are invalid and the snapshot falls back to the replacement history estimate until the next provider usage arrives:

```
"contextUsage": {
  "tokens": number,                // Approximate tokens currently occupying context
  "contextWindow": number,         // Effective context window for the thread's effective model (denominator)
  "autoCompactThreshold": number,  // Token count at which auto-compact runs
  "warningThreshold": number,      // Token count at which compactWarning starts firing
  "errorThreshold": number,        // Token count at which compactError starts firing
  "percentLeft": number,           // Fraction of the context window still available (0.0 - 1.0)
  "source": string,                // Optional diagnostic source, e.g. "provider_context" or "estimate"; extensible
  "isEstimate": boolean            // Optional; true when tokens are estimated rather than provider-reported
}
```

The same snapshot is also embedded on `thread/start` and `thread/resume` responses (and their matching `thread/started` / `thread/resumed` notifications) so clients can seed the token ring without an extra round-trip. Clients must prefer server-provided `contextUsage` over local token or ring estimates and must not independently enter compacting state from local estimates when the server snapshot is present. When `Compaction.ContextWindow` is inferred from the model catalog, `contextWindow` is computed from the thread's effective model, including `Thread.configuration.model` overrides. Freshly-created threads initialize persisted context usage to `tokens = 0`; the field is omitted only for older threads or hosts that have no persisted context usage state yet.

Persisted context usage is display state. A stored token count without a matching provider anchor for the current model-visible history and request shape must not by itself trigger automatic compaction.

### 4.5 `thread/rollback`

Drop one or more turns from the end of a thread's canonical history.

**Params**:

```json
{
  "threadId": "thread_...",
  "numTurns": 1
}
```

**Response**:

```json
{
  "thread": { "id": "thread_...", "turns": [] }
}
```

`numTurns` must be `>= 1`. The target thread must not be archived and must not contain a `running` or `waitingApproval` turn. Rollback only changes conversation history; it does not revert workspace files, command output, or other side effects produced by the dropped turns. The response includes the updated thread with turns/items so clients can replace local conversation state.

### 4.6 `thread/subscribe`

Subscribe the current connection to future lifecycle events for a thread. Multiple passive subscribers may observe the same thread concurrently.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread to observe. |
| `replayRecent` | boolean | no | Default `false`. When `true`, the server may replay a small recent buffer for reconnect smoothing. |

**Result**: `{}`

After subscription succeeds, the server may emit future `thread/*`, `turn/*`, and `item/*` notifications for that thread even when the current connection did not originate the turn.

If the subscribed thread is already paused in a `waitingApproval` or `waitingInput` turn, the server must re-deliver the unresolved interactive request to the subscribing connection using the same rules as `thread/resume`. `thread/unsubscribe` and ordinary thread switching are not dismissals; they must not resolve, reject, or answer an outstanding interactive request.

### 4.7 `thread/unsubscribe`

Remove the current connection's passive subscription to a thread.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread to stop observing. |

**Result**: `{}`

Cancellation of the transport connection also implicitly unsubscribes all active thread subscriptions owned by that connection.

### 4.8 `thread/pause`

Pause an active thread. A paused thread cannot accept new turns until resumed.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to pause. |

**Result**: `{}`

The server emits a `thread/statusChanged` notification.

### 4.9 `thread/archive`

Archive a thread. Archived threads are read-only — they can be listed and read but not resumed or turned. If the target is a top-level parent with session-backed SubAgent descendants, the server recursively archives the full child-thread subtree. Directly archiving a SubAgent child thread is invalid; callers manage it through its parent.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to archive. |

**Result**: `{}`

The server emits a `thread/statusChanged` notification.

### 4.10 `thread/unarchive`

Restore an archived thread to Active status so it can appear in the normal active thread list again. If the target is a top-level parent with session-backed SubAgent descendants, the server recursively restores the full child-thread subtree.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to restore. |

**Result**: `{}`

The server emits a `thread/statusChanged` notification with `newStatus: "active"`.

### 4.11 `thread/delete`

Permanently delete a thread, its associated session data, and all tracing sessions/events bound to that thread. If the target is a top-level parent with session-backed SubAgent descendants, the server recursively deletes the full child-thread subtree and its graph edges. Directly deleting a SubAgent child thread is invalid; callers manage it through its parent.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to delete. |

**Result**: `{}`

After the thread is permanently removed, the server **broadcasts** a `thread/deleted` notification to **all** connected clients (see Section 6.1). For recursive SubAgent deletion, a notification is emitted for each removed thread. Deletion is only considered successful after the persisted thread record and all bound tracing data have been removed. Clients that initiated `thread/delete` on this connection may remove the thread from local state when the RPC returns; receiving `thread/deleted` afterward is idempotent.

### 4.12 `thread/mode/set`

Set the agent mode for a thread (e.g., `"plan"`, `"agent"`).

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `mode` | string | yes | New agent mode. |

**Result**: `{}`

**Behavior**: The server recreates the execution context for the specified thread using the tool set associated with the requested mode.

### 4.13 `thread/rename`

Update the display name of a thread.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `displayName` | string | yes | New display name for the thread. |

**Result**: `{}`

After the display name is persisted, the server **broadcasts** a `thread/renamed` notification to **all** connected clients (see [Section 6.1](#61-thread-notifications)). The same notification is used when Session Core sets the display name from the first user message on a turn (not only in response to this RPC).

### 4.14 `thread/config/update`

Update per-thread agent configuration (MCP servers, extensions, etc.).

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `config` | ThreadConfiguration | yes | Configuration patch. |

**Result**: `{}`

---

### 4.15 Thread Goal Methods

Thread goal behavior is defined by [Goal Design](../core/goal-design.md). AppServer projects the Session Core goal runtime through these JSON-RPC methods:

| Method | Params | Result |
|--------|--------|--------|
| `thread/goal/get` | `{ threadId }` | `{ goal: ThreadGoal? }` |
| `thread/goal/set` | `{ threadId, objective?, status?, tokenBudget?, mode? }` | `{ goal: ThreadGoal }` |
| `thread/goal/clear` | `{ threadId }` | `{ cleared: boolean }` |

Clients must check `capabilities.threadGoals` before calling these methods. When absent or false, servers return method-not-found or a capability error.

`thread/goal/set.mode` defaults to `"upsertOrUpdate"` and may be `"replaceExisting"`, `"createOnly"`, or `"updateOnly"`. `status` values are `"active"`, `"paused"`, `"budgetLimited"`, and `"complete"`. Interactive clients should confirm before replacing a different non-complete objective; the server still enforces authoritative state transitions.

`ThreadGoal` uses the normal wire casing:

```json
{
  "threadId": "thread_...",
  "goalId": "goal_...",
  "objective": "Ship the feature",
  "status": "active",
  "tokenBudget": null,
  "tokensUsed": { "totalTokens": 0 },
  "timeUsedSeconds": 0,
  "createdAt": "2026-05-08T00:00:00Z",
  "updatedAt": "2026-05-08T00:00:00Z"
}
```

Goal notifications:

| Notification | Params | Notes |
|--------------|--------|-------|
| `thread/goal/updated` | `{ threadId, turnId?, goal }` | `turnId` is present when a running turn caused the update, such as accounting or `UpdateGoal(complete)`. |
| `thread/goal/cleared` | `{ threadId }` | Emitted only when a goal was deleted. |

`thread/read`, `thread/start`, `thread/resume`, and `thread/list` may include an optional `goal` snapshot for hydration. Clients must still consume goal notifications as the incremental source of truth.

### 4.16 `thread/compact/start`

Manually compact the model-visible context for an idle server-managed thread.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |

**Result**:

| Field | Type | Description |
|-------|------|-------------|
| `outcome` | string | `"partial"`, `"skipped"`, `"failed"`, or `"cancelled"` (`"micro"` is reserved for legacy compatibility and is not expected from manual compaction). |
| `message` | string? | Optional skip/failure reason. |
| `contextUsage` | ContextUsageSnapshot? | Updated snapshot when available. |

Servers advertise this method with `capabilities.manualCompaction = true`. The method is valid only for Active, server-managed threads that have history and no `Running` / `WaitingApproval` turn or active thread maintenance. The response wire shape is stable: `outcome`, `message`, and `contextUsage` are the only result fields. The server emits `system/event` in the order `compacting` -> exactly one terminal event (`compacted`, `compactSkipped`, `compactFailed`, or `compactCancelled`). While running, the thread reports `maintenanceKind = "compacting"` through `thread/runtimeChanged`; new input must be queued instead of submitted with `turn/start`. Manual compaction does not run a microcompact pre-pass. It first tries partial compaction; if there is no older prefix, or the partial attempt cannot produce a summary, it falls back to full-history compaction so short histories can still be compacted. On success it persists the compacted agent session, updates `contextUsage` from that compacted session estimate without carrying over the previous request's provider overhead, and appends a `SystemNotice` item with `kind = "compacted"` and `trigger = "manual"` to the latest completed turn.

Compaction model-call cancellation, provider timeout, and summary validation failure must be observable in trace storage with a terminal maintenance-fork response. User interruption maps to `outcome = "cancelled"` and `compactCancelled`. Provider timeout, missing summary text, and overlong summaries map to `outcome = "failed"` and `compactFailed`. Failure messages should be machine-readable reasons such as `summary_unavailable`, `compact_summary_too_long`, or `provider_timeout`.

### 4.17 `thread/memory/consolidate/start`

Manually consolidate the current thread's model-visible history into workspace long-term memory.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |

**Result**:

| Field | Type | Description |
|-------|------|-------------|
| `outcome` | string | `"succeeded"`, `"skipped"`, `"failed"`, or `"cancelled"`. |
| `message` | string? | Optional skip/failure reason. |
| `memoryWritten` | boolean | Whether `MEMORY.md` was updated. |
| `historyWritten` | boolean | Whether `HISTORY.md` was appended. |

Servers advertise this method with `capabilities.manualMemoryConsolidation = true`. The method is valid only for Active, server-managed, idle threads with at least one completed turn, no active thread maintenance, and non-empty model-visible history. Manual consolidation bypasses `Memory.AutoConsolidateEnabled` because it is an explicit user action, but it still requires the server to have a memory consolidator. The server emits thread-scoped `system/event` notifications in the order `consolidating` → `consolidated` / `consolidationSkipped` / `consolidationFailed` / `consolidationCancelled`. While running, the thread reports `maintenanceKind = "consolidating"` through `thread/runtimeChanged`; new input must be queued instead of submitted with `turn/start`. On success it persists a `SystemNotice` item with `kind = "memoryConsolidated"` into the latest completed turn.

### 4.18 `thread/maintenance/interrupt`

Interrupts active thread-level maintenance such as manual compaction or memory consolidation. This method is advertised with `capabilities.threadMaintenanceInterrupt = true`.

If no maintenance is active, the request succeeds as a no-op. If maintenance is active, the server signals its cancellation token and later emits the matching terminal `system/event` (`compactCancelled` or `consolidationCancelled`). Cancelling maintenance does not cancel any completed turn and does not remove queued inputs.

### 4.19 Worktree Methods

Worktree methods are advertised with `capabilities.gitWorktrees = true`. They create, inspect, and switch DotCraft-managed Git worktrees bound to threads. Thread state stays in the original workspace; the worktree is only the execution workspace.

#### 4.19.1 `worktree/createAndFork`

Create a Git worktree, optionally copy dirty source changes, then fork a source thread into that worktree.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sourceThreadId` | string | yes | Source thread to fork. |
| `forkPoint` | object | no | Same prefix selector as `thread/fork`. |
| `identity` | SessionIdentity | no | Replacement identity for the forked thread. It must not move the state workspace away from the source workspace. |
| `config` | ThreadConfiguration | no | Thread configuration overrides. The server sets the execution workspace to the created worktree. |
| `displayName` | string | no | Explicit display name for the forked thread. When omitted, the fork uses the source thread's visible display name, or the first retained user message when the source has no display name. |
| `branchName` | string | no | Requested Git branch name. Omitted lets the server allocate one. |
| `baseRef` | string | no | Git ref for worktree creation. Defaults to the source execution workspace `HEAD`. |
| `path` | string | no | Explicit worktree path. It must resolve under `<workspace>/.craft/worktrees/`. |
| `copyDirtyChanges` | boolean | no | Whether to copy tracked and non-ignored untracked source changes into the worktree. Defaults to true. |
| `excludeTurns` | boolean | no | When true, omit copied turns from the response thread. |

**Result**: `{ "thread": Thread, "worktree": ThreadWorktreeInfo }`

Semantics:

- The source thread and source working tree are not mutated.
- The server creates the thread only after worktree creation and dirty handoff have succeeded.
- The returned thread has `forkedFromId`, `worktree`, and an `effectiveWorkspacePath` pointing to the worktree path.
- The forked thread's rollout, memory, goals, plans, app bindings, and metadata remain in the original state workspace.
- Dirty handoff failure is recoverable and must not switch the active thread in clients.

#### 4.19.2 `worktree/createAndStart`

Create a Git worktree, optionally copy dirty source changes, then start a new empty thread in that worktree.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | yes | New thread identity and state workspace. |
| `config` | ThreadConfiguration | no | Thread configuration overrides. The server sets the execution workspace to the created worktree. |
| `dynamicTools` | DynamicToolSpec[] | no | Same thread-scoped client tool binding as `thread/start`. |
| `additionalContext` | object | no | Same runtime additional context binding as `thread/start`. |
| `historyMode` | string | no | `"server"` or `"client"`. Defaults to `"server"`. |
| `displayName` | string | no | Explicit display name for the new thread. |
| `branchName` | string | no | Requested Git branch name for the created worktree. Omitted lets the server allocate one. |
| `baseRef` | string | no | Git ref for worktree creation. Defaults to the source workspace `HEAD`. |
| `path` | string | no | Explicit worktree path. It must resolve under `<workspace>/.craft/worktrees/`. |
| `copyDirtyChanges` | boolean | no | Whether to copy tracked and non-ignored untracked source changes into the worktree. Defaults to true. |

**Result**: `{ "thread": Thread, "worktree": ThreadWorktreeInfo }`

Semantics:

- The server creates the thread only after worktree creation and dirty handoff have succeeded.
- The returned thread has `worktree` and an `effectiveWorkspacePath` pointing to the worktree path.
- The thread's rollout, memory, goals, plans, app bindings, and metadata remain in the original state workspace.
- After success, the server emits `thread/started` for the new thread.

#### 4.19.3 `thread/worktree/handoff`

Move an existing thread between its local workspace and a DotCraft-managed worktree without changing the thread ID.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread to move. |
| `mode` | string | yes | `"worktree"` moves local -> worktree; `"local"` moves worktree -> local. |
| `branchName` | string | no | Requested branch name when creating a worktree. |
| `baseRef` | string | no | Git ref for worktree creation. Defaults to the current execution workspace `HEAD`. |
| `path` | string | no | Explicit worktree path under `<workspace>/.craft/worktrees/`. |
| `copyDirtyChanges` | boolean | no | Whether to copy dirty source changes during local -> worktree. Defaults to true. |

**Result**: `{ "thread": Thread, "mode": "local" | "worktree", "worktree"?: ThreadWorktreeInfo, "dirtyHandoff"?: ThreadWorktreeDirtyHandoffInfo }`

Semantics:

- The thread must be Active and must not have a running turn, waiting approval/input, or active blocking maintenance.
- Local -> worktree creates a managed worktree, copies local dirty changes by default, sets `thread.worktree`, and sets `configuration.executionWorkspaceOverride`.
- Worktree -> local checks local dirty conflicts first. If a local dirty path would be overwritten, the request fails with `WorktreeHandoffConflict` and `params.conflictPaths`.
- When no conflict exists, worktree -> local stashes modified, deleted, and non-ignored untracked worktree changes, detaches the worktree from its branch, checks out the worktree branch in the local workspace, applies the stashed changes locally, clears `thread.worktree`, clears `configuration.executionWorkspaceOverride`, and removes the registered managed worktree.
- After success, the server emits `thread/updated` with the updated compact thread.

#### 4.19.4 `worktree/list`

List registered DotCraft-managed worktrees for the connected workspace.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | no | Optional workspace identity filter. |
| `includeOrphans` | boolean | no | Whether to include recoverable worktree directories not bound to an active thread. |

**Result**: `{ "data": ThreadWorktreeStatus[] }`

The list is scoped to registered worktrees under `.craft/worktrees`. Clients must not treat arbitrary external Git worktrees as managed DotCraft worktrees unless the server registers them.

#### 4.19.5 `worktree/status`

Return current Git status metadata for the worktree bound to a thread.

**Direction**: client -> server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread bound to a DotCraft-managed worktree. |

**Result**: `ThreadWorktreeStatus`

This method is a lightweight refresh path for worktree indicators. Full file, diff, and commit UX remains a client concern using the thread's `effectiveWorkspacePath`.

---

## 5. Turn Methods

Turn methods correspond to `ISessionService` turn lifecycle operations defined in the [Session Core Specification, Section 5.2](../core/session-core.md#52-turn-lifecycle).

### 5.1 `turn/start`

Submit user input to a thread and begin agent execution. The server creates a new Turn, records the user input as a `UserMessage` Item, and starts the agent.

Before starting the agent, the server **must** ensure the in-memory thread is loaded from persistence if needed and that any persisted `thread.configuration` (mode, MCP servers, etc.) is applied to the execution-time agent, so turns do not silently use workspace-default tooling after a cold load or when only `thread/read` was used earlier ([Session Core](../core/session-core.md) `EnsureThreadLoaded`).

The response is returned **immediately** with the initial Turn object (status `"running"`, empty `items`). The agent's output then streams as notifications: `turn/started`, followed by `item/*` events, and finally `turn/completed` (or `turn/failed` / `turn/cancelled`).

Clients must not call `turn/start` while the thread has a running/waiting turn or active blocking thread maintenance. The server rejects both cases with `TurnInProgress`; clients should use `turn/enqueue` so the input runs after the active turn or maintenance terminal event. Turn-scoped automatic memory consolidation status does not count as active thread maintenance and must not prevent `turn/start`.

For persisted server-managed threads, the execution lifecycle of a started turn is owned by the AppServer, not by the single request transport that submitted it. If the client WebSocket disconnects after `turn/start` has begun, the server must continue consuming the turn event stream so the turn can complete or fail normally. The disconnected client may miss notifications and should recover by reconnecting and calling `thread/read` or `thread/subscribe`.

**Interaction with `thread/subscribe`**: If the calling connection already holds an active subscription for the target thread (via `thread/subscribe`), the server MUST use the subscription path to deliver all turn-scoped notifications instead of creating a separate inline dispatch path. The `turn/start` JSON-RPC response is still sent before the first `turn/started` notification. The server must still keep an internal active-turn drain for the submitted turn so connection loss does not stop execution or strand approvals after the passive subscription is cancelled. See [Section 6.10](#610-notification-delivery-guarantees) for the at-most-once delivery guarantee.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread. Must be `"active"` with no running turn or active maintenance. |
| `input` | InputPart[] | yes | User input. At least one part required. |
| `sender` | SenderContext | no | Sender identity for group sessions. |
| `messages` | ChatMessage[] | conditional | Required when the thread uses `historyMode = "client"`. Forbidden when the thread uses `historyMode = "server"`. |

`InputPart` is a tagged union:

- `{ "type": "text", "text": "..." }` — plain text input. `text` parts carry only literal user text; clients should not encode command, skill, or file-reference tags into `text` when a structured tag part exists.
- `{ "type": "commandRef", "name": "code-review", "argsText": "src/foo.cs", "rawText": "/code-review src/foo.cs" }` — native custom-command reference. The server materializes this reference before agent execution and persists both the native reference and the materialized prompt snapshot.
- `{ "type": "skillRef", "name": "browser" }` — native skill reference. The server materializes this reference into a model-visible `<skill>` block containing only the effective skill name and path while preserving the original `$skill` form for history rendering. Skill instructions are not inlined into the user input; the agent loads them through `SkillView` when available, or by reading the referenced `SKILL.md` path as a fallback.
- `{ "type": "fileRef", "path": "src/foo.cs", "displayPath": "src/foo.cs" }` — native file reference. `path` is the canonical referenced path and may be workspace-relative or a local absolute path. `displayPath` is an optional UI-facing path when the server and client canonical forms differ. Referencing an outside-workspace path does not grant implicit access; later file reads still follow the server file-tool approval policy.
- `{ "type": "image", "url": "https://..." }` — remote image URL.
- `{ "type": "localImage", "path": "/tmp/screenshot.png", "mimeType": "image/png", "fileName": "screenshot.png" }` — local image file path with optional UI metadata.

Before starting the agent, the server MUST normalize the incoming `InputPart[]`, persist a `UserMessage` item whose payload captures both the native input parts and the materialized input parts, and only then convert the materialized parts into the `AIContent[]` passed to Session Core execution.

Tag semantics:

- `/command` denotes a custom command reference and is transmitted as `commandRef`.
- Built-in slash commands such as `/new`, `/stop`, `/help`, `/debug`, `/heartbeat`, and `/cron` are not valid `commandRef` values. Clients must trigger them via `command/execute` or dedicated UI controls. If a client sends a built-in command as `commandRef` in `turn/start`, the server rejects the request with `InvalidParams`.
- `$skill` denotes a skill reference and is transmitted as `skillRef`.
- `@path` denotes a file reference and is transmitted as `fileRef`.
- If a UI presents skills inside a slash-command picker, selecting a skill still produces a `skillRef`, not a `commandRef`.
- A composer slash-command picker that inserts `commandRef` parts should request custom commands only.

`localImage` optional metadata fields:

- `mimeType` (string, optional): client-observed MIME type for UI rehydration hints.
- `fileName` (string, optional): original filename from paste/drop context for UI display.

`QueuedTurnInput` uses the same input snapshot shape:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Queued input ID. |
| `threadId` | string | Parent thread ID. |
| `nativeInputParts` | InputPart[] | Original client input snapshot. |
| `materializedInputParts` | InputPart[] | Model-visible materialized snapshot. |
| `displayText` | string | Human-readable summary for queue UI. |
| `sender` | SenderContext? | Optional sender identity. |
| `status` | string | `"queued"` or `"guidancePending"`. |
| `createdAt` | string | UTC timestamp. |
| `readyAfterTurnId` | string? | Active turn observed when the input was queued. |
| `triggerKind` | string? | Present when the queued input was synthesized by a server/app mechanism rather than typed by a human. Examples include `"goal"`, `"heartbeat"`, `"cron"`, `"automation"`, `"app"`, `"team"`, `"subagentFollowupTask"`, `"subagentMailbox"`, or `"subagentInput"`. |
| `triggerLabel` | string? | Optional human-readable source label. |
| `triggerRefId` | string? | Optional stable source id for client-side click-through or audit correlation. |

When a queued input later starts a Turn or is promoted into current-Turn guidance, the resulting `userMessage` item preserves `triggerKind`, `triggerLabel`, and `triggerRefId`.

`SenderContext`:

```json
{
  "senderId": "user-456",
  "senderName": "Alice",
  "senderRole": "admin",
  "groupId": "group-123"
}
```

The server records two separate provenance fields:

- `thread.originChannel`: the channel that originally created the thread.
- `turn.originChannel`: the channel that initiated this specific turn.

Each persisted Turn also records an `initiator` object with durable actor metadata (`channelName`, `userId`, `userName`, `userRole`, `channelContext`, `groupId`) so cross-channel replay and auditing remain accurate after resume.

**Result**:

```json
{
  "turn": {
    "id": "turn_001",
    "threadId": "thread_20260316_x7k2m4",
    "status": "running",
    "items": [],
    "startedAt": "2026-03-16T10:05:00Z"
  }
}
```

**Example**:

```json
{ "jsonrpc": "2.0", "method": "turn/start", "id": 10, "params": {
    "threadId": "thread_20260316_x7k2m4",
    "input": [
      { "type": "text", "text": "Run the tests and fix any failures" }
    ]
} }

{ "jsonrpc": "2.0", "id": 10, "result": {
    "turn": {
      "id": "turn_001",
      "threadId": "thread_20260316_x7k2m4",
      "status": "running",
      "items": [],
      "startedAt": "2026-03-16T10:05:00Z"
    }
} }
```

### 5.2 `turn/interrupt`

Request cancellation of an in-progress turn. The server cancels the agent execution via `CancellationToken` and emits `turn/cancelled` once shutdown completes.

Before emitting `turn/cancelled`, the server finalizes any currently streaming agent/reasoning items with their accumulated text and persists the cancelled turn as canonical history. Future `turn/start` calls on server-managed threads must include the cancelled turn's user input and completed partial assistant output when rebuilding model context.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `turnId` | string | yes | Turn ID to cancel. |

**Result**: `{}`

The actual cancellation is asynchronous. Rely on the `turn/cancelled` notification to know when the turn has stopped.

### 5.2.1 `turn/enqueue`

Persist user input in the thread FIFO queue. Desktop clients use this as the default send behavior while another Turn is running.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target active thread. |
| `input` | InputPart[] | yes | Same input model as `turn/start`; at least one part required. |
| `sender` | SenderContext | no | Sender identity for group sessions. |

**Result**:

```json
{
  "queuedInput": {
    "id": "queued_20260425100000000_ab12cd",
    "threadId": "thread_...",
    "displayText": "Run tests next",
    "status": "queued",
    "createdAt": "2026-04-25T10:00:00Z",
    "readyAfterTurnId": "turn_003"
  },
  "queuedInputs": [ ... ]
}
```

After enqueue, remove, reorder, or dequeue, the server emits `thread/queue/updated`.

### 5.2.2 `turn/queue/remove`

Remove one queued input without starting a Turn.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread. |
| `queuedInputId` | string | yes | Queued input ID to remove. |

**Result**: `{ "queuedInputs": QueuedTurnInput[] }`

### 5.2.3 `turn/queue/reorder`

Replace the current queued input order. This changes the order in which queued inputs become future turns.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread. |
| `orderedQueuedInputIds` | string[] | yes | Complete ordered list of the current queued input IDs. The set must exactly match the current queue: no missing, duplicate, or unknown IDs. |

**Result**: `{ "queuedInputs": QueuedTurnInput[] }`

### 5.2.4 `turn/steer`

Promote a queued input into a pending guidance request for the current active Turn. This is not the default send path; clients should call it only when the user explicitly promotes a queued message into guidance.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target active thread. |
| `expectedTurnId` | string | yes | Active Turn ID observed by the client. The server rejects the request if it no longer matches. |
| `queuedInputId` | string | yes | Queued input ID to promote. The server uses the persisted queued input snapshot as the source of truth. |
| `sender` | SenderContext | no | Sender identity for group sessions. |

**Result**: `{ "turnId": "<active-turn-id>", "queuedInputs": QueuedTurnInput[] }`

The server first marks the queued input as `guidancePending` and broadcasts `thread/queue/updated`; clients should keep the queue row visible in that state. When the model/tool loop reaches the next safe boundary, the server appends a `userMessage` item with `deliveryMode = "guidance"`, injects the input into the active Turn's model history, removes the queued input, and broadcasts `thread/queue/updated` again. If the Turn ends before insertion, the pending item returns to `queued`.

### 5.3 `workspace/commitMessage/suggest`

Suggest a git commit message from the **source thread’s** recent conversation context plus a **unified diff** for the given file paths. The AppServer runs an internal **temporary thread** (dedicated channel identity, commit-suggest-only tool) so this request does not contend with a user turn on the source thread.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Source thread whose messages supply context. Must belong to the server’s workspace. |
| `paths` | string[] | yes | Paths **relative to the workspace root** for `git diff`. Empty is invalid. |
| `maxDiffChars` | number | no | Optional cap on diff size sent to the model (server may truncate further). |

**Result**:

| Field | Type | Description |
|-------|------|-------------|
| `message` | string | Full commit message (first line subject; optional blank line and body). |

**Errors** (non-exhaustive): source thread not found; paths outside workspace; not a git repository; empty diff; model did not emit the `CommitSuggest` tool; timeout. If the server cannot run the suggest pipeline (e.g. no session service), it returns an appropriate JSON-RPC error.

**Note**: The server may create and delete an **ephemeral** thread for this operation. Clients may observe transient `thread/*` / `turn/*` notifications for that internal thread; implementations typically filter or ignore threads whose origin channel marks commit-message generation.

### 5.4 `welcome/suggestions`

Return welcome-screen quick suggestions for the current workspace. This method is intended for clients that render an empty or ready-to-start conversation state and want to show a small set of prompts that feel relevant to the user's recent work.

The result is advisory and read-only. The server derives these suggestions from workspace-scoped memory artifacts such as `MEMORY.md` and `HISTORY.md`; it should not inspect full conversation history on this path.

**Direction**: client → server (request)

**Capability advertisement**: clients should check `capabilities.extensions.welcomeSuggestions` before calling this method. If the capability is absent or `false`, the server returns `-32601` (`Method not found`) or an equivalent capability error.

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | `SessionIdentity` | yes | Workspace identity whose history and memory scope the suggestions. `identity.workspacePath` is required. |
| `maxItems` | number | no | Maximum number of suggestions requested. Defaults to `4`. The server may clamp overly large values. |

**Result**:

| Field | Type | Description |
|-------|------|-------------|
| `items` | `WelcomeSuggestionItem[]` | Suggested welcome actions for the current workspace. |
| `source` | string | Suggestion source kind. Initial values are `dynamic` and `none`. |
| `generatedAt` | string | ISO 8601 UTC timestamp describing when this result was generated. |
| `fingerprint` | string | Stable identifier for the returned evidence/result snapshot. Clients may use it to avoid redundant UI refresh. |

**`WelcomeSuggestionItem`**:

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Short label suitable for a welcome suggestion list. |
| `prompt` | string | Full prompt text to prefill into the input composer when the suggestion is chosen. |
| `reason` | string | Optional explanatory rationale intended for diagnostics, analytics, or non-primary UI surfaces. |

**Semantics**:

- Suggestions should be grounded in the current workspace rather than a global user profile.
- Suggestions should represent likely next tasks or follow-up asks, not a fixed taxonomy of product features.
- `source = "dynamic"` means the server returned workspace-specific personalized suggestions.
- `source = "none"` means the server intentionally did not return personalized suggestions for this call. Typical reasons include insufficient workspace evidence, a workspace-level preference disabling the feature, or transient generation unavailability.
- When `source = "none"`, `items` may be an empty list. Client-owned default suggestions remain out of band and are not serialized by this method.
- The server may inspect workspace-local memory through internal read-only mechanisms before generating suggestions, but those inspection steps are implementation-defined and not part of the wire contract.
- Servers may cache results for a short period and return the same `fingerprint` across repeated calls while the underlying workspace evidence has not materially changed.
- Servers SHOULD serve this method from a persisted workspace cache and SHOULD NOT trigger synchronous model generation from this request path. The persisted cache is a cross-process restart snapshot of the most recent successful dynamic result; it should not be deleted on normal client shutdown, and failed, canceled, or insufficient-context refresh attempts should leave the previous snapshot available.
- Cache refresh should run asynchronously after successful long-term memory consolidation. If the current memory evidence fingerprint already matches the persisted snapshot, the server may skip regeneration.

**Errors** (non-exhaustive): missing `identity.workspacePath`; unsupported capability; invalid `maxItems`; workspace not available.

---

## 6. Event Notifications

Event notifications are server-initiated messages (no `id`) that stream the turn lifecycle to the client. They correspond 1:1 to the `SessionEvent` types defined in the [Session Core Specification, Section 6](../core/session-core.md#6-event-model).

All notifications share the pattern:

```json
{ "jsonrpc": "2.0", "method": "<event-method>", "params": { ... } }
```

### 6.1 Thread Notifications

#### `thread/started`

Emitted when a new thread is created. Sent to the initiating client after `thread/start` (see Section 4.1), and **broadcast to connected clients** when a thread is created by any other channel in the same process. When a thread is created as a side effect of another JSON-RPC request, such as a protocol extension request, the broadcast is also delivered to the connection that initiated that request. Session-backed SubAgent child thread creation is broadcast to the current connection too so sidebar/thread-list UIs can show the child immediately while the parent turn is still running.

**Params**: `{ "thread": Thread }`

#### `thread/updated`

Emitted when a thread's compact metadata changes without creating, deleting, renaming, or changing lifecycle status. Typical triggers include successful `thread/worktree/handoff`.

**Params**: `{ "thread": Thread }`

The `thread` payload may omit full turn history. Clients should merge the compact metadata into existing active-thread state instead of treating omitted history as deleted history.

#### `thread/renamed`

Emitted when a thread's **display name** changes. The server **broadcasts** this notification to **all** connected clients (same delivery model as `thread/started`). Typical triggers include successful `thread/rename` (Section 4.11) and automatic display-name assignment from turn input.

**Params**: `{ "threadId": "<id>", "displayName": "<non-empty string>" }`

Duplicate or idempotent deliveries for the same `threadId` and `displayName` are allowed.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/renamed", "params": {
    "threadId": "thread_20260316_x7k2m4",
    "displayName": "Fix login bug"
} }
```

#### `thread/deleted`

Emitted when a thread is **permanently** deleted. The server **broadcasts** this notification to **all** connected clients after deletion completes, regardless of which protocol entry point or host integration triggered the removal.

**Params**: `{ "threadId": "<id>" }`

Duplicate notifications for the same `threadId` should be ignored.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/deleted", "params": {
    "threadId": "thread_20260316_x7k2m4"
} }
```

#### `thread/resumed`

Emitted when a thread is resumed via `thread/resume`.

**Params**: `{ "thread": Thread, "resumedBy": "<channelName>" }`

#### `thread/statusChanged`

Emitted when a thread's status changes (Active → Paused, Active → Archived, etc.).

**Params**: `{ "threadId": "<id>", "previousStatus": "<status>", "newStatus": "<status>" }`

#### `thread/runtimeChanged`

Emitted when the server's aggregated **runtime snapshot** for a thread changes. This is a **workspace-level broadcast notification**: it is delivered to all initialized connections that have not opted out, regardless of whether they currently hold a `thread/subscribe` subscription for that thread.

This notification is a **summary channel** for sidebar or thread-list style UIs. It does **not** replace turn-scoped notifications such as `turn/started`, `turn/completed`, or `item/*`; those notifications continue to follow thread-subscription delivery rules. Clients that need full turn details must still subscribe to the target thread.

The server emits `thread/runtimeChanged` when any of the following state transitions changes the aggregated snapshot for a thread:

- a turn starts;
- a turn ends (`completed`, `failed`, or `cancelled`);
- an approval request is created;
- an approval request is resolved;
- a model-initiated user input request is created;
- a model-initiated user input request is resolved;
- a turn finishes in plan mode with a successful terminal `CreatePlan` tool call, setting `waitingOnPlanConfirmation = true`;
- the next `turn/start` for that thread clears the pending plan confirmation state;
- thread maintenance starts or completes.

The server SHOULD broadcast this notification only when the effective snapshot actually changes. Duplicate deliveries are allowed; clients should treat the latest payload as authoritative and replace any prior cached snapshot for that `threadId`.

**Params**:

```json
{
  "threadId": "thread_20260420_x7k2m4",
  "runtime": {
    "running": true,
    "waitingOnApproval": false,
    "waitingOnInput": false,
    "waitingOnPlanConfirmation": false,
    "busy": true,
    "maintenanceKind": "consolidating"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Target thread id. |
| `runtime.running` | boolean | Whether a turn is currently executing for the thread. |
| `runtime.waitingOnApproval` | boolean | Whether the thread currently has one or more unresolved approval requests. |
| `runtime.waitingOnInput` | boolean | Whether the thread currently has one or more unresolved model-initiated user input requests. |
| `runtime.waitingOnPlanConfirmation` | boolean | Whether the previous turn ended in plan mode with a successful terminal `CreatePlan` call and has not yet been cleared by the next `turn/start`. |
| `runtime.busy` | boolean | Whether the thread is currently unable to start a new turn because a turn, approval, model-initiated input request, or blocking maintenance operation is active. |
| `runtime.maintenanceKind` | string? | Current blocking thread maintenance kind (`"compacting"` or manual `"consolidating"`), or omitted/null when no maintenance is active. Automatic memory consolidation does not set this field. |

Forward-compatibility rule: future server versions may add additional boolean flags under `runtime`. Clients MUST ignore unknown fields.

### 6.2 Turn Notifications

#### `turn/started`

Emitted when a turn begins execution (after `turn/start` response).

**Params**: `{ "turn": Turn }`

The `turn` object includes the `UserMessage` input item.

#### `turn/completed`

Emitted when a turn finishes successfully.

Responses truncated by the provider output token limit (for example `finish_reason = "length"` or Anthropic `max_tokens`) are not successful completions and are emitted as `turn/failed`.

**Params**:

```json
{
  "turn": {
    "id": "turn_001",
    "threadId": "thread_...",
    "status": "completed",
    "items": [ ... ],
    "startedAt": "2026-03-16T10:05:00Z",
    "completedAt": "2026-03-16T10:07:30Z",
    "tokenUsage": {
      "inputTokens": 1200,
      "outputTokens": 800,
      "totalTokens": 2000
    }
  }
}
```

#### `turn/failed`

Emitted when a turn fails due to an unrecoverable error.

Also emitted when the model reaches the provider output token limit before completing the answer, so clients can retry or report the run as incomplete.

**Params**: `{ "turn": Turn, "error": "<message>" }`

The `turn.status` is `"failed"` and `turn.error` contains the error description.

#### `turn/cancelled`

Emitted when a turn is cancelled via `turn/interrupt` or client disconnect.

**Params**: `{ "turn": Turn, "reason": "<description>" }`

#### `thread/queue/updated`

Emitted whenever a thread queue changes because input was enqueued, removed, dequeued, or restored after a failed dequeue start.

**Params**:

```json
{
  "threadId": "thread_...",
  "queuedInputs": [
    {
      "id": "queued_...",
      "threadId": "thread_...",
      "displayText": "Run tests next",
      "status": "queued",
      "createdAt": "2026-04-25T10:00:00Z",
      "readyAfterTurnId": "turn_003"
    }
  ]
}
```

### 6.3 Item Notifications

Items follow the lifecycle: `item/started` → zero or more `item/*/delta` → `item/completed`. See [Session Core, Section 5.3](../core/session-core.md#53-item-lifecycle).

#### `item/started`

Emitted when a new item is created within a turn.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_002",
    "turnId": "turn_001",
    "type": "toolCall",
    "status": "started",
    "payload": {
      "toolName": "Exec",
      "arguments": { "command": "npm test" },
      "callId": "call_001"
    },
    "createdAt": "2026-03-16T10:05:12Z"
  }
}
```

The canonical item payload schemas are defined in [Session Core, Section 4.2](../core/session-core.md#42-item-payload-schemas). On the wire, clients should treat `item.type` as the discriminator and apply the following mapping rules:

| `item.type` | Wire-specific notes |
|-------------|---------------------|
| `userMessage` | Payload shape matches Session Core; property names are camelCase and nullable fields are omitted when absent. `text` is a compatibility/display field derived from the native input parts, not the sole source of truth. When present, `nativeInputParts` is authoritative for history rendering and `materializedInputParts` captures the exact snapshot sent to the model. Optional `deliveryMode` (`"normal"` / `"queued"` / `"guidance"` / `"subagentMailbox"`) lets clients distinguish direct input, queued input that later became a Turn, active-Turn guidance, and internal SubAgent mailbox delivery. Optional `triggerKind` (`"heartbeat"` / `"cron"` / `"automation"` / `"goal"` / `"app"` / `"team"` / `"subagentFollowupTask"` / `"subagentMailbox"` / `"subagentInput"`), `triggerLabel`, and `triggerRefId` are emitted when the turn was synthesized by an automation, goal continuation, authorized app mechanism, team runner, or SubAgent coordination mechanism rather than typed by a human. Clients may render a source affordance and route click-through when the source has a client surface, but `subagentMailbox` items are internal/model-visible notifications and should not render as parent-thread user bubbles or child-agent reply bubbles. SubAgent `triggerRefId` values are agent paths and should not be treated as thread ids. |
| `agentMessage` | Text deltas stream through `item/agentMessage/delta`; snapshots still use the canonical payload schema. |
| `reasoningContent` | Reasoning deltas stream through `item/reasoning/delta`; snapshots still use the canonical payload schema. |
| `toolCall` | Tool invocation payload uses camelCase fields such as `toolName`, `arguments`, and `callId`. When argument construction is streamed, clients receive `item/toolCall/argumentsDelta` between `item/started` and `item/completed`. |
| `commandExecution` | Command execution payload uses camelCase fields such as `command`, `workingDirectory`, `source`, `status`, `aggregatedOutput`, `exitCode`, `durationMs`, and `callId`. |
| `toolExecution` | Runtime lifecycle enhancement for a normal tool invocation. Payload uses `callId`, `toolName`, `status`, `success`, `durationMs`, `resultPreview`, and `errorMessage`. It is emitted only when the client advertises `capabilities.toolExecutionLifecycle = true`. |
| `pluginFunctionCall` | Plugin function payload uses camelCase fields such as `pluginId`, `namespace`, `functionName`, `callId`, `arguments`, `contentItems`, `structuredResult`, `success`, `errorCode`, and `errorMessage`. For plugin-backed tools, including adapter-declared channel tools, this is the only conversation-item projection: the server emits `item/started` -> `item/completed` for `pluginFunctionCall` and does not emit companion `toolCall`/`toolResult` items. Plugin discovery and manifest architecture are defined in [plugin-architecture.md](../extensions/plugin-architecture.md). |
| `dynamicToolCall` | Runtime dynamic tool payload uses camelCase fields such as `namespace`, `toolName`, `callId`, `arguments`, `contentItems`, `structuredResult`, `success`, `errorCode`, and `errorMessage`. Dynamic tools are thread-scoped AppServer client callbacks declared on `thread/start`; the server emits `item/started` -> `item/completed` for `dynamicToolCall` and does not emit companion `toolCall`/`toolResult` items. |
| `toolResult` | Result payload uses the canonical fields; transport serialization preserves nested JSON values losslessly. |
| `approvalRequest` | Approval payload uses the canonical fields plus wire enum/string serialization rules from this spec. |
| `approvalResponse` | Response payload uses the canonical fields; decision values are serialized as wire strings. |
| `userInputRequest` | Plan Mode question request payload. The item is paired with a server-to-client `item/tool/requestUserInput` request and puts the turn in `waitingInput`. |
| `userInputResponse` | User answer payload for a previously emitted `userInputRequest`. |
| `error` | Error payload uses the canonical fields; transport-level JSON-RPC errors remain separate from item-level error items. |

Clients that render conversation tool activity MUST treat `pluginFunctionCall` and `dynamicToolCall` as tool-like items. They have different authority and lifecycle sources, but the visible invocation row/card should use the same result extraction rules: show the invocation name, arguments, `contentItems`, `structuredResult`, success state, and error fields without waiting for a companion `toolResult`. If a `dynamicToolCall` includes `presentation`, supporting clients MAY render it through the [Tool Result Presentation](tool-result-presentation.md) contract; unsupported clients MUST fall back to ordinary result rendering.

#### `item/agentMessage/delta`

Streamed text delta for an `agentMessage` item. Concatenate `delta` values in order to reconstruct the full reply.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "itemId": "item_004",
  "deltaKind": "agentMessage",
  "delta": "Here is my analysis of the"
}
```

#### `item/reasoning/delta`

Streamed text delta for a `reasoningContent` item.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "itemId": "item_003",
  "deltaKind": "reasoningContent",
  "delta": "I need to check the test output first"
}
```

#### `item/toolCall/argumentsDelta`

Streamed arguments delta for a `toolCall` item. Concatenate `delta` values in order to build a progressive JSON-text preview of the tool arguments.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "itemId": "item_002",
  "deltaKind": "toolCallArguments",
  "toolName": "WriteFile",
  "callId": "call_001",
  "delta": "{\"path\":\"a.txt\",\"content\":\"hello"
}
```

`toolCall` items with streamed arguments follow this sequence:

1. `item/started` with `item.type = "toolCall"` (payload may contain partial metadata; `arguments` may be omitted or incomplete).
2. zero or more `item/toolCall/argumentsDelta`.
3. `item/completed` with the final `toolCall` payload, including complete `payload.arguments`.

Server coverage:

- Argument deltas are emitted for non-external tools by default, including built-in, module-contributed, and MCP tools. Individual tools can opt out via a server-side annotation, in which case clients only observe `item/started` followed by `item/completed` with no deltas.
- Plugin-backed tools do not emit `item/toolCall/argumentsDelta` because they are projected as `pluginFunctionCall` items instead of `toolCall` items.
- Clients MUST NOT assume a specific built-in set has streaming enabled. Render UX based on the presence of `argumentsDelta` events for a given `toolCall` item.
- Clients are expected to render tool-specific UX only for tools they recognise; for unknown tool names (for example MCP tools), render a generic "generating parameters" placeholder without displaying the raw JSON to the user.

Client handling rules:

- `deltaKind` is fixed to `toolCallArguments`.
- `delta` is a raw JSON text fragment (not JSON Patch and not guaranteed to be parseable mid-stream).
- `toolName` and `callId` are typically present on the first chunk and may be omitted on subsequent chunks.
- Clients should merge chunks by `itemId` (or `callId` when useful) and append `delta` in arrival order for preview rendering.
- The authoritative executable/persisted arguments are the final `item/completed.item.payload.arguments`.
- Empty deltas are suppressed by the server and are not delivered.

#### `item/completed`

Emitted when an item is finalized. The `item.status` is `"completed"` and the payload contains the final accumulated value.

#### `item/commandExecution/outputDelta`

Streamed output delta for a `commandExecution` item. Concatenate `delta` values in order to reconstruct the command output for clients that use the compatibility projection.

**Params**:

```json
{
  "threadId": "thread_20260413_ab12cd",
  "turnId": "turn_001",
  "itemId": "item_004",
  "delta": "Downloading package 1 of 5...\n"
}
```

`commandExecution` items follow a fixed sequence:

1. `item/started` with `item.type = "commandExecution"` and payload `status = "inProgress"`
2. zero or more `item/commandExecution/outputDelta`
3. `item/completed` with final payload status and `aggregatedOutput`

Compatibility rule:

- Terminal-capable clients that advertise `capabilities.backgroundTerminals = true` should use `terminal/started`, `terminal/outputDelta`, and `terminal/completed` as the primary live shell output source for `Exec`-style tools.
- When a connection advertises `capabilities.commandExecutionStreaming = true`, the server may also emit the `commandExecution` projection for persistence, history summaries, and compatibility fallback.
- The underlying `toolCall` / `toolResult` items still exist for model execution and persistence. Clients that consume both `terminal/*` and `commandExecution` must merge by `callId` and avoid double-rendering the same shell output.
- A client may use `commandExecution` as an enhancement source for an existing `Exec` tool card when `terminal/*` notifications are unavailable.
- Clients that do not advertise the capability continue to rely on existing `toolCall` / `toolResult` behavior.

#### `toolExecution` lifecycle

When a connection advertises `capabilities.toolExecutionLifecycle = true`, the server may emit a `toolExecution` item for each non-plugin tool invocation so clients can update one tool card as soon as that invocation finishes, even when other parallel tool calls are still running.

`toolExecution` items follow a fixed sequence:

1. `item/started` with `item.type = "toolExecution"` and payload `status = "inProgress"`.
2. `item/completed` with final payload `status`, `success`, `durationMs`, and optional `resultPreview` / `errorMessage`.

Payload shape:

```ts
{
  callId: string
  toolName: string
  status: "inProgress" | "completed" | "failed" | "cancelled"
  success?: boolean
  durationMs?: number
  resultPreview?: string
  errorMessage?: string
}
```

Compatibility rule:

- `toolExecution` does not replace `toolCall` or `toolResult`. `toolCall` remains the model-request and final-arguments item; `toolResult` remains the complete, authoritative model-visible result.
- `resultPreview` is a UI preview only. It is sanitized text and may be truncated to 4096 characters. Clients should replace it with the matching `toolResult.result` when that result arrives.
- Plugin-backed tools do not emit companion `toolExecution`; their lifecycle is already represented by `pluginFunctionCall`.
- Runtime dynamic tools do not emit companion `toolExecution`; their lifecycle is represented by `dynamicToolCall`.
- Clients that do not advertise the capability continue to rely on existing `toolCall` / `toolResult` behavior.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_004",
    "turnId": "turn_001",
    "type": "agentMessage",
    "status": "completed",
    "payload": {
      "text": "Here is my analysis of the test failures..."
    },
    "createdAt": "2026-03-16T10:05:30Z",
    "completedAt": "2026-03-16T10:06:15Z"
  }
}
```

### 6.4 Approval Notifications

#### `item/approval/resolved`

Emitted after the client responds to an approval request and the server processes the decision. This is distinct from `item/completed` for the `approvalResponse` item — `item/approval/resolved` is emitted first, then the regular `item/completed` follows.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_006",
    "type": "approvalResponse",
    "status": "completed",
    "payload": {
      "requestId": "approval_001",
      "approved": true
    }
  }
}
```

### 6.5 SubAgent Notifications

#### `subagent/progress`

Emitted periodically (~200ms) when one or more SubAgent tool calls (`SpawnAgent`) are active during a Turn. Each notification carries a **complete snapshot** of all tracked SubAgents' progress, allowing clients to replace their local state on each receipt.

This notification is a sideband signal — it may interleave with `item/*` and `turn/*` notifications.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "entries": [
    {
      "label": "code_explorer",
      "currentTool": "ReadFile",
      "inputTokens": 4500,
      "outputTokens": 1200,
      "isCompleted": false
    },
    {
      "label": "test_runner",
      "currentTool": null,
      "inputTokens": 2000,
      "outputTokens": 600,
      "isCompleted": true
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `entries` | SubAgentEntry[] | Snapshot of all tracked SubAgents. |

`SubAgentEntry` fields:

| Field | Type | Description |
|-------|------|-------------|
| `label` | string | SubAgent identifier/label (matches the `agentNickname` argument passed to `SpawnAgent`). |
| `currentTool` | string? | Name of the tool the SubAgent is currently executing. `null` when the SubAgent is thinking (waiting for model response). |
| `inputTokens` | integer | Cumulative input token consumption. |
| `outputTokens` | integer | Cumulative output token consumption. |
| `isCompleted` | boolean | Whether the SubAgent has finished execution. |

**Emission rules**:

- The server emits this notification at ~200ms intervals while SubAgents are active. The exact interval is an implementation detail and may vary.
- Each notification contains the **complete set** of tracked SubAgents for the current Turn — not incremental deltas.
- The server stops emitting once all tracked SubAgents have completed and a final snapshot with all `isCompleted = true` has been sent.
- Clients that do not need SubAgent progress can opt out via `optOutNotificationMethods: ["subagent/progress"]` during `initialize`.

#### `subagent/graphChanged`

Emitted when a session-backed SubAgent parent/child edge is created or changes status. Clients should refresh `subagent/children/list` for the parent and may use returned `thread` summaries to hydrate thread lists/sidebar entries immediately.

**Params**: `{ "parentThreadId": "<parent>", "childThreadId": "<child>" }`

**Example sequence**:

```
Server                                          Client
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "SpawnAgent",                    |
  |    arguments: { message: "inspect code",      |
  |      taskName: "code_explorer",               |
  |      agentNickname: "Code Explorer" } }        |
  |---------------------------------------------->|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "code_explorer",          |
  |    currentTool: "ReadFile",                   |
  |    inputTokens: 1200, outputTokens: 300,      |
  |    isCompleted: false }]                      |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)  (~200ms)    |
  |  entries: [{ label: "code_explorer",          |
  |    currentTool: "SearchContent",              |
  |    inputTokens: 3500, outputTokens: 900,      |
  |    isCompleted: false }]                      |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "code_explorer",          |
  |    currentTool: null,                         |
  |    inputTokens: 4500, outputTokens: 1200,     |
  |    isCompleted: true }]                       |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "...", success: true }             |
  |---------------------------------------------->|
```

### 6.6 Usage Notifications

#### `item/usage/delta`

Emitted each time the agent completes an LLM iteration and produces a `UsageContent` with non-zero token counts. Carries the **incremental** token consumption for that single iteration.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "inputTokens": 1200,
  "outputTokens": 350,
  "cachedInputTokens": 0,
  "cacheWriteInputTokens": 0,
  "freshInputTokens": 1200,
  "reasoningOutputTokens": 0,
  "llmCallDelta": 1,
  "contextInputTokens": 14820,
  "turnInputTokens": 1200,
  "turnOutputTokens": 350,
  "turnLlmCalls": 1,
  "totalInputTokens": 14820,
  "totalOutputTokens": 2610,
  "contextUsage": {
    "tokens": 14820,
    "contextWindow": 200000,
    "autoCompactThreshold": 180000,
    "warningThreshold": 176000,
    "errorThreshold": 194000,
    "percentLeft": 0.9259
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `inputTokens` | integer | Input tokens consumed in this LLM iteration (delta, not cumulative). |
| `outputTokens` | integer | Output tokens consumed in this LLM iteration (delta, not cumulative). |
| `cachedInputTokens` | integer | Cache-hit/cache-read input tokens consumed in this LLM iteration (delta, not cumulative). |
| `cacheWriteInputTokens` | integer | Cache-creation/cache-write input tokens consumed in this LLM iteration (delta, not cumulative). |
| `freshInputTokens` | integer | Derived fresh input delta: `max(0, inputTokens - cachedInputTokens - cacheWriteInputTokens)`. |
| `reasoningOutputTokens` | integer | Reasoning output tokens consumed in this LLM iteration (delta, not cumulative). |
| `llmCallDelta` | integer | Optional. `1` when this delta starts a new LLM request, otherwise `0`. |
| `contextInputTokens` | integer | Optional. Latest provider input-token snapshot for the request. It is not billing/cumulative thread usage. |
| `totalInputTokens` | integer | Optional. Backward-compatible alias of `contextInputTokens`; not billing/cumulative thread usage. |
| `turnInputTokens` | integer | Optional. Cumulative billing input tokens emitted so far in the current turn. |
| `totalOutputTokens` | integer | Optional. Backward-compatible cumulative output tokens emitted so far in the current turn. |
| `turnOutputTokens` | integer | Optional. Cumulative billing output tokens emitted so far in the current turn. |
| `turnLlmCalls` | integer | Optional. Cumulative LLM request count emitted so far in the current turn. |
| `contextUsage` | object | Optional. Full `ContextUsageSnapshot` for current context pressure, including thresholds needed to seed the desktop token ring. Its `tokens` may include output/cache pressure and therefore may be greater than `contextInputTokens`. |

**Emission rules**:

- Emitted once per LLM iteration, immediately after the provider's `UsageContent` is processed.
- Each notification carries only the delta for the current LLM request. Providers may emit cumulative usage snapshots within one request; Session Core normalizes those snapshots before emitting.
- The sum of all `item/usage/delta` notifications for a Turn's main agent equals the main-agent billing portion of `turn/completed.tokenUsage`.
- `turn/completed.tokenUsage` is the final aggregate for the Turn. Clients that already consumed `item/usage/delta` notifications must treat it as a final snapshot, not an additional delta to add again.
- Context-window occupancy is separate: `contextInputTokens`/`totalInputTokens` is the latest main-agent request input snapshot, not the billing sum; `contextUsage.tokens` is the ring/threshold value and may also include provider output/cache tokens plus appended-message estimation.
- Example: if one Turn has request input snapshots `12000 | 20000 | 41000`, `turnInputTokens` reaches `73000`, while `contextInputTokens` remains `41000`. If the final request also generated `3000` output tokens, `contextUsage.tokens` is at least `44000`.
- Cache-hit accounting is cumulative by the same rule: `cachedInputTokens` deltas sum to the Turn's cache-hit input total, so dashboards can show how much of `turnInputTokens` came from cache hits.
- SubAgent tokens are reported separately via `subagent/progress` and are not included in `item/usage/delta`.
- Clients that do not need real-time token display can opt out via `optOutNotificationMethods: ["item/usage/delta"]` during `initialize`.

**Example sequence**:

```
Server                                          Client
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 1200, outputTokens: 350         |
  |<----------------------------------------------|
  |                                               |
  | (tool calls execute...)                       |
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 2100, outputTokens: 480         |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  tokenUsage: { inputTokens: 3300,             |
  |    cachedInputTokens: 0, cacheWriteInputTokens: 0, |
  |    outputTokens: 830, totalTokens: 4130 }     |
  |<----------------------------------------------|
```

### 6.7 System Notifications

#### `system/event`

Emitted when a system-level maintenance operation occurs during a Turn's post-processing phase. These operations (context compaction, memory consolidation) are not part of the agent's conversational output but affect the session's internal state.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "kind": "compactWarning",
  "messageKey": "context.limit_reached",
  "params": {},
  "fallbackText": "Context token limit reached, compacting conversation...",
  "message": "Context token limit reached, compacting conversation...",
  "percentLeft": 0.12,
  "tokenCount": 176000,
  "contextUsage": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string? | Active turn. May be null for asynchronous thread-scoped maintenance events such as `consolidated`, `consolidationSkipped`, and `consolidationFailed`. |
| `kind` | string | Event kind. One of: `"compactWarning"`, `"compactError"`, `"compacting"`, `"compacted"`, `"compactSkipped"`, `"compactFailed"`, `"compactCancelled"`, `"streamError"`, `"consolidating"`, `"consolidated"`, `"consolidationSkipped"`, `"consolidationFailed"`, `"consolidationCancelled"`. |
| `messageKey` | string? | Stable client-localization key. May be null when no key exists. |
| `params` | object? | Optional interpolation params for `messageKey`. User text, model output, and raw tool output MUST NOT be translated by the server. |
| `fallbackText` | string? | English fallback text suitable for display when the client has no translation. |
| `message` | string? | Compatibility alias for `fallbackText`. New clients should prefer `messageKey` + `params` + `fallbackText`. |
| `percentLeft` | number? | Fraction of the effective context window still unused (`0.0`-`1.0`). Populated for compaction-related events. |
| `tokenCount` | number? | Current estimated prompt token usage. Populated for compaction-related events. |
| `contextUsage` | object? | Full `ContextUsageSnapshot` on compaction-related events when available. Clients should prefer it over `tokenCount` / `percentLeft` when updating context-window UI because it includes thresholds and the exact server-side token source. `source` is diagnostic and extensible; clients should not compute local replacements or compacting state when this snapshot is present. |

**Defined `kind` values**:

| Kind | Meaning |
|------|---------|
| `compactWarning` | Token usage crossed the warning threshold but not the error threshold. Advisory only. |
| `compactError` | Token usage crossed the error threshold; auto-compaction is imminent. Advisory only. |
| `compacting` | A compaction attempt (auto or reactive) is starting. |
| `compacted` | Compaction completed successfully. Token tracker has been reset. |
| `compactSkipped` | Compaction was evaluated but not executed (below threshold, nothing new to summarize, or circuit breaker tripped). |
| `compactFailed` | Compaction attempted but failed (LLM error, provider timeout, missing summary, or overlong summary). Repeated failures trip the circuit breaker. |
| `compactCancelled` | Thread-scoped manual compaction was interrupted by the user. |
| `consolidating` | Memory consolidation is starting. When `turnId` is non-null, this is non-blocking automatic background consolidation; when `turnId` is null, this is blocking manual consolidation. |
| `consolidated` | Memory consolidation completed successfully. MEMORY.md / HISTORY.md have been updated. |
| `consolidationSkipped` | Memory consolidation completed without writing MEMORY.md or HISTORY.md (for example, the model did not call `save_memory` or produced no valid changes). Clients should dismiss any active consolidation status and should not show a success marker. |
| `consolidationFailed` | Memory consolidation failed. Clients should dismiss any active consolidation status and may surface `message`. |
| `consolidationCancelled` | Memory consolidation was interrupted by the user. Clients should dismiss any active consolidation status. |
| `streamError` | A provider stream disconnected or timed out while idle before the sampling request completed. The server is retrying the same sampling request and `message` uses `Reconnecting... x/y`. |

**Emission rules**:

- System events are emitted during the Turn's post-processing phase, before `turn/completed`.
- Threshold advisory events (`compactWarning`, `compactError`) fire when token usage crosses a threshold but auto-compaction has not yet been triggered.
- Auto-compaction is a synchronous pair: `compacting` → one of `compacted` / `compactSkipped` / `compactFailed`.
- Reactive compaction fires on the Turn's error path when the model rejects a request with `prompt_too_long`, `context_length_exceeded`, or another conservatively classified context-overflow equivalent. The Turn still fails, but `compacting` and its terminal event are emitted first so UIs know the history was repaired before the user retries.
- Provider stream retry emits `streamError` during agent execution before the retry delay. The event is transient and does not persist a `SystemNotice`. Servers only retry attempts that have not emitted visible item output; after visible assistant/reasoning/tool output, a stream failure remains a normal failed Turn with partial state preserved. Idle-timeout detection must surface retry or failure promptly; cleanup of the failed provider stream is best-effort and must not indefinitely delay the retry notification or terminal failure.
- Automatic memory consolidation is fire-and-forget after a configured number of successful Turns; it is independent from compaction and the Turn completes without awaiting it. Its start event is turn-scoped `consolidating` and should be displayed as a non-blocking background status. The terminal event is one of `consolidated`, `consolidationSkipped`, or `consolidationFailed`. Manual consolidation emits thread-scoped `consolidating` and remains blocking thread maintenance. See [Memory Consolidation](../core/memory-consolidation.md) for the design contract.
- Clients that do not need system maintenance status can opt out via `optOutNotificationMethods: ["system/event"]` during `initialize`.
- On a successful summary-producing `compacted` event (auto, reactive, or manual trigger with `mode = "partial"`), Session Core includes `contextUsage` when available and additionally persists a `SystemNotice` SessionItem (kind = `"compacted"`) into the current or latest completed turn, emitting the normal `item/started` + `item/completed` pair for it. This gives clients a persistent timeline marker that survives thread reload, alongside the transient `system/event` notification used to drive toast/status-line and context-ring UX. Cold-cache tool-result clearing that returns `outcome = "micro"` is transient, updates optimized session/context usage, and must not append a persistent notice. See [Session Core](../core/session-core.md#systemnotice) for the payload schema.
- On a successful `consolidated` event, Session Core additionally persists a `SystemNotice` SessionItem (kind = `"memoryConsolidated"`) into the completed turn and emits the normal `item/started` + `item/completed` pair through the thread event broker. `consolidationSkipped` does not create a persistent notice.
- Thread fork creation additionally places a persistent `SystemNotice` SessionItem (kind = `"forked"`, `sourceThreadId = <source thread id>`) at the end of the selected copied history in the forked thread. This marker is returned by `thread/fork`, `worktree/createAndFork`, and `thread/read` when turns are included. The `thread/started` broadcast keeps its normal compact shape and does not include turns.

**Example sequence**:

```
Server                                          Client
  |                                               |
  | system/event (notification)                   |
  |  kind: "compactWarning",                      |
  |  percentLeft: 0.12, tokenCount: 176000        |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "compacting",                          |
  |  percentLeft: 0.03, tokenCount: 194000        |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "compacted",                           |
  |  percentLeft: 0.78, tokenCount: 44000         |
  |  contextUsage: { tokens: 44000, ... }         |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "consolidating"                        |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "consolidated"                         |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  turn: { ... }                                |
  |<----------------------------------------------|
```

### 6.8 Plan Notifications

#### `plan/updated`

Emitted when the agent creates or updates a structured plan via plan-management tools. The notification carries the complete plan snapshot.

This notification is independent of the Turn event stream. Clients that do not need plan progress display can opt out via `optOutNotificationMethods: ["plan/updated"]` during `initialize`.

**Params**:

```json
{
  "threadId": "thread_20260316_x7k2m4",
  "title": "Implement user authentication",
  "overview": "Add JWT-based auth with login and registration endpoints",
  "content": "## Scope\n\nImplement backend auth endpoints and middleware.\n\n## Steps\n\n1. Add User model\n2. Add login/register APIs\n3. Add JWT middleware",
  "todos": [
    {
      "id": "setup-models",
      "content": "Create User model and migration",
      "priority": "high",
      "status": "completed"
    },
    {
      "id": "auth-endpoints",
      "content": "Implement login and register API endpoints",
      "priority": "high",
      "status": "in_progress"
    },
    {
      "id": "jwt-middleware",
      "content": "Add JWT validation middleware",
      "priority": "medium",
      "status": "pending"
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread that produced this plan snapshot. |
| `title` | string | Plan title. |
| `overview` | string | Brief plan overview/description. May be empty. |
| `content` | string | Full Markdown plan body. May be empty. |
| `todos` | PlanTodo[] | Complete list of plan tasks. |

`PlanTodo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Short kebab-case task identifier. |
| `content` | string | Human-readable task description. |
| `priority` | string | One of: `"high"`, `"medium"`, `"low"`. |
| `status` | string | One of: `"pending"`, `"in_progress"`, `"completed"`, `"cancelled"`. |

Compatibility note: older servers may omit `content`; clients should treat missing `content` as an empty string.

**Emission rules**:

- Emitted each time any plan tool (`CreatePlan`, `UpdateTodos`, `TodoWrite`) completes successfully.
- Each notification carries the **complete plan snapshot** — not incremental deltas. Clients should replace their local plan state on each receipt.
- Clients with a currently selected thread must ignore `plan/updated` notifications whose `threadId` does not match that selected thread, and should recover a selected thread's plan with `thread/read.thread.plan` when switching threads.
- The notification is sent outside the `SessionEvent` stream; it is a direct JSON-RPC notification from the host to all connected transports.
- Clients that do not need plan progress can opt out via `optOutNotificationMethods: ["plan/updated"]` during `initialize`.

---

## 7. Approval Flow

When the agent encounters a sensitive operation (file write, shell command) that requires user consent, the server initiates a bidirectional approval exchange. This is a **server-to-client request** — the server sends a JSON-RPC request with an `id`, and the client must respond.

### 7.1 Sequence

```
Server                              Client
  |                                   |
  | item/started (notification)       |
  |   type: "approvalRequest"         |
  |---------------------------------->|
  |                                   |
  | item/approval/request (request)   |
  |   id: <server-assigned>           |
  |---------------------------------->|
  |                                   |
  |   (client shows approval UI)      |
  |                                   |
  | response (id: <same>)             |
  |   result: { decision: "..." }     |
  |<----------------------------------|
  |                                   |
  | item/approval/resolved (notify)   |
  |---------------------------------->|
  |                                   |
  | item/completed (notification)     |
  |   (for the tool call item)        |
  |---------------------------------->|
```

The turn enters `"waitingApproval"` status while the server waits for the client's response.

### 7.2 `item/approval/request`

**Direction**: server → client (request)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `itemId` | string | The `approvalRequest` item ID. |
| `requestId` | string | Unique correlation ID for this approval. |
| `approvalType` | string | `"shell"` or `"file"`. |
| `operation` | string | For shell: the command. For file: `"read"`, `"write"`, `"edit"`, `"list"`. |
| `target` | string | For shell: working directory. For file: the file path. |
| `scopeKey` | string | Session-scoped cache key used when the client returns `acceptForSession`. |
| `reason` | string | Human-readable explanation of why approval is needed. |

**Example**:

```json
{ "jsonrpc": "2.0", "method": "item/approval/request", "id": 100, "params": {
    "threadId": "thread_20260316_x7k2m4",
    "turnId": "turn_001",
    "itemId": "item_005",
    "requestId": "approval_001",
    "approvalType": "shell",
    "operation": "npm test",
    "target": "/home/dev/myproject",
    "scopeKey": "shell:*",
    "reason": "Agent wants to execute a shell command"
} }
```

### 7.3 Client Response

The client responds with the standard JSON-RPC response format:

```json
{ "jsonrpc": "2.0", "id": 100, "result": {
    "decision": "accept"
} }
```

**Decision values**:

| Value | Meaning |
|-------|---------|
| `"accept"` | Approve this single operation. |
| `"acceptForSession"` | Approve this operation and similar operations for the remainder of the thread's lifetime. |
| `"acceptAlways"` | Approve this operation permanently. The server persists the approval so future sessions do not prompt again. Also suppresses further prompts for the current session. |
| `"decline"` | Reject the operation. The agent receives a rejection signal and may try an alternative approach. |
| `"cancel"` | Reject and cancel the entire turn. Equivalent to `turn/interrupt`. |

When approval resolution is persisted or echoed back in a later event, the response item carries both:

- `approved`: boolean convenience field for legacy consumers.
- `decision`: the exact rich decision value chosen by the user.

### 7.4 Clients Without Approval Support

If a client declared `capabilities.approvalSupport = false` during initialization, the server must not send `item/approval/request`. Instead, the server resolves approvals non-interactively using the same server-owned thread policy model:

- `approvalPolicy = autoApprove` resolves as `accept`.
- `approvalPolicy = interrupt` resolves as `cancel`.
- `approvalPolicy = default` first resolves through the workspace default approval policy. If both the thread policy and workspace default are `default` or unset, the server cannot prompt on a non-interactive client, so it falls back to its non-interactive default decision. In the current implementation and spec baseline, that fallback is `decline`.

The same non-interactive fallback may also be applied when an approval-capable client disconnects, the approval request cannot be written to the transport, or the client times out before replying. Cancelling a passive `thread/subscribe` subscription is not itself a rejection, timeout, or disconnect; it must not resolve an outstanding approval request.

When a client later resumes or subscribes to a thread that is still waiting for unresolved approvals, the server replays `item/approval/request` with the original `requestId` values so the client can render actionable approval UI again. Multiple replayed approvals are started serially per thread; a later approval's server-to-client reply timeout begins only when that later request is actually sent.

### 7.5 Model-Initiated User Input Requests

The model may ask the client to collect a small amount of structured user input before continuing. This is a **server-to-client request** with the same bidirectional JSON-RPC pattern as approvals.

The server exposes this path through the root-thread `RequestUserInput` tool. SubAgents do not receive this tool.

Sequence:

```
Server                              Client
  | item/started                     |
  |   type: "userInputRequest"       |
  |--------------------------------->|
  | item/tool/requestUserInput        |
  |   id: <server-assigned>          |
  |--------------------------------->|
  |   (client shows question UI)      |
  | response                         |
  |   result: { answers: ... }       |
  |<---------------------------------|
  | item/tool/requestUserInput/resolved
  |--------------------------------->|
```

The turn enters `"waitingInput"` status while waiting for the response.

**Request method**: `item/tool/requestUserInput`

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `itemId` | string | The `userInputRequest` item ID. |
| `requestId` | string | Unique correlation ID. |
| `questions` | `RequestUserInputQuestion[]` | One to three questions. |

`RequestUserInputQuestion`:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stable snake_case identifier used as the answer key. |
| `header` | string | Short UI label. |
| `question` | string | Prompt shown to the user. |
| `options` | `{ label: string, description: string }[]` | Two or three mutually exclusive options. The recommended option should be first and include `(Recommended)` in its label when applicable. |
| `isOther` | boolean | Whether the client should offer a free-form "Other" path. Defaults to `true`. |
| `isSecret` | boolean | Optional hint for clients to mask free-form text. |

**Client response result**:

```json
{
  "answers": {
    "provider_id_handling": {
      "answers": ["自动生成 (Recommended)"]
    }
  }
}
```

Clients should return `{ "answers": {} }` when the user explicitly dismisses the request or when the request cannot be displayed. If a client did not declare `capabilities.requestUserInputSupport = true`, the server must not send the request and resolves it with empty answers so the turn can continue. Cancelling a passive `thread/subscribe` subscription, for example because the user switched to another thread, must not resolve an outstanding user-input request; the request remains pending until the client responds, the transport becomes unavailable, or the turn is cancelled. `RequestUserInput` does not have a response timeout while the client transport remains available.

When a client later resumes or subscribes to a thread that is still waiting for the same unresolved user-input request, the server replays `item/tool/requestUserInput` with the original `requestId` so the client can render an actionable question composer again.

---

## 8. Error Handling

### 8.1 JSON-RPC Error Response

Errors follow the standard JSON-RPC 2.0 error response format:

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "error": {
    "code": -32600,
    "message": "Invalid request",
    "data": {
      "code": "InvalidRequest",
      "messageKey": "errors.invalidRequest",
      "params": {},
      "fallbackText": "Invalid request",
      "detail": "Thread not found: thread_invalid"
    }
  }
}
```

`error.message` is always an English fallback for legacy clients and diagnostics. New UI clients should use `error.data.messageKey`, `error.data.params`, and `error.data.fallbackText` for localized display, falling back to `error.message` only when structured data is absent.

### 8.2 Standard Error Codes

| Code | Name | When |
|------|------|------|
| `-32700` | Parse error | Malformed JSON. |
| `-32600` | Invalid request | Missing required fields, invalid params, or constraint violation. |
| `-32601` | Method not found | Unknown method name. |
| `-32602` | Invalid params | Params present but do not match the expected schema. |
| `-32603` | Internal error | Unexpected server failure. |

### 8.3 DotCraft-Specific Error Codes

| Code | Name | When |
|------|------|------|
| `-32001` | Server overloaded | Backpressure: too many in-flight requests. Retryable. |
| `-32002` | Not initialized | Method called before `initialize` handshake. |
| `-32003` | Already initialized | `initialize` called more than once on the same connection. |
| `-32010` | Thread not found | The specified `threadId` does not exist. |
| `-32011` | Thread not active | Operation requires an active thread but the thread is paused or archived. |
| `-32012` | Turn in progress | A turn is already running or waiting for approval on this thread. |
| `-32013` | Turn not found | The specified `turnId` does not exist on the thread. |
| `-32014` | Turn not running | `turn/interrupt` called on a turn that is not in progress. |
| `-32020` | Approval timeout | The client took too long to respond to an approval request. |
| `-32030` | Channel rejected | The channel adapter name is not registered in server configuration. |
| `-32031` | Cron job not found | The specified cron job ID does not exist. |
| `-32040` | Skill not found | The requested skill name does not exist in any source (workspace, user, or builtin). |
| `-32051` | Task not found | `automation/*`: the specified task does not exist. |
| `-32052` | Task invalid status | `automation/*`: the operation is not valid for the task’s current status. |
| `-32054` | Task already exists | `automation/task/create`: a task with the same ID already exists. |
| `-32055` | Thread binding invalid | `automation/task/updateBinding` / `automation/task/create`: the target `threadId` does not exist or is archived. |

Automation task methods are defined in full in [automations-lifecycle.md §13](../runtime/automations-lifecycle.md). Summary of the v1 wire surface:

- `automation/task/list`, `automation/task/read`, `automation/task/create`, `automation/task/updateBinding`, `automation/task/delete` — CRUD and binding updates for local automation tasks. Task-level review and cancel endpoints are not part of this surface.
- `automation/task/updateBinding` `{ taskId, threadBinding?: { threadId, mode } | null }` → `{ task }` — rewrites only the `thread_binding` block on disk; pass `null` to unbind.
- `automation/template/list` `{}` → `{ templates: AutomationTemplateWire[] }` — returns the built-in local task templates followed by any user-authored templates so desktop clients can render the "Use template" picker without bundling a copy. User templates carry `isUser: true`; built-ins omit the field (default `false`). User templates also populate `createdAt` / `updatedAt` (ISO-8601 UTC).
- `automation/template/save` `{ id?, title, description?, icon?, category?, workflowMarkdown, defaultSchedule?, defaultWorkspaceMode?, defaultApprovalPolicy?, needsThreadBinding, defaultTitle?, defaultDescription? }` → `{ template: AutomationTemplateWire }` — upsert a user template. When `id` is omitted the server assigns `"user-" + shortGuid`. Rejects built-in id collisions, path-traversal / invalid id shapes (`^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$`), empty `title` / `workflowMarkdown`, and overlong `title` (>200 chars).
- `automation/template/delete` `{ id }` → `{ ok: true }` — delete a user template directory. Built-in ids and invalid id shapes are rejected with `-32602` Invalid params. Idempotent: missing directories return `{ ok: true }`.
- User template disk layout: `<CraftPath>/automations/templates/<id>/template.md` (overridable via `Automations.UserTemplatesRoot`). The file is YAML front matter (`id`, `title`, `description`, `icon`, `category`, `default_schedule`, `default_workspace_mode`, `default_approval_policy`, `needs_thread_binding`, `default_title`, `default_description`, `created_at`, `updated_at`) followed by the complete `workflow.md` body that is copied into new tasks applying the template.
- `AutomationTaskWire.status` is one of `pending`, `running`, `completed`, or `failed`, and carries optional `schedule` (mirrors `CronSchedule`), `threadBinding` (`{ threadId, mode: "run-in-thread" }`), and `nextRunAt` (ISO-8601 UTC).
- `automation/task/create` accepts `schedule`, `threadBinding`, and `templateId` in addition to the existing fields. When both `templateId` and explicit fields are supplied, the explicit fields win.

### 8.4 Turn-Level Errors

Errors during agent execution are delivered as `turn/failed` notifications (not as JSON-RPC error responses to the `turn/start` request, because the request itself succeeded — it is the asynchronous agent run that failed).

The `turn/failed` notification includes the error in `turn.error`:

```json
{ "jsonrpc": "2.0", "method": "turn/failed", "params": {
    "turn": {
      "id": "turn_001",
      "threadId": "thread_...",
      "status": "failed",
      "error": "Model returned an error: context window exceeded"
    }
} }
```

If an `Error` item was created during the turn, it appears in the `items` array and is also emitted via `item/started` / `item/completed` before the `turn/failed` notification.

---

## 9. Backpressure

### 9.1 Server-Side Queuing

The server uses bounded internal queues between transport ingress, request processing, and outbound writes. When the inbound queue is saturated:

- New requests are rejected with error code `-32001` and message `"Server overloaded; retry later."`.
- Clients should treat this as retryable and use **exponential backoff with jitter**.

### 9.2 Client-Side Considerations

- Clients should not send a `turn/start` while a turn is already in progress on the same thread. The server rejects this with error code `-32012`.
- Clients should consume notifications promptly. If a client falls behind on reading stdout (stdio transport) or WebSocket frames, the server may buffer up to a limit and then drop the connection.

### 6.9 Job Result Notifications

#### `system/jobResult`

Emitted after a server-managed cron or heartbeat job completes. This allows connected wire clients to receive the agent's response as an out-of-band notification, without the client initiating a turn.

Clients can opt out via `optOutNotificationMethods: ["system/jobResult"]` during `initialize`.

**Params**:

```json
{
  "source": "cron",
  "jobId": "9c933b01",
  "jobName": "喝水提醒",
  "threadId": "thread_abc123",
  "result": "提醒：该喝水了！保持水分对健康很重要。",
  "error": null,
  "tokenUsage": { "inputTokens": 420, "outputTokens": 38 }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `source` | string | `"cron"` or `"heartbeat"`. |
| `jobId` | string? | Cron job ID. Present when `source` is `"cron"`; absent for heartbeat. |
| `jobName` | string? | Human-readable job name. |
| `threadId` | string? | The thread ID used for execution. |
| `result` | string? | Agent's text response. Null if the turn failed or produced no text output. |
| `error` | string? | Error message if the turn failed. |
| `tokenUsage` | object? | `{ inputTokens, outputTokens }`. |

**Targeting rules**:

- Emitted to initialized protocol connections that are eligible to receive job-result notifications.
- Server hosts that route job results through another delivery surface may omit `system/jobResult`.
- Clients that do not wish to receive cron/heartbeat results can opt out via `optOutNotificationMethods: ["system/jobResult"]`.

**Behavior notes**:

- The `result` field carries the agent's full text output from the completed run.
- The `threadId` field may be used with `thread/read` to retrieve the associated conversation history.
- `cron/stateChanged` may also be emitted for the same completion when the source is a cron job.

**Example sequence**:

```
Server                                         Client
  |                                               |
  | (60 s after job was scheduled)                |
  |                                               |
  | [CronService timer fires, AgentRunner runs]   |
  |                                               |
  | system/jobResult (notification)               |
  |  source: "cron",                              |
  |  jobId: "9c933b01",                           |
  |  jobName: "喝水提醒",                         |
  |  threadId: "thread_abc123",                   |
  |  result: "该喝水了！保持水分对健康很重要。"   |
  |  tokenUsage: { inputTokens: 420, ... }        |
  |<----------------------------------------------|
  |                                               |
  | cron/stateChanged (notification)              |
  |  job.state.lastThreadId: "thread_abc123",     |
  |  job.state.lastResult: "该喝水了！...",       |
  |  removed: false                               |
  |<----------------------------------------------|
```

### 6.10 Notification Delivery Guarantees

The server MUST deliver each event notification **at most once per connection**, regardless of how many delivery paths are active for that thread.

**At-most-once rule**: When a connection holds an active `thread/subscribe` subscription for a thread and calls `turn/start` on the same thread, the server MUST NOT create a separate inline notification dispatch path for the turn. The existing subscription dispatcher is the sole delivery path for all turn-scoped notifications. The `turn/start` JSON-RPC response is still returned inline before any notifications are emitted.

This rule applies to all turn-scoped notifications:

| Notification | Covered |
|---|---|
| `turn/started` | yes |
| `turn/completed` | yes |
| `turn/failed` | yes |
| `turn/cancelled` | yes |
| `item/started` | yes |
| `item/agentMessage/delta` | yes |
| `item/reasoning/delta` | yes |
| `item/toolCall/argumentsDelta` | yes |
| `item/commandExecution/outputDelta` | yes |
| `item/completed` | yes |
| `item/usage/delta` | yes |
| `subagent/progress` | yes |
| `system/event` | yes |

Broadcast summary notifications such as `thread/started`, `thread/renamed`, `thread/deleted`, `thread/statusChanged`, and `thread/runtimeChanged` are **not** part of this thread-subscription delivery rule. They remain workspace-level broadcasts and may be delivered even when the connection is not subscribed to the target thread.

**Rationale**: Without this rule, a connection that both subscribes to a thread and starts a turn on that thread could receive duplicate notifications through multiple delivery paths.

**Ordering guarantee**: The at-most-once rule does not relax the ordering guarantee. The `turn/start` response still arrives before the first `turn/started` notification.

**Best-effort delivery**: Notifications are best-effort per connection. A transport write failure must stop further writes to that client, but it must not stop the server from draining an already-started persisted turn's event stream. Passive `thread/subscribe` streams remain tied to the connection and are cancelled when that connection closes; active turn execution continues independently. When `turn/start` uses the subscription path, the server's internal active-turn drain must continue after subscription cancellation. Outstanding interactive requests are resolved only through their normal client response, explicit non-interactive fallback for unsupported/unavailable clients, transport disconnect, or a request-specific timeout such as approval timeout; `thread/unsubscribe` alone must not answer them. Reconnected or returning clients recover state through `thread/read`, `thread/list`, fresh subscriptions, and server replay of unresolved interactive requests on `thread/subscribe` or `thread/resume`.

---

## 10. Notification Opt-Out

Clients can suppress specific notification methods per connection by listing exact method names in `initialize.params.capabilities.optOutNotificationMethods`.

- Matching is **exact** — no wildcards or prefix matching.
- Unknown method names are accepted and silently ignored.
- Applies only to server-to-client notifications, not to requests or responses.
- Opt-out is negotiated once at initialization time and cannot be changed for the connection's lifetime.

**Common opt-out targets**:

| Method | When to opt out |
|--------|-----------------|
| `item/agentMessage/delta` | Client does not support streaming; will wait for `item/completed`. |
| `item/reasoning/delta` | Client does not display reasoning content. |
| `item/toolCall/argumentsDelta` | Client does not need progressive tool-argument preview; waits for final `item/completed` payload. |
| `thread/started` | Client does not need thread lifecycle events. |
| `thread/renamed` | Client does not need server-pushed display name updates (e.g. refreshes `thread/list` on a timer only). |
| `thread/deleted` | Client does not need thread list sync when threads are removed elsewhere (e.g. polls `thread/list` only). |
| `thread/statusChanged` | Client manages thread status locally. |
| `thread/runtimeChanged` | Client does not display per-thread live activity indicators (e.g. batch runner, headless integration). |
| `subagent/progress` | Client does not display SubAgent real-time progress. |
| `item/usage/delta` | Client does not need real-time token consumption display; will use `turn/completed.tokenUsage` for final totals. |
| `system/event` | Client does not need system maintenance status (compaction, consolidation). |
| `plan/updated` | Client does not need real-time plan/todo progress display. |
| `system/jobResult` | Client does not need cron/heartbeat result notifications (e.g. batch or headless client). |
| `cron/stateChanged` | Client polls `cron/list` instead of reacting to server-push job state updates. |

**Example**:

```json
{
  "clientInfo": { "name": "batch-runner", "version": "1.0.0" },
  "capabilities": {
    "streamingSupport": false,
    "optOutNotificationMethods": [
      "item/agentMessage/delta",
      "item/reasoning/delta",
      "item/toolCall/argumentsDelta"
    ]
  }
}
```

---

## 11. Extension Methods

The core wire protocol (Sections 3–10) covers the `ISessionService` surface. Modules may expose **extension methods** for capabilities that are not intrinsic to the session core.

### 11.1 Design Rules

- Core methods are owned by the AppServer protocol runtime. Module methods are contributed by loaded modules and routed by method name at runtime.
- Module methods must not reuse a Core method name. If a module method is unavailable because the contributing module is not loaded or cannot operate in the current workspace, the server returns `-32601` (`Method not found`).
- Server-to-client extension families continue to use the `ext/<namespace>/...` prefix (for example `ext/acp/...`).
- Client-to-server module methods may use stable product namespaces; they are standard protocol extensions even when implemented by a module instead of Core.
- `initialize` may advertise extension availability in `capabilities.extensions`. Compatibility top-level capability fields may coexist during migration.
- Clients must treat the spec as the source of truth for a documented extension's method names and payloads; implementation location inside the server is not wire-visible.

### 11.2 Unified Channel Runtime (Remote Projection)

The external channel adapter integration uses **server → client** JSON-RPC requests under the `ext/channel/*` namespace. These methods are bidirectional protocol extensions in the same sense as `item/approval/request`: the server sends a request with an `id`, and the adapter returns a structured `result`.

Capability negotiation happens during `initialize` via `capabilities.channelAdapter`:

- `deliveryCapabilities.structuredDelivery = true` means the adapter implements the unified delivery contract through `ext/channel/send`.
- media entries under `deliveryCapabilities.media` describe which `message.kind` values the remote backend accepts and which source forms are allowed.
- `channelTools` declares the channel-scoped tools that may be injected into matching-origin threads for the lifetime of the connection.
- adapter-declared tools are validated and registered once per connection; later thread-level tool construction only filters visibility for the matching origin channel and current reserved names.

#### 11.2.1 `ext/channel/send`

Structured delivery path for text and media payloads.

**Direction**: server → client (request, requires response)

**Params**:

```json
{
  "target": "group:12345",
  "message": {
    "kind": "file",
    "caption": "Latest report",
    "fileName": "report.pdf",
    "mediaType": "application/pdf",
    "source": {
      "kind": "artifactId",
      "artifactId": "artifact_001"
    }
  },
  "metadata": {
    "origin": "cron"
  }
}
```

`message.kind` values standardized in v1:

- `text`
- `file`
- `audio`
- `image`
- `video`

`message` fields:

- `kind: string`
- `text?: string`
- `caption?: string`
- `fileName?: string`
- `mediaType?: string`
- `source?: ChannelMediaSource`

`ChannelMediaSource` fields:

- `kind: "hostPath" | "url" | "dataBase64" | "artifactId"`
- `hostPath?: string`
- `url?: string`
- `dataBase64?: string`
- `artifactId?: string`

Adapters must treat `source.kind` as authoritative and ignore unrelated source fields.

**Result**:

```json
{
  "delivered": true,
  "remoteMessageId": "msg_123",
  "remoteMediaId": "media_456",
  "errorCode": null,
  "errorMessage": null
}
```

When `delivered` is `false`, `errorCode` should use a stable string when possible. Standard protocol-level values:

- `UnsupportedDeliveryKind`
- `UnsupportedMediaSource`
- `MediaTooLarge`
- `MediaTypeNotAllowed`
- `MediaArtifactNotFound`
- `MediaResolutionFailed`
- `AdapterDeliveryFailed`
- `AdapterProtocolViolation`

#### 11.2.3 `ext/channel/toolCall`

Structured runtime tool invocation for adapter-declared channel tools.

**Direction**: server → client (request, requires response)

**Params**:

```json
{
  "threadId": "thread_001",
  "turnId": "turn_002",
  "callId": "exttool_001",
  "tool": "TelegramSendDocumentToCurrentChat",
  "arguments": {
    "fileName": "report.pdf"
  },
  "context": {
    "channelName": "telegram",
    "channelContext": "-1001234567890",
    "senderId": "user_42",
    "groupId": "-1001234567890"
  }
}
```

**Result**:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Sent report.pdf to the current chat." }
  ],
  "structuredResult": {
    "delivered": true,
    "fileName": "report.pdf"
  }
}
```

### 11.3 `item/tool/call`

Runtime dynamic tool invocation for client-declared `thread/start.dynamicTools`.

**Direction**: server -> client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread in which the tool is being executed. |
| `turnId` | string | Turn that owns the tool call. |
| `callId` | string | Server-generated tool call identifier. |
| `namespace` | string? | Optional namespace from the dynamic tool spec. |
| `tool` | string | Declared dynamic tool name. |
| `arguments` | object | Validated tool arguments matching `inputSchema`. |

**Result**:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Review draft recorded." }
  ],
  "structuredResult": {
    "draftId": "draft_123"
  }
}
```

If the tool fails, the client returns `{ "success": false, "errorCode": "...", "errorMessage": "..." }`. If the client disconnects, times out, or returns an invalid result, DotCraft completes the `dynamicToolCall` item as failed.

Interactive UI (when the tool declares `_meta.ui`) is rendered from the tool's UI resource and the host⇄UI bridge, not from the tool result; see [Interactive Tool UI](tool-result-presentation.md). The dynamic tool result itself carries only `contentItems` / `structuredResult` / error fields, which also serve as the text fallback for non-Desktop clients.

When `success` is `false`, `errorCode` should use a stable string when possible. Standard protocol-level values:

- `UnsupportedTool`
- `MissingChatContext`
- `InvalidArguments`
- `AdapterToolCallFailed`
- `AdapterProtocolViolation`
- `ExternalChannelToolTimeout`

Behavior rules:

- The server must only call tools declared in `capabilities.channelAdapter.channelTools` during `initialize`.
- A connected adapter's declared tool set is immutable for the lifetime of that connection.
- If an adapter declares channel tools, it must handle `ext/channel/toolCall` requests for those tools.
- Tool registration comes from the adapter's runtime handshake, not from static `ExternalChannels` config.
- When a tool descriptor declares `approval`, the server may gate execution before sending `ext/channel/toolCall`.
- `approval` metadata identifies approval targets for server interception only; it does not define an adapter-local approval policy.
- Any gating decision for adapter-declared tools must be resolved from the same server-owned thread/workspace policy surfaces used by built-in tools.
- For adapter-declared tools, item lifecycle projection is `pluginFunctionCall` only (`item/started` → `item/completed`). The server does not emit companion `toolCall`, `toolResult`, or `item/toolCall/argumentsDelta` events for the same invocation.

#### 11.3.1 Interactive Tool UI Resource Read

When a Desktop client that negotiated `interactiveToolUi` (see [Interactive Tool UI](tool-result-presentation.md)) renders a completed `dynamicToolCall` whose tool declared `_meta.ui.resourceUri`, it reads the `ui://` resource to load the app's UI into a sandboxed iframe.

**Method:** `ui/resource/read` — **Direction:** client → server (request, requires response)

**Params:**

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread whose App Binding owns the resource. |
| `namespace` | string? | Tool namespace selecting the bound app. |
| `uri` | string | The `ui://` resource URI from `_meta.ui.resourceUri`. |

**Result:**

```json
{
  "contents": [
    { "uri": "ui://oratorio/board.html", "mimeType": "text/html;profile=mcp-app", "text": "<!doctype html>…" }
  ]
}
```

Rules:

- The server brokers the read to the app that owns the binding for `threadId` (the app plays the MCP-server role and returns the resource over its App Binding connection).
- Only `ui://` resources belonging to a tool the binding exposes may be read; the server rejects reads outside the binding's tools/scope, and rejects this method for clients that did not negotiate `interactiveToolUi`.
- The host SHOULD cache by `uri`; changing the `uri` is the version / cache-bust lever.
- After load, the UI talks to the host over the client-internal postMessage bridge (`ui/*` + `tools/call`); a UI-initiated `tools/call` is forwarded by the host as `ui/tool/call` (§11.3.2). The bridge is not AppServer wire — see [Interactive Tool UI](tool-result-presentation.md) §7.

#### 11.3.2 UI-Initiated Tool Call (`ui/tool/call`)

When the interactive UI invokes `tools/call` over the bridge ([Interactive Tool UI](tool-result-presentation.md) §7.3), the Desktop host forwards it to AppServer so the call is gated and audited. This mirrors MCP Apps `callTool`: it is a UI↔app interaction, **decoupled from the agent conversation**. The UI never reaches a tool directly.

**Method:** `ui/tool/call` — **Direction:** client → server (request, requires response)

**Params:**

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread whose binding owns the tool. |
| `namespace` | string? | Tool namespace selecting the bound app. |
| `tool` | string | Tool name. |
| `arguments` | object | Tool arguments; validated against the tool's `inputSchema`. |
| `sourceCallId` | string? | `callId` of the `dynamicToolCall` whose UI initiated this call (provenance). |

Behavior:

- AppServer MUST verify the tool is app-bound to `threadId`, is `app`-visible (`_meta.ui.visibility` includes `"app"`), and within the binding's granted scope; otherwise it rejects. Cross-binding / cross-app calls are rejected.
- The call is **decoupled from the agent conversation**: it does **not** create a turn or a `dynamicToolCall` item, and the model does not observe it. The consent boundary is the app author's UI-visibility declaration (`_meta.ui.visibility`) plus the binding's granted scope, established when the user accepted the binding; AppServer does not re-prompt per UI call. Every call is recorded on the App Binding **audit trail**.
- AppServer dispatches the call to the app (brokered `item/tool/call`) and returns the result to the host, which relays it to the UI via the bridge (`ui/notifications/tool-result`). The model becomes aware of UI state only when the UI explicitly calls `ui/update-model-context` (push state) or `ui/message` (inject a follow-up turn).

**Result:** the same shape as a dynamic tool result — `{ "success", "contentItems"?, "structuredResult"?, "_meta"?, "errorCode"?, "errorMessage"? }`.

### 11.3 ACP Tool Proxy

The ACP (Agent Client Protocol) integration allows the agent's tools to access the IDE client's filesystem, terminals, and custom extension methods. On the AppServer wire, these map to **server → client** JSON-RPC requests (same bidirectional pattern as `item/approval/request` in [Section 7](#7-approval-flow)): the server sends a request with a numeric `id`; the client responds with a `result` or `error`.

**Capability negotiation**: The client declares `capabilities.acpExtensions` during `initialize` (see [Section 3.2](#32-initialize)). The server must only send `ext/acp/*` requests that the client has advertised:

- `fsReadTextFile` → may send `ext/acp/fs/readTextFile`
- `fsWriteTextFile` → may send `ext/acp/fs/writeTextFile`
- `terminalCreate` → may send `ext/acp/terminal/*`
- Each entry in `extensions` (e.g. `"_unity"`) → may send `ext/acp/<family>/<method>` for that family

**Per-thread binding**: When a connection that declared `acpExtensions` successfully creates a thread via `thread/start`, the server binds that thread id to that connection. While the agent runs a turn on that thread, `ext/acp/*` calls from tools are routed to the **bound** client's transport. If that connection closes before a pending `ext/acp/*` completes, the request fails (timeout or connection error).

**`threadId` in server→client params**: Every server→client `ext/acp/*` request MUST include `threadId` (string, camelCase) in `params`, equal to the Session Wire thread id for that turn. This lets clients with a single Wire connection (e.g. an ACP bridge) route concurrent server-initiated requests to the correct IDE session. Method-specific fields (e.g. `path`, `terminalId`) are in the same `params` object alongside `threadId`. ACP bridges SHOULD strip `threadId` before forwarding to the IDE when the IDE protocol does not define that field.

**Custom extensions**: Method pattern `ext/acp/<family>/<method>` where `<family>` was listed in `acpExtensions.extensions` (e.g. `ext/acp/_unity/scene_query`).

**ACP bridge runtime tools**: ACP clients that expose DotCraft-specific runtime tool descriptors during ACP `initialize` can have those descriptors translated by the ACP bridge into `thread/start.dynamicTools` and `thread/resume.dynamicTools`. Each descriptor's ACP method must correspond to an advertised filesystem, terminal, or custom extension capability. The model-visible tool contract is the Runtime Dynamic Tool spec from [Section 4.1.0](#410-runtime-dynamic-tools); the ACP method remains the private client callback used to execute the tool.

| ACP method (IDE) | Wire extension method |
|------------------|----------------------|
| `fs/readTextFile` | `ext/acp/fs/readTextFile` |
| `fs/writeTextFile` | `ext/acp/fs/writeTextFile` |
| `terminal/create` | `ext/acp/terminal/create` |
| `terminal/getOutput` | `ext/acp/terminal/getOutput` |
| `terminal/waitForExit` | `ext/acp/terminal/waitForExit` |
| `terminal/kill` | `ext/acp/terminal/kill` |
| `terminal/release` | `ext/acp/terminal/release` |

### 11.4 Node REPL Browser Runtime

The browser integrations expose agent tools through a **server -> client** Node REPL backend. The server only sends these requests to a thread-bound client that declared both `capabilities.nodeRepl` and `capabilities.browserUse` during `initialize`.

Clients may back the runtime with Desktop embedded browser tabs, a Chrome extension connected through Native Messaging, or another compatible backend declared in `capabilities.browserUse.backends`. Backend-specific setup and user-consent rules are owned by the contributing plugin skill, but all backends share the same `ext/nodeRepl/*` transport. Desktop in-app browser lifecycle, transport, diagnostics, and browser-use compatibility are defined in [Desktop In-App Browser Runtime](../runtime/desktop-inapp-browser.md). Chrome-specific browser session lifecycle, tab ownership, timeout, diagnostics, and migration goals are defined in [Chrome Browser Runtime](../runtime/chrome-browser-runtime.md).

#### `ext/nodeRepl/evaluate`

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID whose Desktop runtime owns the persistent REPL. |
| `turnId` | string | no | Current turn ID when the server can resolve one from tool execution scope. |
| `evaluationId` | string | yes | Unique ID for this evaluation, used for cancellation and late-result suppression. |
| `browserSession` | object | no | Browser session identity forwarded to embedded browser and Chrome backends. See [Desktop In-App Browser Runtime](../runtime/desktop-inapp-browser.md) and [Chrome Browser Runtime](../runtime/chrome-browser-runtime.md). |
| `code` | string | yes | JavaScript source to evaluate in the thread-bound persistent Node REPL. |
| `timeoutMs` | number | no | Requested overall timeout in milliseconds. Client may clamp to its supported range. |

`browserSession` fields:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `protocolVersion` | number | yes | Browser session metadata version. Current value is `1`. |
| `sessionId` | string | yes | Browser session isolation key. For normal DotCraft agent calls this is the thread ID. |
| `threadId` | string | no | Thread that owns the runtime. Duplicates `threadId` for clients that forward the session object deeper into browser backends. |
| `turnId` | string | no | Current turn ID when known. |
| `evaluationId` | string | yes | Evaluation ID associated with this Node REPL call. |

**Result**:

```json
{
  "text": "optional stdout-like text",
  "resultText": "serialized final expression result",
  "images": [
    { "mediaType": "image/png", "dataBase64": "..." }
  ],
  "logs": ["console output"],
  "error": "optional user-readable error"
}
```

The client should return before `timeoutMs` when possible. Browser sub-operations should use shorter internal timeouts and return a readable `error` rather than leaving the server request pending until the overall timeout.

#### `ext/nodeRepl/cancel`

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID whose REPL evaluation should be cancelled. |
| `evaluationId` | string | yes | Evaluation ID previously sent to `ext/nodeRepl/evaluate`. |

**Result**:

```json
{ "ok": true }
```

If no matching in-flight evaluation exists, the client returns `{ "ok": false }`. Cancellation is best-effort: the client should abort pending browser operations, rebuild the thread's REPL context when needed, and ignore any late result from the cancelled evaluation.

---

## 12. Versioning and Compatibility

### 12.1 Protocol Version

The protocol version is a single integer string (`"1"`, `"2"`, etc.) returned in `initialize` as `serverInfo.protocolVersion`.

### 12.2 Compatibility Rules

- **Within a major version**: The server may add new optional fields to existing method params/results, add new notification methods, and add new error codes. Clients must ignore unknown fields and unknown notification methods.
- **Breaking changes** (removing fields, changing semantics, removing methods) require incrementing the protocol version.
- **Method additions**: New methods may be added within a major version. Clients that call an unknown method receive a `-32601` error and can fall back gracefully.

### 12.3 Negotiation

The client and server agree on the protocol version during `initialize`. If the server's `protocolVersion` is higher than what the client supports, the client should log a warning and proceed with best-effort compatibility (ignoring unknown fields and methods). If the server's version is lower, the client should restrict itself to the server's supported surface.

---

## 13. Full Turn Example

This section shows the complete message sequence for a turn where the agent reads a file, runs a test (requiring approval), and responds.

### 13.1 ACP client turn (extension proxy)

When the wire client is an **ACP bridge** (IDE ↔ AppServer), the agent may need to read files through the IDE. The server sends `ext/acp/*` to the bridge; the bridge forwards to the IDE and returns the result.

```
IDE (ACP)          ACP Bridge          AppServer
  |                    |                    |
  | session/prompt     |                    |
  |------------------->|                    |
  |                    | turn/start         |
  |                    |------------------->|
  |                    |                    | (agent runs, needs file read)
  |                    | ext/acp/fs/readTextFile (server request)
  |                    |<-------------------|
  | fs/readTextFile    |                    |
  |<-------------------|                    |
  | (response)         |                    |
  |------------------->|                    |
  |                    | (response)         |
  |                    |------------------->|
  |                    |                    | (agent continues)
  |                    | item/agentMessage/delta
  |                    |<-------------------|
  | session/update     |                    |
  |<-------------------|                    |
  |                    | turn/completed     |
  |                    |<-------------------|
  | session/prompt response (end_turn)      |
  |<-------------------|                    |
```

### 13.2 Standard wire turn (no ACP)

```
Client                                          Server
  |                                               |
  | turn/start (request, id: 10)                  |
  |  threadId, input: "Run tests and fix"         |
  |---------------------------------------------->|
  |                                               |
  | (response, id: 10)                            |
  |  turn: { id: "turn_001", status: "running" }  |
  |<----------------------------------------------|
  |                                               |
  | turn/started (notification)                   |
  |  turn: { id: "turn_001", ... }                |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "userMessage", text: "..." }   |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "userMessage", ... }           |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "ReadFile", callId: "c1" }       |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c1", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 1200, outputTokens: 350         |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "approvalRequest",             |
  |    approvalType: "shell",                     |
  |    operation: "npm test" }                    |
  |<----------------------------------------------|
  |                                               |
  | item/approval/request (request, id: 100)      |
  |  requestId: "approval_001",                   |
  |  approvalType: "shell",                       |
  |  operation: "npm test"                        |
  |<----------------------------------------------|
  |                                               |
  | (response, id: 100)                           |
  |  decision: "accept"                           |
  |---------------------------------------------->|
  |                                               |
  | item/approval/resolved (notification)         |
  |  requestId: "approval_001", approved: true    |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "Exec", callId: "c2" }           |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c2", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "SpawnAgent",                    |
  |    arguments: { message: "analyze data",      |
  |      taskName: "analyzer",                    |
  |      agentNickname: "Analyzer" } }             |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "analyzer",               |
  |    currentTool: "ReadFile", ... }]            |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)  (~200ms)    |
  |  entries: [{ label: "analyzer",               |
  |    isCompleted: true, ... }]                  |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c3", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "agentMessage" }               |
  |<----------------------------------------------|
  |                                               |
  | item/agentMessage/delta (notification) x N    |
  |  delta: "I found 2 failing tests..."          |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "agentMessage",                |
  |    text: "I found 2 failing tests..." }       |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  turn: { status: "completed",                 |
  |    tokenUsage: { ... }, items: [...] }        |
  |<----------------------------------------------|
```

---

## 15. WebSocket Transport

### 15.1 Overview

The WebSocket transport is a network-accessible alternative to the stdio transport. It is the primary transport for external channel adapters (see the [External Channel Adapter Specification](external-channel-adapter.md)) and for any client that cannot be co-located with the server process.

Both transports use identical JSON-RPC 2.0 message shapes. The only differences are at the framing and connection-lifecycle layers described in this section.

| Property | stdio | WebSocket |
|----------|-------|-----------|
| Connection model | 1:1 (one client per server process) | N:1 (multiple concurrent clients per server process) |
| Frame format | Newline-delimited JSON (JSONL) | One JSON-RPC message per WebSocket text frame (UTF-8) |
| Client lifecycle | Bounded to process lifetime | Independent per-connection |
| Authentication | Not applicable (process isolation) | Optional bearer token (see §15.4) |
| Health probes | Not applicable | HTTP `GET /healthz` and `GET /readyz` |

### 15.2 Endpoint

The server listens on a configurable host and port. The WebSocket upgrade endpoint is:

```
ws://HOST:PORT/ws
```

The same HTTP server also serves the health probe endpoints:

- `GET /healthz` — returns `200 OK` with body `{"status":"ok"}` when the server process is alive.
- `GET /readyz` — returns `200 OK` when the server has completed protocol startup and is ready to accept connections. Readiness does not wait for workspace or plugin MCP servers to finish connecting; MCP readiness and failures are reported through `mcp/status/list`.

The default listen address binds to `127.0.0.1` only. Binding to `0.0.0.0` or a public interface must be explicitly configured and requires authentication to be enabled (see §15.4).

### 15.3 Connection Lifecycle

Each WebSocket connection is fully independent:

1. Client opens a WebSocket connection to `ws://HOST:PORT/ws` (with optional `?token=` query parameter, see §15.4).
2. Server accepts the connection and creates a new `AppServerConnection` state object. At this point the connection is **unauthenticated and uninitialized**.
3. Client sends `initialize` as the first JSON-RPC message (same as stdio, see §3.1).
4. Server responds and the standard initialization handshake proceeds.
5. Normal protocol operation: client sends requests, server sends responses and notifications.
6. On connection close (either side), the server cancels all active thread subscriptions for that connection.

```
Client                                    Server
  |                                         |
  | WebSocket upgrade (GET /ws)             |
  |---------------------------------------->|
  |                                         |
  | 101 Switching Protocols                 |
  |<----------------------------------------|
  |                                         |
  | initialize (request, id: 0)             |
  |---------------------------------------->|
  |                                         |
  | (response, id: 0)                       |
  |<----------------------------------------|
  |                                         |
  | initialized (notification)              |
  |---------------------------------------->|
  |                                         |
  | (protocol ready)                        |
```

### 15.4 Authentication

When the server is configured with a bearer token, the token must be provided by the client in the WebSocket upgrade request URL:

```
ws://HOST:PORT/ws?token=<token>
```

The server validates the token before completing the WebSocket upgrade. If the token is missing or invalid, the server closes the connection with HTTP `401 Unauthorized` before the WebSocket handshake completes. The client never reaches the JSON-RPC `initialize` step.

Token validation rules:

- Tokens are compared using constant-time equality to resist timing attacks.
- An empty string is not a valid token. If the server is configured with an empty token, authentication is disabled.
- Token values must be URL-safe (alphanumeric plus `-`, `_`, `.`). Tokens that do not meet this requirement must be URL-percent-encoded by the client.

When the server is bound to `127.0.0.1` only, authentication is optional. When the server is bound to a non-loopback address, authentication must be enabled — the server refuses to start without a token in this configuration.

### 15.5 Multi-Connection Behavior

Multiple clients may be connected simultaneously. Each connection has isolated state:

- Its own `initialize`/`initialized` handshake.
- Its own set of active thread subscriptions (registered via `thread/subscribe`).
- Its own backpressure gate (32 concurrent in-flight requests, same as stdio).

Shared state across all connections on the same server process:

- The `ISessionService` instance (and therefore thread persistence) is shared. A thread started by one connection is visible to other connections that look it up via `thread/list` or `thread/read`.
- A `thread/subscribe` from Connection A will receive notifications for events triggered by Connection B on the same thread.

There is no built-in per-connection identity isolation. Callers with different privilege levels must use separate server processes or implement identity enforcement in the `SessionIdentity` layer.

### 15.6 Framing

Each JSON-RPC message is sent as a single WebSocket **text frame** (opcode `0x1`). The message must be a complete, valid JSON object. Binary frames are not used.

Servers and clients must not split a single JSON-RPC message across multiple frames, and must not combine multiple JSON-RPC messages into a single frame.

Maximum message size is 4 MB by default. Messages exceeding this limit cause the connection to be closed with WebSocket close code `1009` (message too big).

### 15.7 Reconnection

The WebSocket transport does not provide built-in session resumption. When a client reconnects after a disconnect:

- The client must perform the full `initialize` / `initialized` handshake again.
- Active thread subscriptions are lost and must be re-registered via `thread/subscribe`.
- Any turn that was in progress when the disconnect occurred continues executing on the server. The client can re-subscribe to the thread to receive subsequent notifications, but events emitted during the disconnection period are not replayed unless `replayRecent = true` is used in `thread/subscribe`.
- Server-to-client approval requests (`item/approval/request`) that were in flight when the client disconnected will time out according to the approval timeout policy (error code `-32020`), and the turn will fail.

Client reconnection behavior requirements:

- Clients should implement transport reconnection with **exponential backoff with jitter** starting at 1 second and capping at 30 seconds.
- After each successful transport reconnect, clients must run a fresh protocol handshake (`initialize`, then `initialized`) before issuing normal requests.
- Clients should track and re-register any prior thread subscriptions immediately after reconnect.
- If the client process starts before the server is reachable, the client should keep retrying transport connection using the same backoff policy and complete handshake as soon as the server becomes available.

### 15.8 Native WebSocket Ping/Pong

The server sends native WebSocket ping frames every 30 seconds to detect stale connections. If a client does not respond with a pong frame within 10 seconds, the server closes the connection. Clients that use compliant WebSocket libraries will handle pong responses automatically.

### 15.9 Differences from Stdio

| Behavior | stdio | WebSocket |
|----------|-------|-----------|
| Connection count | One (process boundary) | Many (network connections) |
| Authentication | N/A | Optional token query param |
| Turn cancellation on disconnect | Turn is cancelled (process exit) | Turn continues; client must re-subscribe |
| Event replay on reconnect | N/A | Via `thread/subscribe replayRecent: true` |
| Approval request on disconnect | Turn cancelled (process exit) | Turn fails with `-32020` approval timeout |
| Diagnostic output | stderr | Not available on wire; use server logs |

---

## 16. Cron Management Methods

### 16.1 Scope

These methods extend the protocol beyond `ISessionService` to cover server-managed cron job lifecycle. They operate on shared server state that is independent of any session or thread.

Unlike thread/turn methods, cron methods are not scoped to a session, thread, or channel identity. They operate on the server's shared `CronService` singleton. All connections on the same server process observe the same cron state.

Clients must check `capabilities.cronManagement` in the `initialize` response before calling any `cron/*` method. If the flag is absent or `false`, the server returns `-32601` (method not found).

### 16.2 `CronJobInfo` Wire DTO

All cron methods that return job data use the following `CronJobInfo` wire object.

```json
{
  "id": "9c933b01",
  "name": "drink water reminder",
  "schedule": {
    "kind": "every",
    "everyMs": 3600000,
    "atMs": null,
    "initialDelayMs": null,
    "dailyHour": null,
    "dailyMinute": null,
    "tz": null
  },
  "enabled": true,
  "createdAtMs": 1710590400000,
  "deleteAfterRun": false,
  "state": {
    "nextRunAtMs": 1710594000000,
    "lastRunAtMs": 1710590400000,
    "lastStatus": "ok",
    "lastError": null,
    "lastThreadId": "thread_abc123",
    "lastResult": "提醒：该喝水了！保持水分对健康很重要。"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Short opaque job identifier (8 hex chars). |
| `name` | string | Human-readable job name. |
| `schedule.kind` | string | `"every"` (recurring), `"at"` (one-time), or `"daily"` (fixed local time of day). |
| `schedule.everyMs` | integer? | Interval in milliseconds. Present when `kind` is `"every"`. |
| `schedule.atMs` | integer? | Unix timestamp (ms) for one-time execution. Present when `kind` is `"at"`. |
| `schedule.initialDelayMs` | integer? | Present when `kind` is `"every"`: optional delay (ms) before the **first** run only; omitted or `null` when not used. |
| `schedule.dailyHour` | integer? | Present when `kind` is `"daily"`: local hour 0–23. |
| `schedule.dailyMinute` | integer? | Present when `kind` is `"daily"`: local minute 0–59. |
| `schedule.tz` | string? | IANA time zone id for `daily` schedules (e.g. `Asia/Shanghai`). Omitted or `null` means UTC. |
| `enabled` | boolean | Whether the job is active and will fire when due. |
| `createdAtMs` | integer | Unix timestamp (ms) when the job was created. |
| `deleteAfterRun` | boolean | If `true`, the job is removed after its first successful execution. |
| `state.nextRunAtMs` | integer? | Unix timestamp (ms) of the next scheduled run. `null` if the job has no valid schedule. May still be set when `enabled` is `false` (paused; the slot is preserved). |
| `state.lastRunAtMs` | integer? | Unix timestamp (ms) of the last execution. `null` if never run. |
| `state.lastStatus` | string? | `"ok"` or `"error"`. `null` if never run. |
| `state.lastError` | string? | Error message from the last failed run. `null` when `lastStatus` is `"ok"` or never run. |
| `state.lastThreadId` | string? | Thread ID used for the most recent execution. `null` if the job has never run. |
| `state.lastResult` | string? | Agent's text response from the most recent execution, truncated to 500 characters. `null` if the job has never run or the last run produced no text output. |

### 16.3 `cron/list`

List cron jobs managed by the server.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `includeDisabled` | boolean | no | Default `false`. When `true`, disabled jobs are included in the result. |

**Result**:

```json
{
  "jobs": [
    {
      "id": "9c933b01",
      "name": "drink water reminder",
      "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
      "enabled": true,
      "createdAtMs": 1710590400000,
      "deleteAfterRun": false,
      "state": {
        "nextRunAtMs": 1710594000000,
        "lastRunAtMs": 1710590400000,
        "lastStatus": "ok",
        "lastError": null
      }
    }
  ]
}
```

**Behavior**: Returns the server's current job list. When `includeDisabled` is `false` (default), only jobs with `enabled: true` are returned.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/list", "id": 50, "params": {
    "includeDisabled": true
} }

{ "jsonrpc": "2.0", "id": 50, "result": {
    "jobs": [
      {
        "id": "9c933b01",
        "name": "drink water reminder",
        "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
        "enabled": true,
        "createdAtMs": 1710590400000,
        "deleteAfterRun": false,
        "state": { "nextRunAtMs": 1710594000000, "lastRunAtMs": null, "lastStatus": null, "lastError": null }
      }
    ]
} }
```

### 16.4 `cron/remove`

Permanently remove a cron job from the server.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jobId` | string | yes | ID of the cron job to remove. |

**Result**:

```json
{ "removed": true }
```

**Errors**:

| Code | When |
|------|------|
| `-32031` | The specified `jobId` does not exist. |

**Behavior**: Removes the job from the server-managed cron set. If the job fires concurrently, removal is applied after the current execution completes.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/remove", "id": 51, "params": {
    "jobId": "9c933b01"
} }

{ "jsonrpc": "2.0", "id": 51, "result": { "removed": true } }
```

### 16.5 `cron/enable`

Enable or disable a cron job.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jobId` | string | yes | ID of the cron job to update. |
| `enabled` | boolean | yes | `true` to enable the job; `false` to disable it. |

**Result**:

```json
{
  "job": { ... }
}
```

The `job` field contains the updated `CronJobInfo` object reflecting the new `enabled` state. When **enabling** a job, `state.nextRunAtMs` is recomputed **only** if it was `null` or less than or equal to the current time (UTC, i.e. due or overdue); otherwise the existing future `nextRunAtMs` is kept so pause/resume does not shift the schedule.

**Errors**:

| Code | When |
|------|------|
| `-32031` | The specified `jobId` does not exist. |

**Behavior**: Updates the job's `enabled` field in the server's in-memory `CronService`. Disabling does not clear `nextRunAtMs`. When enabling, `nextRunAtMs` is updated only as described above. Persists the change to disk immediately.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/enable", "id": 52, "params": {
    "jobId": "9c933b01",
    "enabled": false
} }

{ "jsonrpc": "2.0", "id": 52, "result": {
    "job": {
      "id": "9c933b01",
      "name": "drink water reminder",
      "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
      "enabled": false,
      "createdAtMs": 1710590400000,
      "deleteAfterRun": false,
      "state": { "nextRunAtMs": 1710594000000, "lastRunAtMs": null, "lastStatus": null, "lastError": null }
    }
} }
```

### 16.6 `cron/run`

Manually queue one immediate run of a cron job.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jobId` | string | yes | ID of the cron job to run now. |

**Result**:

```json
{ "queued": true, "job": { "...": "CronJobInfo" } }
```

**Behavior**: Queues one manual execution on the server's existing serialized cron execution queue and returns immediately. This does not change the job's `enabled` state; disabled jobs can still be run manually. The job state is updated when execution completes and is surfaced via `cron/stateChanged` / `system/jobResult` when those notifications are enabled.

### 16.7 Notification Opt-Out

Cron management methods (`cron/list`, `cron/remove`, `cron/enable`, `cron/run`) are request/response pairs. The `cron/stateChanged` notification (Section 16.8) is the real-time push for cron job state. The `system/jobResult` notification (Section 6.9) remains the full result delivery mechanism. Clients that do not need either can opt out:

| Method | When to opt out |
|--------|-----------------|
| `cron/stateChanged` | Client polls `cron/list` instead of reacting to push updates. |
| `system/jobResult` | Client does not need cron/heartbeat result notifications. |

### 16.8 `cron/stateChanged` Notification

**Direction**: server → client (notification)

Emitted when a cron job's state changes.

**Triggers**:

| Trigger | What changed |
|---------|-------------|
| Job execution completes (success or error) | `state.lastRunAtMs`, `state.lastStatus`, `state.lastError`, `state.lastThreadId`, `state.lastResult`, `state.nextRunAtMs` updated. |
| `cron/enable` called | `enabled` updated; `state.nextRunAtMs` may change when enabling only if the previous next run was missing or in the past (otherwise unchanged). |
| `cron/run` called | No immediate persisted field changes; completion later emits the normal execution update. |
| `cron/remove` called | Notifies clients the job no longer exists (see `removed` field). |

**Params**:

```json
{
  "job": {
    "id": "9c933b01",
    "name": "drink water reminder",
    "schedule": { "kind": "every", "everyMs": 3600000 },
    "enabled": true,
    "createdAtMs": 1710590400000,
    "deleteAfterRun": false,
    "state": {
      "nextRunAtMs": 1710597600000,
      "lastRunAtMs": 1710594000000,
      "lastStatus": "ok",
      "lastError": null,
      "lastThreadId": "thread_abc123",
      "lastResult": "提醒：该喝水了！"
    }
  },
  "removed": false
}
```

| Field | Type | Description |
|-------|------|-------------|
| `job` | CronJobInfo | The updated job state. Contains the full `CronJobInfo` DTO reflecting the new state. |
| `removed` | boolean | `true` when the notification is triggered by `cron/remove`. When `true`, only `job.id` is guaranteed to be present. |

**Delivery**: Broadcast to all initialized connections that have not opted out of `cron/stateChanged`.

**Example sequence — job completes**:

```
Server                                          Client
  |                                               |
  | [CronService timer fires, AgentRunner runs]   |
  |                                               |
  | cron/stateChanged (notification)              |
  |  job.id: "9c933b01",                          |
  |  job.state.lastStatus: "ok",                  |
  |  job.state.lastThreadId: "thread_abc123",     |
  |  removed: false                               |
  |---------------------------------------------> |
```

**Example sequence — job removed**:

```
Server                                          Client
  |                                               |
  | [cron/remove request received]                |
  |                                               |
  | cron/stateChanged (notification)              |
  |  job.id: "9c933b01",                          |
  |  removed: true                                |
  |---------------------------------------------> |
```

---

## 17. Heartbeat Management Methods

### 17.1 Scope

Like cron management (Section 16), these methods cover a server-managed background service. The `heartbeat/trigger` method lets clients trigger a heartbeat run on demand.

Clients must check `capabilities.heartbeatManagement` before calling any method in this section. If the capability is absent or `false`, the server returns `-32601` (Method not found).

### 17.2 `heartbeat/trigger`

Trigger an immediate heartbeat run on the server.

**Direction**: client → server (request)

**Params**: `{}` (empty object, no parameters required)

**Result**:

```json
{
  "result": "HEARTBEAT_OK",
  "error": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `result` | string? | Agent response text. `null` if no `HEARTBEAT.md` was found or its content was empty. |
| `error` | string? | Error message if the heartbeat run failed. `null` on success. |

**Errors**:

| Code | When |
|------|------|
| `-32601` | The heartbeat service is not configured on this server. |

**Timeout note**: This is a **long-running request**. Clients should use a generous timeout. The result is also separately broadcast via `system/jobResult` with `source: "heartbeat"` to subscribed clients.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "heartbeat/trigger", "id": 60, "params": {} }

{ "jsonrpc": "2.0", "id": 60, "result": {
    "result": "Reviewed open issues and updated tracking.",
    "error": null
} }
```

### 17.3 Capability Advertisement

Clients must check `capabilities.heartbeatManagement` before calling `heartbeat/trigger`.

---

## 18. Skills Management Methods

### 18.1 Scope

These methods expose skill discovery and control to wire clients. Skills are markdown files (`SKILL.md`) that teach the agent specific capabilities. The server may load them from multiple sources with a defined priority order:

| Priority | Source | Location | Description |
|----------|--------|----------|-------------|
| 1 (highest) | `builtin` | Server-defined | Server-provided built-in skill. |
| 2 | `workspace` | Server-defined | Workspace-scoped skill. |
| 3 (lowest) | `user` | Server-defined | User-scoped skill. |

When the same skill name exists in multiple sources, the higher-priority source takes precedence.

Skills may declare requirements (executables, environment variables) in their frontmatter. A skill whose requirements are not met is reported as `available: false` with a diagnostic reason.

Clients must check `capabilities.skillsManagement` in the `initialize` response before calling any `skills/*` method. If the flag is absent or `false`, the server returns `-32601` (method not found).

### 18.2 `SkillInfo` Wire DTO

All skills methods that return skill data use the following `SkillInfo` wire object.

```json
{
  "name": "browser",
  "description": "Browser automation via Playwright MCP - navigate, click, fill forms, take screenshots, and inspect web pages.",
  "displayName": "Browser",
  "shortDescription": "Automate browser-based workflows",
  "source": "builtin",
  "available": true,
  "unavailableReason": null,
  "enabled": true,
  "path": "/home/user/project/skills/browser/SKILL.md",
  "hasVariant": true,
  "iconSmallDataUrl": "data:image/svg+xml;base64,...",
  "iconLargeDataUrl": "data:image/png;base64,...",
  "defaultPrompt": "Use $browser to inspect a local browser target.",
  "metadata": {
    "description": "Browser automation via Playwright MCP...",
    "bins": "npx"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Skill directory name, used as the skill identifier. |
| `description` | string | Human-readable description extracted from frontmatter `description` field. Falls back to `name` if absent. |
| `displayName` | string? | Optional UI display name from `agents/openai.yaml` `interface.display_name`. |
| `shortDescription` | string? | Optional compact UI description from `agents/openai.yaml` `interface.short_description`. |
| `source` | string | One of `"workspace"`, `"plugin"`, `"builtin"`, or `"user"`. Indicates where the skill is installed. |
| `available` | boolean | `true` if all declared requirements (bins, env) are met on the server. |
| `unavailableReason` | string? | Diagnostic message listing missing requirements. `null` when `available` is `true`. |
| `enabled` | boolean | `true` if the skill is active and will be included in agent context. `false` if the user has disabled it via `skills/setEnabled`. |
| `path` | string | Absolute filesystem path to the source `SKILL.md` file. |
| `hasVariant` | boolean? | Present and `true` when the current runtime resolves this skill through a current workspace variant. Omitted or `false` means the effective skill currently falls back to source. |
| `iconSmallDataUrl` | string? | Optional small icon as a data URL. Resolved only from safe relative paths inside the skill directory. |
| `iconLargeDataUrl` | string? | Optional large icon as a data URL. Resolved only from safe relative paths inside the skill directory. |
| `defaultPrompt` | string? | Optional default starter prompt from `agents/openai.yaml` `interface.default_prompt`. |
| `metadata` | object | Key-value pairs from the YAML frontmatter of `SKILL.md`. Common keys: `description`, `name`, `bins`, `env`, `always`. |

Servers may read skill interface metadata from `agents/openai.yaml`:

```yaml
interface:
  display_name: "Browser"
  short_description: "Automate browser-based workflows"
  icon_small: "./assets/browser-small.svg"
  icon_large: "./assets/browser.png"
  default_prompt: "Use $browser to inspect a local browser target."
```

Icon paths MUST be relative to the skill directory, MUST remain inside that directory after normalization, and SHOULD be ignored if missing, too large, or not an allowed image type.

### 18.3 `skills/list`

List all installed skills across all sources.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `includeUnavailable` | boolean | no | Default `true`. When `false`, skills with unmet requirements are excluded. |

**Result**:

```json
{
  "skills": [
    {
      "name": "browser",
      "description": "Browser automation via Playwright MCP...",
      "source": "builtin",
      "available": true,
      "unavailableReason": null,
      "enabled": true,
      "path": "/home/user/project/skills/browser/SKILL.md",
      "hasVariant": true,
      "metadata": { "description": "Browser automation via Playwright MCP...", "bins": "npx" }
    },
    {
      "name": "create-hooks",
      "description": "Create and configure DotCraft lifecycle hooks...",
      "source": "builtin",
      "available": true,
      "unavailableReason": null,
      "enabled": true,
      "path": "/home/user/project/skills/create-hooks/SKILL.md",
      "metadata": { "name": "create-hooks", "description": "Create and configure DotCraft lifecycle hooks..." }
    },
    {
      "name": "my-custom-skill",
      "description": "Custom workspace skill for this project.",
      "source": "workspace",
      "available": true,
      "unavailableReason": null,
      "enabled": true,
      "path": "/home/user/project/skills/my-custom-skill/SKILL.md",
      "metadata": { "description": "Custom workspace skill for this project." }
    },
    {
      "name": "code-review",
      "description": "Code review guidelines and procedures.",
      "source": "user",
      "available": true,
      "unavailableReason": null,
      "enabled": false,
      "path": "/home/user/.craft/skills/code-review/SKILL.md",
      "metadata": { "description": "Code review guidelines and procedures." }
    }
  ]
}
```

**Behavior**: Returns skills from all sources merged by the standard priority rules. Skills may have source `workspace`, `plugin`, `builtin`, or `user`. Plugin skills include `pluginId` and `pluginDisplayName` attribution. Workspace user-owned skills have highest priority, then enabled plugin skills, compatibility built-ins, and user-global skills.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "skills/list", "id": 70, "params": {} }

{ "jsonrpc": "2.0", "id": 70, "result": {
    "skills": [
      {
        "name": "browser",
        "description": "Browser automation via Playwright MCP...",
        "source": "builtin",
        "available": true,
        "unavailableReason": null,
        "enabled": true,
        "path": "/home/user/project/skills/browser/SKILL.md",
        "hasVariant": true,
        "metadata": { "description": "Browser automation via Playwright MCP...", "bins": "npx" }
      }
    ]
} }
```

### 18.4 `skills/read`

Read the full content of a skill's `SKILL.md` file.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Skill name (directory name) to read. |

**Result**:

```json
{
  "name": "browser",
  "content": "---\ndescription: \"Browser automation via Playwright MCP...\"\nbins: npx\n---\n\n# Browser Automation (Playwright MCP)\n\nYou have access to browser automation tools...",
  "metadata": {
    "description": "Browser automation via Playwright MCP...",
    "bins": "npx"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | The skill name that was requested. |
| `content` | string | Raw `SKILL.md` content including frontmatter. |
| `metadata` | object | Parsed frontmatter key-value pairs. `null` if the file has no frontmatter. |

**Errors**:

| Code | When |
|------|------|
| `-32040` | The specified skill name does not exist in any source. |

**Behavior**: Loads the resolved skill content according to the server's source-priority rules. Returns the raw markdown content of the `SKILL.md` file and its parsed frontmatter metadata.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "skills/read", "id": 71, "params": {
    "name": "browser"
} }

{ "jsonrpc": "2.0", "id": 71, "result": {
    "name": "browser",
    "content": "---\ndescription: \"Browser automation...\"\nbins: npx\n---\n\n# Browser Automation\n\n...",
    "metadata": { "description": "Browser automation...", "bins": "npx" }
} }
```

### 18.5 `skills/view`

Read the effective skill body after source/variant resolution.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Skill name to view. |

**Result**:

```json
{
  "name": "browser",
  "content": "# Browser Automation\n\nYou have access to browser automation tools..."
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | The skill name that was requested. |
| `content` | string | Effective `SKILL.md` body with YAML frontmatter stripped. |

**Behavior**: Resolves the current workspace adaptation when one exists and falls back to the source skill otherwise. The result intentionally omits variant ids, source paths, fingerprints, and metadata.

### 18.6 `skills/restoreOriginal`

Restore the original source skill for the current workspace target.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Skill name to restore. |

**Result**:

```json
{
  "name": "browser",
  "restored": true
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | The skill name that was requested. |
| `restored` | boolean | `true` when a current adaptation was restored; `false` when the skill was already using its source body. |

**Behavior**: Marks the current workspace adaptation as restored so future effective views fall back to the source skill. It does not modify the source skill.

### 18.7 `skills/setEnabled`

Enable or disable a skill. Disabled skills remain on disk but are excluded from agent context.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Skill name to enable or disable. |
| `enabled` | boolean | yes | `true` to enable the skill; `false` to disable it. |

**Result**:

```json
{
  "skill": {
    "name": "browser",
    "description": "Browser automation via Playwright MCP...",
    "source": "builtin",
    "available": true,
    "unavailableReason": null,
    "enabled": false,
    "path": "/home/user/project/skills/browser/SKILL.md",
    "metadata": { "description": "Browser automation via Playwright MCP...", "bins": "npx" }
  }
}
```

The `skill` field contains the updated `SkillInfo` object reflecting the new `enabled` state.

On success, the server emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "skills/setEnabled"` and `regions: ["skills"]`.

**Errors**:

| Code | When |
|------|------|
| `-32040` | The specified skill name does not exist in any source. |

**Behavior**: Toggles a skill's enabled state in the server's persisted skill-preference store.

When disabling, the skill is marked unavailable for future agent context resolution. When enabling, that exclusion is removed. If the skill is already in the requested state, the operation is a no-op and returns the current `SkillInfo`.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "skills/setEnabled", "id": 72, "params": {
    "name": "browser",
    "enabled": false
} }

{ "jsonrpc": "2.0", "id": 72, "result": {
    "skill": {
      "name": "browser",
      "description": "Browser automation via Playwright MCP...",
      "source": "builtin",
      "available": true,
      "unavailableReason": null,
      "enabled": false,
      "path": "/home/user/project/skills/browser/SKILL.md",
      "hasVariant": true,
      "metadata": { "description": "Browser automation via Playwright MCP...", "bins": "npx" }
    }
} }
```

### 18.8 `skills/uninstall`

Uninstall a user-managed source skill.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Skill name to uninstall. |

**Result**:

```json
{
  "name": "code-review",
  "uninstalled": true,
  "source": "user",
  "removedSourcePath": "/home/user/.craft/skills/code-review",
  "removedVariantCount": 1
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | The skill name that was requested. |
| `uninstalled` | boolean | `true` when the source skill directory was removed. |
| `source` | string | Removed source kind, either `"workspace"` or `"user"`. |
| `removedSourcePath` | string | Absolute directory path that was removed. |
| `removedVariantCount` | number | Number of associated workspace variants removed. |

On success, the server removes the skill from `Skills.DisabledSkills`, deletes associated variants for that source skill, and emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "skills/uninstall"` and `regions: ["skills"]`.

**Errors**:

| Code | When |
|------|------|
| `-32602` | The resolved skill source is `builtin` or `plugin`, or the source path is outside the expected skill root. |
| `-32040` | The specified skill name does not exist in any source. |

**Behavior**: Only `workspace` and `user` skills are directly uninstallable. `builtin` skills are managed by DotCraft, and `plugin` skills are managed by their owning plugin lifecycle.

### 18.9 Plugin Management Methods

Clients must check `capabilities.pluginManagement` before calling any `plugin/*` method. These methods expose local plugin discovery and workspace enablement state for Desktop and other UI clients. Plugin architecture, manifest fields, plugin-bundled MCP servers, and plugin-contained skills are defined in [Plugin Architecture](../extensions/plugin-architecture.md).

#### `plugin/list`

Returns discovered plugins, including disabled installed plugins and installable built-in catalog plugins when requested.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `includeDisabled` | boolean? | no | When false, disabled plugins are excluded. Default true. |

**Result**:

```json
{
  "plugins": [
    {
      "id": "browser",
      "displayName": "Browser",
      "description": "Control the in-app browser with DotCraft",
      "enabled": true,
      "installed": true,
      "installable": false,
      "removable": true,
      "source": "workspace",
      "interface": {
        "displayName": "Browser",
        "shortDescription": "Control the in-app browser with DotCraft",
        "developerName": "DotHarness",
        "category": "Coding",
        "capabilities": ["Interactive", "Read", "Write"],
        "defaultPrompt": "Test my checkout flow on localhost"
      },
      "functions": [],
      "skills": [{ "name": "browser", "displayName": "Browser", "enabled": true }],
      "apps": [],
      "mcpServers": [
        {
          "name": "review",
          "runtimeName": "review-tools:review",
          "transport": "stdio",
          "enabled": true,
          "active": true
        }
      ]
    }
  ],
  "diagnostics": []
}
```

#### `plugin/view`

Returns one plugin by id.

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Plugin id. |

**Result**: `{ "plugin": PluginInfo }`

`PluginInfo` includes:

| Field | Type | Description |
|-------|------|-------------|
| `installed` | boolean | True when the plugin exists in a discovered local plugin root and can contribute runtime behavior. |
| `installable` | boolean | True for known desktop-bundled built-in catalog entries that are not installed in the workspace. |
| `removable` | boolean | True for DotCraft-managed built-in plugin directories that carry a `.builtin` marker. |
| `functions` | `PluginFunctionInfo[]` | Compatibility field for older clients; manifest native tools are no longer supported, so this is empty for plugin manifest contributions. |
| `skills` | `PluginSkillInfo[]` | Plugin-contained skills declared by the bundle. |
| `apps` | `PluginAppInfo[]` | Plugin-contained App Binding descriptors declared by the bundle. These are catalog/detail metadata; connection and binding still use `app/*` and `thread/appBindings/*`. |
| `mcpServers` | `PluginMcpServerInfo[]` | Plugin-bundled MCP declarations. This is declaration metadata for the plugin detail page, not an editable workspace MCP config. |

`PluginAppInfo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `appId` | string | App Binding app id, for example `com.dotharness.oratorio`. |
| `toolNamespace` | string | Namespace required for app-bound tools. |
| `displayName` | string | User-visible app name. |
| `developerName` | string | App developer or organization. |
| `description` | string | User-visible app description. |
| `category` | string | Optional UI category. |
| `icon` | string | Optional icon as a data URL or safe URL. |
| `nativeApplication` | object | Native app display name, protocol, and install URL metadata. |
| `toolCatalog` | `PluginAppToolInfo[]` | Summary of app-bound tools declared by the descriptor. |
| `dynamicToolCatalog` | object | Indicates whether this app may attach a runtime tool catalog during App Binding attachment. |

`PluginMcpServerInfo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Declared server name inside the plugin `.mcp.json`, for example `review`. |
| `runtimeName` | string | Effective runtime name, usually `{pluginId}:{name}`. |
| `transport` | `"stdio" \| "streamableHttp"` | MCP transport after normalization. |
| `enabled` | boolean | Whether the bundled server declaration is enabled. |
| `active` | boolean | True when the plugin is installed, enabled, the server is enabled, and it is not shadowed by workspace or higher-priority plugin MCP. |
| `shadowedBy` | `"workspace" \| "plugin"` | Optional reason the bundled server is not active. |

#### `plugin/install`

Installs a known desktop-bundled built-in plugin into the workspace. Uninstalled built-ins are installable only when AppServer was launched with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` pointing at bundled plugin source roots.

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Canonical plugin id. |

**Result**: `{ "plugin": PluginInfo }`

On success, the server copies the selected bundled plugin source to `.craft/plugins/<id>`, writes a `.builtin` source fingerprint marker, removes that id from `Plugins.DisabledPlugins`, refreshes plugin-contributed skill sources, reconciles effective MCP runtime state, and emits `workspace/configChanged` with `source: "plugin/install"` and `regions: ["plugins", "skills", "mcp"]`.

#### `plugin/remove`

Removes a DotCraft-managed built-in plugin from the workspace.

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Canonical plugin id. |

**Result**: `{ "plugin": PluginInfo }`

The server deletes only workspace plugin directories that carry the `.builtin` marker and are inside `.craft/plugins`. User-owned plugin directories are rejected. On success, the server refreshes plugin-contributed skill sources, reconciles effective MCP runtime state, and emits `workspace/configChanged` with `source: "plugin/remove"` and `regions: ["plugins", "skills", "mcp"]`.

#### `plugin/setEnabled`

Enables or disables an installed plugin for the workspace.

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Plugin id. |
| `enabled` | boolean | yes | Desired enabled state. |

**Result**: `{ "plugin": PluginInfo }`

`plugin/setEnabled` does not install a built-in catalog entry. If the plugin is not installed, the server rejects the request. On success, the server persists `Plugins.DisabledPlugins`, refreshes plugin-contributed skill sources, reconciles effective MCP runtime state, and emits `workspace/configChanged` with `source: "plugin/setEnabled"` and `regions: ["plugins", "skills", "mcp"]`.

### 18.9 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32040` | `SkillNotFound` | The requested skill name does not exist in any source (workspace, user, or builtin). |

### 18.10 Capability Advertisement

Clients must check `capabilities.skillsManagement` before calling any `skills/*` method.
Clients should additionally check `capabilities.skillVariants` before offering variant-dependent UX such as restoring the original skill. `skills/view` may still be available as a source-only effective view when this capability is absent or false.
Clients must check `capabilities.pluginManagement` before calling any `plugin/*` method.

---

## 19. Command Management Methods

### 19.1 Scope

These methods expose the server-side command registry to wire clients.

- `command/list` returns discoverable server-registered command metadata (including custom commands, and optionally built-ins when requested).
- `command/execute` executes a slash command and returns a normalized `CommandResult`.

Command resolution and execution semantics are server-authoritative.
Client-local UX commands (for example CLI/TUI `/clear`) are intentionally outside this registry surface and do not need to appear in `command/list`.

### 19.2 `CommandInfo` Wire DTO

```json
{
  "name": "/new",
  "aliases": [],
  "description": "Create a new conversation",
  "category": "builtin",
  "requiresAdmin": false
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Canonical slash command name. |
| `aliases` | string[] | Alternative slash names mapped to the same handler. |
| `descriptionKey` | string | Stable key for client-side localization. Empty for custom commands without a server key. |
| `fallbackDescription` | string | English fallback description supplied by the server. |
| `description` | string | Compatibility alias for `fallbackDescription`; clients should prefer `descriptionKey` + `fallbackDescription`. |
| `category` | string | `"builtin"` or `"custom"`. |
| `requiresAdmin` | boolean | Whether the command requires admin permission. |

### 19.3 `command/list`

List all available commands for the current workspace/runtime.

Clients that build a composer slash-command picker for `commandRef` insertion should pass `includeBuiltins = false` and treat the result as the custom-command catalog only. Built-in commands remain discoverable through `command/list` by default for general command surfaces.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `language` | string | no | Deprecated compatibility field. Servers MUST ignore it. Clients SHOULD omit it. |
| `includeBuiltins` | boolean | no | Optional filter for built-in commands. Defaults to `true`. Pass `false` when the caller wants a `commandRef`-safe custom-command list for a composer picker. |

**Result**:

```json
{
  "commands": [
    {
      "name": "/new",
      "aliases": [],
      "descriptionKey": "cmd.new",
      "fallbackDescription": "Create a new session",
      "description": "Create a new session",
      "category": "builtin",
      "requiresAdmin": false
    },
    {
      "name": "/code-review",
      "aliases": [],
      "descriptionKey": "",
      "fallbackDescription": "Review changed files and report issues",
      "description": "Review changed files and report issues",
      "category": "custom",
      "requiresAdmin": false
    }
  ]
}
```

### 19.4 `command/execute`

Execute one slash command through the server-side command pipeline.

Built-in slash commands must be invoked through this method (or equivalent dedicated UI controls), not encoded as `commandRef` parts in `turn/start`.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread for command execution context. |
| `command` | string | yes | Slash command string, for example `"/stop"` or `"/cron"`. |
| `arguments` | string[] | no | Optional parsed arguments. Server also accepts empty/omitted and performs standard parsing from `command` when needed. |
| `sender` | SenderContext | no | Optional sender identity used for permission evaluation and auditing. |

**Result**:

```json
{
  "handled": true,
  "message": "Started a new conversation.",
  "isMarkdown": false,
  "expandedPrompt": null,
  "sessionReset": true,
  "thread": {
    "id": "thread_20260414_ab12cd"
  },
  "archivedThreadIds": ["thread_20260414_old001"],
  "createdLazily": true
}
```

When `expandedPrompt` is non-null, the command resolved to a prompt expansion and the caller may submit that text with `turn/start`.

When `sessionReset` is `true` (for `/new`), clients should switch their active thread pointer to `thread.id` immediately. `createdLazily = true` means the new thread id is valid, but its thread file may not be materialized on disk until the first turn is submitted.

### 19.5 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32060` | `CommandNotFound` | The requested command is not registered. |
| `-32061` | `CommandPermissionDenied` | Caller lacks permission for an admin-only command. |
| `-32062` | `CommandServiceUnavailable` | Command exists but required backing service is unavailable. |

### 19.6 Capability Advertisement

Clients must check `capabilities.commandManagement` before calling `command/list` or `command/execute`.

---

## 19A. Background Terminal Methods

### 19A.1 Scope

These methods expose server-managed host shell processes that may continue after an `Exec` tool call returns. They are pipe-based in v1: clients can read output, write stdin, stop a session, list sessions, and clean all sessions for a thread. Full PTY/curses behavior and sandbox process persistence are outside this version.

Clients must check `capabilities.backgroundTerminals` before calling `terminal/*` methods. If absent or `false`, the server returns `-32601` (Method not found).

### 19A.2 `BackgroundTerminal` Wire DTO

```json
{
  "sessionId": "term_abcd1234",
  "threadId": "thread_001",
  "turnId": "turn_001",
  "callId": "call_abc",
  "command": "npm run dev",
  "workingDirectory": "C:/repo",
  "source": "host",
  "status": "running",
  "output": "...",
  "outputPath": "C:/repo/.craft/terminals/thread_001/term_abcd1234.log",
  "exitCode": null,
  "startedAt": "2026-04-25T00:00:00Z",
  "completedAt": null,
  "wallTimeMs": 1000,
  "originalOutputChars": 42,
  "truncated": false,
  "backgroundReason": "runInBackground"
}
```

`status` is one of `running`, `completed`, `failed`, `killed`, `timedOut`, or `lost`.

### 19A.3 Requests

- `terminal/list` params: `{ "threadId"?: string | null }`, result: `{ "terminals": BackgroundTerminal[] }`
- `terminal/read` params: `{ "sessionId": string, "waitMs"?: number, "maxOutputChars"?: number }`, result: `{ "terminal": BackgroundTerminal }`
- `terminal/write` params: `{ "sessionId": string, "input": string, "yieldTimeMs"?: number, "maxOutputChars"?: number }`, result: `{ "terminal": BackgroundTerminal }`
- `terminal/stop` params: `{ "sessionId": string }`, result: `{ "terminal": BackgroundTerminal }`
- `terminal/clean` params: `{ "threadId": string }`, result: `{ "terminals": BackgroundTerminal[] }`

`terminal/read.waitMs` is the maximum time the server waits for an active terminal to produce an updated snapshot or exit. If the wait elapses before the process exits, the server may return the current `running` snapshot.

### 19A.4 Notifications

Servers that have a client-declared `backgroundTerminals` capability may emit:

- `terminal/started`
- `terminal/outputDelta`
- `terminal/completed`
- `terminal/stalled`
- `terminal/cleaned`

Notifications use the same terminal snapshot shape. `terminal/outputDelta` additionally carries the output delta text.

First-party terminal-capable clients such as Desktop and TUI use these notifications for live Shell tool output, including foreground `Exec` calls. When a terminal originates from an `Exec` tool call, `terminal.callId` correlates it to the `toolCall` item that should receive live output and status updates. `terminal.threadId` scopes the update to the owning thread, and `terminal.turnId` scopes it to the originating turn when available.

If `terminal.backgroundReason = "runInBackground"`, the client must not keep appending later process output into the inline foreground `Exec` card. The inline card may show the returned session/status/final summary, while the background terminal UI owns ongoing process output.

---

## 20. Channel Status Methods

### 20.1 Scope

These methods expose runtime status of social and external channels — whether each channel is configured and whether it is currently active.

The existing `channel/list` method returns discoverable origin names. It does not reflect configuration state or runtime activity. `channel/status` is a separate method that reports runtime status.

Clients must check `capabilities.channelStatus` in the `initialize` response before calling `channel/status`. If the capability is absent or `false`, the server returns `-32601` (Method not found).

### 20.2 `ChannelStatusInfo` Wire DTO

```json
{
  "name": "qq",
  "category": "social",
  "enabled": true,
  "running": true
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Canonical channel name (matches `channel/list` names). |
| `category` | string | `social` or `external`. |
| `enabled` | boolean | `true` when the channel is configured as enabled. |
| `running` | boolean | `true` when the server currently considers the channel active. |

Only channels that are explicitly configured for status reporting are included.

### 20.3 `channel/status`

Returns runtime status for all configured social and external channels.

**Direction**: client → server (request)

**Params**: `{}` (empty object) or omitted.

**Result**:

```json
{
  "channels": [
    {
      "name": "qq",
      "category": "social",
      "enabled": true,
      "running": true
    },
    {
      "name": "wecom",
      "category": "social",
      "enabled": false,
      "running": false
    },
    {
      "name": "weixin",
      "category": "external",
      "enabled": true,
      "running": false
    },
    {
      "name": "telegram",
      "category": "external",
      "enabled": false,
      "running": false
    }
  ]
}
```

**Semantics**:

- `enabled` reflects configuration state, not runtime activity.
- `running` reflects current server-observed activity state.
- Results are sorted by category order (`social` → `external`), then by `name` (ordinal case-insensitive).
- If the server has no channel status data, the result is an empty `channels` array.

### 20.4 Capability Advertisement

Clients must check `capabilities.channelStatus` before calling `channel/status`.

---

## 21. Provider And Model Catalog Methods

### 21.1 Scope

These methods expose personal model provider management and provider model discovery. Provider records are personal configuration; workspace configuration only selects a provider id and model id.

Clients must check `capabilities.providerManagement` before calling `provider/list`, `provider/create`, `provider/update`, `provider/delete`, or `provider/test`. Clients must check `capabilities.modelCatalogManagement` before calling `model/list`. If absent or `false`, the server returns `-32601` (Method not found).

### 21.2 `ProviderInfo` Wire DTO

```json
{
  "id": "anthropic",
  "displayName": "Anthropic",
  "protocol": "anthropic",
  "apiKey": "********",
  "hasApiKey": true,
  "endPoint": "https://api.anthropic.com",
  "networkTimeoutSeconds": 600,
  "streamMaxRetries": 5,
  "streamIdleTimeoutMs": 300000,
  "isImplicit": false,
  "capabilities": {
    "streamingChat": true,
    "toolCalling": true,
    "modelListing": true
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stable personal provider id. |
| `displayName` | string | User-facing label. |
| `protocol` | string | Provider protocol. Canonical values are `openai-chat-completions`, `openai-responses`, and `anthropic`. Servers accept legacy `openai` as a read-only alias for `openai-chat-completions`, but provider mutations return and persist canonical values. |
| `apiKey` | string? | Redacted secret marker when a key is present; raw secrets are never returned. |
| `hasApiKey` | boolean | Whether the provider has an API key configured. |
| `endPoint` | string | Configured provider base URL. Empty means the protocol's official default endpoint. |
| `networkTimeoutSeconds` | integer? | Provider-specific timeout override. |
| `streamMaxRetries` | integer? | Provider-specific maximum stream reconnection attempts. Defaults to `5`; valid range is `0`-`100`. |
| `streamIdleTimeoutMs` | integer? | Provider-specific idle timeout for streaming responses. Defaults to `300000` milliseconds. |
| `isImplicit` | boolean | Reserved for runtime-managed providers; persisted personal providers return `false`. |
| `capabilities` | object | Provider-neutral capability flags such as streaming, tool calling, model listing, token usage, prompt-cache shaping, extended thinking, tool-choice controls, raw metadata passthrough, Responses API support, and native deferred tool loading. |

Additional capability flags include:

| Field | Type | Description |
|-------|------|-------------|
| `responsesApi` | boolean | Whether the provider protocol uses the OpenAI Responses API surface. |
| `nativeDeferredToolLoading` | boolean | Whether `Tools.DeferredLoading.Strategy = Native` is protocol-valid for this provider. |

### 21.3 Provider Management Methods

`provider/list` returns explicit personal providers with secrets redacted. It does not synthesize providers from root-level legacy LLM fields.

`provider/create` params:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Stable provider id. `openai` is allowed as an explicit personal provider id. |
| `displayName` | string | no | User-facing label. Defaults to `id`. |
| `protocol` | string | yes | `openai-chat-completions`, `openai-responses`, or `anthropic`. Legacy `openai` is accepted as an alias for `openai-chat-completions`. |
| `apiKey` | string | no | Provider credential or secret reference. |
| `endPoint` | string | no | Provider base URL. Empty values use the protocol's official default endpoint. |
| `networkTimeoutSeconds` | integer? | no | Timeout override; must be greater than zero. |
| `streamMaxRetries` | integer? | no | Stream reconnection retry budget; `0` disables stream retry and values above `100` are rejected. |
| `streamIdleTimeoutMs` | integer? | no | Per-stream idle timeout in milliseconds; must be greater than zero. |

`provider/update` accepts `id` plus any mutable provider fields from `provider/create`. Omitted fields are unchanged. `provider/delete` accepts `{ "id": "..." }` and removes a provider only when the active workspace selection would not be broken.

`provider/test` performs a low-cost provider-neutral probe by attempting model listing. It never performs a hidden chat-completion request. Params may reference a persisted provider:

```json
{ "providerId": "anthropic" }
```

or an unsaved provider draft:

```json
{
  "protocol": "anthropic",
  "apiKey": "${ANTHROPIC_API_KEY}",
  "endPoint": "https://api.anthropic.com",
  "networkTimeoutSeconds": 60,
  "streamMaxRetries": 5,
  "streamIdleTimeoutMs": 300000
}
```

The result shape mirrors model-list success and error handling:

```json
{
  "success": false,
  "protocol": "anthropic",
  "models": [],
  "errorCode": "EndpointNotSupported",
  "errorMessage": "Endpoint does not support model listing."
}
```

`EndpointNotSupported` is a normal setup outcome. Clients must still allow saving the provider and manually entering a model id. Provider test responses must not include raw credentials.

Provider mutations emit `workspace/configChanged` with region `providers`.

### 21.4 `ModelCatalogItem` Wire DTO

```json
{
  "id": "gpt-4o-mini",
  "ownedBy": "openai",
  "createdAt": "2025-06-12T00:00:00Z",
  "reasoning": {
    "supportsDisable": true,
    "supportedEfforts": [
      { "effort": "low", "label": "Low" },
      { "effort": "medium", "label": "Medium" },
      { "effort": "high", "label": "High" }
    ],
    "defaultEffort": "medium",
    "supportedOutputs": ["none", "summary", "full"],
    "defaultOutput": "full"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Model id used in `config.Model` / request payloads. |
| `ownedBy` | string | Provider-reported owner string when available; may be empty. |
| `createdAt` | string (ISO 8601 UTC) | Provider-reported creation time. |
| `reasoning` | object | Optional server-authored reasoning UI capability metadata. Clients must not hardcode model compatibility rules; use this metadata when present. |

`reasoning` fields:

| Field | Type | Description |
|-------|------|-------------|
| `supportsDisable` | boolean | Whether clients may show an enabled Off choice. |
| `supportedEfforts` | object[] | Supported quick-pick efforts. Each item contains `effort` (`low`, `medium`, `high`, or `extraHigh`) and a display `label`. |
| `defaultEffort` | string | Model default effort for Default/inherited behavior. |
| `supportedOutputs` | string[] | Supported reasoning output visibility values (`none`, `summary`, `full`). Quick pickers may leave this unchanged. |
| `defaultOutput` | string | Model default reasoning output visibility. |

### 21.5 `model/list`

Returns available models from a requested provider id, or from the workspace-selected provider when no provider id is supplied.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `providerId` | string? | no | Provider id to list. Omit or null to use the workspace-selected provider. |

**Result**:

```json
{
  "success": true,
  "providerId": "openai",
  "protocol": "openai-chat-completions",
  "models": [
    {
      "id": "gpt-4o-mini",
      "ownedBy": "openai",
      "createdAt": "2025-06-12T00:00:00Z"
    }
  ]
}
```

On provider/config errors, the method still returns a successful JSON-RPC response with structured error fields:

```json
{
  "success": false,
  "providerId": "anthropic",
  "protocol": "anthropic",
  "models": [],
  "errorCode": "MissingApiKey",
  "errorMessage": "API key is not configured."
}
```

If a provider cannot list models, the server returns `success: false` with a provider-neutral `errorCode`; clients must continue to allow manual model entry.

### 21.6 Capability Advertisement

Clients must check `capabilities.providerManagement` before calling provider management methods and `capabilities.modelCatalogManagement` before calling `model/list`.

---

## 22. MCP Management Methods

### 22.1 Scope

These methods provide a server-authoritative read/write path for MCP server configuration.

Clients must check `capabilities.mcpManagement` before calling `mcp/list`, `mcp/get`, `mcp/upsert`, or `mcp/remove`. Clients must check `capabilities.mcpStatus` before calling `mcp/status/list` or relying on `mcp/status/updated` notifications.

### 22.2 `McpServerConfig` Wire DTO

```json
{
  "name": "sqlite",
  "enabled": true,
  "transport": "stdio",
  "command": "openai-dev-mcp",
  "args": ["serve-sqlite"],
  "env": { "DB_PATH": "./test.db" },
  "envVars": ["OPENAI_API_KEY"],
  "cwd": "./tools",
  "origin": { "kind": "workspace" },
  "readOnly": false
}
```

Supported fields:

- `name: string`
- `enabled: boolean`
- `transport: "stdio" | "streamableHttp"`
- `command?: string`
- `args?: string[]`
- `env?: Record<string, string>`
- `envVars?: string[]`
- `cwd?: string | null`
- `url?: string`
- `bearerTokenEnvVar?: string | null`
- `httpHeaders?: Record<string, string>`
- `envHttpHeaders?: Record<string, string>`
- `startupTimeoutSec?: number | null`
- `toolTimeoutSec?: number | null`
- `origin?: { kind: "workspace" | "plugin", pluginId?: string, pluginDisplayName?: string, declaredName?: string }`
- `readOnly?: boolean`

Validation rules:

- `name` is the logical primary key and is compared case-insensitively.
- `stdio` only allows `command`, `args`, `env`, `envVars`, and `cwd`.
- `streamableHttp` only allows `url`, `bearerTokenEnvVar`, `httpHeaders`, and `envHttpHeaders`.
- `mcp/test` validates and probes a temporary configuration but does not persist it.
- When `capabilities.mcpServerOrigins` is true, `mcp/list`, `mcp/get`, `mcp/status/list`, and `mcp/status/updated` include origin metadata. Workspace-origin servers are editable. Plugin-origin servers are read-only runtime entries derived from plugin `.mcp.json`.

### 22.3 `mcp/list`

Returns effective MCP runtime servers for the current workspace. The result includes workspace MCP servers and active plugin-bundled MCP servers. Workspace MCP takes precedence when a runtime name conflicts; shadowed plugin MCP remains visible on `plugin/list` / `plugin/view` as declaration metadata but is not returned by `mcp/list`.

**Result**:

```json
{
  "servers": [
    {
      "name": "sqlite",
      "enabled": true,
      "transport": "stdio",
      "command": "openai-dev-mcp",
      "args": ["serve-sqlite"],
      "origin": { "kind": "workspace" },
      "readOnly": false
    },
    {
      "name": "review-tools:review",
      "enabled": true,
      "transport": "stdio",
      "origin": {
        "kind": "plugin",
        "pluginId": "review-tools",
        "pluginDisplayName": "Review Tools",
        "declaredName": "review"
      },
      "readOnly": true
    }
  ]
}
```

### 22.4 `mcp/get`

Returns one configured MCP server by name.

**Params**:

```json
{ "name": "sqlite" }
```

### 22.5 `mcp/upsert`

Creates or replaces one workspace-origin MCP server definition. Clients MUST NOT send plugin-origin servers back to `mcp/upsert`.

**Params**:

```json
{
  "server": {
    "name": "docs",
    "enabled": true,
    "transport": "streamableHttp",
    "url": "https://example.com/mcp",
    "bearerTokenEnvVar": "DOCS_TOKEN"
  }
}
```

**Semantics**:

- Upsert replaces the full logical server entry.
- Persistence shape and storage location are server-defined, but only workspace-origin servers are persisted to workspace config.
- If the target name currently resolves to a plugin-origin server, the server returns `McpServerReadOnly`.
- On success, the server emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "mcp/upsert"` and `regions: ["mcp"]`.

### 22.6 `mcp/remove`

Removes one workspace-origin MCP server definition by name. Removing a plugin-origin server returns `McpServerReadOnly`; plugin-bundled MCP is controlled through plugin install/enable/remove lifecycle, not MCP settings persistence.

On success, the server emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "mcp/remove"` and `regions: ["mcp"]`.

### 22.7 `McpServerStatus` Wire DTO

```json
{
  "name": "sqlite",
  "enabled": true,
  "startupState": "ready",
  "toolCount": 3,
  "resourceCount": 0,
  "resourceTemplateCount": 0,
  "lastError": null,
  "transport": "stdio"
}
```

Runtime status is separate from config truth:

- `mcp/list` describes effective runtime configuration and may include plugin-origin read-only entries.
- `mcp/status/list` and `mcp/status/updated` describe runtime state. MCP startup runs in the background and must not block AppServer readiness or unrelated MCP management requests.

### 22.8 `mcp/status/list`

Returns current runtime state for all known MCP servers.

### 22.9 `mcp/test`

Validates and probes a temporary MCP configuration without persisting it.

**Result**:

```json
{
  "success": true,
  "toolCount": 3
}
```

On failure, the method returns a successful JSON-RPC response with structured fields:

```json
{
  "success": false,
  "errorCode": "McpServerTestFailed",
  "errorMessage": "Connection refused"
}
```

### 22.10 `mcp/status/updated`

Server notification emitted when one server's runtime status changes.

```json
{
  "jsonrpc": "2.0",
  "method": "mcp/status/updated",
  "params": {
    "server": {
      "name": "sqlite",
      "enabled": true,
      "startupState": "ready",
      "toolCount": 3,
      "transport": "stdio"
    }
  }
}
```

### 22.11 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32070` | `McpServerNotFound` | Requested MCP server name does not exist. |
| `-32072` | `McpServerValidationFailed` | MCP config payload is invalid for the selected transport. |
| `-32073` | `McpServerTestFailed` | Temporary test/probe failed. |
| `-32074` | `McpServerNameConflict` | Name conflicts with an existing logical key after case-insensitive comparison. |
| `-32075` | `McpServerReadOnly` | A write method attempted to modify a plugin-origin read-only MCP server. |

## 23. External Channel Management Methods

### 23.1 Scope

These methods provide a server-authoritative read/write path for external channel configuration.

Clients must check `capabilities.externalChannelManagement` before calling `externalChannel/list`, `externalChannel/get`, `externalChannel/upsert`, or `externalChannel/remove`. If absent or `false`, the server returns `-32601` (Method not found).

### 23.2 `ExternalChannelConfig` Wire DTO

```json
{
  "name": "telegram",
  "enabled": true,
  "transport": "subprocess",
  "command": "python",
  "args": ["-m", "dotcraft_telegram"],
  "workingDirectory": "./adapters/telegram",
  "env": { "TELEGRAM_BOT_TOKEN": "..." }
}
```

Supported fields:

- `name: string`
- `enabled: boolean`
- `transport: "subprocess" | "websocket" | "managedWebsocket"`
- `command?: string`
- `builtinModule?: string`
- `args?: string[]`
- `workingDirectory?: string | null`
- `env?: Record<string, string>`

Validation rules:

- `name` is the logical primary key and is compared case-insensitively.
- `name` must not conflict with a reserved or existing channel name.
- `subprocess` requires either `command` or `builtinModule`.
- `subprocess` allows `command`, `builtinModule`, `args`, `workingDirectory`, and `env`.
- `managedWebsocket` requires either `command` or `builtinModule`, allows the same process-launch fields as `subprocess`, and can only be activated when AppServer WebSocket mode is enabled.
- `websocket` does not allow process-launch fields (`command`, `builtinModule`, `args`, `workingDirectory`, `env`) because the adapter is started independently.
- Persistence shape and storage location are server-defined.

### 23.3 `externalChannel/list`

Returns all configured external channels for the current workspace.

**Result**:

```json
{
  "channels": [
    {
      "name": "telegram",
      "enabled": true,
      "transport": "subprocess",
      "command": "python",
      "args": ["-m", "dotcraft_telegram"]
    }
  ]
}
```

### 23.4 `externalChannel/get`

Returns one configured external channel by name.

**Params**:

```json
{ "name": "telegram" }
```

### 23.5 `externalChannel/upsert`

Creates or replaces one external channel definition.

**Params**:

```json
{
  "channel": {
    "name": "weixin",
    "enabled": true,
    "transport": "managedWebsocket",
    "builtinModule": "channel-weixin"
  }
}
```

**Semantics**:

- Upsert replaces the full logical channel entry.
- Persistence shape and storage location are server-defined.
- On success, the server emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "externalChannel/upsert"` and `regions: ["externalChannel"]`.

### 23.6 `externalChannel/remove`

Removes one external channel definition by name.

On success, the server emits `workspace/configChanged` (see [Section 24.5](#245-workspaceconfigchanged)) with `source: "externalChannel/remove"` and `regions: ["externalChannel"]`.

### 23.7 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32080` | `ExternalChannelNotFound` | Requested external channel name does not exist. |
| `-32081` | `ExternalChannelValidationFailed` | External channel config payload is invalid for the selected transport. |
| `-32082` | `ExternalChannelNameConflict` | Name conflicts with an existing logical key or a native channel name after case-insensitive comparison. |

## 24. SubAgent Profile Management Methods

### 24.1 Scope

These methods provide a server-authoritative read/write path for workspace SubAgent profile configuration.

Clients must check `capabilities.subAgentManagement` before calling `subagent/profiles/list`, `subagent/settings/update`, `subagent/profiles/setEnabled`, `subagent/profiles/upsert`, or `subagent/profiles/remove`. If absent or `false`, the server returns `-32601` (Method not found).

### 24.2 `SubAgentProfileWrite` Wire DTO

`definition` payloads use the full persisted SubAgent profile definition. The write payload mirrors the config shape of `SubAgentProfiles.<name>` and excludes `name`, which is carried by the RPC envelope.

Supported fields mirror the effective `SubAgentProfile` contract, including:

- `runtime`
- `bin`
- `args`
- `env`
- `envPassthrough`
- `workingDirectoryMode`
- `supportsStreaming`
- `supportsResume`
- `supportsModelSelection`
- `inputFormat`
- `outputFormat`
- `inputMode`
- `inputArgTemplate`
- `inputEnvKey`
- `resumeArgTemplate`
- `resumeSessionIdJsonPath`
- `resumeSessionIdRegex`
- `outputJsonPath`
- `outputInputTokensJsonPath`
- `outputOutputTokensJsonPath`
- `outputTotalTokensJsonPath`
- `outputFileArgTemplate`
- `readOutputFile`
- `deleteOutputFileAfterRead`
- `maxOutputBytes`
- `timeout`
- `trustLevel`
- `permissionModeMapping`
- `sanitizationRules`

### 24.3 `SubAgentProfileEntry` Wire DTO

```json
{
  "name": "agent-cli",
  "isBuiltIn": true,
  "isTemplate": false,
  "hasWorkspaceOverride": true,
  "isDefault": false,
  "enabled": true,
  "definition": {
    "runtime": "cli-oneshot",
    "bin": "agent",
    "workingDirectoryMode": "workspace"
  },
  "builtInDefaults": {
    "runtime": "cli-oneshot",
    "bin": "agent",
    "workingDirectoryMode": "workspace"
  },
  "diagnostic": {
    "enabled": true,
    "binaryResolved": true,
    "hiddenFromPrompt": false,
    "warnings": []
  }
}
```

Supported fields:

- `name: string`
- `isBuiltIn: boolean`
- `isTemplate: boolean`
- `hasWorkspaceOverride: boolean`
- `isDefault: boolean`
- `enabled: boolean`
- `definition: SubAgentProfileWrite`
- `builtInDefaults?: SubAgentProfileWrite`
- `diagnostic: { enabled, binaryResolved, hiddenFromPrompt, hiddenReason?, warnings[] }`

Semantics:

- `definition` is the effective current definition after builtin + workspace override resolution.
- `builtInDefaults` is only present for builtin profiles.
- `hasWorkspaceOverride=true` means the workspace currently persists `SubAgentProfiles.<name>`.
- `diagnostic.hiddenFromPrompt` reflects effective prompt visibility after enablement, template handling, runtime registration, binary resolution, and validation are all applied.

### 24.4 `subagent/profiles/list`

Returns all builtin profiles plus workspace-defined custom profiles for the current workspace.

**Result**:

```json
{
  "defaultName": "native",
  "settings": {
    "externalCliSessionResumeEnabled": false,
    "model": null,
    "minWaitTimeoutMs": 15000,
    "defaultWaitTimeoutMs": 60000,
    "maxWaitTimeoutMs": 3600000
  },
  "profiles": []
}
```

`settings.externalCliSessionResumeEnabled` is the workspace-scoped toggle that controls whether supported external CLI profiles may reuse saved external session ids.
`settings.model` is the optional workspace-scoped default model for DotCraft-managed SubAgents. `null` or an empty string means the server uses the effective MainAgent model for the current thread.
`settings.minWaitTimeoutMs`, `settings.defaultWaitTimeoutMs`, and `settings.maxWaitTimeoutMs` define the configured `WaitAgent(timeoutMs?)` range in milliseconds. Omitted `timeoutMs` uses the default; explicit values outside the configured range are rejected rather than clamped.
`SubAgent.MaxDepth` defaults to `1`, so root threads can spawn first-level SubAgents but child SubAgents cannot recursively call `SpawnAgent` unless the workspace explicitly raises the depth limit and the selected role exposes Agent control.

### 24.5 `subagent/settings/update`

Update workspace-level SubAgent settings.

**Params**:

```json
{
  "externalCliSessionResumeEnabled": true,
  "model": "gpt-4.1",
  "minWaitTimeoutMs": 15000,
  "defaultWaitTimeoutMs": 60000,
  "maxWaitTimeoutMs": 3600000
}
```

**Semantics**:

- clients may send `externalCliSessionResumeEnabled`, `model`, any `*WaitTimeoutMs` field, or a combination; at least one supported field is required
- `externalCliSessionResumeEnabled` updates `SubAgent.EnableExternalCliSessionResume`
- `model` updates `SubAgent.Model`; `null`, empty, or whitespace clears the SubAgent model override
- `minWaitTimeoutMs`, `defaultWaitTimeoutMs`, and `maxWaitTimeoutMs` update `SubAgent.MinWaitTimeoutMs`, `SubAgent.DefaultWaitTimeoutMs`, and `SubAgent.MaxWaitTimeoutMs`; each value must be between `0` and `3600000`, and the resulting triple must satisfy `min <= default <= max`
- `SubAgent.Model` only affects DotCraft-managed native SubAgents in v1; external CLI profiles may opt into model selection in a future profile/runtime-specific contract
- the resume toggle affects only profiles whose effective definition has `supportsResume=true`
- clearing or changing these settings does not delete existing saved external session ids
- on success, the server emits `workspace/configChanged` (see [Section 25.5](#255-workspaceconfigchanged)) with `source: "subagent/settings/update"` and `regions: ["subagent"]`

### 24.6 `subagent/profiles/setEnabled`

Enable or disable one profile for the current workspace.

**Params**:

```json
{
  "name": "cursor-cli",
  "enabled": false
}
```

**Semantics**:

- updates `SubAgent.DisabledProfiles`
- returns the updated `SubAgentProfileEntry`
- `native` is protected and cannot be disabled
- on success, the server emits `workspace/configChanged` (see [Section 25.5](#255-workspaceconfigchanged)) with `source: "subagent/profiles/setEnabled"` and `regions: ["subagent"]`

### 24.7 `subagent/profiles/upsert`

Create or replace one workspace profile definition.

**Params**:

```json
{
  "name": "my-local-agent",
  "definition": {
    "runtime": "cli-oneshot",
    "bin": "my-agent",
    "workingDirectoryMode": "workspace",
    "inputMode": "arg",
    "outputFormat": "text"
  }
}
```

**Semantics**:

- builtin name creates or replaces a workspace override
- non-builtin name creates or replaces a custom workspace profile
- the workspace persists the full expanded definition
- on success, the server emits `workspace/configChanged` (see [Section 25.5](#255-workspaceconfigchanged)) with `source: "subagent/profiles/upsert"` and `regions: ["subagent"]`

### 24.8 `subagent/profiles/remove`

Remove one workspace-managed SubAgent definition.

**Params**:

```json
{ "name": "agent-cli" }
```

**Semantics**:

- builtin name removes only the workspace override and restores builtin defaults
- custom name removes the workspace profile entirely
- removing a builtin profile that has no workspace override fails
- on success, the server emits `workspace/configChanged` (see [Section 25.5](#255-workspaceconfigchanged)) with `source: "subagent/profiles/remove"` and `regions: ["subagent"]`

**Result**:

```json
{ "removed": true }
```

### 24.9 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32083` | `SubAgentProfileNotFound` | Requested profile or workspace override does not exist. |
| `-32084` | `SubAgentProfileValidationFailed` | The profile payload is invalid or incompatible with runtime rules. |
| `-32085` | `SubAgentProfileProtected` | The requested operation targets a protected profile such as `native`. |

### 24.10 Session-Backed SubAgent Child Threads

Servers advertising `capabilities.subAgentSessions = true` expose profile-backed SubAgents as ordinary child threads plus a lightweight parent/child graph. Native profiles run real child agent turns; external CLI profiles persist synthetic child turns containing the submitted prompt, final output or error, and token metadata when available.

`agentRole` selects the child thread role. Built-in roles are `default`, `worker`, and `explorer`; workspace configuration may override or add roles. The resolved role determines the child thread's tool allow/deny policy, Agent control exposure, prompt profile, and role instructions. External CLI profiles receive role instructions as prompt context, but the server cannot enforce tool filtering inside a third-party CLI runtime.

SubAgent control uses stable agent paths. The root agent path is `/root`; each spawned child adds its `taskName` as a path segment, such as `/root/researcher`. `taskName` segments must use lowercase ASCII letters, digits, and underscores, and the reserved segment values `root`, `.`, and `..` are invalid. Relative control targets append valid path segments to the current agent path; absolute control targets must begin with `/root`. `agentNickname` is display metadata. A child thread's `displayName` is initialized from `agentNickname` when present, otherwise from `taskName`; renaming a thread does not change its path.

`thread/list` hides subagent child threads unless `includeSubAgents` is true. Children follow the parent lifecycle: parent archive/delete recursively applies to descendants, and parent unarchive restores only descendants whose parent/child edge is still open. Direct child archive/delete calls are invalid. Clients rendering a composer-adjacent background-agent widget should use `subagent/children/list` for the active parent thread, then call `thread/read` for a child when the user expands or jumps into it.

When `includeThreads` is true, the returned child thread uses the same wire model as `thread/read` and may include a `runtime` snapshot derived from persisted turns. Clients should use `thread.runtime.running` to decide whether the child is actively executing. `edge.status: "open"` means the parent/child relationship remains available for path-based control and must not by itself be interpreted as a running child.

#### `subagent/children/list`

Params:

```json
{
  "parentThreadId": "thread_parent",
  "includeClosed": false,
  "includeThreads": true
}
```

Result:

```json
{
  "data": [
    {
      "edge": {
        "parentThreadId": "thread_parent",
        "childThreadId": "thread_child",
        "parentTurnId": "turn_1",
        "depth": 1,
        "agentPath": "/root/worker",
        "taskName": "worker",
        "agentNickname": "Worker",
        "agentRole": "worker",
        "profileName": "native",
        "runtimeType": "native",
        "supportsSendMessage": true,
        "supportsFollowupTask": true,
        "supportsClose": true,
        "status": "open"
      },
      "thread": {
        "id": "thread_child",
        "source": {
          "kind": "subagent"
        }
      }
    }
  ]
}
```

By default, closed edges are not returned. Clients that set `includeClosed: true` may receive closed historical edges for audit or settings surfaces, but composer-adjacent background-agent widgets should keep the default so explicitly closed agents disappear from active background activity. Edges without `agentPath` are returned without path fields and cannot be used as path-control targets.

#### `subagent/sendMessage`

Params:

```json
{
  "parentThreadId": "thread_parent",
  "target": "worker",
  "message": "Record this note for your current task."
}
```

`target` is an absolute agent path or a relative reference resolved from the caller's agent path. `subagent/sendMessage` records an inter-agent message for the target and does not start a child turn by itself.

Path-addressable child turn completion writes a mailbox notification for the parent agent path. The notification is model-visible inside the parent turn at the next sampling boundary and is persisted as a `userMessage` with `deliveryMode = "subagentMailbox"` and `triggerKind = "subagentMailbox"`. Clients should preserve these items for history/model reconstruction but should not render them as user-authored parent-thread bubbles or as visible child-agent reply bubbles. AppServer child listing remains a graph/status surface and does not expose child final text.

#### `subagent/followupTask`

Params:

```json
{
  "parentThreadId": "thread_parent",
  "target": "/root/worker",
  "message": "Continue with the implementation pass.",
  "deliveryMode": "queue"
}
```

`deliveryMode` is optional and defaults to `"queue"`. Supported values are `"queue"` and `"steer"`.

`subagent/followupTask` resolves `target` and starts a new child turn with `message` when the target is idle, regardless of `deliveryMode`. When the target has an active turn, `"queue"` appends the task to the target thread's FIFO queue, and `"steer"` promotes the task into current-Turn guidance for a running native SubAgent. Running external SubAgents reject `"steer"`; callers must use `"queue"`. Pending mailbox messages for the target are delivered with the submitted, queued, or steered task. Started, queued, or steered follow-up inputs carry `triggerKind = "subagentFollowupTask"` with `triggerLabel` set to the target's display label and `triggerRefId` set to the target agent path.

#### `subagent/close`

Params:

```json
{
  "parentThreadId": "thread_parent",
  "target": "/root/worker"
}
```

`subagent/close` resolves `target`, rejects `/root` and self-targets, cancels any active child turn when the server still owns the running task, marks the parent/child edge closed, and archives the target child subtree. The result shape is unchanged; the closed edge remains available only to callers that explicitly request closed edges, while default child listing no longer returns the closed child.

## 25. Workspace Config Methods

### 25.1 Scope

These methods provide a server-authoritative write path for workspace-level configuration values.

In v1, the wire surface standardizes workspace model persistence while keeping per-thread overrides in `thread/config/update`.

Clients must check `capabilities.workspaceConfigManagement` in `initialize` before calling workspace configuration methods (`workspace/config/schema`, `workspace/config/update`). If absent or `false`, the server returns `-32601` (Method not found).

### 25.2 `workspace/config/schema`

Return the server-derived workspace config schema, including per-field reload metadata.

**Direction**: client → server (request)

**Params**: `{}`

**Result**:

```json
{
  "sections": [
    {
      "section": "Core",
      "order": 0,
      "path": null,
      "fields": [
        {
          "key": "ProviderId",
          "type": "string",
          "reload": "processRestart"
        },
        {
          "key": "Model",
          "type": "string",
          "reload": "processRestart"
        }
      ]
    }
  ]
}
```

**Semantics**:

- The payload is additive and forward-compatible; clients must ignore unknown properties.
- `reload` uses the `ReloadBehavior` enum names serialized as camelCase strings.
- `subsystemKey` is present only when `reload` is `subsystemRestart`.

### 25.3 `workspace/config/update`

Update workspace-level config values.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `providerId` | string \| null | no | Workspace-selected personal provider id. `null` or empty removes the workspace `ProviderId` key; runtime then has no selected provider unless a managed runtime override supplies one. |
| `model` | string \| null | no | Workspace default model. `null`, empty, or `"Default"` removes the `Model` key so runtime falls back to provider default behavior. |
| `welcomeSuggestionsEnabled` | boolean \| null | no | Workspace-level override for personalized welcome suggestions. `true` enables, `false` disables, and `null` removes the explicit override so server defaults apply. |
| `skillsSelfLearningEnabled` | boolean \| null | no | Workspace-level override for `Skills.SelfLearning.Enabled`. `true` enables the SkillManage tool surface and skill-authoring built-in skill, `false` disables, and `null` removes the explicit override so server defaults apply (`true` by default). Takes effect on next AppServer restart (`Skills.SelfLearning.Enabled` is a `ProcessRestart` field). |
| `memoryAutoConsolidateEnabled` | boolean \| null | no | Workspace-level override for `Memory.AutoConsolidateEnabled`. `true` enables turn-count-based long-term memory consolidation, `false` disables it, and `null` removes the explicit override so server defaults apply (`true` by default). Takes effect for future successful turns without restart. |
| `dreamsEnabled` | boolean \| null | no | Workspace-level override for `Dreams.Enabled`. `true` enables scheduled Dreams, `false` disables scheduled Dreams, and `null` removes the explicit override so server defaults apply (`true` by default). |
| `dreamsInterval` | string \| null | no | Workspace-level override for `Dreams.Interval` as a positive `TimeSpan` string. `null` removes the explicit override. |
| `dreamsThreadLookbackCount` | number \| null | no | Workspace-level override for `Dreams.ThreadLookbackCount`, which limits how many eligible candidate threads are listed in each Dreams source manifest. Must be a positive integer when provided; `null` removes the explicit override. |
| `dreamsAutoApply` | boolean \| null | no | Workspace-level override for `Dreams.AutoApply`. `true` makes future successful Dreams runs active immediately, `false` keeps them pending for Dashboard review, and `null` removes the explicit override. |
| `defaultApprovalPolicy` | string \| null | no | Workspace default approval policy for threads whose `ThreadConfiguration.approvalPolicy` is `default` or unset. Supported values are `default` and `autoApprove`; `null` removes the explicit workspace override so server defaults apply. |
| `toolsLspEnabled` | boolean \| null | no | Workspace-level override for `Tools.Lsp.Enabled`. `true` enables the built-in LSP tool, `false` disables it, and `null` removes the explicit override so server defaults apply. |
| `reasoning` | object \| null | no | Workspace default reasoning override. Omitted means no change; `null` removes the workspace `Reasoning` section; an object may set `enabled`, `effort`, and/or `output` using camelCase wire enum values. |

**Result**:

```json
{
  "providerId": "anthropic",
  "model": "gpt-4o-mini",
  "welcomeSuggestionsEnabled": true,
  "skillsSelfLearningEnabled": true,
  "memoryAutoConsolidateEnabled": true,
  "dreamsEnabled": true,
  "dreamsInterval": "24:00:00",
  "dreamsThreadLookbackCount": 20,
  "dreamsAutoApply": false,
  "defaultApprovalPolicy": "default",
  "toolsLspEnabled": true,
  "reasoning": {
    "enabled": true,
    "effort": "high",
    "output": "full"
  }
}
```

If `model` is removed, the result returns:

```json
{
  "model": null
}
```

**Semantics**:

- This method updates **workspace default** only, not any active thread state.
- Clients that need immediate effect in a running thread should additionally call `thread/config/update`.
- Server preserves unrelated configuration state.
- At least one of `providerId`, `model`, `welcomeSuggestionsEnabled`, `skillsSelfLearningEnabled`, `memoryAutoConsolidateEnabled`, `dreamsEnabled`, `dreamsInterval`, `dreamsThreadLookbackCount`, `dreamsAutoApply`, `defaultApprovalPolicy`, `toolsLspEnabled`, or `reasoning` must be provided.
- Key matching is case-insensitive and normalized in-place (`ProviderId`, `Model`, and nested sections).
- Provider-aware saves persist only `ProviderId` and `Model` to workspace config. Credentials and endpoints are changed through `provider/create` and `provider/update`.
- Requests containing legacy root-level `apiKey` or `endPoint` parameters are rejected.
- When `skillsSelfLearningEnabled` is provided, the server writes the boolean to the nested `Skills.SelfLearning.Enabled` key. Setting it to `null` removes the leaf, and the server prunes empty `Skills.SelfLearning` / `Skills` objects when no other keys remain.
- When `memoryAutoConsolidateEnabled` is provided, the server writes the boolean to `Memory.AutoConsolidateEnabled`. Setting it to `null` removes the leaf, and the server prunes the empty `Memory` object when no other keys remain.
- When Dreams fields are provided, the server writes them to `Dreams.Enabled`, `Dreams.Interval`, `Dreams.ThreadLookbackCount`, and `Dreams.AutoApply`. Setting a field to `null` removes that leaf, and the server prunes the empty `Dreams` object when no other keys remain.
- When `defaultApprovalPolicy` is provided, the server writes the value to `Permissions.DefaultApprovalPolicy`. Setting it to `null` removes the leaf, and the server prunes the empty `Permissions` object when no other keys remain.
- When `toolsLspEnabled` is provided, the server writes the boolean to `Tools.Lsp.Enabled`. Setting it to `null` removes the leaf, and the server prunes empty `Tools.Lsp` / `Tools` objects when no other keys remain.
- When `reasoning` is provided, `null` removes the workspace `Reasoning` section. `enabled: false` writes an explicit Off override; `enabled: true` or a payload that only sets `effort` writes an enabled override. Missing `effort` and `output` are filled from existing workspace values, merged config values, then `medium` / `full`.
- On success, the server emits `workspace/configChanged` (see [Section 25.5](#255-workspaceconfigchanged)) with `source: "workspace/config/update"` and one or more regions from `workspace.provider`, `workspace.model`, `workspace.reasoning`, `providers`, `welcomeSuggestions`, `skills`, `memory`, `workspace.defaultApprovalPolicy`, or `lsp`.

### 25.4 Capability Advertisement

Clients must check `capabilities.workspaceConfigManagement` before calling workspace configuration methods (`workspace/config/schema`, `workspace/config/update`).

Clients may set `capabilities.configChange = false` during `initialize` to suppress server-initiated `workspace/configChanged` notifications for that connection. When omitted, the server treats it as `true`.

### 25.5 `workspace/configChanged`

Server notification emitted after a successful workspace configuration write.

**Direction**: server → client (notification, no `id`)

**Params**:

```json
{
  "source": "skills/setEnabled",
  "regions": ["skills"],
  "changedAt": "2026-04-19T10:15:03Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `source` | string | RPC method that triggered the mutation (`provider/create`, `provider/update`, `provider/delete`, `workspace/config/update`, `memory/reset`, `skills/setEnabled`, `skills/uninstall`, `plugin/install`, `plugin/remove`, `plugin/setEnabled`, `mcp/upsert`, `mcp/remove`, `externalChannel/upsert`, `externalChannel/remove`, `subagent/settings/update`, `subagent/profiles/setEnabled`, `subagent/profiles/upsert`, `subagent/profiles/remove`). |
| `regions` | string[] | Coarse region tags describing what changed. |
| `changedAt` | string (ISO-8601) | Server-side UTC timestamp when the change event was emitted. |

Current `regions` taxonomy:

| Region | Fired by |
|--------|----------|
| `providers` | `provider/create`, `provider/update`, `provider/delete` |
| `workspace.provider` | `workspace/config/update` |
| `workspace.model` | `workspace/config/update` |
| `workspace.reasoning` | `workspace/config/update` |
| `welcomeSuggestions` | `workspace/config/update` |
| `skills` | `skills/setEnabled`, `skills/uninstall`, `plugin/install`, `plugin/remove`, `plugin/setEnabled`, `workspace/config/update` |
| `plugins` | `plugin/install`, `plugin/remove`, `plugin/setEnabled` |
| `memory` | `workspace/config/update`, `memory/reset` |
| `workspace.defaultApprovalPolicy` | `workspace/config/update` |
| `lsp` | `workspace/config/update`, `plugin/install`, `plugin/remove`, `plugin/setEnabled` |
| `mcp` | `mcp/upsert`, `mcp/remove`, `plugin/install`, `plugin/remove`, `plugin/setEnabled` |
| `externalChannel` | `externalChannel/upsert`, `externalChannel/remove` |
| `subagent` | `subagent/settings/update`, `subagent/profiles/setEnabled`, `subagent/profiles/upsert`, `subagent/profiles/remove` |

Semantics:

- Notification is emitted after write completion and in-process state update.
- Payload is intentionally coarse; clients should re-read relevant state (`skills/list`, `mcp/list`, etc.) when needed.
- Unknown region tags are forward-compatible and must be ignored by clients that do not recognize them.

### 25.6 Backward Compatibility

- Clients that set `capabilities.configChange = false` are supported indefinitely and simply do not receive `workspace/configChanged` on that connection.
- Older servers may not emit `workspace/configChanged`; clients must tolerate its absence and rely on existing refresh paths.

## 26. Memory Management Methods

These methods provide a server-authoritative path for destructive workspace memory maintenance.

Clients must check `capabilities.memoryManagement` in `initialize` before calling memory management methods. If absent or `false`, the server returns `-32601` (Method not found).

### 26.1 `memory/reset`

Clear the current workspace's durable memory artifacts.

**Direction**: client -> server (request)

**Params**: omitted, `null`, or `{}`

**Result**:

```json
{}
```

**Semantics**:

- The server clears the contents of the current workspace memory root, including `MEMORY.md`, `HISTORY.md`, and derived memory files, and clears Dreams-derived memory under `.craft/dreams`, while preserving the memory and Dreams directories themselves.
- The operation does not delete sessions, archived sessions, thread history, skills, plugins, automation tasks, or configuration.
- The operation preserves `Memory.AutoConsolidateEnabled`; future successful turns may create new memory according to the current configuration.
- The server clears memory-derived welcome suggestion caches so clients do not continue displaying suggestions generated from deleted memory.
- On success, the server emits `workspace/configChanged` with `source: "memory/reset"` and `regions: ["memory"]`.

### 26.2 Capability Advertisement

Clients must check `capabilities.memoryManagement` before calling `memory/reset`.

## 27. Dreams Management Methods

Dreams methods expose workspace-level background memory organization, pending output stores, and review actions. Actual Dreams model runs are Session-backed internal maintenance threads with two turns: pruning pass and consolidation pass. Those threads are trace-visible for Dashboard users, but ordinary thread lists omit them by default.

Clients must check `capabilities.dreams` in `initialize` before calling Dreams methods. If absent or `false`, the server returns `-32601` (Method not found).

### 27.1 `dreams/status`

Return current Dreams configuration and latest run state for the connected workspace.

**Direction**: client -> server (request)

**Params**: omitted, `null`, or `{}`

**Result**:

```json
{
  "enabled": true,
  "interval": "24:00:00",
  "threadLookbackCount": 20,
  "autoApply": false,
  "historyTailChars": 20000,
  "minCompletedTurnsSinceLastRun": 5,
  "nextRunAt": "2026-05-12T00:00:00Z",
  "running": false,
  "activeDreamStoreId": "store_20260510000000_active",
  "lastRun": {
    "id": "dream_20260511000000_abc123",
    "status": "succeeded",
    "startedAt": "2026-05-11T00:00:00Z",
    "endedAt": "2026-05-11T00:00:28Z",
    "processedThreadCount": 18,
    "candidateThreadCount": 18,
    "evidenceThreadIds": ["thread_abc"],
    "writtenPaths": ["stores/store_20260511000000_pending/INDEX.md"],
    "evidenceSearchCount": 3,
    "evidenceReadCount": 4,
    "dreamWritten": true,
    "historyWritten": false,
    "outputStoreId": "store_20260511000000_pending",
    "reviewStatus": "pending",
    "autoApplied": false,
    "threadId": "thread_20260511_abcd",
    "turnId": "turn_002",
    "turnIds": ["turn_001", "turn_002"],
    "trigger": "manual",
    "message": null,
    "inputManifestPath": ".craft/dreams/runs/dream_20260511000000_abc123/input/MANIFEST.md"
  }
}
```

If Dreams has never run, `lastRun` is `null`. Run status values are `running`, `succeeded`, `skipped`, `failed`, and `canceled`. Review status values are `pending`, `applied`, `discarded`, and `archived`.

`threadLookbackCount` limits the number of eligible candidate sessions listed in the Dream Run source manifest. Raw transcripts are not inlined into the initial Dreams request; the internal Dreams session uses its file-tool sandbox to read/search eligible input snapshots, repo evidence, and the candidate output store.

### 27.2 `dreams/run`

Shortcut for requesting an immediate Dream Run with default manual parameters.

**Direction**: client -> server (request)

**Params**: omitted, `null`, or `{}`

**Result**: same shape as `dreams/status`.

**Semantics**:

- If Dreams is enabled and idle, the server persists a `running` state, starts one forced Dream Run in the background, and returns a status snapshot quickly.
- If a Dream Run is already active, the server returns the active status snapshot without starting a duplicate run.
- If Dreams is disabled, the server returns a skipped or disabled status without starting a run.
- With `Dreams.AutoApply = false`, successful runs generate pending output stores and do not affect prompts until `dreams/apply`. With `Dreams.AutoApply = true`, future successful runs immediately switch the active Dream Store, record `reviewStatus = "applied"`, and set `autoApplied = true`; existing pending runs are unchanged.
- Actual model runs create a new internal Session Core thread with `originChannel = "dreams"` and `dotcraft.internal = "dreams"` metadata. The Dashboard may surface the trace/session for that thread; default `thread/list` responses continue to hide it unless `includeInternal = true`.
- Skipped attempts before model generation do not create Session threads.
- The baseline protocol does not require streaming progress or a completion notification. Clients may poll `dreams/status`.

### 27.3 `dreams/create`

Request an immediate Dream Run with optional input selection and additional instructions.

**Params**:

```json
{
  "threadIds": ["thread_abc"],
  "threadLookbackCount": 20,
  "instructions": "Focus protocol decisions.",
  "model": "gpt-5.2"
}
```

**Result**:

```json
{
  "run": {
    "id": "dream_20260511000000_abc123",
    "status": "running"
  },
  "activeDreamStoreId": "store_20260510000000_active"
}
```

`threadLookbackCount`, when present, must be positive. `threadIds` narrows the candidate sessions. `model`, when present, overrides the model recorded and used for the internal Dreams session.

### 27.4 `dreams/get`

Read one Dream Run plus preview data for detailed Dashboard review.

**Params**:

```json
{ "runId": "dream_20260511000000_abc123" }
```

**Result**:

```json
{
  "run": {
    "id": "dream_20260511000000_abc123",
    "status": "succeeded",
    "reviewStatus": "pending",
    "outputStoreId": "store_20260511000000_pending"
  },
  "activeDreamStoreId": "store_20260510000000_active",
  "preview": {
    "activeStoreId": "store_20260510000000_active",
    "outputStoreId": "store_20260511000000_pending",
    "activeIndexMarkdown": "# Dream Store\n\n...",
    "outputIndexMarkdown": "# Dream Store\n\n...",
    "activeTopicPaths": [],
    "outputTopicPaths": ["api-conventions.md"]
  }
}
```

### 27.5 `dreams/list`

List Dream Runs for the connected workspace.

**Params**:

```json
{ "includeArchived": false }
```

**Result**:

```json
{ "runs": [] }
```

### 27.6 Review Actions

`dreams/cancel`, `dreams/apply`, `dreams/discard`, and `dreams/archive` all take:

```json
{ "runId": "dream_20260511000000_abc123" }
```

They return the same run-result envelope as `dreams/create` without preview.

Review semantics:

- `dreams/apply` switches `activeDreamStoreId` to the run's `outputStoreId` and emits a memory-region config change.
- `dreams/discard` marks non-applied runs discarded without deleting the output store.
- `dreams/archive` hides runs from default list results without deleting the output store.
- `dreams/cancel` is best-effort for running jobs.

## 27A. Usage Telemetry Methods

### 27A.1 Scope

These methods expose the workspace's trace/usage telemetry over the JSON-RPC surface,
so in-process clients (Desktop, TUI) can render usage overviews without opening the
hosted HTML Dashboard. `usage/summary` returns the workspace aggregate (the same number
the Dashboard serves at `GET /dashboard/api/summary`); `usage/timeseries` returns a
per-day breakdown for activity charts. Both are independent of whether the HTML
Dashboard endpoint is enabled.

Clients must check `capabilities.usageTelemetry` before calling `usage/summary` or
`usage/timeseries`. If the capability is absent or `false`, the server returns `-32601`
(Method not found). The capability is `false` when tracing is disabled, because no trace
store exists.

### 27A.2 `usage/summary`

Return the aggregate usage summary across all traced sessions in the workspace.

**Direction**: client → server (request)

**Params**: `{}` (empty object, no parameters required)

**Result**:

```json
{
  "sessionCount": 12,
  "totalRequests": 240,
  "totalResponses": 238,
  "totalToolCalls": 512,
  "totalErrors": 3,
  "totalContextCompactions": 4,
  "totalInputTokens": 1840221,
  "totalOutputTokens": 96540,
  "totalCachedInputTokens": 1502118,
  "totalCacheWriteInputTokens": 88004,
  "totalFreshInputTokens": 250099,
  "totalNonCachedInputTokens": 338103,
  "totalReasoningOutputTokens": 12044,
  "totalToolDurationMs": 81234,
  "avgToolDurationMs": 158.6,
  "maxToolDurationMs": 4210,
  "cacheHitRate": 0.8162,
  "totalTokens": 1936761
}
```

| Field | Type | Description |
|-------|------|-------------|
| `sessionCount` | int | Number of traced sessions. |
| `totalRequests` | int | LLM requests issued across all sessions. |
| `totalResponses` | int | LLM responses received across all sessions. |
| `totalToolCalls` | int | Tool calls executed across all sessions. |
| `totalErrors` | int | Errors recorded across all sessions. |
| `totalContextCompactions` | int | Context compaction events across all sessions. |
| `totalInputTokens` | long | Total prompt (input) tokens. |
| `totalOutputTokens` | long | Total completion (output) tokens. |
| `totalCachedInputTokens` | long | Input tokens served from the provider prompt cache. |
| `totalCacheWriteInputTokens` | long | Input tokens written into the prompt cache. |
| `totalFreshInputTokens` | long | Input tokens neither cached nor cache-write (`max(0, input − cached − cacheWrite)`). |
| `totalNonCachedInputTokens` | long | Input tokens not served from cache (`max(0, input − cached)`). |
| `totalReasoningOutputTokens` | long | Reasoning tokens (subset of output tokens). |
| `totalToolDurationMs` | long | Summed tool execution time in milliseconds. |
| `avgToolDurationMs` | double | Mean tool execution time in milliseconds. |
| `maxToolDurationMs` | long | Longest single tool execution in milliseconds. |
| `cacheHitRate` | double | `totalCachedInputTokens / totalInputTokens`, in `[0, 1]`; `0` when there are no input tokens. |
| `totalTokens` | long | `totalInputTokens + totalOutputTokens`. |

When there are no traced sessions yet, every numeric field is `0`.

**Errors**:

| Code | When |
|------|------|
| `-32601` | Tracing is disabled on this server (no trace store available). |

**Example**:

```json
{ "jsonrpc": "2.0", "method": "usage/summary", "id": 70, "params": {} }

{ "jsonrpc": "2.0", "id": 70, "result": {
    "sessionCount": 12,
    "totalInputTokens": 1840221,
    "totalOutputTokens": 96540,
    "cacheHitRate": 0.8162,
    "totalTokens": 1936761
} }
```

### 27A.3 `usage/timeseries`

Return per-day token usage across all traced sessions in the workspace, for rendering
activity charts (e.g. a contribution-style heatmap). Each traced session contributes its
token totals to the calendar day of its `StartedAt`, evaluated in the client's local time
frame (see `tzOffsetMinutes`). Only days with at least one session are returned (the
series is sparse); clients fill the gaps when laying out a calendar.

**Direction**: client → server (request)

**Params**:

```json
{ "from": "2025-07-01", "to": "2026-05-31", "tzOffsetMinutes": -480 }
```

| Field | Type | Description |
|-------|------|-------------|
| `from` | string? | Inclusive lower bound, `YYYY-MM-DD` in the client's local frame. Omit for no lower bound. |
| `to` | string? | Inclusive upper bound, `YYYY-MM-DD` in the client's local frame. Omit for no upper bound. |
| `tzOffsetMinutes` | int? | Minutes to add to UTC to obtain the client's local time, i.e. `-new Date().getTimezoneOffset()` in JS. Used to bucket sessions by local calendar day. Defaults to `0` (UTC). Clamped to `[-840, 840]`. |

A malformed (non-empty) `from`/`to` that is not a valid `YYYY-MM-DD` date yields
`-32602` (Invalid params).

**Result**:

```json
{
  "tzOffsetMinutes": -480,
  "longestTaskMs": 7830000,
  "days": [
    { "date": "2026-05-29", "inputTokens": 100, "outputTokens": 23, "totalTokens": 123, "sessionCount": 2 },
    { "date": "2026-05-30", "inputTokens": 40, "outputTokens": 8, "totalTokens": 48, "sessionCount": 1 }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `tzOffsetMinutes` | int | The (clamped) offset the server used for bucketing. |
| `longestTaskMs` | long | Longest single Turn (one unit of agent work, `completedAt − startedAt`) across the workspace, in milliseconds. Lifetime maximum, independent of `from`/`to`. `0` when none recorded. |
| `days` | array | Days with activity, ascending by `date`. Empty when no sessions match. |
| `days[].date` | string | `YYYY-MM-DD` in the client's local frame. |
| `days[].inputTokens` | long | Summed prompt (input) tokens for sessions started that day. |
| `days[].outputTokens` | long | Summed completion (output) tokens for sessions started that day. |
| `days[].totalTokens` | long | `inputTokens + outputTokens`. |
| `days[].sessionCount` | int | Number of sessions started that day. |

When there are no traced sessions in range, `days` is `[]`.

**Errors**:

| Code | When |
|------|------|
| `-32601` | Tracing is disabled on this server (no trace store available). |
| `-32602` | `from` or `to` is present but not a valid `YYYY-MM-DD` date. |

### 27A.4 `profile/insights`

Return aggregate "activity insights" for the workspace, used by the Desktop Profile page:
the most-used model and reasoning effort, how many skills the user has explored / used, the
total thread count, and a ranked list of the most-used skills.

Two semantics apply because the underlying data has different availability:

- **Model usage** is derived from the model id already recorded on every LLM `Response`
  trace event, so it reflects the full persisted history. Models are keyed by **model id
  only** (provider is not distinguished).
- **Reasoning effort and skill references** are **forward-only**: they are recorded from the
  point this feature shipped. They may be empty/zero until new activity accrues, even on a
  workspace with prior history. A skill "use" is counted when a skill is exercised either way:
  a `$name` skill tag in turn input, or an agent loading the skill via the SkillView tool.
  Skills injected by other means (e.g. `always: true`) are not counted.

**Direction**: client → server (request)

**Params**:

```json
{ "topSkills": 5 }
```

| Field | Type | Description |
|-------|------|-------------|
| `topSkills` | int? | Max number of skills to return in `skills`. Defaults to `5`, clamped to `[1, 20]`. |

**Result**:

```json
{
  "topModel": { "key": "example-model", "count": 240, "total": 600 },
  "topReasoning": { "key": "high", "count": 90, "total": 150 },
  "skillsExplored": 6,
  "totalSkillsUsed": 42,
  "totalThreads": 137,
  "skills": [
    { "name": "code-review", "count": 12, "pluginId": "example-plugin", "pluginDisplayName": "Example Plugin" },
    { "name": "release-draft", "count": 5 }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `topModel` | object? | Most-used model by `Response`-event count, or omitted/null when none recorded. |
| `topModel.key` | string | The model id. |
| `topModel.count` | long | Responses attributed to this model. |
| `topModel.total` | long | All responses with a known model; `count / total` is the share. |
| `topReasoning` | object? | Most-used reasoning effort (e.g. `"low"`/`"medium"`/`"high"`/`"extrahigh"`), or omitted/null when reasoning was never used. Same `key`/`count`/`total` shape as `topModel`. |
| `skillsExplored` | int | Distinct skills referenced at least once. |
| `totalSkillsUsed` | long | Total skill references across all turns. |
| `totalThreads` | int | Non-internal threads in this workspace (active + archived). |
| `skills` | array | Most-referenced skills, descending by `count`, then by `name`. |
| `skills[].name` | string | Skill name (without the `$` prefix). |
| `skills[].count` | long | Times this skill was referenced. |
| `skills[].pluginId` | string? | Owning plugin id, present only when the skill currently resolves to a plugin source. |
| `skills[].pluginDisplayName` | string? | Human-readable plugin name for a badge, when from a plugin. |

Plugin attribution is resolved live at read time against the current skill registry, so a
skill whose plugin was later uninstalled simply returns without plugin fields.

**Errors**:

| Code | When |
|------|------|
| `-32601` | Tracing is disabled on this server (no trace store available). |

### 27A.5 Capability Advertisement

Clients must check `capabilities.usageTelemetry` before calling `usage/summary`,
`usage/timeseries`, or `profile/insights`.

---

## 28. Protocol Ownership

The DotCraft AppServer Protocol is the authoritative wire contract for
DotCraft clients and adapters. Its methods, notifications, item types,
capability flags, transport behaviors, and extension surfaces are defined on
their own terms in this document.
