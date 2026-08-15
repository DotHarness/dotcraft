using DotCraft.Channels;
using DotCraft.AppBinding;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Automations.DashBoard;
using DotCraft.Automations.Protocol;
using DotCraft.Automations;
using DotCraft.DynamicWorkflows;
using DotCraft.Teams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.AppServer;

/// <summary>
/// AppServer module: exposes the DotCraft Session Wire Protocol over stdio (JSON-RPC 2.0 JSONL).
/// Enabled via the <c>app-server</c> subcommand or <c>AppServer.Enabled = true</c> in config.
/// </summary>
[DotCraftModule("app-server", Priority = 250, Description = "AppServer: JSON-RPC 2.0 Session Wire Protocol over stdio for multi-language client integration", CanBePrimaryHost = true)]
public sealed partial class AppServerModule : ModuleBase, IModuleHostComposition
{
    /// <inheritdoc />
    public IReadOnlyList<IDotCraftModule> GetModules(ModuleRegistry registry, AppConfig config) =>
        registry.GetEnabledModules(config);

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) =>
        config.GetSection<AppServerConfig>("AppServer").Mode != AppServerMode.Disabled;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.AddDotCraftAppServer();
        if (context.Config.GetSection<AutomationsConfig>("Automations").Enabled)
        {
            services.TryAddSingleton<AutomationsRequestHandler>();
            services.TryAddSingleton<IAutomationsRequestHandler>(sp => sp.GetRequiredService<AutomationsRequestHandler>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOrchestratorSnapshotProvider, AutomationsDashboardSnapshotProvider>());
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, TeamsProtocolExtension>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, DynamicWorkflowProtocolExtension>());

        // AppServer owns channel routing, external channels, cron delivery, and channel tool discovery.
        services.TryAddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>();
        services.TryAddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));
        services.TryAddSingleton<ExternalChannelRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginToolSourceProvider, ExternalChannelToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, WireRuntimeAdditionalContextSystemPromptProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, DotCraft.CLI.TerminalVisualizationInstructionsPromptProvider>());
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
