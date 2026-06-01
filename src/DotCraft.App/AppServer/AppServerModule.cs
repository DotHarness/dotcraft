using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Gateway;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.AppServer;

/// <summary>
/// AppServer module: exposes the DotCraft Session Wire Protocol over stdio (JSON-RPC 2.0 JSONL).
/// Enabled via the <c>app-server</c> subcommand or <c>AppServer.Enabled = true</c> in config.
/// </summary>
[DotCraftModule("app-server", Priority = 250, Description = "AppServer: JSON-RPC 2.0 Session Wire Protocol over stdio for multi-language client integration", CanBePrimaryHost = true)]
public sealed partial class AppServerModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) =>
        config.GetSection<AppServerConfig>("AppServer").Mode != AppServerMode.Disabled;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // When gateway submodule is disabled, AppServer still needs MessageRouter / ExternalChannelRegistry
        // for ChannelRunner (native channels, external channels, cron delivery, channel tool discovery).
        services.TryAddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>();
        services.TryAddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));
        services.TryAddSingleton<ExternalChannelRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginFunctionProvider, ExternalChannelToolProvider>());
        services.TryAddSingleton<AppBindingService>();
        services.TryAddSingleton<WireRuntimeAdditionalContextProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, WireDynamicToolProxy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, AppBindingRuntimeToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, WireRuntimeAdditionalContextSystemPromptProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, AppBindingThreadSystemPromptContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, ThreadPluginFunctionToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, AppBindingProtocolExtension>());
        services.TryAddSingleton<IChannelRuntimeToolProvider, CompositeChannelRuntimeToolProvider>();
        services.TryAddSingleton<IAppServerChannelRunnerFactory, DefaultAppServerChannelRunnerFactory>();
        services.TryAddSingleton<IAppServerAutomationRuntimeFactory, DefaultAppServerAutomationRuntimeFactory>();
        services.TryAddSingleton<IWorkspaceRuntimeAppServerFeatureFactory, AppServerWorkspaceRuntimeFeatureFactory>();
    }
}

/// <summary>
/// Host factory for AppServer mode.
/// </summary>
[HostFactory("app-server")]
public sealed class AppServerHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<AppServerHost>(serviceProvider);
    }
}
