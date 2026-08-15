using DotCraft.Modules;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Runtime;

/// <summary>Connects <see cref="WorkspaceRuntime"/> to the .NET Generic Host lifecycle.</summary>
public sealed class WorkspaceRuntimeHostedService(
    WorkspaceRuntime runtime,
    ModuleRegistry moduleRegistry) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        runtime.StartAsync(moduleRegistry, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        runtime.StopAsync(cancellationToken);
}
