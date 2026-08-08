# .NET SDK reference

`DotCraft.Sdk` is the .NET client for AppServer applications. Start with the [Quickstart](./quickstart) for installation and a first run.

## Package

| Field | Value |
| --- | --- |
| Package | `DotCraft.Sdk` (NuGet) |
| Target framework | `net10.0` |
| Serialization | `System.Text.Json` with `DotCraftJson.Options` |

Install only `DotCraft.Sdk`. The package includes `DotCraft.Sdk.dll` and `DotCraft.Protocol.dll`.

## Namespaces

| Namespace | Public surface |
| --- | --- |
| `DotCraft.Sdk` | `DotCraftClient`, thread and run APIs, callbacks, MCP runtime, and high-level exceptions. |
| `DotCraft.Protocol` | Protocol primitives, RPC descriptors, and JSON options. |
| `DotCraft.Protocol.AppServer` | AppServer DTOs, payloads, results, and notifications. |
| `DotCraft.Sdk.Wire` | `DotCraftWireClient`, transports, connection state, timeouts, and JSON-RPC errors. |
| `DotCraft.Sdk.Hub` | Hub discovery, AppServer management, process policy, events, and structured errors. |
| `DotCraft.Sdk.DynamicTools` | Attribute-based Runtime Dynamic Tool authoring. |
| `DotCraft.Sdk.AppBinding` | App Binding handoff and result helpers. |

Contracts is a separate assembly and logical layer, not a separate NuGet package.

## High-level API

| Task | API |
| --- | --- |
| Connect | `ConnectLocalAsync()`, `ConnectLocalChatAsync()`, `ConnectRemoteAsync()`, `ConnectAsync()` |
| Close | `DisposeAsync()` / `await using` |
| Threads | `Threads.StartAsync()`, `ResumeAsync()`, `ListAsync()`, `ReadAsync()`, `ListTurnsAsync()`, `ListItemsAsync()` |
| Run | `RunAsync()`, `RunStreamedAsync()`, `EnqueueAsync()`, `InterruptAsync()` |
| Thread state | `Snapshot`, `RefreshAsync()`, `SubscribeAsync()`, `UnsubscribeAsync()`, `SetModeAsync()`, `ArchiveAsync()`, `DeleteAsync()` |
| Providers and models | `Providers.ListAsync()`, `Models.GetCatalogAsync()` |
| Model configuration | `Threads.ReadModelConfigurationAsync()`, `UpdateModelConfigurationAsync()` |
| MCP runtime | `McpRuntime.ListStatusAsync()`, `ReadResourceAsync()`, `CallToolAsync()`, `LoginOAuthAsync()`, `ReloadAsync()` |
| App Binding | `AppBindings` |
| Runtime tools | `OnToolCall()` or `RegisterDynamicToolHandler()` |

Configure `ApprovalHandler` and `UserInputHandler` in connection options. See [Threads & runs](./runs) and [Tools & approvals](./tools) for task flows.

## Connect

| Method | Required argument | Connection ownership |
| --- | --- | --- |
| `ConnectLocalAsync(workspacePath, options?, cancellationToken)` | Workspace path | Uses Hub to ensure the workspace AppServer, then connects to it. |
| `ConnectLocalChatAsync(options?, cancellationToken)` | None | Uses Hub to ensure the default Chat workspace AppServer. |
| `ConnectRemoteAsync(appServerUrl, options?, cancellationToken)` | WebSocket URL; optional token in `DotCraftRemoteOptions` | Connects directly to an existing AppServer. |
| `ConnectAsync(transport, options?, cancellationToken)` | `IJsonRpcTransport` | Uses an application-owned custom transport. |

`DotCraftClientOptions` controls client identity, capabilities, callbacks, streaming, config-change notifications, and reconnect behavior. `DotCraftLocalOptions` adds executable, Hub lock, user-profile, and startup-timeout settings.

The shared option fields are `AutoReconnect`, `ClientName`, `ClientTitle`, `ClientVersion`, `ApprovalSupport`, `StreamingSupport`, `RequestUserInputSupport`, `ConfigChange`, `ExtraCapabilities`, `ApprovalHandler`, and `UserInputHandler`. `DotCraftRemoteOptions` adds `Token`.

## Threads and runs

`DotCraftThreadClient` exposes typed contract parameters:

```csharp
Task<DotCraftThread> StartAsync(ThreadStartParams parameters, CancellationToken cancellationToken = default);
Task<DotCraftThread> ResumeAsync(ThreadResumeParams parameters, CancellationToken cancellationToken = default);
Task<ThreadListResult> ListAsync(ThreadListParams parameters, CancellationToken cancellationToken = default);
Task<ThreadReadResult> ReadAsync(ThreadReadParams parameters, CancellationToken cancellationToken = default);
Task<ThreadTurnsListResult> ListTurnsAsync(ThreadTurnsListParams parameters, CancellationToken cancellationToken = default);
Task<ThreadItemsListResult> ListItemsAsync(ThreadItemsListParams parameters, CancellationToken cancellationToken = default);
```

`ReadAsync()` and `DotCraftThread.RefreshAsync()` return the current Thread header without persisted Turns or Items. `ListTurnsAsync()` reads Turn metadata without Items; `ListItemsAsync()` reads Items across the Thread or for the optional `ThreadItemsListParams.TurnId`. Both page requests accept an opaque cursor, limit, and sort direction.

`DotCraftThread` accepts either text or `IReadOnlyList<InputPart>` in `RunAsync()` and `RunStreamedAsync()`. `RunOptions` controls sender context, raw-event collection, queue-if-busy behavior, and whether failed terminal turns throw. Cancellation interrupts an active turn once its ID is known.

