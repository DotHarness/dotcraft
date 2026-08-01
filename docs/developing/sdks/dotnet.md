# .NET SDK reference

`DotCraft.Sdk` provides generated contracts, pure JSON-RPC access, high-level Thread and Run APIs, and Hub management. For installation and a first run, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `DotCraft.Sdk` (NuGet) |
| Target framework | `net10.0` |
| Serialization | `System.Text.Json` with camel-case web defaults (`DotCraftJson.Options`) |

```bash
dotnet add package DotCraft.Sdk
```

This reference describes the current repository surface. The latest NuGet release may follow the repository on the normal release cadence.

## Layers and namespaces

| Layer | Public surface |
|-------|----------------|
| Contracts | `DotCraft.Protocol.Contracts` Wire DTOs, `RpcRequest<TParams,TResult>`, `RpcNotification<TParams>`, and method descriptors. |
| Wire | `DotCraft.Sdk.Wire.DotCraftWireClient`, stdio/WebSocket transports, connection state, and JSON-RPC errors. |
| High-level | `DotCraft.Sdk.AppServer.DotCraftClient`, `DotCraftThread`, Run APIs, dynamic tools, typed callbacks, and errors. |
| Hub | `DotCraft.Sdk.Hub.HubClient`, Hub DTOs, process policy, events, and structured `HubClientException`. |
| App Binding | `DotCraft.Sdk.AppBinding.DotCraftAppBindingClient`, handoff models, and error helpers. |

Core and the SDK use the same `DotCraft.Protocol.Contracts` assembly; the SDK does not maintain a second copy of Wire DTOs.

## Typed and raw Wire APIs

`DotCraftWireClient.RequestAsync` and `NotifyAsync` accept generated descriptors, so the descriptor determines the parameter and result types. Typed notification and server-request handlers use the same contracts.

Unknown or third-party extensions use the separate `RequestRawAsync` and `NotifyRawAsync` methods and raw handler registration. There is no arbitrary-string overload on the typed methods.

`DotCraftClient` is the normal application entry point. `DotCraftWireClient` only manages JSON-RPC and the connection session; it does not automatically approve requests, answer user-input prompts, or restore application resources.

## Connection lifecycle

The Wire client reports `Connecting`, `Initializing`, `Ready`, `Disconnected`, `Reconnecting`, `ReconnectError`, and `Closed`.

- Raw Wire connections default to no reconnect. High-level clients and host profiles opt in explicitly.
- The default RPC timeout is 30 seconds and includes reconnect queue time.
- Reconnect uses 1-to-30-second exponential backoff with jitter and queues at most 1024 new requests in call order.
- In-flight requests fail on disconnect and are not replayed. The client replays `initialize`, sends `initialized`, and then releases queued requests.
- Registered handlers remain installed. Thread subscriptions, active Runs, and Runtime Dynamic Tools are not recreated automatically.

`ConnectLocalAsync` uses the Hub to ensure a workspace AppServer. Set `DotCraftLocalClientOptions.Executable` when the host has resolved a specific DotCraft binary. `ConnectRemoteAsync` connects to a known AppServer WebSocket, and `ConnectAsync` accepts a custom `IJsonRpcTransport`.

## Hub API

`HubClient` supports lock/default-chat helpers, live-Hub query and ensure, workspace AppServer resolution, ensure/restart/stop/list operations, status, events, and shutdown.

Hub exceptions preserve `Code`, `Message`, and `Details`. Direct Hub startup accepts the expected executable and a binary match policy:

- `Ignore`
- `RestartIfMismatch`
- `ErrorIfMismatch`

When no expected executable is supplied, the default policy is `Ignore`.

## Validation

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```

## Related docs

- [Quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [Build an app](../integrations/build-an-app)
- [AppServer Protocol](../protocols/appserver-protocol)
