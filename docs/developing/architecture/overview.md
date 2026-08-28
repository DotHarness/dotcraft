# Architecture overview

DotCraft is a .NET 10 / C# Agent Harness. Its assemblies separate the provider-neutral agent foundation, product kernel, reusable hosting, external protocol, and official application composition. This page defines those boundaries for integrators and contributors.

![DotCraft runtime architecture topology](/runtime-architecture-topology.svg)

## Assembly boundaries

Higher-level components depend only on the foundational layers beneath them:

```text
DotCraft.App (official composition root)
  |-- DotCraft.Runtime
  |     `-- DotCraft.Core
  |           `-- DotCraft.Agents
  |-- DotCraft.AppServer
  |     |-- DotCraft.Core
  |     `-- DotCraft.Protocol
  |-- model providers
  `-- optional features
```

| Component | Responsibility |
|---|---|
| **`DotCraft.Agents`** | Provider-neutral Agent APIs, provider contracts, common middleware, the tool loop, and prompt-cache selection |
| **`DotCraft.Core`** | Product kernel for sessions, Agent orchestration, tools, context, memory, skills, plugins, security, configuration, modules, and workspace semantics |
| **`DotCraft.Runtime`** | Reusable dependency-injection registration and Generic Host lifecycle for a workspace |
| **`DotCraft.Protocol`** | Wire contracts shared by AppServer and protocol clients |
| **`DotCraft.AppServer`** | JSON-RPC request handling, contract mapping, connection state, and stdio and WebSocket transports |
| **`DotCraft.App`** | Official composition root for process entry points, providers, optional features, logging, and process policy |
| **Feature assemblies** | Automations, Teams, Dynamic Workflows, channels, and other feature-owned behavior built on Core contracts |

Core builds on the provider-neutral Agents foundation. Runtime and feature assemblies build on Core. AppServer adapts Core domain capabilities to Protocol contracts. The official App selects these components and connects feature-specific protocol adapters at the composition boundary. Runtime and AppServer operate on the same host-owned `ISessionService`.

## Module discovery and capability facets

`DotCraft.Generators` discovers compiled modules that implement `IDotCraftModule`. The base contract defines the module identifier, configuration checks, and dependency-injection registration. A module implements a capability facet when it contributes that capability:

| Facet | Contribution |
|---|---|
| **`IToolSourceModule`** | Tool sources exposed to the Agent runtime |
| **`IChannelServiceModule`** | A managed channel service |
| **`ISessionChannelModule`** | Session origins exposed by a channel |

`DotCraft.App` owns host selection and process composition. Its host factories use `IModuleHostComposition` to select the compiled modules included in each official host service graph.

## Session Core

Session Core defines the `Thread → Turn → Item` model. `ISessionService` is the central in-process API for thread lifecycle, input submission, events, approvals, and user-input requests.

CLI, ACP, Automations, and channel adapters use the same Session Core and persistent thread model. Transport boundaries project this model without changing its domain semantics. See [Unified Session Core](./session-core) for the model and lifecycle.

## AppServer

AppServer is the optional protocol and transport boundary over the host-owned Session Core. It projects `ISessionService` through JSON-RPC 2.0 over stdio and WebSocket, maps Core domain models to `DotCraft.Protocol` contracts, and manages connection-scoped resources.

Desktop, CLI, ACP, external channel adapters, and SDK clients can use this out-of-process boundary. See [AppServer Protocol](../protocols/appserver-protocol) and [AppServer mode](../lifecycle/appserver).

## Hub

Each user has one [Hub](../lifecycle/hub) on the machine. Hub starts or reuses one AppServer per workspace and maintains discovery information and locks under `~/.craft/hub/`. Desktop and CLI use Hub by default. Remote, CI, bot, and protocol-debugging scenarios can manage AppServer directly.

## Configuration in the official host

The official `DotCraft.App` host loads the following default configuration layers:

| Layer | Path | Purpose |
|---|---|---|
| **Global** | `~/.craft/config.json` | Provider credentials, endpoints, and personal preferences |
| **Workspace** | `<workspace>/.craft/config.json` | Model selection, entry switches, automations, and security policy |

Configuration policy belongs to the host. `DotCraft.App` merges the global and workspace layers, then supplies the effective `AppConfig` when it composes Runtime. Core and Runtime consume that effective configuration. Modules declare their config sections with `[ConfigSection("Key")]`, and the source generator includes those sections in the merged schema.

See [Configuration reference](../configuration) for fields and [Settings lifecycle](../lifecycle/settings-lifecycle) for when changes take effect.

## Related docs

- [Unified Session Core](./session-core)
- [Configuration reference](../configuration)
- [AppServer mode](../lifecycle/appserver)
- [Hub local coordination](../lifecycle/hub)
- [SDK overview](../sdks/)