The result is `DotCraftRunResult`; streamed events are `DotCraftRunEvent` or `DotCraftRunEvent<TParams>`. Thread-control methods operate on the handle's `Id`, while the low-level `Turns` surface accepts explicit protocol parameters.

## Providers, models, MCP, and App Binding

| Client | Operations |
| --- | --- |
| `Providers` | `ListAsync()` lists configured providers. |
| `Models` | `GetCatalogAsync(providerId?)` returns models and typed capabilities. |
| `Threads` | `ReadModelConfigurationAsync()` and `UpdateModelConfigurationAsync()` preserve unrelated thread configuration fields. |
| `McpRuntime` | `ListStatusAsync()`, `ReadResourceAsync()`, `CallToolAsync()`, `LoginOAuthAsync()`, `ReloadAsync()`. |
| `AppBindings` | Typed connection, surface, thread-binding, and principal operations. |

See [MCP runtime](./mcp-runtime) and [DotCraft App](../integrations/app-binding) for task-oriented flows.

The MCP client accepts generated Contracts DTOs and returns generated result DTOs:

```csharp
Task<McpServerStatusListResult> ListStatusAsync(McpServerStatusListParams? parameters = null, CancellationToken cancellationToken = default);
Task<McpServerResourceReadResult> ReadResourceAsync(McpServerResourceReadParams parameters, CancellationToken cancellationToken = default);
Task<McpServerToolCallResult> CallToolAsync(McpServerToolCallParams parameters, CancellationToken cancellationToken = default);
Task<McpServerOAuthLoginResult> LoginOAuthAsync(McpServerOAuthLoginParams parameters, CancellationToken cancellationToken = default);
Task<McpServerReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
```

## Callbacks and Runtime Dynamic Tools

```csharp
delegate Task<ApprovalResponseResult> ApprovalHandler(
    ApprovalRequestParams request, CancellationToken cancellationToken);
delegate Task<UserInputResponseResult> UserInputHandler(
    UserInputRequestParams request, CancellationToken cancellationToken);

IDisposable OnToolCall(
    string? @namespace,
    string toolName,
    Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler);
```

`DotCraftThread.OnToolCall()` scopes a handler to one thread. `DotCraftClient.RegisterDynamicToolHandler()` also supports a catch-all handler or an explicit thread/namespace/tool key. Dispose registrations with their owning scope.

## Contracts and payloads

High-level methods return `DotCraft.Protocol.AppServer` contracts instead of duplicate SDK DTOs. `SessionItem.Payload` stays open as `JsonElement` so unknown item kinds survive older clients.

Use `SessionItemPayloadParser.Parse(item)` or `TryGet<TPayload>` for known payloads. Preserve `Raw` when your application does not recognize a future payload kind.

## Typed and raw Wire API

Use generated descriptors or generated `XxxAsync` extension methods for cataloged operations:

```csharp
var result = await client.Wire.ThreadListAsync(parameters, cancellationToken);
```

Use `RequestRawAsync`, `NotifyRawAsync`, and raw handlers only for third-party or not-yet-cataloged extensions. Typed methods do not accept arbitrary method strings.

`DotCraftWireClient` owns JSON-RPC and connection state. It does not approve requests, answer user input, or rebuild thread and tool resources.

## Connection lifecycle

Wire state is `Connecting`, `Initializing`, `Ready`, `Disconnected`, `Reconnecting`, `ReconnectError`, or `Closed`.

- Raw Wire connections do not reconnect by default.
- Local and remote high-level connections enable reconnect by default. Override it with `DotCraftClientOptions.AutoReconnect`.
- Ordinary requests default to a 30-second timeout.
- Reconnect uses exponential backoff and queues at most 1024 new calls.
- In-flight calls fail and are never replayed.
- Initialization completes before queued calls are released.
- Handler registrations survive reconnect. Thread subscriptions and runtime tool resources do not.
- An active run fails with `RunDisconnectedException`; `turn/start` is never replayed.

Disposing a local high-level client closes its AppServer connection. It does not stop Hub or the Hub-managed AppServer.

## Errors

SDK exceptions derive from `DotCraftException` and carry a stable `Code`.

| Exception | Condition |
| --- | --- |
| `JsonRpcException` | AppServer returned a JSON-RPC error. Preserves `RpcCode` and `ErrorData`. |
| `InitializationFailedException` | Connection initialization failed. |
| `ProtocolViolationException` | A known message does not match its contract. |
| `TurnInProgressException` | The thread already has an active turn. |
| `ThreadNotFoundException` / `ThreadNotActiveException` | The target thread is missing or cannot run. |
| `TurnFailedException` / `TurnCancelledException` | A buffered run reached a failed or cancelled terminal state. |
| `RunDisconnectedException` | The Wire session ended during an active run. |
| `ApprovalTimeoutException` | AppServer reports approval timeout. |
| `RequestTimeoutException` | A Wire request exceeded its timeout. |
| `ReconnectQueueFullException` | The reconnect queue reached its capacity. |

`HubClientException` carries the Hub `Code`, message, and `Details`.

## Hub API

`HubClient` discovers or starts Hub, validates the local lock, resolves a workspace AppServer, and supports ensure, restart, stop, list, status, events, and shutdown operations.

Do not log Hub tokens, App Binding credentials, or full token-bearing WebSocket URLs.

## Related docs

- [SDK quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [MCP runtime](./mcp-runtime)
- [DotCraft App](../integrations/app-binding)
- [AppServer Protocol](../protocols/appserver-protocol)
