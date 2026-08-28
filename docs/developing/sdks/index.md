# DotCraft SDKs

Use a DotCraft SDK to connect an application to AppServer. Start with the high-level API for threads, runs, tools, approvals, and user input.

![DotCraft SDK layers and connection: your app starts at the high-level API, which builds on the wire layer and the generated contracts; locally, Hub ensures the workspace AppServer while the SDK connects to that AppServer directly and reaches the session core](/sdk-layers-topology.svg)

## Start here

- [Quickstart](./quickstart) — connect and run your first turn.
- [Threads & runs](./runs) — manage threads, input, streaming, and recovery.
- [Tools & approvals](./tools) — add runtime tools and interactive callbacks.
- [MCP runtime](./mcp-runtime) — inspect configured servers, resources, tools, and authentication.
- [Channel adapters](./channels) — connect an external messaging platform.

## Choose an API layer

| Layer | Use it for |
| --- | --- |
| **High-level** | Applications that work with `DotCraft`, threads, runs, callbacks, models, MCP runtime, or App Binding. Start here. |
| **Wire** | Typed JSON-RPC, connection state, timeouts, and explicit raw extension calls. |
| **Contracts** | Generated DTOs, method maps, registries, and protocol metadata without transport I/O. |

Host adapters and Channel runtimes use these SDK layers to add environment-specific integration such as workspace routing, heartbeat, platform delivery, and UI interaction.

## Packages

| Language | Package | Availability |
| --- | --- | --- |
| TypeScript | `@dotcraft/sdk` | Published on npm. |
| .NET | `DotCraft.Sdk` | Published on NuGet. |
| Python | `dotcraft` | Source preview; install from this repository. |

The [Quickstart](./quickstart) is the single source for installation commands.

## Common capabilities

| Task | TypeScript | .NET | Python |
| --- | --- | --- | --- |
| Connect to a workspace | `DotCraft.local()` | `ConnectLocalAsync()` | `connect_local()` |
| Connect to default Chat | `DotCraft.localChat()` | `ConnectLocalChatAsync()` | `connect_local_chat()` |
| Connect remotely | `DotCraft.remote()` | `ConnectRemoteAsync()` | `connect_remote()` |
| Run a turn | `run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `run()` / `run_streamed()` |
| Read history pages | `listTurns()` / `listItems()` | `ListTurnsAsync()` / `ListItemsAsync()` | `list_turns()` / `list_items()` |
| List models | `models.list()` | `Models.GetCatalogAsync()` | `models.list()` |
| Use MCP runtime | `mcpRuntime` | `McpRuntime` | `mcp_runtime` |
| Use App Binding | `appBindings` | `AppBindings` | `app_bindings` |

TypeScript and Python also provide a Channel Adapter profile. .NET does not.

## Connection ownership

A local high-level client asks [Hub](../lifecycle/hub) to ensure the workspace AppServer, then connects to AppServer directly. Closing the SDK connection does not stop a Hub-managed AppServer.

Reconnect restores Wire transport and initialization. It does not replay in-flight requests or rebuild thread subscriptions, active runs, or runtime tool bindings. See [Threads & runs](./runs) for recovery steps.

## Language reference

- [TypeScript](./typescript)
- [.NET](./dotnet)
- [Python](./python)

## Related docs

- [Hub lifecycle](../lifecycle/hub)
- [AppServer mode](../lifecycle/appserver)
- [AppServer Protocol](../protocols/appserver-protocol)
- [MCP runtime](./mcp-runtime)
- [DotCraft App](../integrations/app-binding)
