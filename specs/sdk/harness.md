# DotCraft Harness Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Preview Ready |
| **Date** | 2026-08-15 |
| **Related Specs** | [Runtime Module Boundaries](../architecture/runtime-module-boundaries.md), [Session Core](../architecture/session-core.md), [.NET SDK](dotnet.md) |

Purpose: define the in-process .NET Agent Harness package, its hosting boundary, path ownership,
configuration contract, package composition, and release acceptance criteria.

## 1. Scope and goals

`DotCraft.Harness` is the in-process .NET integration package for applications that host DotCraft
inside their own Generic Host lifecycle. It supports console applications, desktop applications,
services, tests, and other .NET 10 hosts without requiring AppServer or an out-of-process client.

The package contains the Harness facade, Runtime, Core, Agents, and the built-in OpenAI and
Anthropic model providers. One NuGet package preserves the existing assembly boundaries.

## 2. Non-goals

The package does not include AppServer, Protocol, Automations, Teams, Dynamic Workflows,
OpenSandbox, or other optional product features. It does not provide Roslyn capability authoring,
runtime hot-plugging, `AssemblyLoadContext` isolation, or a formal public API compatibility
baseline.

`DotCraft.Harness` is distinct from `DotCraft.Sdk`. The Harness embeds the runtime in the current
process. The SDK connects to AppServer from another process.

## 3. Hosting and configuration contract

The aggregate registration entry point is:

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

`AddDotCraftHarness` accepts an effective `AppConfig`. It does not load configuration files or
choose configuration precedence. The embedding host owns configuration sources and may use files,
databases, environment variables, command-line arguments, or in-memory configuration.

Registration is side-effect free. Directory creation, persistent-store initialization, provider
startup, and background work happen through Generic Host startup. Host shutdown owns convergence
and disposal.

The official `DotCraft.App` follows the same registration path. It remains responsible for loading
global and workspace configuration, applying command-line and environment overrides, and passing
the resulting `AppConfig` to the Harness. It explicitly supplies its product user-data directory,
preserving global discovery and persistence without making that directory a Harness default.

## 4. Path model

Each Harness instance has three path concepts:

- `WorkspacePath` is the application workspace root and is required.
- `DataPath` is the workspace-owned DotCraft state directory. It defaults to `.craft` and must
  resolve to a direct child of `WorkspacePath`.
- `UserDataPath` is an optional user-owned state directory. It defaults to `null`.

These values are unresolved host input. Runtime normalizes and validates them once, then registers
one immutable `DotCraft.Workspaces.DotCraftPaths` instance. `DotCraftPaths` exposes the workspace
root, a required `Data` path root, and an optional `UserData` path root. It is a public read-only
contract, but only Runtime constructs it.

All path composition goes through those roots:

```csharp
paths.Data.Resolve("skills");
paths.UserData.ResolveOrNull("skills");
paths.UserData.Require("OpenAI authentication").Resolve("auth.json");
```

`Resolve` rejects rooted or escaping relative input. `ResolveOrNull` represents optional discovery
when user data is disabled. `Require` produces one consistent failure when an operation needs
user-owned persistence. Business components do not accept default-path switches and do not choose
operating-system profile paths.

`DataPath` accepts either a direct-child name such as `.craft` or `.agents`, or an absolute path to
that direct child. Relative nested paths, traversal, roots outside the workspace, and filesystem
links that resolve outside the workspace are rejected before Runtime becomes ready.

All workspace state reachable from Runtime is derived from `DataPath`, including sessions,
worktrees, recovery packages, plugins, Skills, Commands, Hooks, logs, tool spill files, and inline
visualizations. Model-visible descriptions use the configured data-directory name rather than
assuming `.craft`.

Runtime-scoped contexts carry the resolved data root separately from the current execution
workspace. Components that plan tools, project protocol state, or resolve thread-owned files use
that resolved root. They do not reconstruct it by appending `.craft` to a workspace path. This
distinction remains intact when a thread executes from a worktree or another scoped workspace.

Literal `.craft` selection is limited to host composition and host-side helpers that intentionally
implement the official DotCraft product defaults. Runtime, Core, providers, AppServer projections,
and optional feature modules consume resolved paths supplied by their host or runtime context.

