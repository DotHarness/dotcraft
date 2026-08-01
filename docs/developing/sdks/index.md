# DotCraft SDKs

DotCraft SDKs connect applications, native hosts, tools, and external channels to the same AppServer protocol. Start with the high-level API for normal application work, and move down a layer only when you need more control.

## Choose a layer

| Layer | Use it for |
|-------|------------|
| Contracts | Generated DTOs, method maps, registries, and protocol metadata without transport or runtime dependencies. |
| Wire | Typed JSON-RPC requests, notifications, server requests, connection lifecycle, and explicit raw escape hatches. |
| High-level | `DotCraft`, Thread, Run, approvals, user input, and Runtime Dynamic Tools. This is the default application API. |
| Host Adapter | Desktop and Channel policies such as workspace routing, reconnect profiles, heartbeat, and platform delivery. These policies are not part of the general Wire client. |

Use the [Quickstart](./quickstart) to install the SDK, connect to a workspace, and run a turn. Use the [AppServer Protocol](../protocols/appserver-protocol) directly only for a custom transport, an unsupported language, or protocol debugging.

## Package availability

| Language | Package | Availability |
|----------|---------|--------------|
| TypeScript | `@dotcraft/sdk` | Source preview; build and install from this repository. |
| .NET | `DotCraft.Sdk` | Published on NuGet. |
| Python | `dotcraft` | Source preview; install from this repository. |

Installation commands are kept in the [Quickstart](./quickstart) so setup guidance has one source of truth.

## Guide

- [Quickstart](./quickstart) — install, connect, run a turn, and stream events.
- [Threads & runs](./runs) — thread lifecycle, run options, events, and reconnect boundaries.
- [Tools & approvals](./tools) — Runtime Dynamic Tools, approvals, and user-input callbacks.
- [Channel adapters](./channels) — build external channels with the TypeScript or Python host profile.

## Capability snapshot

| Capability | TypeScript | .NET | Python |
|------------|------------|------|--------|
| Local Hub-managed connection | `DotCraft.local()` | `DotCraftClient.ConnectLocalAsync()` | `DotCraft.connect_local()` |
| Remote WebSocket connection | `DotCraft.remote()` | `DotCraftClient.ConnectRemoteAsync()` | `DotCraft.connect_remote()` |
| Typed Wire request | `request()` | `RequestAsync()` with a descriptor | Generated typed RPC methods |
| Raw Wire request | `requestRaw()` / `notifyRaw()` | `RequestRawAsync()` / `NotifyRawAsync()` | `request_raw()` / `notify_raw()` |
| High-level one-turn run | `thread.run()` / `runStreamed()` | `RunAsync()` / `RunStreamedAsync()` | `thread.run()` / `run_streamed()` |
| Approval and user-input callbacks | Typed handlers | Typed handlers | Typed handlers |
| Runtime Dynamic Tools | Declaration and typed callbacks | Declaration and typed callbacks | Declaration and typed callbacks |
| Channel adapter profile | TypeScript runtime | Not applicable | Python adapter base class |

AppServer remains the authority for thread state, queue behavior, approvals, model resolution, and persistence. The SDK presents those capabilities without creating a second source of truth.

## App integration paths

SDK clients can expose Runtime Dynamic Tools on a live connection or participate in App Binding when a native app grants app-owned tools to a thread. Runtime tools are bound to the active connection; App Binding tools are bound to a persisted thread grant.

![DotCraft app integration paths: Wire Client and App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/app-integration.png)

## Event topology

SDK clients consume notifications and may answer server-initiated requests. Notifications have no JSON-RPC `id`; server requests do, and the client must respond.

![DotCraft SDK event topology](/sdk-event-topology.svg)

All three SDKs normalize common notifications into run events and preserve unknown notifications as raw events. The Wire layer also exposes an explicit raw notification listener for extensions that are not in the generated contracts.

## Language reference

- [TypeScript](./typescript) — `@dotcraft/sdk`
- [.NET](./dotnet) — `DotCraft.Sdk`
- [Python](./python) — `dotcraft`

## Related docs

- [AppServer Protocol](../protocols/appserver-protocol)
- [Hub lifecycle](../lifecycle/hub)
