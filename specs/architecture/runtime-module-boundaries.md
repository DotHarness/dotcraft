# Runtime module boundaries

| Field | Value |
|---|---|
| Version | 1.0.0 |
| Status | Living |
| Date | 2026-09-01 |

This specification defines the stable ownership, dependency, composition, and lifecycle rules for
the DotCraft runtime. It describes the finished architecture rather than the repository migration
used to reach it.

## Assembly model

Runtime dependencies point toward the product kernel:

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

Embedded application
  |-- DotCraft.Runtime
  |     `-- DotCraft.Core
  `-- selected providers and features
```

- `DotCraft.Core` owns the product kernel and feature-neutral domain contracts. It does not depend
  on AppServer, Protocol, ASP.NET, the official application, or an optional feature.
- `DotCraft.Runtime` owns reusable dependency injection and lifecycle composition. It depends on
  Core, but not on AppServer, Protocol, the official application, or an optional feature.
- `DotCraft.AppServer` owns the wire transport and protocol projection. It depends on Core and
  Protocol, but does not require Runtime as its host.
- `DotCraft.App` is the official composition root. It selects entry points, providers, optional
  features, logging, process policy, and exit behavior.
- `DotCraft.RemoteTools` owns the Remote Tool Host feature and depends on Core plus the MCP ASP.NET
  transport. A Remote Tool Host is an application-selected provider-free host. Its feature implementation owns
  remote-tool transport, registry, leases, and Host policy; `DotCraft.App` owns CLI and process
  composition. It MUST NOT cause Runtime to enable a provider, Session Core, or AppServer implicitly.
- An optional feature owns its behavior and depends on Core. Core never depends on a feature
  implementation.
- Dependency cycles are not permitted. A project reference must not be added merely to make a move
  compile.

## Core ownership

Core represents what DotCraft is. It contains the implementations and domain contracts for the
session model, agent orchestration, tools, context, memory, skills, plugins, MCP, LSP, security,
configuration, modules, and workspace semantics.

Core contracts are added only when they belong to the product kernel or support multiple
independent consumers. They are capability-oriented, feature-neutral, and no broader than required.
Concrete orchestration details and persistence internals are not exposed to complete an extraction.

Domain models use functional namespaces such as `DotCraft.Sessions`, `DotCraft.Tools`,
`DotCraft.Memory`, and `DotCraft.Workspaces`. Assemblies and namespaces named only for a technical
role, such as `.Abstractions`, are not introduced. A domain type does not use transport-oriented
names such as `Wire` merely because AppServer projects it to a protocol contract.

Core does not own JSON-RPC handlers, protocol mappers, transport DTOs, client proxies, ASP.NET
hosting, process entry-point selection, or web-channel pooling.

## Runtime ownership

Runtime represents how a host creates, starts, and stops DotCraft. Its public APIs use the
`DotCraft.Runtime` namespace. It owns:

- the `AddDotCraftRuntime` dependency-injection entry point and strongly typed runtime options;
- `WorkspaceRuntime`, exposing kernel capabilities without transport or application-host types;
- configuration validation, workspace provisioning, initialization, readiness, shutdown, and
  disposal coordination;
- integration with the .NET Generic Host lifecycle.

Runtime does not automatically enable a model provider, AppServer, or an optional feature. The
host selects those capabilities explicitly. Runtime does not reference a desktop UI framework;
WPF, WinUI, services, command-line applications, and tests use the same Generic Host contract.

## AppServer ownership

AppServer represents how clients access Core through the DotCraft protocol. Its public APIs use the
`DotCraft.AppServer` namespace. It owns:

- JSON-RPC, stdio, WebSocket, and ASP.NET transport behavior;
- request, response, notification, and connection handling;
- mapping between Core domain models and `DotCraft.Protocol` contracts;
- transport-specific feature contexts and client proxies;
- the optional `AddDotCraftAppServer` registration entry point.

AppServer delegates session work to the `ISessionService` registered by the host. It does not create
a second session kernel. Closing a connection releases transport resources; the owning host decides
whether and when Runtime stops.

## Application and feature ownership

The official application owns CLI, ACP, AppServer, and Hub entry-point selection. Host factories,
process policy, web-channel pooling, shared web addresses, logging policy, and exit codes are
application responsibilities rather than Runtime contracts.

Compile-time module discovery remains supported. Modules contribute services and domain
capabilities through dependency injection; they do not select process hosts or mix protocol,
channel, tool, and UI-projection responsibilities in one contract. Tool providers contribute
through the Core tool-source contract. Protocol registration belongs to AppServer. Channel creation
and web hosting belong to the application or the feature that owns the channel.

Features do not depend on one another for optional collaboration. The composition root connects
them through the narrowest applicable domain contract. Module names are identifiers, not control
flow: Core and Runtime do not switch behavior based on a module name string.

## Registration and lifecycle

All `AddDotCraft*` methods are registration-only. They do not create directories, deploy files,
open databases, bind ports, start processes, or run background loops.

The owning Generic Host controls the lifecycle:

1. Registration adds Runtime, selected providers, optional features, and optional AppServer.
2. Host startup validates configuration and required capabilities.
3. Runtime provisions the workspace, initializes persistent services, starts owned background
   services, and becomes ready.
4. Requests use the single Core service graph owned by that host.
5. Host shutdown stops new work, converges owned activity, and disposes resources in ownership
   order.

A startup failure never exposes a partially ready Runtime. Resources acquired before failure are
released through the host startup-failure path. Stop and disposal are safe when invoked through the
normal Generic Host lifecycle.

Missing providers, invalid workspace configuration, and conflicting registrations fail before the
Runtime accepts work. Transport failures follow the AppServer protocol contract and do not expose
internal exception types as wire contracts.

## Embedded-host contract

An application can embed DotCraft without referencing `DotCraft.App` or enabling AppServer:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
{
    Config = config,
    WorkspacePath = workspacePath,
    DataPath = ".agents",
    UserDataPath = applicationDataPath
});

builder.Services.AddOpenAIModelProvider();

using var host = builder.Build();
await host.StartAsync();

var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();
ISessionService sessions = runtime.Sessions;
```

The host explicitly selects providers and features. Session capabilities do not require AppServer.
When AppServer is selected, direct API calls and protocol calls share the same Core instances.

## Ownership and extraction rules

A responsibility has one owner. Its implementation, resources, configuration binding, and behavior
tests move together. Host wiring tests remain with the composition root.

Do not use compatibility shims, type forwarding, module-name branches, or friend-assembly access as
substitutes for a clear dependency boundary. Production internals are not broadened for tests.

Apply a boundary change in this order:

1. Map the implementation, resources, consumers, tests, and references.
2. Define the intended owner and dependency direction.
3. Move the responsibility, resources, and tests as one coherent change.
4. Wire it through the composition root with the smallest required contract.
5. Remove the old implementation and references.
6. Validate the owner, affected consumers, dependency graph, and full solution.

## Test boundaries

Tests follow the production responsibility they verify. Kernel tests remain with Core, AppServer
behavior tests remain with AppServer, Runtime lifecycle tests remain with Runtime, feature behavior
tests remain with the feature, and host wiring tests remain with the composition root.

Test projects do not reference other test projects. Substantial support shared by multiple owners
may live in a narrowly scoped test-support assembly, but it must not become a general utility layer.
Tests assert public behavior, state, persisted data, wire payloads, and lifecycle results rather than
repository layout or source text.

A boundary change is complete when ownership is unambiguous, dependencies remain one-way, existing
observable behavior is preserved, and the former owner contains no residual implementation.
