# Harness hosting

`AddDotCraftHarness` does **not** read configuration files, environment variables, or the user profile. The application loads and merges its own `AppConfig` and hands one effective instance to Harness. Start every Harness answer with that, because it is the failure people arrive with: a `~/.craft/config.json` that the embedded runtime never saw.

Harness embeds the agent runtime in a .NET process. It is not a client — it does not speak the AppServer protocol. An application that wants to talk to a workspace another process owns needs the client SDK instead; see `client-sdk.md`.

## Install

```bash
dotnet add package DotCraft.Harness
dotnet add package Microsoft.Extensions.Hosting
```

`DotCraft.Harness` is the only DotCraft package to reference; it ships the Runtime, Core, and built-in model provider assemblies. The application targets the framework the package declares.

## Register

```csharp
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

AppConfig appConfig = LoadApplicationConfig();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});

using var host = builder.Build();
await host.StartAsync();
```

The signature is `AddDotCraftHarness(this IServiceCollection services, AppConfig appConfig, Action<DotCraftHarnessOptions> configure)`. All three arguments are required and null-checked.

## Path options

| Option | Required | Default | Owns |
| --- | --- | --- | --- |
| `WorkspacePath` | Yes | none | The workspace sessions and tools operate on |
| `DataPath` | No | `.craft` | Workspace-local sessions, traces, tool results, runtime state |
| `UserDataPath` | No | `null` | User-level skills, commands, hooks, plugins, marketplaces, provider auth |

`DataPath` accepts a direct child name or that child's absolute path. Harness rejects nested relative paths, traversal outside the workspace, and existing filesystem links that escape it. Paths resolve into one validated `DotCraftPaths` during registration, before `Build()`.

## What registration gives you

`AddDotCraftHarness` registers Runtime, the built-in configuration schema, the OpenAI and Anthropic model providers, one validated `DotCraftPaths`, and one host-owned `ISessionService`. Runtime joins the Host lifecycle as an `IHostedService` and initializes when the Host starts, so resolve services after `StartAsync`.

Depend on `ISessionService` and `DotCraftPaths` from domain code. Keep composition at the application boundary; do not pass `IHost` around.

## Host shapes

| Application | Integration |
| --- | --- |
| Console tool | One Host, one or more threads, then stop |
| WPF or WinUI 3 | Bind Host start and stop to the application lifecycle |
| Background service | Application services and Harness in one Generic Host |
| Integration test | Temporary paths and a test-owned model provider configuration |

In every shape the application renders streaming events, collects approvals, and decides how configuration is stored.

## Live sources

- Pages under `/developing/harness/`: `nuget-package`, `hosting-lifecycle`, `configuration-paths`, `threads-turns`, `tools-approvals`, `model-providers`.
- `src/DotCraft.Harness/DotCraftHarnessServiceCollectionExtensions.cs` and `DotCraftHarnessOptions.cs` — the two files that define the whole public surface.
- `specs/sdk/harness.md` — design rationale.
- `tests/DotCraft.Harness.Consumer/` — a minimal consumer project; `src/DotCraft.TraceViewer/` is a WinUI 3 integration sample, not a supported product.
