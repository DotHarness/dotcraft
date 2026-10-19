# DotCraft .NET SDK Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Related Specs** | [Unified SDK](sdk.md), [Protocol Contracts and Generation](protocol-contract-generation.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md), [Session Core](../architecture/session-core.md), [Hub Architecture](../architecture/hub-architecture.md) |

Purpose: define the .NET package, its generated Wire binding, its Contracts-first public API, high-level Thread and Run behavior, raw extension boundary, and error model.

## 1. Scope and principles

`DotCraft.Sdk` is the .NET 10 client SDK for AppServer, Hub-backed local connection, Runtime Dynamic Tools, and native App Binding integrations.

- `DotCraft.Protocol` is the only public AppServer Wire model assembly.
- Known AppServer methods use `AppServerRpc` descriptors and generated `DotCraftWireClient.XxxAsync` or `RegisterXxxHandler` bindings.
- The SDK owns high-level handles, reducers, Hub models, authoring attributes, and exceptions because those are not Wire DTOs.
- Raw JSON is available only for unknown third-party extensions, deliberately open protocol fields, diagnostics, and forward compatibility.
- AppServer remains authoritative for lifecycle, queueing, configuration, permission, provider, and App Binding behavior.

## 2. Package contract

The package id is `DotCraft.Sdk` and the target framework is `net10.0`. The package contains `DotCraft.Sdk.dll` and `DotCraft.Protocol.dll`; Contracts is a separate transport-free assembly but not a separate package dependency, so a consumer installs only `DotCraft.Sdk`.

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

