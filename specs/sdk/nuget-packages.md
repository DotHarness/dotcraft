# DotCraft NuGet Packages

| Field | Value |
|---|---|
| Version | 0.11.1 |
| Status | Living |
| Date | 2026-11-13 |

This specification defines how DotCraft's .NET assemblies are published as NuGet packages and how
applications choose the granularity they depend on. Assembly ownership and dependency direction
remain defined by [Runtime Module Boundaries](../architecture/runtime-module-boundaries.md); the
aggregate in-process package is defined by [DotCraft Harness](harness.md).

## Package model

Each published DotCraft assembly is its own package, and the package id equals the assembly name.
A package never embeds another DotCraft assembly. Project references between published assemblies
become package dependencies, so a consumer restores exactly the closure of the packages it selects.

All DotCraft packages, including `DotCraft.Sdk`, share one version and are released together.
Dependencies between DotCraft packages use the exact version range `[x.y.z]`; mixing versions is not
supported. Published packages do not grant one another friend-assembly access, so every type a
package consumes from another DotCraft package is public API of that package.

A third-party dependency is declared only by the package whose code uses it. Native assets follow
their owning package through ordinary NuGet runtime asset selection.

## Package catalog

| Package | Role | Notable third-party dependencies |
|---|---|---|
| `DotCraft.Imaging` | Image codecs and resampling | None |
| `DotCraft.Agents` | Provider-neutral agent foundation and model-service client contract | Microsoft.Extensions.AI |
| `DotCraft.Agents.OpenAI` | OpenAI-compatible model provider | OpenAI |
| `DotCraft.Agents.Anthropic` | Anthropic model provider | Anthropic |
| `DotCraft.Agents.OpenRouter` | OpenRouter model provider | Through the OpenAI and Anthropic providers |
| `DotCraft.Agents.Remote` | Remote model-service transport | None |
| `DotCraft.Core` | Product kernel: sessions, tools, context, plugins, MCP, security | SQLite, ModelContextProtocol, PowerShell SDK |
| `DotCraft.Runtime` | Dependency injection, Generic Host lifecycle, plugin and .NET extension runtime | Microsoft.Extensions.Hosting.Abstractions |
| `DotCraft.CSharp` | Agent authoring of .NET extensions: C# inspection and compilation | Roslyn |
| `DotCraft.Scripting` | JavaScript engine for script features | Jint |
| `DotCraft.CodeMode` | Code Mode | Through `DotCraft.Scripting` |
| `DotCraft.SessionImport` | Import of external agent sessions | Tomlyn |
| `DotCraft.Protocol` | AppServer wire contracts | None |
| `DotCraft.RemoteTools` | Remote Tool Host feature | Through `DotCraft.Protocol` |
| `DotCraft.Harness` | Aggregate in-process host facade | Through its dependencies |
| `DotCraft.ModelService` | Embeddable model HTTP service | ASP.NET Core shared framework |
| `DotCraft.Sdk` | Out-of-process AppServer client | Defined by [.NET SDK](dotnet.md) |

`DotCraft.Core` carries the `DotCraft.Generators` source generator as an analyzer, so a project that
references Core alone compiles strongly typed tools without a separate generator reference.
`DotCraft.CSharp` also carries the same generator binary as a runtime asset for in-process
compilation; both copies come from one build output.

## Composition tiers

Applications select the smallest tier that covers their use case. These tiers are guidance, not
additional packages.

| Need | Packages |
|---|---|
| Model providers and chat clients only | One or more `DotCraft.Agents.<Provider>` |
| Session kernel inside an application-owned host | `DotCraft.Core` and selected providers |
| Generic Host lifecycle, configuration validation, and plugins | `DotCraft.Runtime` and selected providers |
| The default in-process DotCraft | `DotCraft.Harness` |
| Agent-authored .NET extensions | Add `DotCraft.CSharp` to any Runtime-based tier |
| Optional features | Add `DotCraft.CodeMode`, `DotCraft.SessionImport`, or `DotCraft.RemoteTools` |

An optional feature is registered explicitly by the host through its own `AddDotCraft*` entry point.
Installing a package never enables a capability by itself.

## Dependency budget

The following dependencies are excluded from the listed closures. Package validation fails if a
release violates them.

| Closure | Excluded |
|---|---|
| `DotCraft.Core`, `DotCraft.Runtime`, `DotCraft.Harness` | Roslyn (`Microsoft.CodeAnalysis.*`), Jint, Tomlyn, `DotCraft.Protocol` |
| `DotCraft.Agents` and its provider packages | `DotCraft.Core` and its dependencies other than `DotCraft.Agents` and `DotCraft.Imaging` |

## Naming

A package is named for the capability it owns. A package that exists to isolate one third-party
dependency is still named for its capability rather than the dependency. Package names do not use
technical-role suffixes such as `.Abstractions` or `.Common`.

## Future directions

The following splits require inverting Session Core dependencies first and are not part of the
current package catalog:

- Shell execution, its safety kernel, and background terminals as an optional module, which would
  make the PowerShell SDK an optional dependency.
- MCP client support as a `DotCraft.Mcp` package.
- Separating the Session kernel from the built-in tool set.
