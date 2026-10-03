# DotCraft .NET SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.1 |
| **Status** | Living |
| **Date** | 2026-10-03 |
| **Related Specs** | [Unified SDK](sdk.md), [Protocol Contracts and Generation](protocol-contract-generation.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md), [Session Core](../architecture/session-core.md), [Hub Architecture](../architecture/hub-architecture.md) |

Purpose: define the .NET package, its generated Wire binding, its Contracts-first public API, high-level Thread and Run behavior, raw extension boundary, and error model.

## 1. Scope and principles

`DotCraft.Sdk` is the .NET 10 client SDK for AppServer, Hub-backed local connection, Runtime Dynamic Tools, and native App Binding integrations.

The binding follows these rules:

- `DotCraft.Protocol` is the only public AppServer Wire model assembly.
- Known AppServer methods use `AppServerRpc` descriptors and generated `DotCraftWireClient.XxxAsync` or `RegisterXxxHandler` bindings.
- The SDK may own high-level handles, reducers, Hub models, authoring attributes, and exceptions because those are not Wire DTOs.
- Raw JSON remains available only for unknown third-party extensions, deliberately open protocol fields, diagnostics, and forward compatibility.
- AppServer remains authoritative for lifecycle, queueing, configuration, permission, provider, and App Binding behavior.

The SDK does not define Hub HTTP semantics, Session persistence, provider behavior, or App Binding policy. Those remain in their owning specifications.

## 2. Package contract

The package id is `DotCraft.Sdk` and the target framework is `net10.0`.

The NuGet package contains both:

- `DotCraft.Sdk.dll`
- `DotCraft.Protocol.dll`

Contracts is a separate transport-free assembly but is not a separate package dependency. A consumer installs only `DotCraft.Sdk`.

Package and assembly versions are controlled by the release process. This specification does not require changing a package version as part of a protocol implementation change.

Public namespaces are:

| Namespace | Responsibility |
|-----------|----------------|
| `DotCraft.Protocol` | `Optional<T>`, extensibility, payload catalog, common descriptor primitives. |
| `DotCraft.Protocol.AppServer` | AppServer DTOs and `AppServerRpc` descriptors. |
| `DotCraft.Protocol.ScreenView` | Remote screen view frame format, ceilings, and control records; outside the contract generator. |
| `DotCraft.Sdk.Wire` | JSON-RPC transports, lifecycle, typed generated bindings, raw escape hatches. |
| `DotCraft.Sdk` | High-level client, Thread handle, Run, callbacks, options, and stable SDK exceptions. |
| `DotCraft.Sdk.DynamicTools` | Attribute-based Runtime Dynamic Tool authoring and invocation. |
| `DotCraft.Sdk.AppBinding` | App Binding client, handoff parsing, and error helpers. |
| `DotCraft.Sdk.Hub` | Local Hub discovery and AppServer lifecycle. |

## 3. Contracts ownership

