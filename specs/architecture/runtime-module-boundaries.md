# Runtime module boundaries

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |

This specification defines the stable ownership, dependency, composition, and lifecycle rules for
the DotCraft runtime.

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
- `DotCraft.RemoteTools` owns the Remote Tool Host feature: remote-tool transport, registry, leases,
  and Host policy. It depends on Core, Runtime's provider-free plugin execution facade, the MCP
  ASP.NET transport, and the screen view contracts in Protocol. `DotCraft.App` owns its CLI and
  process composition. It must not cause Runtime to enable a provider, Session Core, or AppServer.
- An optional feature owns its behavior and depends on Core. Core never depends on a feature
  implementation.
- Dependency cycles are not permitted.

`DotCraft.Imaging` is a provider-independent image capability used by Agents and Core. It depends
only on the .NET BCL and owns codecs, pixel resampling, and image resource limits. Model input budgets
and content adaptation belong to Agents; platform capture and screenshot policy belong to Core.
Imaging does not depend on either consumer, Runtime, Protocol, or a platform UI framework.

## Core ownership

Core represents what DotCraft is. It contains the implementations and domain contracts for the
session model, agent orchestration, tools, context, memory, skills, plugins, MCP, LSP, security,
configuration, modules, workspace semantics, and platform capabilities several hosts share, such as
desktop capture.

Core contracts are added only when they belong to the product kernel or support multiple
independent consumers. They are capability-oriented, feature-neutral, and no broader than required.

Domain models use functional namespaces such as `DotCraft.Sessions`, `DotCraft.Tools`,
`DotCraft.Memory`, and `DotCraft.Workspaces`. Assemblies and namespaces named only for a technical
role, such as `.Abstractions`, are not introduced. A domain type does not use transport-oriented
names merely because AppServer projects it to a protocol contract.

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
desktop applications, services, command-line applications, and tests use the same Generic Host
contract.

## AppServer ownership

AppServer represents how clients access Core through the DotCraft protocol. Its public APIs use the
`DotCraft.AppServer` namespace. It owns:

- JSON-RPC, stdio, WebSocket, and ASP.NET transport behavior;
- request, response, notification, and connection handling;
- mapping between Core domain models and `DotCraft.Protocol` contracts;
- transport-specific feature contexts and client proxies;
- the optional `AddDotCraftAppServer` registration entry point.

AppServer delegates session work to the session service registered by the host. It does not create
a second session kernel. Closing a connection releases transport resources; the owning host decides
whether and when Runtime stops.

## Application and feature ownership

The optional Model Service hosts credential-backed provider HTTP access without Session Core or
workspace tools. Its reusable endpoints belong to `DotCraft.ModelService`; its client and transport
contract belong to Agents. `DotCraft.App` owns the standalone command and file configuration.
Embedded hosts provide their own authorization and storage.

The mobile relay (`dotcraft relay serve`) forwards phone tunnels for Hub without Session Core,
workspace tools, or providers. Like Hub, it belongs to `DotCraft.App`.

The official application owns CLI, ACP, AppServer, and Hub entry-point selection. Host factories,
process policy, web-channel pooling, shared web addresses, logging policy, and exit codes are
application responsibilities rather than Runtime contracts.

Modules contribute services and domain capabilities through compile-time discovery and dependency
injection; they do not select process hosts or mix protocol, channel, tool, and UI-projection
responsibilities in one contract. Tool providers contribute through the Core tool-source contract.
Protocol registration belongs to AppServer. Channel creation and web hosting belong to the
application or the feature that owns the channel. Configuration-schema generation and application
module registration each require an explicit opt-in; an embedded Harness can expose the schema
without composing product modules or host factories.

Features do not depend on one another for optional collaboration. The composition root connects
them through the narrowest applicable domain contract. Module names are identifiers, not control
flow: Core and Runtime do not switch behavior based on a module name string.

## Registration and lifecycle

All `AddDotCraft*` methods are registration-only. They do not create directories, deploy files,
open databases, bind ports, start processes, or run background loops.

The owning Generic Host controls the lifecycle:

1. Registration adds Runtime, selected providers, optional features, and optional AppServer.
2. Host startup validates provider-independent configuration and required host capabilities.
3. Runtime provisions the workspace, initializes persistent services, starts owned background
   services, and becomes ready.
4. Requests use the single Core service graph owned by that host.
5. Host shutdown stops new work, converges owned activity, and disposes resources in ownership
   order.

A startup failure never exposes a partially ready Runtime; resources acquired before failure are
released through the host startup-failure path.

Missing model providers, model preferences, and provider implementations are validated when an
operation first builds an agent or otherwise requires model access. A Runtime without a resolvable
model remains available for provider-independent work such as config-schema inspection, agent
profile, skill, and command discovery, thread indexing, and recovery export. Invalid
provider-independent workspace configuration and conflicting registrations still fail before the
Runtime accepts work. Transport failures follow the AppServer protocol contract and do not expose
internal exception types as wire contracts.

## Embedded-host contract

An embedding application registers Runtime, explicitly selected providers and features, and optional
AppServer through the .NET Generic Host. It supplies workspace, data, and user-data roots and uses
`WorkspaceRuntime` to access kernel capabilities. Session capabilities do not require AppServer.
When AppServer is selected, direct and protocol calls share the same Core instances.

## Ownership rules

A responsibility has one owner. Its implementation, resources, configuration binding, and behavior
tests live together; host wiring tests live with the composition root.

Compatibility shims, type forwarding, module-name branches, and friend-assembly access are not
substitutes for a clear dependency boundary. Production internals are not broadened for tests.

## Test boundaries

Tests follow the production responsibility they verify. Test projects do not reference other test
projects. Substantial support shared by multiple owners may live in a narrowly scoped test-support
assembly, but it must not become a general utility layer. Tests assert public behavior, state,
persisted data, wire payloads, and lifecycle results rather than repository layout or source text.