Public APIs use `DotCraft.Protocol` DTOs directly, following the [contract ownership rules](protocol-contract-generation.md#3-sources-of-truth). SDK-owned handles, Run models, Hub helpers, dynamic-tool authoring attributes, App Binding handoff parsing, and exceptions must not duplicate wire DTOs.

## 4. Wire client and generated bindings

`DotCraftWireClient` owns JSON-RPC correlation, initialization gating, timeouts, connection state, notifications, and server-initiated request dispatch.

Generated extension methods (for example `ThreadStartAsync(ThreadStartParams, CancellationToken)` and `RegisterItemApprovalRequestHandler(...)`) call the exact `AppServerRpc` descriptor member recorded by the Manifest, never a method string or anonymous params object. The generic typed primitives `RequestAsync<TParams, TResult>(RpcRequest<TParams, TResult>, ...)` and `NotifyAsync<TParams>(RpcNotification<TParams>, ...)` remain public.

`RequestRawAsync`, `NotifyRawAsync`, raw server-request handlers, and raw notification handlers are explicit escape hatches; high-level clients must not use them for registered methods. The initialize handshake still takes its method identity and DTOs from `AppServerRpc.Initialize`.

## 5. Connection and initialization

`DotCraftClient` exposes `Wire`, `ServerInfo`, `Capabilities`, and typed clients `Threads`, `Turns`, `Providers`, `Models`, `AgentProfiles`, `McpRuntime`, and `AppBindings`.

Connection entry points are:

- `ConnectLocalAsync(workspacePath, options, cancellationToken)` through Hub;
- `ConnectLocalChatAsync(options, cancellationToken)` through the default Chat workspace;
- `ConnectRemoteAsync(appServerUrl, token, options, cancellationToken)` over WebSocket;
- `ConnectAsync(transport, options, cancellationToken)` for custom transports and tests.

Raw/custom transports default to no reconnect. Local and remote high-level connections default to reconnect unless `AutoReconnect` overrides it. Reconnect follows the [shared Wire lifecycle](sdk.md#42-wire-client); an active Run terminates with `RunDisconnectedException`.

## 6. Thread API

`DotCraftThreadClient` takes Contracts params and returns Contracts results or a `DotCraftThread` handle: `StartAsync`, `ResumeAsync`, `ListAsync`, `ReadAsync`, `ListTurnsAsync`, `ListItemsAsync`, `ExportRecoveryAsync`, and `RestoreRecoveryAsync`.

`thread/list` always sends its required `SessionIdentity`; there is no parameterless list API.

`ReadAsync` and `DotCraftThread.RefreshAsync` return the Thread header without persisted Turns or Items. `ListTurnsAsync` returns Turn metadata without Items; `ListItemsAsync` returns bounded Item pages across the Thread or for one Turn.

`DotCraftThread` is a high-level handle whose `Snapshot` is `SessionThread`. Its lifecycle helpers (subscribe, unsubscribe, mode, archive, delete, enqueue, interrupt, Runtime Dynamic Tool handler registration) use generated typed bindings.

`ExportRecoveryAsync` and `RestoreRecoveryAsync` transfer the recovery package without parsing or rewriting its Session fields, following the [AppServer recovery contract](../protocols/appserver-protocol.md#418-thread-recovery-methods). Callers resume the restored thread to obtain a handle.

The model-configuration convenience API reads the latest complete `ThreadConfiguration`, preserves every unrelated `Optional<T>` state and unknown extension field, replaces only provider/model/reasoning/speed/context-window fields, sends `ThreadConfigUpdateParams`, then re-reads and returns the authoritative `ThreadConfiguration`.

`DotCraftAgentProfileClient` exposes typed list, read, validate, upsert, remove, and thread-refresh methods. Callers check `Capabilities.AgentProfileManagement` before presenting management functionality.

## 7. Turn and input API

Turn APIs (`StartAsync`, `EnqueueAsync`, `InterruptAsync`) accept Contracts params with `InputPart` input. Convenience overloads may accept a thread id, `IReadOnlyList<InputPart>`, and `SenderContext`, but construct the Contracts params without adding fields.

`RunOptions.Sender` is `SenderContext?`. `RunOptions` has no model id because `turn/start` has none; model selection belongs to Thread configuration.

## 8. Session item payloads

`SessionItem.Payload` is `Optional<JsonElement?>` because payload kinds are open. Consumers parse canonical payloads with `SessionItemPayloadParser.Parse(item)` and `TryGet<TPayload>`. The parser returns payload kind, presence, original raw JSON, known/unknown status, and typed value, with the [canonical parsing outcomes](protocol-contract-generation.md#58-session-item-payloads).

The high-level Run reducer uses canonical payload DTOs rather than JSON property paths.

## 9. Typed Run model

`DotCraftRunEvent` carries `Type`, `ThreadId`, `TurnId`, and the raw `AppServerNotification`. Known notifications produce `DotCraftRunEvent<TParams>`, where `Type` is the canonical Wire method name and `Params` is the descriptor's Contracts params DTO; generated classification covers the full server-notification catalog. Unknown extension notifications become `DotCraftRawRunEvent` with their original parameters. A known notification with an invalid shape terminates the Run with `ProtocolViolationException`.

`DotCraftRunResult` contains `ThreadId`, `TurnId`, merged `Text`, the typed terminal `Turn` (`SessionTurn?`, null only when busy-enqueue succeeds before a new turn exists), and optional `RawEvents`.

Run orchestration follows the [shared Run profile](sdk.md#34-run-profile). Failed and cancelled turns raise stable typed errors when `ThrowOnFailure` is true.

## 10. Callbacks and Runtime Dynamic Tools

`ApprovalHandler` maps `ApprovalRequestParams` to `ApprovalResponseResult`; `UserInputHandler` maps `UserInputRequestParams` to `UserInputResponseResult`. The client does not advertise either capability without its handler. `ApprovalResponses` provides standard responses without a duplicate Wire DTO.

Runtime Dynamic Tool handlers map `DynamicToolCallParams` to `DynamicToolCallResult`. Declarations use Contracts `RuntimeDynamicToolDeclaration`, `RuntimeDynamicToolFunction`, `RuntimeDynamicToolNamespace`, and `ToolApprovalDescriptor`; the SDK registry and authoring attributes may generate them from typed .NET methods. Missing and failing handlers follow the [shared Runtime Dynamic Tool contract](sdk.md#44-runtime-dynamic-tools).

## 11. Provider, model, MCP, and App Binding clients

Provider and model clients return `ProviderListResult` and `ModelListResult` without projecting a second capability model.

The MCP runtime client uses Contracts DTOs for status list, resource read, tool call, OAuth login, and reload.

`DotCraftAppBindingClient` exposes typed Contracts methods for connection request, connect/status/revoke/authenticate/refresh, app list/view, binding request/activate/rebind, surface publish/resolve, and Thread binding enable/list/revoke/confirmation. Known App Binding methods have no generic `<T>` or `object` overloads.

`AppBindingHandoff` is a high-level URL parser. `ToolError` returns a standard failed `DynamicToolCallResult`.

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

Wire JSON-RPC failures are `JsonRpcException` with the numeric JSON-RPC code and complete raw `ErrorData`; the same response is projected into `DotCraftException.ServerError` so callers catching the base type retain diagnostics.

## 13. Security and compatibility

- Raw request methods never log credentials or bearer tokens.
- App Binding credential and bearer fields are opaque secrets.
- Unknown fields are preserved where Contracts types inherit `ExtensibleJsonObject`.
- Open status, role, and kind values remain strings when future server values are valid.
- Adding a known notification or payload kind is additive; removing or retyping one is breaking.