Public APIs use `DotCraft.Protocol` DTOs directly, following the [contract ownership rules](protocol-contract-generation.md#4-sources-of-truth). The SDK owns high-level handles, Run models, Hub helpers, dynamic-tool authoring attributes, App Binding handoff parsing, and exceptions; these must not duplicate wire DTOs.

## 4. Wire client and generated bindings

`DotCraftWireClient` owns JSON-RPC correlation, initialization gating, timeouts, connection state, notifications, and server-initiated request dispatch.

The generator reads Manifest format version 1 and emits:

```csharp
Task<ThreadStartResult> ThreadStartAsync(
    this DotCraftWireClient client,
    ThreadStartParams parameters,
    CancellationToken cancellationToken = default);

IDisposable RegisterItemApprovalRequestHandler(
    this DotCraftWireClient client,
    Func<ApprovalRequestParams, CancellationToken, Task<ApprovalResponseResult>> handler);
```

Every generated binding calls the exact `AppServerRpc` descriptor member recorded by the Manifest. It must not make a known call through a method string or anonymous params object.

The generic typed primitives remain public:

```csharp
Task<TResult> RequestAsync<TParams, TResult>(
    RpcRequest<TParams, TResult> descriptor,
    TParams parameters,
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null);

Task NotifyAsync<TParams>(
    RpcNotification<TParams> descriptor,
    TParams parameters,
    CancellationToken cancellationToken = default);
```

`RequestRawAsync`, `NotifyRawAsync`, raw server-request handlers, and raw notification handlers remain explicit escape hatches. High-level clients must not use them for registered methods. The initialize handshake may use an internal bypass-ready path, but its method identity and DTOs still come from `AppServerRpc.Initialize` and Contracts.

## 5. Connection and initialization

`DotCraftClient` exposes:

```csharp
public DotCraftWireClient Wire { get; }
public ServerInfo ServerInfo { get; }
public ServerCapabilities Capabilities { get; }
public DotCraftThreadClient Threads { get; }
public DotCraftTurnClient Turns { get; }
public DotCraftProviderClient Providers { get; }
public DotCraftModelClient Models { get; }
public DotCraftAgentProfileClient AgentProfiles { get; }
public DotCraftMcpRuntimeClient McpRuntime { get; }
public DotCraftAppBindingClient AppBindings { get; }
```

Connection entry points are:

- `ConnectLocalAsync(workspacePath, options, cancellationToken)` through Hub;
- `ConnectLocalChatAsync(options, cancellationToken)` through the default Chat workspace;
- `ConnectRemoteAsync(appServerUrl, token, options, cancellationToken)` over WebSocket;
- `ConnectAsync(transport, options, cancellationToken)` for custom transports and tests.

Raw/custom transports default to no reconnect. Local and remote high-level connections default to reconnect, unless `AutoReconnect` overrides the default.

Reconnect follows the [shared Wire lifecycle](sdk.md#42-wire-client). An active Run terminates with `RunDisconnectedException`.

## 6. Thread API

`DotCraftThreadClient` uses Contracts params and results:

```csharp
Task<DotCraftThread> StartAsync(ThreadStartParams parameters, ...);
Task<DotCraftThread> ResumeAsync(ThreadResumeParams parameters, ...);
Task<ThreadListResult> ListAsync(ThreadListParams parameters, ...);
Task<ThreadReadResult> ReadAsync(ThreadReadParams parameters, ...);
Task<ThreadTurnsListResult> ListTurnsAsync(ThreadTurnsListParams parameters, ...);
Task<ThreadItemsListResult> ListItemsAsync(ThreadItemsListParams parameters, ...);
Task<ThreadRecoveryExportResult> ExportRecoveryAsync(ThreadRecoveryExportParams parameters, ...);
Task<ThreadRecoveryRestoreResult> RestoreRecoveryAsync(ThreadRecoveryRestoreParams parameters, ...);
```

`thread/list` always sends its required `SessionIdentity`. There is no parameterless list API that depends on server leniency.

`ReadAsync` and `DotCraftThread.RefreshAsync` return the current Thread header without persisted Turns or Items. `ListTurnsAsync` returns Turn metadata without Items; `ListItemsAsync` returns bounded Item pages across the Thread or for one Turn.

`DotCraftThread` is a high-level handle. Its `Snapshot` is `SessionThread`. Lifecycle helpers such as subscribe, unsubscribe, mode, archive, delete, enqueue, interrupt, and Runtime Dynamic Tool handler registration use generated typed bindings internally.

`ExportRecoveryAsync` and `RestoreRecoveryAsync` transfer the recovery package without parsing or rewriting its Session fields, following the [AppServer recovery contract](../protocols/appserver-protocol.md#419-thread-recovery-methods). Callers resume the restored thread to obtain a high-level handle.

The model-configuration convenience API reads the latest complete `ThreadConfiguration`, copies every unrelated `Optional<T>` state and unknown extension field, replaces only provider/model/reasoning/speed/context-window fields, sends `ThreadConfigUpdateParams`, then re-reads and returns the authoritative `ThreadConfiguration`.

`DotCraftAgentProfileClient` exposes typed list, read, validate, upsert, remove, and thread-refresh methods backed by the corresponding generated `AppServerRpc.AgentProfile*` descriptors. Callers check `Capabilities.AgentProfileManagement` before presenting management functionality.

## 7. Turn and input API

Turn APIs accept `InputPart` directly:

```csharp
Task<TurnStartResult> StartAsync(TurnStartParams parameters, ...);
Task<TurnEnqueueResult> EnqueueAsync(TurnEnqueueParams parameters, ...);
Task<RpcEmpty> InterruptAsync(TurnInterruptParams parameters, ...);
```

Convenience overloads may accept a thread id, `IReadOnlyList<InputPart>`, and `SenderContext`, but they must construct these Contracts params without adding fields not present in the protocol.

`RunOptions.Sender` is `SenderContext?`. `RunOptions` does not contain a model id because `turn/start` has no model-id field. Model selection belongs to Thread configuration.

## 8. Session item payloads

`SessionItem.Payload` remains `Optional<JsonElement?>` because payload kinds are open and future kinds must survive old clients.

For canonical payloads, consumers use:

```csharp
var parsed = SessionItemPayloadParser.Parse(item);
if (parsed.TryGet<AgentMessagePayload>(out var message))
    Console.WriteLine(message.Text);
```

The parser returns payload kind, presence, original raw JSON, known/unknown status, and typed value. It preserves missing, explicit null, and value states. Unknown kinds do not throw. A malformed known payload throws a serialization error. All canonical payload DTOs inherit `ExtensibleJsonObject` and retain unknown fields.

The high-level Run reducer must use canonical payload DTOs rather than JSON property paths.

## 9. Typed Run model

The event hierarchy is:

```csharp
public record DotCraftRunEvent(
    string Type,
    string ThreadId,
    string? TurnId,
    AppServerNotification Raw);

public sealed record DotCraftRunEvent<TParams>(...) : DotCraftRunEvent;
public sealed record DotCraftRawRunEvent(...) : DotCraftRunEvent;
```

For known notifications, `Type` is the canonical Wire method name and `Params` is the descriptor's Contracts params DTO. Generated classification covers the full registered server-notification catalog; the Run layer does not maintain a handwritten method-name switch.

Unknown extension notifications become `DotCraftRawRunEvent` and retain their original parameters. A known notification whose shape is invalid terminates the Run with `ProtocolViolationException`.

`DotCraftRunResult` contains:

```csharp
string ThreadId;
string? TurnId;
string Text;
SessionTurn? Turn;
IReadOnlyList<AppServerNotification>? RawEvents;
```

`Turn` is the typed terminal turn. It may be null only when busy-enqueue succeeds before a new turn exists.

Run orchestration follows the [shared Run profile](sdk.md#34-run-profile). Failed and cancelled turns raise stable typed errors when `ThrowOnFailure` is true.

## 10. Callbacks and Runtime Dynamic Tools

Approval and user-input callbacks directly use Contracts:

```csharp
delegate Task<ApprovalResponseResult> ApprovalHandler(
    ApprovalRequestParams request,
    CancellationToken cancellationToken);

delegate Task<UserInputResponseResult> UserInputHandler(
    UserInputRequestParams request,
    CancellationToken cancellationToken);
```

The client may not advertise either capability without its required handler. `ApprovalResponses` provides common standard responses without defining a duplicate Wire DTO.

Runtime Dynamic Tool handlers use:

```csharp
Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>>
```

Declarations use Contracts `RuntimeDynamicToolDeclaration`, `RuntimeDynamicToolFunction`, `RuntimeDynamicToolNamespace`, and `ToolApprovalDescriptor`. The SDK-owned registry and authoring attributes may generate these declarations from typed .NET methods.

When no matching handler exists, the SDK returns `UnsupportedTool`. When a handler throws, it returns `AdapterToolCallFailed`. Both are valid `DynamicToolCallResult` failures.

## 11. Provider, model, MCP, and App Binding clients

Provider and model clients return `ProviderListResult` and `ModelListResult`; they do not project a second capability model.

The MCP runtime client accepts and returns the corresponding Contracts DTOs for status list, resource read, tool call, OAuth login, and reload. Every method uses its generated binding.

`DotCraftAppBindingClient` exposes typed Contracts methods for connection request, connect/status/revoke/authenticate/refresh, app list/view, binding request/activate/rebind, surface publish/resolve, and Thread binding enable/list/revoke/confirmation. Generic `<T>` or `object` overloads for known App Binding methods are not part of the API.

`AppBindingHandoff` remains a high-level URL parser. `ToolError` returns a standard failed `DynamicToolCallResult`.

## 12. Error model

Stable SDK errors derive from `DotCraftException` and expose a stable string `Code`. AppServer-originated errors additionally expose optional `ServerError` metadata containing the stable server code, message key, fallback text, actionable detail, and raw structured params.

Required errors include:

- `InitializationFailedException`;
- `ProtocolViolationException`;
- `TurnInProgressException`;
- `ThreadNotFoundException`;
- `ThreadNotActiveException`;
- `TurnFailedException`;
- `TurnCancelledException`;
- `RunDisconnectedException`;
- `RequestTimeoutException`;
- `ReconnectQueueFullException`.

Wire JSON-RPC failures remain `JsonRpcException` with the numeric JSON-RPC code and complete raw `ErrorData`; the same response is projected into `DotCraftException.ServerError` so callers catching the base type retain actionable diagnostics.

## 14. Security and compatibility

- Raw request methods must never log credentials or bearer tokens.
- App Binding credential and bearer fields are opaque secrets.
- Unknown fields are preserved where Contracts types inherit `ExtensibleJsonObject`.
- Open status, role, and kind values remain strings when future server values are valid.
- Adding a new known notification or payload kind is additive; removing or retyping an existing one is breaking.
