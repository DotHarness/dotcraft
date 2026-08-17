# Host DotCraft Harness

DotCraft Harness participates in the standard .NET Generic Host lifecycle. The application constructs the Host, starts it when dependencies are ready, and stops it during application shutdown.

## Build the Host

Register Harness while configuring the service collection. `AppConfig` must already represent the configuration that should be effective for this Host.

```csharp
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

AppConfig appConfig = LoadApplicationConfig();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = Directory.GetCurrentDirectory();
});

using var host = builder.Build();
```

Building the Host validates composition and creates the service provider. Runtime-owned workspace state is initialized when the Host starts.

## Start and stop

Start the Host before resolving services that depend on an initialized Runtime. Stop it before disposing application resources.

```csharp
await host.StartAsync(cancellationToken);

var sessions = host.Services.GetRequiredService<ISessionService>();
// Use the session service while the Host is running.

await host.StopAsync(cancellationToken);
```

For Console applications, `RunAsync` can own the complete wait-and-shutdown loop when no application-specific loop is required:

```csharp
await host.RunAsync(cancellationToken);
```

> [!CAUTION]
> Do not resolve Harness services from a temporary service provider during registration. Resolve them from `host.Services` after the final Host has been built.

## Integrate a desktop lifecycle

WPF and WinUI 3 applications can keep the Host as application state. Start it from the application startup path and stop it from the exit path.

```csharp
public sealed class AgentHost : IDisposable
{
    private readonly IHost _host;

    public AgentHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public Task StartAsync(CancellationToken ct = default) =>
        _host.StartAsync(ct);

    public Task StopAsync(CancellationToken ct = default) =>
        _host.StopAsync(ct);

    public void Dispose() => _host.Dispose();
}
```

Call these methods from the startup and exit hooks provided by the UI framework.

## Choose service lifetimes

| Service | Ownership guidance |
| --- | --- |
| `IHost` | One per application-owned Harness instance. |
| `WorkspaceRuntime` | Registered by Harness and owned by the Host. |
| `ISessionService` | Resolve from the running Host and reuse for the workspace. |
| Application UI services | Register in the same service collection when they need Harness dependencies. |

Create separate Hosts when an application needs independently configured Runtime instances. Each instance must own its workspace and data paths explicitly.

## Handle startup failure

Treat Host startup as an application initialization step. Surface invalid paths, missing dependencies, and configuration failures before accepting user work.

```csharp
try
{
    await host.StartAsync(cancellationToken);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "DotCraft Harness failed to start.");
    throw;
}
```

## Related docs

- [Harness overview](./)
- [Configuration and paths](./configuration-paths)
- [Threads and Turns](./threads-turns)
