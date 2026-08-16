# Install the DotCraft Harness package

Install `DotCraft.Harness` when an application needs to host and customize DotCraft in the same .NET process.

## Install

Add Harness and the .NET Generic Host implementation to the application project:

```bash
dotnet add package DotCraft.Harness
dotnet add package Microsoft.Extensions.Hosting
```

The application must target .NET 10 or a compatible target framework.

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

- [Harness overview](./)
- [Hosting and lifecycle](./hosting-lifecycle)
- [Configuration and paths](./configuration-paths)
