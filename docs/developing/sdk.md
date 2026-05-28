# DotCraft SDKs

DotCraft SDKs are language bindings over the same AppServer protocol. They let applications, native apps, tools, and external channels connect to a workspace, reuse persistent threads, stream turn events, and participate in approvals without reimplementing Session Core.

All SDKs share the same AppServer and Hub model. Language-specific pages describe package shape, runtime behavior, and available helpers.

## Common Model

All SDKs build on the same layers:

| Layer | Role |
|-------|------|
| Hub bootstrap | Finds or starts the local Hub and ensures a workspace AppServer when using local mode. |
| AppServer JSON-RPC | Carries `initialize`, thread and turn methods, notifications, server requests, and raw escape-hatch calls. |
| Session Core | Provides the durable `Thread -> Turn -> Item` model, approvals, event ordering, and persisted history. |
| SDK binding | Adds language-idiomatic clients, helpers, callbacks, stream reducers, and typed wrappers where available. |

Use the SDK when you want a client library. Use [AppServer Protocol](./appserver-protocol.md) directly when you are implementing a new transport, debugging the wire protocol, or need complete control over JSON-RPC messages.

## Event Topology

SDK clients consume AppServer notifications and sometimes answer server-initiated requests. Notifications have no JSON-RPC `id`; server requests do, and clients must respond to them.

![DotCraft SDK event topology](/sdk-event-topology.svg)

TypeScript `runStreamed()` normalizes common wire notifications into `DotCraftRunEvent` values and keeps unknown notifications as `raw`. .NET currently exposes raw notifications, so applications implement their own reducer when they need final text merging or terminal-run handling.

## SDK Choices

| SDK | Package | Best for | Notes |
|-----|---------|----------|-------|
| [TypeScript](./sdk-typescript.md) | `@dotcraft/sdk` | Node.js applications, first-party TypeScript channel modules, high-level run helpers. | Most complete high-level app surface today, including `run()` / `runStreamed()`, callback handlers, Hub helpers, and channel runtime components. |
| [.NET](./sdk-dotnet.md) | `DotCraft.Sdk` | Native apps, App Binding integrations, C# tools, and typed AppServer clients. | Strong local/remote connection, thread/turn/model wrappers, runtime dynamic tools, App Binding helpers, and raw notification access. |
| [Python](./sdk-python.md) | `dotcraft_wire` | Python external channel adapters and wire protocol clients. | Preserved as the Python adapter/wire SDK, including stdio/WebSocket transports, approvals, delivery, channel tools, and a Telegram reference adapter. |

## Capability Snapshot

| Capability | TypeScript | .NET |
|------------|------------|------|
| Local Hub-managed connection | Typed `DotCraft.local()` | Typed `DotCraftClient.ConnectLocalAsync()` |
| Remote WebSocket connection | Typed `DotCraft.remote()` | Typed `DotCraftClient.ConnectRemoteAsync()` |
| Raw AppServer request | `request()` / wire client | `RequestAsync()` / wire client |
| Streaming notifications | Normalized run events plus raw messages | Raw `AppServerNotification` stream |
| High-level one-turn run | `run()` / `runStreamed()` | Not yet available |
| Runtime Dynamic Tools | Declaration plus typed callbacks | Declaration plus `RegisterDynamicToolHandler` |
| Approval and user input callbacks | High-level handlers | Register low-level server request handlers today |
| App Binding helpers | Typed/generic helpers | First-class native-app helper surface |
| Channel adapter runtime | First-party TypeScript channel runtime | Not in .NET |

SDKs should not duplicate server authority. AppServer remains the source of truth for thread state, queue behavior, approvals, model catalog resolution, and persistence.

## Further Reading

- [TypeScript SDK](./sdk-typescript.md)
- [.NET SDK](./sdk-dotnet.md)
- [Python SDK](./sdk-python.md)
- [AppServer Protocol](./appserver-protocol.md)
- [Hub Local Coordination](./hub.md)
