using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.RemoteTools;

public static class RemoteExecutionServiceCollectionExtensions
{
    /// <summary>Registers a process-owned remote execution client without product discovery or Thread routing.</summary>
    public static IServiceCollection AddDotCraftRemoteExecution(this IServiceCollection services)
    {
        services.TryAddSingleton<RemoteExecutionClient>();
        return services;
    }
}
