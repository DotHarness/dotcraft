# DotCraft SDKs

DotCraft SDKs are language bindings over the same AppServer protocol. They let applications, native apps, tools, and external channels connect to a workspace, reuse persistent threads, stream turn events, and participate in approvals without reimplementing Session Core.

## Get started

::: code-group

```bash [TypeScript]
npm install @dotcraft/sdk
```

```bash [.NET]
dotnet add package DotCraft.Sdk
```

```bash [Python]
pip install dotcraft
```

:::

Then follow the [Quickstart](./quickstart) to connect, start a thread, and run your first turn.

## Guide

- [Quickstart](./quickstart) — install, connect, run a turn, stream events.
- [Threads & runs](./runs) — thread lifecycle, run options, and the normalized event model.
- [Tools & approvals](./tools) — runtime dynamic tools and approval / user-input callbacks.
- [Channel adapters](./channels) — build external channels (TypeScript and Python).

## Common Model

All SDKs build on the same layers:

| Layer | Role |
|-------|------|
| Hub bootstrap | Finds or starts the local Hub and ensures a workspace AppServer when using local mode. |
| AppServer JSON-RPC | Carries `initialize`, thread and turn methods, notifications, server requests, and raw escape-hatch calls. |
| Session Core | Provides the durable `Thread -> Turn -> Item` model, approvals, event ordering, and persisted history. |
| SDK binding | Adds language-idiomatic clients, helpers, callbacks, stream reducers, and typed wrappers. |

Use the SDK when you want a client library. Use the [AppServer Protocol](../protocols/appserver-protocol) directly when you are implementing a new transport, debugging the wire protocol, or need complete control over JSON-RPC messages.

## Capability Snapshot

The [SDK specification](https://github.com/DotHarness/dotcraft/blob/master/specs/sdk/sdk.md) tracks the full cross-language parity matrix.

| Capability | TypeScript | .NET | Python |
|------------|------------|------|--------|
| Local Hub-managed connection | `DotCraft.local()` | `DotCraftClient.ConnectLocalAsync()` | `DotCraft.connect_local()` |
| Remote WebSocket connection | `DotCraft.remote()` | `DotCraftClient.ConnectRemoteAsync()` | `DotCraft.connect_remote()` |
| Raw AppServer request | `request()` | `RequestAsync()` | `request()` |
| High-level one-turn run | `thread.run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `thread.run()` / `run_streamed()` |
| Normalized streaming events | `DotCraftRunEvent` + raw | `DotCraftRunEvent` + raw | `RunEvent` + raw |
| Approval & user-input callbacks | Typed handlers | Typed handlers | Typed handlers |
| Runtime Dynamic Tools | Declaration + typed callbacks | Declaration + typed callbacks | Declaration + typed callbacks |
| App Binding helpers | Typed/generic + handoff parse | Typed/generic + handoff parse | Typed/generic + handoff parse |
| Channel adapter runtime | First-party TypeScript runtime | Not applicable | Channel adapter base class |

AppServer remains the source of truth for thread state, queue behavior, approvals, model catalog resolution, and persistence; the SDK is a client over it, not a second authority.

## Event Topology

SDK clients consume AppServer notifications and sometimes answer server-initiated requests. Notifications have no JSON-RPC `id`; server requests do, and clients must respond to them.

![DotCraft SDK event topology](/sdk-event-topology.svg)

All three SDKs normalize common wire notifications into run events (`DotCraftRunEvent` in TypeScript and .NET, `RunEvent` in Python) and keep unknown notifications as `raw`, while still exposing the raw notification stream for advanced clients.

## Reference

Per-language package details — identity, exports/namespaces, runtime baseline, version, and language-specific profiles:

- [TypeScript](./typescript) — `@dotcraft/sdk`
- [.NET](./dotnet) — `DotCraft.Sdk`
- [Python](./python) — `dotcraft`

## Further Reading

- [AppServer Protocol](../protocols/appserver-protocol)
- [Hub Local Coordination](../lifecycle/hub)
