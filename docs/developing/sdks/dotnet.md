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
| Threads | `Threads.StartAsync()`, `ResumeAsync()`, `ListAsync()`, `ReadAsync()` |
| Run | `RunAsync()`, `RunStreamedAsync()`, `EnqueueAsync()`, `InterruptAsync()` |
| Thread state | `Snapshot`, `RefreshAsync()`, `SubscribeAsync()`, `UnsubscribeAsync()`, `SetModeAsync()`, `ArchiveAsync()`, `DeleteAsync()` |
| Providers and models | `Providers.ListAsync()`, `Models.GetCatalogAsync()` |
| Model configuration | `Threads.ReadModelConfigurationAsync()`, `UpdateModelConfigurationAsync()` |
| MCP runtime | `McpRuntime.ListStatusAsync()`, `ReadResourceAsync()`, `CallToolAsync()`, `LoginOAuthAsync()`, `ReloadAsync()` |
| App Binding | `AppBindings` |
| Runtime tools | `OnToolCall()` or `RegisterDynamicToolHandler()` |

Configure `ApprovalHandler` and `UserInputHandler` in connection options. See [Threads & runs](./runs) and [Tools & approvals](./tools) for task flows.

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
- [Build an app](../integrations/build-an-app)
- [AppServer Protocol](../protocols/appserver-protocol)
