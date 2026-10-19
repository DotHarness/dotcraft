# DotCraft Harness

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |

This specification defines the stable boundary of the in-process .NET Harness package. Runtime
ownership and lifecycle rules remain defined by
[Runtime Module Boundaries](../architecture/runtime-module-boundaries.md), and session behavior
remains defined by [Session Core](../architecture/session-core.md).

## Role and boundary

`DotCraft.Harness` is the in-process integration package for .NET applications that host DotCraft
inside their own Generic Host. It is distinct from `DotCraft.Sdk`, which connects to an AppServer
host from another process.

The Harness composes its facade, Runtime, Core, Agents, and the built-in OpenAI and Anthropic model
providers. AppServer and optional product features remain separate capabilities selected by the
embedding host.

## Host contract

`services.AddDotCraftHarness(appConfig, options => ...)` is the aggregate registration entry point.
It accepts an effective `AppConfig` and host-owned path options (`WorkspacePath`, `DataPath`,
`UserDataPath`).

The host owns configuration sources, precedence, and overrides. Harness registration does not load
configuration or start runtime work. The owning Generic Host controls initialization, readiness,
shutdown, and disposal.

The official application uses the same registration path and composes its optional product features
separately. When AppServer is enabled, direct API calls and protocol calls share the same session
service graph.

## Path ownership

Each Harness instance has a required `WorkspacePath`, a workspace-owned `DataPath`, and an optional
user-owned `UserDataPath`. `DataPath` defaults to `.craft`; `UserDataPath` defaults to disabled.
Workspace data must remain within the workspace boundary.

Runtime resolves these inputs once into an immutable `DotCraftPaths`. Core, providers, and optional
features consume the resolved paths rather than reconstructing `.craft` paths or selecting
operating-system profile directories. When user data is disabled, Harness does not discover or
persist user-level DotCraft state.

`DotCraftPaths.WorkspaceTempPath` resolves to `<DataPath>/tmp` for general workspace-scoped temporary
files. Operating-system temporary files and purpose-specific staging directories keep their own
locations.

## Runtime composition

The Harness facade registers one Runtime and Session graph together with the built-in model
providers and the Core configuration schema. Lower-level Runtime and provider registration APIs
remain available to hosts that need explicit composition. Hosts may add AppServer or optional
features to the same service collection; those additions project or extend the existing Runtime and
never create a second session kernel.

Harness supports one optional remote model-service connection for the entire runtime. It selects the
remote HTTP transport for the built-in providers, preserving their protocol and history
capabilities, and does not register local credential storage or OAuth refresh for remote providers.
The client is packaged with Harness; the optional server is packaged as `DotCraft.ModelService`.

The default Harness refreshes the model-service catalog at startup and every 30 seconds. Hosts that
deliver catalogs through revisions set `RefreshModelServiceCatalog = false`, supply
`InitialModelServiceCatalog`, and apply later snapshots with `RemoteModelConfiguration.Apply`.

## Package contract

The package id is `DotCraft.Harness` and the target framework is `net10.0`. The package contains
`DotCraft.Harness.dll`, `DotCraft.Runtime.dll`, `DotCraft.Core.dll`, `DotCraft.Agents.dll`,
`DotCraft.Agents.OpenAI.dll`, and `DotCraft.Agents.Anthropic.dll`.

The included DotCraft assemblies keep their assembly boundaries and do not become dependencies on
unpublished DotCraft packages. Third-party managed and native assets are ordinary NuGet
dependencies, and the dependency graph covers the runtime requirements of every bundled assembly.

A consumer installs only `DotCraft.Harness`, supplies its configuration and paths, and uses its own
Generic Host lifecycle. It needs no project reference to the DotCraft repository and no AppServer
process.