User-level discovery and persistence are derived only from `UserDataPath`. When it is `null`,
user-level Skills, Commands, Hooks, plugins, marketplaces, provider state, and other DotCraft user
state are not implicitly read from or written to the operating-system user profile. Discovery
returns no user-level entries. Operations that require user-level persistence fail with a clear
error instead of selecting a hidden default path.

`AppConfig.Load` and `AppConfig.LoadWithGlobalFallback` remain optional host-side helpers. Calling
them is never part of Harness registration or Runtime startup.

## 5. Runtime registration

The Harness facade registers:

- `DotCraft.Runtime` and the single `WorkspaceRuntime` / `ISessionService` graph;
- the built-in OpenAI and Anthropic providers;
- the built-in Core configuration schema required by Runtime validation.

Provider registration is idempotent and remains composable with explicit lower-level registration.
Repeated provider registration retains an explicitly configured user-data path. Conflicting
explicit paths fail during service registration instead of making registration order observable.
The lower-level `AddDotCraftRuntime`, `AddOpenAIModelProvider`, and
`AddAnthropicModelProvider` entry points remain available for advanced hosts.

The official application registers optional product features separately. Adding AppServer to the
same host projects the same `ISessionService`; it does not create another Runtime.

## 6. Package contract

The package id is `DotCraft.Harness` and the target framework is `net10.0`. The package contains:

- `DotCraft.Harness.dll`
- `DotCraft.Runtime.dll`
- `DotCraft.Core.dll`
- `DotCraft.Agents.dll`
- `DotCraft.Agents.OpenAI.dll`
- `DotCraft.Agents.Anthropic.dll`

`DotCraft.Harness.csproj` is the only pack owner. Referenced DotCraft assemblies are collected
explicitly into `lib/net10.0`; they do not appear as dependencies on unpublished DotCraft packages.
Third-party managed and native assets remain NuGet dependencies rather than bundled copies.

The package includes repository metadata, Apache-2.0 license metadata, README, and XML
documentation. Release builds do not emit PDB files.

## 7. Consumer workflow

A consumer installs only `DotCraft.Harness`, prepares an `AppConfig`, configures paths, builds a
Generic Host, and starts it. It can resolve `WorkspaceRuntime` or `ISessionService` directly. A UI
application maps its own startup and shutdown events to Generic Host start, stop, and disposal;
the Harness does not reference a desktop UI framework.

Package acceptance uses a clean consumer restored from a local or public NuGet feed with no
ProjectReference back to the DotCraft repository. The consumer must create a real Thread and Turn
with a fake model provider and complete the full host lifecycle.

## 8. Failure and compatibility behavior

Invalid workspace or data paths fail startup with actionable path information. A missing optional
user data path does not fail startup. A requested user-level write without `UserDataPath` fails at
the operation boundary without writing elsewhere.

Harness versions follow the DotCraft product version line. Preview releases use a `-preview.N`
suffix and may change public APIs that have not been documented as contracts. A formal
compatibility baseline is deferred until the public surface is intentionally stabilized.

## 9. Release and validation contract

Before publishing, validation covers:

- full solution build and tests;
- path normalization, traversal, filesystem-link escape, and user-directory isolation;
- official application behavior with `.craft` and its explicit user data directory;
- aggregate provider, schema, Runtime, and Session registration;
- package contents, dependency closure, XML docs, license, absence of release PDB files, and hashes;
- clean local-feed consumers on Windows and Linux;
- English and Chinese documentation builds;
- Package ID availability immediately before publication.

Publication uses a dedicated Trusted Publishing workflow for `DotCraft.Harness`. It does not alter
the existing `DotCraft.Sdk` release workflow. After publication, a fresh project installs from
NuGet.org and repeats the minimal build and Runtime startup smoke test.

## 10. Acceptance checklist

- [x] A host can run DotCraft in process after installing only `DotCraft.Harness`.
- [x] Runtime workspace state is rooted at the validated configurable `DataPath`.
- [x] No DotCraft user-profile state is read or written unless `UserDataPath` is supplied.
- [x] The official application uses the aggregate registration path without behavior regression.
- [x] The package contains exactly the agreed DotCraft assemblies and no internal package dependencies.
- [x] Clean console and desktop-style consumers complete a real Session lifecycle.
- [x] English and Chinese developer documentation distinguishes Harness from the AppServer SDK.
- [x] Preview publishing and post-publish validation are automated.
