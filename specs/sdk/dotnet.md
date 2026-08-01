# DotCraft .NET SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-05-19 |
| **Related Specs** | [Unified SDK Specification](sdk.md), [AppServer Protocol](../protocols/appserver-protocol.md), [AppServer Protocol Contracts and SDK Generation](protocol-contract-generation.md), [Hub Architecture](../architecture/hub-architecture.md), [App Binding](../protocols/app-binding.md), [Session Core](../architecture/session-core.md), [TypeScript SDK Binding](typescript.md), [Python SDK Binding](python.md) |

Purpose: Define the .NET binding, public API shape, AppServer method coverage, App Binding helper surface, testing expectations, and compatibility strategy for `DotCraft.Sdk`.

Shared SDK behavior is defined by [Unified SDK Specification](sdk.md). This language binding records the current .NET SDK implementation baseline and the .NET-specific design, package, and publishing rules.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Design Principles](#2-design-principles)
- [3. Current Implementation Snapshot](#3-current-implementation-snapshot)
- [4. Package Contract](#4-package-contract)
- [5. Runtime Requirements](#5-runtime-requirements)
- [6. Connection Model](#6-connection-model)
- [7. Hub Client](#7-hub-client)
- [8. Wire Client](#8-wire-client)
- [9. High-Level AppServer Client](#9-high-level-appserver-client)
- [10. Thread API](#10-thread-api)
- [11. Turn API](#11-turn-api)
- [12. Model Catalog API](#12-model-catalog-api)
- [13. Runtime Dynamic Tools](#13-runtime-dynamic-tools)
- [14. App Binding API](#14-app-binding-api)
- [15. Notification Model](#15-notification-model)
- [16. Error Model](#16-error-model)
- [17. AppServer Coverage Matrix](#17-appserver-coverage-matrix)
- [18. Documentation and Examples](#18-documentation-and-examples)
- [19. Testing and Conformance](#19-testing-and-conformance)
- [20. Security](#20-security)
- [21. Versioning and Compatibility](#21-versioning-and-compatibility)
- [22. Acceptance Contract](#22-acceptance-contract)
- [23. Future Work](#23-future-work)

---

## 1. Scope

### 1.1 What This Spec Defines

This specification defines the .NET SDK that native applications, .NET tools, tests, and advanced protocol clients use to integrate with DotCraft.

It defines:

- The package identity and public namespace layout.
- The .NET runtime baseline.
- Hub-backed local AppServer discovery.
- Direct AppServer WebSocket and custom transport connection flows.
- JSON-RPC transport and wire client behavior.
- Current high-level wrappers for thread, turn, model, Runtime Dynamic Tool, and App Binding operations.
- The raw AppServer request escape hatch.
- Current AppServer method coverage and explicit gaps.
- Testing, security, versioning, and conformance rules.

### 1.2 What This Spec Does Not Define

This specification does not define:

- The AppServer JSON-RPC wire protocol. That contract is defined by [AppServer Protocol](../protocols/appserver-protocol.md).
- Hub HTTP endpoint semantics. Those are defined by [Hub Architecture](../architecture/hub-architecture.md).
- App Binding product semantics. Those are defined by [App Binding](../protocols/app-binding.md).
- Session persistence, turn lifecycle semantics, item payload semantics, or agent execution internals. Those are defined by [Session Core](../architecture/session-core.md).
- TypeScript channel module behavior or the TypeScript SDK package contract. Those are defined by [TypeScript SDK](typescript.md).
- A public NuGet publishing process. The SDK may remain source-referenced until a publishing phase is approved.

### 1.3 Primary Audiences

| Audience | Need | SDK Surface |
|----------|------|-------------|
| Native app authors | Complete principal handoffs, authenticate, and activate binding-scoped MCP sessions. | `DotCraft.Sdk.AppBinding`, `DotCraft.Sdk.AppServer` |
| .NET application developers | Connect to a local or remote DotCraft AppServer and run work against persistent threads. | `DotCraft.Sdk.AppServer` |
| Advanced protocol clients | Send raw AppServer JSON-RPC calls and consume notifications. | `DotCraft.Sdk.Wire` |
| Test authors | Use in-memory or custom transports for protocol conformance tests. | `DotCraft.Sdk.Wire.IJsonRpcTransport` |
| Hub-aware local tools | Discover or ensure a workspace AppServer through the local Hub. | `DotCraft.Sdk.Hub` |

---

## 2. Design Principles

### 2.1 AppServer Is Authoritative

The SDK is a client library. It must not duplicate server-side state machines, permission decisions, queue semantics, approval policy, App Binding validation, model catalog resolution, or persistence rules. Typed wrappers should remain ergonomic projections over AppServer and Hub contracts.

### 2.2 Lightweight First, Raw Escape Hatch Always

The current .NET SDK intentionally exposes a compact high-level surface and keeps `RequestAsync` available for all AppServer methods that are not yet typed. This is part of the public contract, not a temporary workaround.

### 2.3 Native App Binding Is A First-Class Use Case

The SDK must make native app integration straightforward:

```text
handoff URL -> principal credential -> authenticate -> inspect request -> activate binding MCP -> rebind
```

The SDK may provide general thread and turn APIs, but App Binding helpers are a primary design center for `DotCraft.Sdk`.

### 2.4 Protocol Changes Are Spec-First

If the SDK implementation discovers a required change to AppServer Protocol, Hub Architecture, App Binding, or Session Core, the owning protocol spec must be updated before server behavior or SDK typed wrappers are treated as stable.

### 2.5 Coverage Must Be Traceable

Every typed SDK method must be traceable to:

- one public .NET type or method;
- one AppServer JSON-RPC method, Hub endpoint, notification, or server-initiated request;
- one owning spec section;
- one conformance test or explicit test exemption.

Section 17 is the living coverage index.

---

## 3. Current Implementation Snapshot

The current repository contains one .NET SDK package:

```text
src/DotCraft.Sdk/DotCraft.Sdk.csproj
```

Current target framework:

```xml
<TargetFramework>net10.0</TargetFramework>
```

Current public namespaces:

| Namespace | Responsibility |
|-----------|----------------|
| `DotCraft.Sdk.AppServer` | High-level AppServer client, thread/turn/model wrappers, Runtime Dynamic Tool models. |
| `DotCraft.Sdk.AppBinding` | App Binding handoff parsing, app-side RPC helpers, standard App Binding tool errors. |
| `DotCraft.Sdk.Hub` | Local Hub lock discovery, Hub health probing, AppServer lookup/ensure. |
| `DotCraft.Sdk.Tools` | Attribute-based Runtime Dynamic Tool authoring, schema generation, argument binding, and declaration projection. |
| `DotCraft.Sdk.Wire` | JSON-RPC transports, wire client, canonical JSON options, JSON-RPC exception. |

Current AppServer features with typed or semi-typed SDK support:

| Feature | AppServer Method Or Event | Current SDK Surface |
|---------|---------------------------|---------------------|
| Initialize handshake | `initialize`, `initialized` | `DotCraftWireClient.InitializeAsync`, `DotCraftClient.ConnectAsync` |
| Remote AppServer connection | WebSocket transport | `DotCraftClient.ConnectRemoteAsync`, `WebSocketJsonRpcTransport` |
| Custom transport connection | transport abstraction | `DotCraftClient.ConnectAsync`, `IJsonRpcTransport` |
| Raw requests | any client-to-server JSON-RPC method | `DotCraftClient.RequestAsync`, `DotCraftWireClient.SendRequestAsync` |
| Raw notifications | any server-to-client notification | `DotCraftClient.ReadNotificationsAsync`, `DotCraftWireClient.ReadNotificationsAsync` |
| Server request dispatch | any registered server-to-client JSON-RPC request | `DotCraftWireClient.RegisterServerRequestHandler` |
| Thread start | `thread/start` | `DotCraftThreadClient.StartAsync` |
| Thread resume | `thread/resume` | `DotCraftThreadClient.ResumeAsync` |
| Thread subscribe | `thread/subscribe` | `DotCraftThreadClient.SubscribeAsync` |
| Thread read | `thread/read` | `DotCraftThreadClient.ReadAsync` |
| Thread model configuration | `thread/read`, `thread/config/update` | `ReadModelConfigurationAsync`, `UpdateModelConfigurationAsync` |
| Turn start | `turn/start` | `DotCraftTurnClient.StartAsync` |
| Turn enqueue | `turn/enqueue` | `DotCraftTurnClient.EnqueueAsync` |
| Turn interrupt | `turn/interrupt` | `DotCraftTurnClient.InterruptAsync` |
| Provider list | `provider/list` | `DotCraftProviderClient.ListAsync` |
| Model list | `model/list` | `DotCraftModelClient.ListAsync`, `GetCatalogAsync(providerId)` |
| Runtime Dynamic Tools | `thread/start.dynamicTools`, `thread/resume.dynamicTools`, `item/tool/call` | `RuntimeDynamicToolDeclaration`, `DynamicToolRegistry`, `RegisterDynamicToolHandler` |
| App connection request read | `app/connection/request/get` | `DotCraftAppBindingClient.GetConnectionRequestAsync<T>` |
| App connection completion | `app/connection/connect` | `DotCraftAppBindingClient.ConnectAsync<T>` |
| App connection status | `app/connection/status` | `DotCraftAppBindingClient.GetConnectionStatusAsync<T>` |
| App binding request read | `app/binding/request/get` | `DotCraftAppBindingClient.GetBindingRequestAsync<T>` |
| App binding activation | `app/binding/activate` | `DotCraftAppBindingClient.ActivateAsync` |
| App binding rebind | `app/binding/rebind` | `DotCraftAppBindingClient.RebindAsync` |
| App-bound tool channel lifetime | raw notification drain | `DotCraftAppBindingClient.KeepAliveAsync` |

Current Hub features:

| Feature | Hub Endpoint Or Artifact | Current SDK Surface |
|---------|--------------------------|---------------------|
| Hub lock path resolution | `~/.craft/hub/hub.lock` | `HubClient.ResolveHubLockPath` |
| Hub lock parsing | Hub lock JSON | `HubClient.ReadHubLock` |
| Hub process liveness check | OS process table | `HubClient.IsProcessAlive` |
| Hub URL validation | loopback HTTP base URL | `HubClient.ParseHubBaseUrl` |
| Live Hub discovery | `GET /v1/status` | `HubClient.TryGetLiveHubAsync` |
| Hub startup | `dotcraft hub` | `HubClient.EnsureHubAsync` |
| Workspace AppServer lookup | `GET /v1/appservers/by-workspace` | `HubClient.GetAppServerByWorkspaceAsync` |
| Workspace AppServer ensure | `POST /v1/appservers/ensure` | `HubClient.EnsureAppServerAsync`, `DotCraftClient.ConnectLocalAsync` |

The Run profile provides:

- `DotCraftThread.RunAsync` / `RunStreamedAsync` provide the high-level run abstraction that waits for terminal turn notifications and returns merged text.
- `DotCraftRunEvent` plus `RunStreamedAsync` normalize streaming notifications; a delta/snapshot reducer merges agent text.
- `DotCraftClientOptions.ApprovalHandler` and `UserInputHandler` provide typed approval and user-input callbacks (with auto-accept / empty-answer fallbacks).
- `ApprovalRequest` exposes the stable request, thread, turn, and item identifiers together with the approval type, operation, target, reason, and authoritative `ExpiresAt`. `Raw` remains available for forward-compatible fields.
- Typed wrappers exist for `thread/list` (`Threads.ListAsync`), `thread/unsubscribe`, `thread/archive`, `thread/delete`, and `thread/mode/set` (on `DotCraftThread`).

Current explicit gaps:

- No typed wrappers for `thread/rename`, goal methods, maintenance methods, or memory consolidation methods.
- Provider discovery and Thread model configuration are typed. Provider mutation, workspace config, skills, plugins, commands, cron, heartbeat, external channel, subagent, memory, and Dreams methods remain raw.
- No automatic reconnect or callback rebind policy.

---

## 4. Package Contract

### 4.1 Package Identity

The canonical .NET SDK package id is:

```text
DotCraft.Sdk
```

Repository-local consumers may reference:

```text
src/DotCraft.Sdk/DotCraft.Sdk.csproj
```

until a public NuGet release process is approved.

### 4.2 Namespace Layout

| Namespace | Public Surface |
|-----------|----------------|
| `DotCraft.Sdk.AppServer` | `DotCraftClient`, `DotCraftClientOptions`, `DotCraftLocalClientOptions`, `DotCraftThreadClient`, `DotCraftTurnClient`, `DotCraftModelClient`, AppServer DTO records. |
| `DotCraft.Sdk.AppBinding` | `AppBindingHandoff`, `DotCraftAppBindingClient`, `AppBindingErrorCodes`. |
| `DotCraft.Sdk.Hub` | `HubClient`, Hub DTO records, `HubClientException`, `HubAppServerStates`. |
| `DotCraft.Sdk.Wire` | `IJsonRpcTransport`, `DotCraftWireClient`, `StreamJsonRpcTransport`, `WebSocketJsonRpcTransport`, `DotCraftJson`, `JsonRpcException`. |

### 4.3 Top-Level Client

`DotCraftClient` represents one initialized AppServer connection.

Required members:

```csharp
public sealed class DotCraftClient : IAsyncDisposable
{
    public DotCraftWireClient Wire { get; }
    public AppServerServerInfo ServerInfo { get; }
    public AppServerServerCapabilities Capabilities { get; }
    public DotCraftThreadClient Threads { get; }
    public DotCraftTurnClient Turns { get; }
    public DotCraftModelClient Models { get; }
    public DotCraftAppBindingClient AppBindings { get; }

    public static Task<DotCraftClient> ConnectLocalAsync(...);
    public static Task<DotCraftClient> ConnectRemoteAsync(...);
    public static Task<DotCraftClient> ConnectAsync(...);
    public Task<T> RequestAsync<T>(...);
    public Task<JsonElement> RequestAsync(...);
    public IAsyncEnumerable<AppServerNotification> ReadNotificationsAsync(...);
}
```

The raw `Wire` client is intentionally public so advanced users can register low-level handlers or call protocol methods not yet represented by high-level clients.

---

## 5. Runtime Requirements

### 5.1 Target Framework

The SDK targets .NET 10:

```text
net10.0
```

Earlier .NET target frameworks are out of scope until a compatibility phase is approved.

### 5.2 Language And Serialization

Required defaults:

- nullable reference types enabled;
- implicit usings enabled;
- `System.Text.Json` with `JsonSerializerDefaults.Web`;
- camelCase JSON property names;
- null values omitted;
- enum strings serialized with camelCase naming.

The canonical options object is:

```csharp
DotCraftJson.Options
```

### 5.3 Dependencies

The SDK should prefer .NET built-in libraries for:

- HTTP;
- WebSocket;
- process startup;
- stream IO;
- JSON serialization;
- channels and asynchronous coordination.

The high-level SDK should avoid framework dependencies.

---

## 6. Connection Model

### 6.1 Local Mode

`DotCraftClient.ConnectLocalAsync()` establishes a Hub-managed local connection:

```text
.NET app
  -> Hub lock discovery
  -> start dotcraft hub if needed
  -> POST /v1/appservers/ensure
  -> connect to endpoints.appServerWebSocket
  -> initialize / initialized
  -> ready DotCraftClient
```

Options:

```csharp
public sealed class DotCraftLocalClientOptions : DotCraftClientOptions
{
    public string? DotCraftBin { get; set; }
    public string? HubLockPath { get; set; }
    public TimeSpan HubStartupTimeout { get; set; }
}
```

Local mode must not stop the Hub-managed AppServer when the SDK client is disposed. Disposing the SDK client closes only the SDK's AppServer transport.

`DotCraftClient.ConnectLocalChatAsync()` follows the same flow after resolving and initializing the default Chat workspace (`~/.craft/workspaces/chats`). It then calls the existing Hub AppServer ensure endpoint with that concrete workspace path.

### 6.2 Remote Mode

`DotCraftClient.ConnectRemoteAsync()` connects directly to an AppServer WebSocket endpoint.

If a token is passed separately, the WebSocket transport appends it as a `token` query parameter.

Endpoint URLs containing `token=` are secrets and must not be logged by default.

### 6.3 Custom Transport Mode

`DotCraftClient.ConnectAsync()` accepts any `IJsonRpcTransport`.

This mode is required for:

- tests;
- in-memory protocol conformance fixtures;
- custom AppServer transport hosts;
- future embedded or brokered transports.

### 6.4 Initialization Capabilities

Current options:

```csharp
public class DotCraftClientOptions
{
    public string ClientName { get; set; }
    public string? ClientTitle { get; set; }
    public string ClientVersion { get; set; }
    public bool ApprovalSupport { get; set; }
    public bool StreamingSupport { get; set; }
    public bool RequestUserInputSupport { get; set; }
    public bool ConfigChange { get; set; }
    public IReadOnlyDictionary<string, object?>? ExtraCapabilities { get; set; }
}
```

The SDK sends:

```json
{
  "clientInfo": {
    "name": "dotcraft-dotnet",
    "title": null,
    "version": "0.1.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "requestUserInputSupport": false,
    "streamingSupport": true,
    "configChange": true
  }
}
```

`ExtraCapabilities` is a forward-compatible escape hatch. Values supplied there override built-in capability fields with the same key.

Before a stable NuGet release, the SDK must reconcile capability defaults with actual handler support. In particular, a client that advertises `approvalSupport = true` should be able to answer `item/approval/request`, or it should require callers to opt in explicitly.

---

## 7. Hub Client

### 7.1 Responsibilities

`HubClient` provides:

- Hub lock path resolution;
- Hub lock JSON parsing;
- process liveness checks;
- loopback Hub URL validation;
- Hub status probing;
- optional Hub startup;
- workspace AppServer lookup;
- workspace AppServer ensure.

### 7.2 Hub Lock Validation

The SDK reads:

```text
~/.craft/hub/hub.lock
```

Expected shape:

```csharp
public sealed record HubLockInfo(
    int Pid,
    string ApiBaseUrl,
    string Token,
    DateTimeOffset? StartedAt = null,
    string? Version = null,
    string? BinaryPath = null);
```

A Hub lock is trusted only after:

1. it parses successfully;
2. `pid` appears live;
3. `apiBaseUrl` is loopback `http://` with an explicit port and no path/query/fragment;
4. `GET /v1/status` succeeds.

### 7.3 Hub Startup

When configured with `StartHubIfMissing = true`, the SDK starts:

```text
dotcraft hub
```

If `DotCraftBin` points to a `.dll`, the SDK starts:

```text
dotnet <DotCraftBin> hub
```

The child process is started without redirected stdio and without a visible console window where supported.

### 7.4 AppServer Ensure

`HubClient.EnsureAppServerAsync()` calls:

```text
POST /v1/appservers/ensure
```

Payload fields:

- `workspacePath`
- `client`
- `startIfMissing`
- `runtimeTools`

The SDK requires `endpoints.appServerWebSocket` when `ConnectLocalAsync()` uses the result.

`HubClient.EnsureDefaultChatAppServerAsync()` is a convenience wrapper around the same endpoint. It resolves and initializes the default Chat workspace, then sends the resolved path as `workspacePath`.

---

## 8. Wire Client

### 8.1 Responsibilities

`DotCraftWireClient` handles:

- request id generation;
- JSON-RPC request serialization;
- response correlation;
- JSON-RPC error conversion;
- notification dispatch;
- server-initiated request dispatch;
- initialize / initialized handshake;
- raw request and notification methods;
- graceful disposal of the underlying transport.

### 8.2 Transport Interface

The transport abstraction is message-oriented:

```csharp
public interface IJsonRpcTransport : IAsyncDisposable
{
    Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(object message, CancellationToken cancellationToken = default);
}
```

`ReadAsync()` returns one complete JSON-RPC message or `null` when the transport closes.

### 8.3 Stdio / Stream Transport

`StreamJsonRpcTransport` implements newline-delimited JSON-RPC over input and output streams.

Required behavior:

- read UTF-8 lines;
- ignore empty lines;
- write one serialized JSON message per line;
- flush writes automatically;
- synchronize concurrent writes.

### 8.4 WebSocket Transport

`WebSocketJsonRpcTransport` implements one JSON-RPC message per WebSocket text message.

Required behavior:

- support direct construction from an already connected `WebSocket`;
- append `token` query parameter when provided separately;
- ignore binary messages;
- return `null` on close;
- enforce the current 4 MB inbound message limit;
- synchronize concurrent writes;
- attempt best-effort normal close on disposal.

Automatic reconnect is not required at the transport layer.

### 8.5 Raw Request Escape Hatch

The wire client exposes:

```csharp
Task<JsonElement> SendRequestAsync(string method, object? parameters = null, ...);
Task SendNotificationAsync(string method, object? parameters = null, ...);
```

The high-level client exposes:

```csharp
Task<T> RequestAsync<T>(string method, object? parameters = null, ...);
Task<JsonElement> RequestAsync(string method, object? parameters = null, ...);
```

Typed wrappers should use the raw request path internally. Callers may use raw requests for any AppServer method not yet wrapped.

### 8.6 Server-Initiated Request Dispatch

The wire client exposes:

```csharp
IDisposable RegisterServerRequestHandler(
    string method,
    Func<ServerRequest, CancellationToken, Task<object?>> handler);
```

When no handler is registered, the SDK responds with JSON-RPC `-32601`.

Current high-level `DotCraftClient` automatically registers one handler:

```text
item/tool/call
```

for Runtime Dynamic Tools.

---

## 9. High-Level AppServer Client

### 9.1 Initialize Result

The SDK parses `initialize` result into:

```csharp
public sealed record AppServerInitializeResult(
    AppServerServerInfo ServerInfo,
    AppServerServerCapabilities Capabilities,
    JsonElement Raw);
```

Server info:

```csharp
public sealed record AppServerServerInfo(
    string Name,
    string Version,
    string ProtocolVersion,
    IReadOnlyList<string> Extensions);
```

Current typed capability flags:

```csharp
public sealed record AppServerServerCapabilities(
    bool ThreadManagement,
    bool ThreadSubscriptions,
    bool DynamicToolRebind,
    bool AppBinding,
    bool ModelCatalogManagement,
    JsonElement Raw);
```

The `Raw` capability payload must be preserved so callers can inspect capability flags added by AppServer before the SDK has typed properties for them.

### 9.2 High-Level Clients

`DotCraftClient` exposes these grouped clients:

| Property | Type | Responsibility |
|----------|------|----------------|
| `Threads` | `DotCraftThreadClient` | Thread start, resume, subscribe, read. |
| `Turns` | `DotCraftTurnClient` | Turn start, enqueue, interrupt. |
| `Models` | `DotCraftModelClient` | Model catalog list. |
| `AppBindings` | `DotCraftAppBindingClient` | App-side App Binding helpers. |

Future grouped clients should be added only when they wrap a coherent protocol family, such as `Providers`, `WorkspaceConfig`, `Skills`, `Mcp`, `Dreams`, or `SubAgents`.

---

## 10. Thread API

### 10.1 `StartAsync`

Maps to:

```text
thread/start
```

Request record:

```csharp
public sealed record DotCraftThreadStartRequest(
    SessionIdentity Identity,
    string? DisplayName = null,
    string HistoryMode = "server",
    object? Config = null,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools = null,
    IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext = null);
```

The SDK sends:

- `identity`
- `displayName`
- `historyMode`
- `dynamicTools`
- `additionalContext`
- `config`

The SDK accepts either `{ "thread": { ... } }` or a direct thread object response and extracts the thread id from `id` or `threadId`.

### 10.2 `ResumeAsync`

Maps to:

```text
thread/resume
```

Request record:

```csharp
public sealed record DotCraftThreadResumeRequest(
    string ThreadId,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools = null,
    IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext = null);
```

When `DynamicTools` is non-empty, callers should check `client.Capabilities.DynamicToolRebind`.
When `AdditionalContext` is non-null, callers should check `client.Capabilities.RuntimeAdditionalContext`; an empty dictionary clears runtime additional context on resume.

### 10.3 `SubscribeAsync`

Maps to:

```text
thread/subscribe
```

Parameters:

- `threadId`
- `replayRecent`

The SDK currently exposes subscription creation only. It does not yet expose typed `thread/unsubscribe`.

### 10.4 `ReadAsync`

Maps to:

```text
thread/read
```

Parameters:

- `threadId`
- `includeTurns`
- `turnLimit`
- `cursor`

Result:

```csharp
public sealed record DotCraftThreadReadResult(
    string ThreadId,
    JsonElement Thread,
    JsonElement? TurnPage = null);
```

The SDK preserves raw thread JSON and optional `turnPage` JSON instead of duplicating the full Thread DTO model.

---

## 11. Turn API

### 11.1 Input Parts

The SDK currently models common AppServer `InputPart` fields as:

```csharp
public sealed record TurnInputPart(
    string Type,
    string? Text = null,
    string? Name = null,
    string? ArgsText = null,
    string? RawText = null,
    string? Path = null,
    string? DisplayPath = null,
    string? Url = null,
    string? MimeType = null,
    string? FileName = null);
```

Supported protocol input kinds are defined by [AppServer Protocol](../protocols/appserver-protocol.md#51-turnstart). The SDK record is intentionally permissive so new AppServer input-part fields can pass through without requiring a full typed union first.

### 11.2 `StartAsync`

Maps to:

```text
turn/start
```

Parameters:

- `threadId`
- `input`
- `sender`
- `modelId` when provided

`modelId` is a current SDK passthrough. It must be reconciled with the AppServer Protocol before the SDK treats it as stable public behavior.

Result:

```csharp
public sealed record DotCraftTurnStartResult(string? TurnId, JsonElement Raw);
```

The SDK extracts the turn id from `turnId`, `id`, `turn.id`, or `turn.turnId`.

### 11.3 `EnqueueAsync`

Maps to:

```text
turn/enqueue
```

Parameters:

- `threadId`
- `input`
- `sender`

Result:

```csharp
public sealed record DotCraftTurnEnqueueResult(string? QueuedInputId, JsonElement Raw);
```

The SDK extracts the queued input id from `queuedInputId` or `queuedInput.id`.

### 11.4 `InterruptAsync`

Maps to:

```text
turn/interrupt
```

Parameters:

- `threadId`
- `turnId`

The method returns after the server accepts the interruption request. Callers should rely on `turn/cancelled` notifications to observe terminal cancellation.

---

## 12. Provider, Model Catalog, And Thread Configuration APIs

### 12.1 Provider `ListAsync`

`DotCraftClient.Providers.ListAsync()` maps to `provider/list` and returns
public Provider identity, display name, protocol, and implicit-provider state.
The raw result remains available for forward compatibility. Consumers must
project only fields appropriate to their trust boundary.

### 12.2 Model catalog

`DotCraftClient.Models.ListAsync()` retains the backward-compatible lightweight
`ModelInfo` projection for the Runtime-selected Provider.

`DotCraftClient.Models.GetCatalogAsync(providerId)` maps to:

```text
model/list
```

Capability:

```text
capabilities.modelCatalogManagement
```

`GetCatalogAsync(providerId)` returns the structured AppServer
result including success/error state, effective Provider, protocol, and every
model's Reasoning, Speed, and Context Window capability. It does not infer
capability from model names.

### 12.3 Thread model configuration

`DotCraftThreadClient.ReadModelConfigurationAsync(threadId)` reads the complete
captured Provider, model, Reasoning, Speed, and Context Window fields.

`UpdateModelConfigurationAsync(threadId, configuration)` reads the latest full
Thread configuration, changes only these model fields, sends one complete
`thread/config/update`, and reads the authoritative value back. This preserves
Agent Profile, tool, approval, Sandbox, workspace, and other unrelated Thread
fields. AppServer remains responsible for normalization and validation.

---

## 13. Runtime Dynamic Tools

### 13.1 Tool declaration

Runtime Dynamic Tools are declared through:

- `DotCraftThreadStartRequest.DynamicTools`
- `DotCraftThreadResumeRequest.DynamicTools`

The wire model is a tagged union:

```csharp
public abstract record RuntimeDynamicToolDeclaration(
    string Name,
    string Description);

public sealed record RuntimeDynamicToolFunction(
    string Name,
    string Description,
    JsonElement InputSchema,
    bool DeferLoading = false,
    ToolApprovalDescriptor? Approval = null)
    : RuntimeDynamicToolDeclaration(Name, Description);

public sealed record RuntimeDynamicToolNamespace(
    string Name,
    string Description,
    IReadOnlyList<RuntimeDynamicToolDeclaration> Tools)
    : RuntimeDynamicToolDeclaration(Name, Description);
```

Approval metadata:

```csharp
public sealed record ToolApprovalDescriptor(
    string Kind,
    string TargetArgument,
    string? Operation = null,
    string? OperationArgument = null);
```

The owning wire contract is [AppServer Protocol, Runtime Dynamic Tools](../protocols/appserver-protocol.md#410-runtime-dynamic-tools).

### 13.2 Attribute-based authoring

The preferred .NET authoring path uses `DotCraft.Sdk.Tools`.

```csharp
[DynamicTool("GetIssue", "Read an issue from MyApp.")]
public Task<Issue> GetIssueAsync(GetIssueArgs args, CancellationToken cancellationToken);
```

`DynamicToolRegistry.Register(target, namespace)` discovers attributed instance methods and exposes immutable descriptors. Each descriptor contains:

- `Namespace`
- `LocalName`
- `QualifiedName`
- `Description`
- `InputSchema`
- `Order`
- `DeferLoading`

`DynamicToolDescriptor.Name` remains a compatibility alias for `QualifiedName`.

The registry supports:

- one typed arguments class or record;
- flat parameters;
- one injected context parameter configured through `DynamicToolRegistryOptions.ContextType`;
- one injected `CancellationToken`.

Schema generation uses `System.Text.Json.Schema.JsonSchemaExporter`. Object schemas are closed by default. `[SchemaAllowAdditionalProperties]` opts a type into accepting undeclared properties. The registry argument binder enforces the same closed/open-object behavior.

Invalid or ambiguous handler signatures fail during registration. Cancellation requested through the invocation token propagates as `OperationCanceledException`. Expected tool failures use `DynamicToolException`; unexpected failures are logged through `InternalErrorLogger` and return the configured internal error code with a generic message.

### 13.3 Runtime declaration projection

`RuntimeDynamicToolDeclarationBuilder.Build(...)` converts any selected descriptor set into the current AppServer declaration union:

```csharp
IReadOnlyList<RuntimeDynamicToolDeclaration> declarations =
    RuntimeDynamicToolDeclarationBuilder.Build(
        descriptors,
        new Dictionary<string, string>
        {
            ["myapp"] = "MyApp tools."
        });
```

The builder:

- groups functions by namespace;
- preserves descriptor order;
- carries `Description`, `InputSchema`, and `DeferLoading`;
- applies optional approval metadata supplied by the caller;
- rejects missing namespace descriptions, duplicate qualified identities, invalid identifiers, and flat names longer than 64 ASCII bytes.

Attribute-authored registries are namespaced. Top-level Runtime Dynamic Functions remain available through direct construction of `RuntimeDynamicToolFunction`.

### 13.4 Tool call dispatch

The SDK handles server-initiated:

```text
item/tool/call
```

Call model:

```csharp
public sealed record DynamicToolCall(
    string ThreadId,
    string? TurnId,
    string? CallId,
    string? Namespace,
    string Tool,
    JsonElement Arguments);
```

Result model:

```csharp
public sealed record DynamicToolResult(
    bool Success,
    IReadOnlyList<ToolContentItem>? ContentItems = null,
    object? StructuredContent = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
```

Content item model:

```csharp
public sealed record ToolContentItem(
    string Type,
    string? Text = null,
    string? MediaType = null,
    string? Url = null,
    string? DataBase64 = null);
```

Successful model-visible calls must include at least one useful text content item. `StructuredContent` is client-facing data and does not replace the required model-visible summary.

### 13.5 Handler registration

Catch-all registration:

```csharp
IDisposable RegisterDynamicToolHandler(
    Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler);
```

Specific registration:

```csharp
IDisposable RegisterDynamicToolHandler(
    string threadId,
    string? @namespace,
    string toolName,
    Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler);
```

Lookup order:

1. exact `threadId + namespace + toolName`;
2. catch-all fallback handler;
3. unsupported tool result.

When no handler exists, the SDK returns:

```json
{
  "success": false,
  "errorCode": "UnsupportedTool",
  "errorMessage": "No handler registered for this runtime dynamic tool."
}
```

When a handler throws, the SDK returns:

```json
{
  "success": false,
  "errorCode": "AdapterToolCallFailed",
  "errorMessage": "<exception message>"
}
```

---

## 14. App Binding API

### 14.1 Handoff Parsing

`AppBindingHandoff.Parse()` parses native app handoff URLs.

Result:

```csharp
public sealed record AppBindingHandoff(
    string Scheme,
    string Operation,
    string AppId,
    string RequestId,
    string RequestToken,
    string? AppServerUrl);
```

Supported query aliases:

| Logical Field | Query Keys |
|---------------|------------|
| App id | `app`, `appId` |
| Request id | `request`, `requestId` |
| Request token | `token`, `requestToken` |
| AppServer endpoint | `endpoint`, `appServer` |

The parser may validate expected URI scheme and expected app id.

### 14.2 Current App-Side RPC Helpers

`DotCraftAppBindingClient` exposes the App Binding principal lifecycle and binding-scoped MCP control plane:

| SDK Method | AppServer Method | Purpose |
|------------|------------------|---------|
| `ListAppsAsync` | `app/list` | List installed or visible apps. |
| `ViewAppAsync` | `app/view` | Read one app descriptor. |
| `StartConnectionAsync` | `app/connection/start` | Start a connection request. |
| `GetConnectionRequestAsync<T>` | `app/connection/request/get` | Inspect an app connection request. |
| `CompleteConnectionAsync` | `app/connection/connect` | Complete an app connection request. |
| `GetConnectionStatusAsync` | `app/connection/status` | Read app connection status. |
| `RevokeConnectionAsync` | `app/connection/revoke` | Revoke an app connection. |
| `PublishSurfaceAsync` | `app/surface/publish` | Publish or renew an authenticated app principal's loopback surface lease. |
| `ResolveSurfaceAsync` | `app/surface/resolve` | Resolve a live surface as a trusted AppServer client. |
| `EnableBindingAsync` | `thread/appBindings/enable` | Enable the whole app for a thread. |
| `GetBindingRequestAsync` | `app/binding/request/get` | Inspect a thread binding request. |
| `AuthenticateAsync` | `app/connection/authenticate` | Authenticate the current AppServer connection as an app principal. |
| `RefreshCredentialAsync` | `app/connection/refresh` | Rotate the principal credential. |
| `ActivateAsync` | `app/binding/activate` | Activate a binding-scoped Streamable HTTP MCP session. |
| `RebindAsync` | `app/binding/rebind` | Replace an offline binding session at the expected authority revision. |
| `ConfirmCapabilitiesAsync` | `thread/appBindings/confirmCapabilities` | Accept or reject a candidate capability expansion. |
| `ListThreadBindingsAsync` | `thread/appBindings/list` | List thread app bindings. |
| `RevokeThreadBindingAsync` | `thread/appBindings/revoke` | Revoke a thread app binding. |
| `RefreshThreadBindingsAsync` | `thread/appBindings/list` | Refresh thread app binding state. |

Typed App Binding DTOs must remain compatible with [App Binding](../protocols/app-binding.md). Methods that carry app-defined payloads may still expose generic type parameters or raw escape hatches so app authors can model their own connection and binding payloads.

### 14.3 Keep Alive

`KeepAliveAsync()` drains AppServer notifications until cancellation or disconnect:

```csharp
Task KeepAliveAsync(
    Func<AppServerNotification, CancellationToken, Task>? onNotification = null,
    CancellationToken cancellationToken = default);
```

The control connection is independent from the binding MCP session. Draining notifications is useful for control-plane changes, but disconnecting this connection does not terminate a healthy binding MCP session.

### 14.4 Standard App Binding Tool Errors

The SDK exposes standard App Binding error codes:

```csharp
public static class AppBindingErrorCodes
{
    public const string Offline = "AppBindingOffline";
    public const string Expired = "AppBindingExpired";
    public const string Revoked = "AppBindingRevoked";
    public const string ScopeDenied = "AppBindingScopeDenied";
    public const string ToolUnavailable = "AppBindingToolUnavailable";
    public const string ProtocolViolation = "AppBindingProtocolViolation";
}
```

Helper:

```csharp
public static DynamicToolResult ToolError(
    string code,
    string message,
    object? structuredContent = null);
```

The helper returns a failed `DynamicToolResult` with matching `ErrorCode`, `ErrorMessage`, and a text content item.

---

## 15. Notification Model

The SDK currently exposes raw AppServer notifications:

```csharp
public sealed record AppServerNotification(string Method, JsonElement Params);
```

`ReadNotificationsAsync()` yields all notifications sent by the server after initialization.

The SDK does not currently:

- normalize notification method names into higher-level event types;
- filter events by thread;
- merge streaming deltas into final text;
- wait for terminal turn events;
- replay missed notifications after reconnect.

Applications that need those behaviors must currently implement them over raw notifications or use raw AppServer methods such as `thread/read` and `thread/subscribe`.

---

## 16. Error Model

### 16.1 JSON-RPC Errors

Server JSON-RPC errors are represented as:

```csharp
public sealed class JsonRpcException : Exception
{
    public int Code { get; }
    public JsonElement? ErrorData { get; }
}
```

The SDK reads:

- `error.code`, default `-32603`;
- `error.message`, default `"Unknown JSON-RPC error."`;
- `error.data`, optional.

### 16.2 Hub Errors

Hub discovery and management failures are represented as:

```csharp
public sealed class HubClientException : Exception
{
    public string Code { get; }
}
```

Current codes include:

| Code | Meaning |
|------|---------|
| `invalidHubLock` | Lock data or Hub URL failed validation. |
| `hubUnavailable` | Hub is not running and could not or should not be started. |
| `hubInvalidResponse` | Hub returned an invalid or empty response. |
| `unauthorized` | Hub rejected bearer authorization. |
| `hubRequestFailed` | Hub request failed without a more specific code. |

### 16.3 Typed Future Errors

Before a stable NuGet release, the SDK should introduce a common base exception and typed high-level errors for common AppServer cases such as:

- initialization failure;
- transport closed;
- turn already in progress;
- thread not found;
- approval timeout;
- App Binding authorization failure.

Until then, callers should catch `JsonRpcException`, `HubClientException`, transport exceptions, and cancellation exceptions.

---

## 17. AppServer Coverage Matrix

This matrix is the required traceability index for `.NET SDK <-> AppServer` coverage.

Legend:

- **Typed**: public SDK wrapper exists with named .NET types or grouped client method.
- **Generic**: helper exists, but request/response payloads are caller-provided objects and `T` results.
- **Raw**: available only through `RequestAsync` / `SendRequestAsync`.
- **Callback**: SDK handles a server-initiated request.
- **Gap**: no dedicated SDK support beyond raw request or raw notification plumbing.

| Protocol Family | AppServer Method Or Notification | Capability | Current .NET SDK Status | Public Surface |
|-----------------|----------------------------------|------------|--------------------------|----------------|
| Initialization | `initialize` | required | Typed | `DotCraftWireClient.InitializeAsync` |
| Initialization | `initialized` | required | Typed | `DotCraftWireClient.InitializeAsync` |
| Threads | `thread/start` | `threadManagement` | Typed | `Threads.StartAsync` |
| Threads | `thread/resume` | `threadManagement` | Typed | `Threads.ResumeAsync` |
| Threads | `thread/read` | `threadManagement` | Typed | `Threads.ReadAsync` |
| Threads | `thread/subscribe` | `threadSubscriptions` | Typed | `Threads.SubscribeAsync` |
| Threads | `thread/list` | `threadManagement` | Typed | `Threads.ListAsync` |
| Threads | `thread/unsubscribe` | `threadSubscriptions` | Typed | `DotCraftThread.UnsubscribeAsync` |
| Threads | `thread/pause` | `threadManagement` | Raw | `RequestAsync` |
| Threads | `thread/archive` | `threadManagement` | Typed | `DotCraftThread.ArchiveAsync` |
| Threads | `thread/delete` | `threadManagement` | Typed | `DotCraftThread.DeleteAsync` |
| Threads | `thread/rename` | `threadManagement` | Raw | `RequestAsync` |
| Threads | `thread/rollback` | `threadManagement` | Raw | `RequestAsync` |
| Thread config | `thread/config/update` | `configOverride` | Typed model update | `Threads.UpdateModelConfigurationAsync` |
| Thread mode | `thread/mode/set` | `modeSwitch` | Typed | `DotCraftThread.SetModeAsync` |
| Thread goals | `thread/goal/*` | `threadGoals` | Raw | `RequestAsync` |
| Thread maintenance | `thread/compact/start` | `manualCompaction` | Raw | `RequestAsync` |
| Thread maintenance | `thread/memory/consolidate/start` | `manualMemoryConsolidation` | Raw | `RequestAsync` |
| Turns | `turn/start` | required | Typed | `Turns.StartAsync` |
| Turns | `turn/enqueue` | required | Typed | `Turns.EnqueueAsync` |
| Turns | `turn/interrupt` | required | Typed | `Turns.InterruptAsync` |
| Notifications | `thread/*`, `turn/*`, `item/*`, `system/*`, `plan/*`, `subagent/*`, `workspace/configChanged` | varies | Raw | `ReadNotificationsAsync`, `Wire.RegisterNotificationHandler` |
| Run | `turn/start` + terminal turn notifications | required | Typed | `DotCraftThread.RunAsync` / `RunStreamedAsync` |
| Run | normalized streaming events + text merge | required | Typed | `DotCraftRunEvent`, `RunStreamedAsync` |
| Approvals | `item/approval/request` | `approvalFlow` | Callback | `DotCraftClientOptions.ApprovalHandler` |
| User input | `item/tool/requestUserInput` | `requestUserInput` | Callback | `DotCraftClientOptions.UserInputHandler` |
| Runtime Dynamic Tools | `thread/start.dynamicTools` | required | Typed | `RuntimeDynamicToolDeclaration`, `DynamicToolRegistry` |
| Runtime Dynamic Tools | `thread/resume.dynamicTools` | `dynamicToolRebind` | Typed | `DotCraftThreadResumeRequest.DynamicTools` |
| Runtime Dynamic Tools | `item/tool/call` | required for declared tools | Callback | `RegisterDynamicToolHandler` |
| Models | `model/list` | `modelCatalogManagement` | Typed | `Models.ListAsync` |
| Providers | `provider/list` | `providerManagement` | Typed | `Providers.ListAsync` |
| Providers | `provider/create` | `providerManagement` | Raw | `RequestAsync` |
| Providers | `provider/update` | `providerManagement` | Raw | `RequestAsync` |
| Providers | `provider/delete` | `providerManagement` | Raw | `RequestAsync` |
| Providers | `provider/test` | `providerManagement` | Raw | `RequestAsync` |
| Workspace config | `workspace/config/schema` | `workspaceConfigManagement` | Raw | `RequestAsync` |
| Workspace config | `workspace/config/update` | `workspaceConfigManagement` | Raw | `RequestAsync` |
| Memory | `memory/reset` | `memoryManagement` | Raw | `RequestAsync` |
| Dreams | `dreams/status` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/run` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/create` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/get` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/list` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/cancel` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/apply` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/discard` | `dreams` | Raw | `RequestAsync` |
| Dreams | `dreams/archive` | `dreams` | Raw | `RequestAsync` |
| Skills | `skills/list` | `skillsManagement` | Raw | `RequestAsync` |
| Skills | `skills/read` | `skillsManagement` | Raw | `RequestAsync` |
| Skills | `skills/view` | `skillsManagement` | Raw | `RequestAsync` |
| Skills | `skills/restoreOriginal` | `skillsManagement` | Raw | `RequestAsync` |
| Skills | `skills/setEnabled` | `skillsManagement` | Raw | `RequestAsync` |
| Skills | `skills/uninstall` | `skillsManagement` | Raw | `RequestAsync` |
| Plugins | `plugin/list` | `pluginManagement` | Raw | `RequestAsync` |
| Plugins | `plugin/view` | `pluginManagement` | Raw | `RequestAsync` |
| Plugins | `plugin/install` | `pluginManagement` | Raw | `RequestAsync` |
| Plugins | `plugin/remove` | `pluginManagement` | Raw | `RequestAsync` |
| Plugins | `plugin/setEnabled` | `pluginManagement` | Raw | `RequestAsync` |
| Commands | `command/list` | `commandManagement` | Raw | `RequestAsync` |
| Commands | `command/execute` | `commandManagement` | Raw | `RequestAsync` |
| Cron | `cron/list` | `cronManagement` | Raw | `RequestAsync` |
| Cron | `cron/remove` | `cronManagement` | Raw | `RequestAsync` |
| Cron | `cron/enable` | `cronManagement` | Raw | `RequestAsync` |
| Cron | `cron/run` | `cronManagement` | Raw | `RequestAsync` |
| Heartbeat | `heartbeat/trigger` | `heartbeatManagement` | Raw | `RequestAsync` |
| MCP | `mcp/list` | `mcpManagement` | Raw | `RequestAsync` |
| MCP | `mcp/get` | `mcpManagement` | Raw | `RequestAsync` |
| MCP | `mcp/upsert` | `mcpManagement` | Raw | `RequestAsync` |
| MCP | `mcp/remove` | `mcpManagement` | Raw | `RequestAsync` |
| MCP runtime | `mcpServerStatus/list` | `mcpRuntime` | Typed | `McpRuntime.StatusListAsync` |
| MCP | `mcp/test` | `mcpManagement` | Raw | `RequestAsync` |
| External channels | `externalChannel/list` | `externalChannelManagement` | Raw | `RequestAsync` |
| External channels | `externalChannel/get` | `externalChannelManagement` | Raw | `RequestAsync` |
| External channels | `externalChannel/upsert` | `externalChannelManagement` | Raw | `RequestAsync` |
| External channels | `externalChannel/remove` | `externalChannelManagement` | Raw | `RequestAsync` |
| Subagents | `subagent/profiles/list` | `subAgentManagement` | Raw | `RequestAsync` |
| Subagents | `subagent/settings/update` | `subAgentManagement` | Raw | `RequestAsync` |
| Subagents | `subagent/profiles/setEnabled` | `subAgentManagement` | Raw | `RequestAsync` |
| Subagents | `subagent/profiles/upsert` | `subAgentManagement` | Raw | `RequestAsync` |
| Subagents | `subagent/profiles/remove` | `subAgentManagement` | Raw | `RequestAsync` |
| Subagent sessions | `subagent/children/list` | `subAgentSessions` | Raw | `RequestAsync` |
| Subagent sessions | `subagent/sendMessage` | `subAgentSessions` | Raw | `RequestAsync` |
| Subagent sessions | `subagent/followupTask` | `subAgentSessions` | Raw | `RequestAsync` |
| Subagent sessions | `subagent/close` | `subAgentSessions` | Raw | `RequestAsync` |
| App discovery | `app/list` | `appBindingVersion: 2` | Typed | `AppBindings.ListAppsAsync` |
| App discovery | `app/view` | `appBindingVersion: 2` | Typed | `AppBindings.ViewAppAsync` |
| App connection | `app/connection/start` | `appBindingVersion: 2` | Typed | `AppBindings.StartConnectionAsync` |
| App connection | `app/connection/request/get` | `appBindingVersion: 2` | Generic | `AppBindings.GetConnectionRequestAsync<T>` |
| App connection | `app/connection/connect` | `appBindingVersion: 2` | Typed | `AppBindings.CompleteConnectionAsync` |
| App connection | `app/connection/status` | `appBindingVersion: 2` | Typed | `AppBindings.GetConnectionStatusAsync` |
| App connection | `app/connection/revoke` | `appBindingVersion: 2` | Typed | `AppBindings.RevokeConnectionAsync` |
| App binding | `thread/appBindings/enable` | `appBindingVersion: 2` | Typed | `AppBindings.EnableBindingAsync` |
| App binding | `app/binding/request/get` | `appBindingVersion: 2` | Typed | `AppBindings.GetBindingRequestAsync` |
| App binding | `app/binding/activate` | `appBindingVersion: 2` | Typed | `AppBindings.ActivateAsync` |
| App binding | `app/binding/rebind` | `appBindingVersion: 2` | Typed | `AppBindings.RebindAsync` |
| Thread app bindings | `thread/appBindings/list` | `appBindingVersion: 2` | Typed | `AppBindings.ListThreadBindingsAsync` |
| Thread app bindings | `thread/appBindings/revoke` | `appBindingVersion: 2` | Typed | `AppBindings.RevokeThreadBindingAsync` |

When a raw-only row gains a typed wrapper, this matrix must be updated in the same change.

---

## 18. Documentation and Examples

### 18.1 Repository README

The SDK README should include:

1. source-reference installation;
2. local Hub-backed connection;
3. direct remote AppServer connection;
4. thread start / subscribe / turn start;
5. Runtime Dynamic Tool declaration and handler registration;
6. App principal handoff/authentication, enable, activate, rebind, confirmation, and revoke flow;
7. direct Hub query;
8. low-level JSON-RPC escape hatch;
9. development commands.

### 18.2 Future DotCraft Docs

When the SDK becomes a user-facing release, docs should be added under the DotCraft documentation site in both English and Chinese.

Candidate locations:

- `docs/developing/sdk-dotnet.md`
- `docs/zh/developing/sdk-dotnet.md`

Documentation should remain concise and should link back to AppServer Protocol and App Binding rather than duplicating full wire contracts.

---

## 19. Testing and Conformance

### 19.1 Current Required Tests

The current SDK test suite must cover:

- initialize request shape and `initialized` notification;
- response correlation;
- notification delivery;
- server-initiated request handling;
- Runtime Dynamic Tool dispatch;
- `thread/start` identity and dynamic tool shape;
- `turn/start` input shape and turn id extraction;
- App Binding activation RPC method shape;
- App Binding handoff parsing;
- standard App Binding tool error shape;
- Hub lock parsing and bearer auth;
- Hub loopback URL validation.

### 19.2 Tests For New Typed Wrappers

Every new typed AppServer wrapper must add or update tests that assert:

- method name;
- request parameter shape;
- capability gate behavior when applicable;
- response parsing shape;
- error behavior when relevant;
- coverage matrix row update.

### 19.3 Validation Commands

SDK validation:

```powershell
dotnet test .\DotCraft.Sdk.sln
```

Package validation before release:

```powershell
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```

---

## 20. Security

### 20.1 Hub Security

The SDK must only trust Hub lock files after validating:

- live process;
- loopback HTTP base URL;
- successful Hub status probe.

Protected Hub requests use bearer authorization.

Hub tokens must not be logged by default.

### 20.2 AppServer WebSocket Tokens

When a WebSocket token is provided, it may appear in the URL query string required by AppServer WebSocket transport.

The SDK should avoid printing full WebSocket URLs with tokens in logs or error messages.

### 20.3 App Binding Handoff Tokens

App Binding request tokens are short-lived secrets. The SDK parser returns them to the native app so the app can complete the AppServer flow. SDK examples must avoid logging the raw handoff URL or token.

### 20.4 Runtime Dynamic Tools

Dynamic tool handlers execute inside the application process. The SDK does not sandbox them.

Tool authors are responsible for:

- validating arguments;
- enforcing app-level authorization;
- checking App Binding principal and authority revision when applicable;
- returning structured failures instead of throwing for expected business errors.

### 20.5 Approval Safety

The SDK currently exposes `ApprovalSupport` as an initialize option but does not provide a typed approval handler. Production applications should not advertise approval support unless they can answer approval requests through a registered low-level handler or a future typed approval API.

---

## 21. Versioning and Compatibility

### 21.1 SDK Version

The package version is currently:

```text
0.1.0
```

Preview NuGet packages use prerelease package versions such as:

```text
0.1.0-preview.1
```

### 21.2 Publishing Policy

NuGet publishing for `DotCraft.Sdk` must run through GitHub Actions, not ordinary local developer machines.

The release workflow must:

- be manually triggered through `workflow_dispatch`;
- default to a non-publishing dry run;
- build, test, pack, inspect, and upload package artifacts before any publish step;
- read the package version from `sdk/dotnet/src/DotCraft.Sdk/DotCraft.Sdk.csproj`;
- publish only from `refs/heads/main`;
- require the exact package version as human confirmation for `nuget.org`;
- require `id-token: write` only on the publish job;
- use NuGet Trusted Publishing through `NuGet/login@v1` when available;
- avoid storing long-lived NuGet API keys in repository secrets;
- push exact package filenames, never broad globs;
- fail when the requested package version already exists on nuget.org.

The Trusted Publishing policy on nuget.org must be configured for:

| Field | Value |
|-------|-------|
| Repository Owner | `DotHarness` |
| Repository | `dotcraft` |
| Workflow File | `publish-nuget.yml` |
| Environment | leave blank |

The workflow requires a GitHub Actions repository or organization variable named `NUGET_USER` containing the nuget.org username that created the Trusted Publishing policy. This value is not the `GitHubActions` publisher label shown by nuget.org, is not an API key, and must not be an email address.

Version rules:

The workflow reads the release version from `DotCraft.Sdk.csproj`:

| Channel | Version Pattern |
|---------|-----------------|
| `preview` | `MAJOR.MINOR.PATCH-preview.N` |
| `stable` | `MAJOR.MINOR.PATCH` |

### 21.3 Protocol Compatibility

The SDK must inspect `initialize` result `serverInfo` and `capabilities`.

High-level wrappers for optional methods must check capabilities before calling methods when the owning protocol marks them optional.

Examples:

- `thread/resume.dynamicTools` requires `dynamicToolRebind`;
- `model/list` requires `modelCatalogManagement`;
- App Binding methods require `appBindingVersion: 2`;
- workspace config methods require `workspaceConfigManagement`.

The current SDK exposes raw capabilities so callers can implement their own gates while typed wrappers are incomplete.

### 21.4 Stable And Unstable Surfaces

Stable baseline:

- `DotCraftClient` connection methods;
- raw `RequestAsync`;
- raw notification consumption;
- `IJsonRpcTransport`;
- `StreamJsonRpcTransport`;
- `WebSocketJsonRpcTransport`;
- current thread, turn, model, Runtime Dynamic Tool, Hub, and App Binding helper methods.

Less stable until a public NuGet release:

- exact high-level DTO shapes that currently store raw `JsonElement`;
- capability default values;
- future typed error hierarchy;
- future normalized streaming event API;
- future run abstraction.

---

## 22. Acceptance Contract

A complete implementation of the current .NET SDK baseline satisfies:

- `DotCraft.Sdk` targets `net10.0`.
- Local mode discovers or starts Hub and connects to the ensured AppServer WebSocket endpoint.
- Remote mode connects directly to AppServer WebSocket.
- Custom transport mode works for tests and embedded clients.
- Initialize sends the AppServer `clientInfo` and capability shape defined by AppServer Protocol.
- The SDK sends `initialized` after initialize succeeds.
- Raw JSON-RPC request and notification APIs are available.
- Raw server notifications can be consumed as `AppServerNotification`.
- Thread start, resume, subscribe, and read wrappers work.
- Turn start, enqueue, and interrupt wrappers work.
- Model list wrapper works when `modelCatalogManagement` is available.
- Runtime Dynamic Tools can be declared during thread start or resume.
- `item/tool/call` is dispatched to registered handlers and returns structured failures when unsupported or when handlers throw.
- App Binding handoff URLs can be parsed safely.
- App-side request inspection, principal authentication, activation, rebind, and capability confirmation helpers work.
- Standard App Binding tool error codes are available.
- Hub lock validation rejects non-loopback URLs and uses bearer auth for protected Hub calls.
- Current test suite passes.
- This spec's coverage matrix reflects all typed and generic SDK capabilities.
- AppServer low-level bindings share `DotCraft.Protocol.Contracts` with the server; no generated or handwritten second copy of the public C# wire DTOs exists, and raw request APIs remain available.

---

## 23. Future Work

Future amendments may cover:

- package signing and stronger package provenance;
- a stable SDK contract version constant;
- automatic reconnect and dynamic tool rebind policy;
- typed wrappers for thread rename, config, goals, and maintenance;
- typed wrappers for provider, workspace config, skills, plugins, commands, cron, heartbeat, MCP, external channels, subagents, memory, and Dreams;
- additional typed App Binding DTO refinements for app-defined connection and binding payloads;
- multi-targeting earlier .NET versions;
- structured logging with safe token redaction;
- bilingual DotCraft documentation pages for .NET SDK users.
