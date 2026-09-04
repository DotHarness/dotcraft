# DotCraft SDKs

Use a DotCraft SDK to connect an application to AppServer. Start with the high-level API for threads, runs, tools, approvals, and user input.

![DotCraft SDK layers and connection: your app starts at the high-level API, which builds on the wire layer and the generated contracts; locally, Hub ensures the workspace AppServer while the SDK connects to that AppServer directly and reaches the session core](/sdk-layers-topology.svg)

## Start here

- [Quickstart](./quickstart) — connect and run your first turn.
- [Threads & runs](./runs) — manage threads, input, streaming, and recovery.
- [Tools & approvals](./tools) — add runtime tools and interactive callbacks.
- [MCP runtime](./mcp-runtime) — inspect configured servers, resources, tools, and authentication.
- [Channel adapters](./channels) — connect an external messaging platform.

Inside a DotCraft conversation, `$dotcraft-api` picks the right shape for your integration (client, in-process host, or extension) and checks names against the generated protocol contract.

## Choose an API layer

| Layer | Use it for |
| --- | --- |
| **High-level** | Applications that work with `DotCraft`, threads, runs, callbacks, models, MCP runtime, or App Binding. Start here. |
| **Wire** | Typed JSON-RPC, connection state, timeouts, and explicit raw extension calls. |
| **Contracts** | Generated DTOs, method maps, registries, and protocol metadata without transport I/O. |

Host adapters and Channel runtimes build on these layers and add environment-specific integration: workspace routing, heartbeat, platform delivery, and UI interaction.

## Packages

| Language | Package | Availability |
| --- | --- | --- |
| TypeScript | `@dotcraft/sdk` | Published on npm. |
| .NET | `DotCraft.Sdk` | Published on NuGet. |

The [Quickstart](./quickstart) is the single source for installation commands.

## Common capabilities

| Task | TypeScript | .NET |
| --- | --- | --- |
| Connect to a workspace | `DotCraft.local()` | `ConnectLocalAsync()` |
| Connect to default Chat | `DotCraft.localChat()` | `ConnectLocalChatAsync()` |
| Connect remotely | `DotCraft.remote()` | `ConnectRemoteAsync()` |
| Run a turn | `run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` |
| Read history pages | `listTurns()` / `listItems()` | `ListTurnsAsync()` / `ListItemsAsync()` |
| List models | `models.list()` | `Models.GetCatalogAsync()` |
| Use MCP runtime | `mcpRuntime` | `McpRuntime` |
| Use App Binding | `appBindings` | `AppBindings` |

TypeScript also provides a Channel Adapter profile. .NET does not.

## Connection ownership

A local high-level client asks [Hub](../lifecycle/hub) to ensure the workspace AppServer, then connects to AppServer directly. Closing the SDK connection does not stop a Hub-managed AppServer.

Reconnect restores Wire transport and initialization. It does not replay in-flight requests or rebuild thread subscriptions, active runs, or runtime tool bindings. See [Threads & runs](./runs) for recovery steps.

## Language reference

- [TypeScript](./typescript)
- [.NET](./dotnet)

## Related docs

- [AppServer mode](../lifecycle/appserver) — how the AppServer an SDK connects to is started, secured, and exposed over WebSocket.
- [AppServer Protocol](../protocols/appserver-protocol) — the methods and notifications these clients speak.
- [DotCraft App](../integrations/app-binding) — App Binding, for applications that expose their own capabilities to a thread.
