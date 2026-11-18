using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Runtime;

internal sealed class UserConfigurationRefresher(IServiceProvider services) : ISessionRuntimeRefresher
{
    public Task RefreshAsync(CancellationToken cancellationToken) =>
        services.GetRequiredService<WorkspaceRuntime>() is { IsStarted: true } runtime
            ? runtime.Configuration.ReloadUserLayerAsync()
            : Task.CompletedTask;
}
