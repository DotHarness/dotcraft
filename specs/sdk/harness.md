# DotCraft Harness

| Field | Value |
|---|---|
| Version | 1.0.0 |
| Status | Living |
| Date | 2026-08-16 |

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

The aggregate registration entry point accepts an effective `AppConfig` and host-owned path
options:

```csharp
services.AddDotCraftHarness(
    appConfig,
    options =>
    {
        options.WorkspacePath = workspacePath;
        options.DataPath = ".agents";
        options.UserDataPath = appDataPath;
    });
```

The host owns configuration sources, precedence, and overrides. Harness registration does not load
configuration or start runtime work. The owning Generic Host controls initialization, readiness,
shutdown, and disposal.

`DotCraft.App` uses the same aggregate registration path and composes its optional product features
separately. When AppServer is enabled, direct API calls and protocol calls share the same
`ISessionService` graph.

## Path ownership

Each Harness instance has a required `WorkspacePath`, a workspace-owned `DataPath`, and an optional
user-owned `UserDataPath`. `DataPath` defaults to `.craft`; `UserDataPath` defaults to disabled.
Workspace data must remain within the workspace boundary.

Runtime resolves these inputs once and registers an immutable `DotCraftPaths`. Core, providers, and
optional features consume the resolved paths rather than reconstructing `.craft` paths or selecting
operating-system profile directories. When user data is disabled, Harness does not implicitly
discover or persist user-level DotCraft state.

## Runtime composition

The Harness facade registers one Runtime and Session graph together with the built-in model
providers and the Core configuration schema. Lower-level Runtime and provider registration APIs
remain available to hosts that need explicit composition.

The official application and embedded hosts may add AppServer or optional features to the same
service collection. Those additions project or extend the existing Runtime; they do not create a
second session kernel.

## Package contract

The package id is `DotCraft.Harness` and the target framework is `net10.0`. The package contains:

- `DotCraft.Harness.dll`
- `DotCraft.Runtime.dll`
- `DotCraft.Core.dll`
- `DotCraft.Agents.dll`
- `DotCraft.Agents.OpenAI.dll`
- `DotCraft.Agents.Anthropic.dll`

`DotCraft.Harness.csproj` is the single pack owner. The included DotCraft assemblies retain their
assembly boundaries and do not become dependencies on unpublished DotCraft packages. Third-party
managed and native assets remain ordinary NuGet dependencies.

A consumer installs only `DotCraft.Harness`, supplies its configuration and paths, and uses its own
Generic Host lifecycle. It does not require a project reference to the DotCraft repository or an
AppServer process.
