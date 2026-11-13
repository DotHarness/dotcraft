# Install the DotCraft Harness package

Install `DotCraft.Harness` when an application needs to host and customize DotCraft in the same .NET process.

## Install

Add Harness and the .NET Generic Host implementation to the application project:

```bash
dotnet add package DotCraft.Harness
dotnet add package Microsoft.Extensions.Hosting
```

The application must target .NET 10 or later.

`DotCraft.Harness` brings the Runtime, Core, and the built-in OpenAI, Anthropic, OpenRouter, and remote model-service providers as package dependencies. Every DotCraft package shares one version, so keep all DotCraft package references on the same version.

## Choose a smaller set of packages

Each DotCraft assembly is also published as its own package. Reference lower-level packages instead of Harness when the application needs less:

| Need | Packages |
| --- | --- |
| Model providers and chat clients only | One or more of `DotCraft.Agents.OpenAI`, `DotCraft.Agents.Anthropic`, `DotCraft.Agents.OpenRouter` |
| The session kernel inside your own composition | `DotCraft.Core` and the providers you use |
| Generic Host lifecycle, configuration validation, and plugins | `DotCraft.Runtime` and the providers you use |

Optional features are separate packages that the host registers explicitly:

| Feature | Package | Registration |
| --- | --- | --- |
| Code Mode | `DotCraft.CodeMode` | `AddDotCraftCodeMode`; see [Let the model script its tool calls](./tools-approvals#let-the-model-script-its-tool-calls) |
| Agent-authored .NET extensions | `DotCraft.CSharp` | `AddDotCraftCSharp`; see [Build a .NET extension](../dotnet-extensions#create-with-dotcraft) |

## Register Harness

After installation, prepare an `AppConfig` and register Harness in the application service collection:

```csharp
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

Continue with [Hosting and lifecycle](./hosting-lifecycle) to build and start the Host.

## Integration scenarios

| Application | Typical integration |
| --- | --- |
| Console tool | Start one Host, run one or more Threads, then stop. |
| WPF or WinUI 3 | Bind Host start and stop to the application lifecycle. |
| Background service | Register application services and Harness in one Generic Host. |
| Integration test | Use temporary paths and a test-owned model provider configuration. |

In every host model, the application renders streaming events, collects approvals, and chooses how configuration is stored.

## Related docs

- [Hosting and lifecycle](./hosting-lifecycle) — building, starting, and stopping the Host after installation.
- [Configuration and paths](./configuration-paths) — the `AppConfig` and path options registration requires.
