using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Context;
using DotCraft.InlineVisualizations;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.AppServer;

/// <summary>
/// Registers the optional AppServer protocol projection and client capabilities.
/// </summary>
public static class AppServerServiceCollectionExtensions
{
    private sealed class RegistrationMarker;

    /// <summary>
    /// Adds AppServer services without starting a transport or a workspace runtime.
    /// </summary>
    public static IServiceCollection AddDotCraftAppServer(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
            return services;

        services.AddSingleton<RegistrationMarker>();
        services.TryAddSingleton<ChannelToolRegistrationService>();
        services.TryAddSingleton<AppBindingService>();
        services.TryAddSingleton<AppBindingCoordinator>();
        services.TryAddSingleton<WireRuntimeAdditionalContextProvider>();
        services.TryAddSingleton<WireAcpExtensionProxy>();
        services.TryAddSingleton<IAcpExtensionProxy>(provider => provider.GetRequiredService<WireAcpExtensionProxy>());
        services.TryAddSingleton<WireNodeReplProxy>();
        services.TryAddSingleton<INodeReplProxy>(provider => provider.GetRequiredService<WireNodeReplProxy>());
        services.TryAddSingleton<InlineVisualizationRuntimeRegistry>();
        services.AddSingleton<IThreadSystemPromptContextProvider>(provider =>
            provider.GetRequiredService<InlineVisualizationRuntimeRegistry>());
        services.TryAddSingleton<WireDynamicToolProxy>();
        services.AddSingleton<IToolSource>(provider => provider.GetRequiredService<WireDynamicToolProxy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider,
            ProfileBuilderSystemPromptProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, AppBindingProtocolExtension>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolSource, AppBindingOfflineToolSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolSource, ManagedSocialToolSource>());
        return services;
    }
}
