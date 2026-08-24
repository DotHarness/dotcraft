using DotCraft.Contributions;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Runtime;

/// <summary>Projects application services to plugin code without exposing Host lifecycle controls.</summary>
internal sealed class PluginVisibleServiceProvider(IServiceProvider services) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return IsControlPlane(serviceType) ? null : services.GetService(serviceType);
    }

    private static bool IsControlPlane(Type serviceType)
    {
        if (serviceType.IsGenericType
            && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return IsControlPlane(serviceType.GetGenericArguments()[0]);
        }

        return typeof(IServiceProvider).IsAssignableFrom(serviceType)
               || typeof(IContributionView).IsAssignableFrom(serviceType)
               || typeof(IServiceScopeFactory).IsAssignableFrom(serviceType)
               || typeof(IServiceScope).IsAssignableFrom(serviceType)
               || typeof(IHost).IsAssignableFrom(serviceType)
               || typeof(IHostedService).IsAssignableFrom(serviceType)
               || typeof(IHostApplicationLifetime).IsAssignableFrom(serviceType)
               || typeof(IHostLifetime).IsAssignableFrom(serviceType)
               || typeof(IPluginDotnetRuntimeCoordinator).IsAssignableFrom(serviceType)
               || serviceType == typeof(WorkspaceRuntime)
               || serviceType == typeof(IWorkspaceRuntimeFactory);
    }
}
