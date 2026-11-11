using DotCraft.Channels;
using DotCraft.Context;
using DotCraft.Context.WorldState;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.ExternalChannel.Messaging;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Automations.DashBoard;
using DotCraft.Automations.Protocol;
using DotCraft.Automations;
using DotCraft.DynamicWorkflows;
using DotCraft.SessionImport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.AppServer;

[DotCraftModule("app-server", Priority = 250, Description = "AppServer: JSON-RPC 2.0 Session Wire Protocol over stdio for multi-language client integration", CanBePrimaryHost = true)]
public sealed partial class AppServerModule : ModuleBase, IModuleHostComposition
{
    public IReadOnlyList<IDotCraftModule> GetModules(ModuleRegistry registry, AppConfig config) =>
        registry.GetEnabledModules(config);

    public override bool IsEnabled(AppConfig config) =>
        config.GetSection<AppServerConfig>("AppServer").Mode != AppServerMode.Disabled;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.AddDotCraftAppServer();
        if (context.Config.GetSection<AutomationsConfig>("Automations").Enabled)
        {
            services.TryAddSingleton<AutomationsRequestHandler>();
            services.TryAddSingleton<IAutomationsRequestHandler>(sp => sp.GetRequiredService<AutomationsRequestHandler>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOrchestratorSnapshotProvider, AutomationsDashboardSnapshotProvider>());
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, DynamicWorkflowProtocolExtension>());
        if (context.Config.GetSection<SessionImportConfig>("AgentImport").Enabled)
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, SessionImportProtocolExtension>());

        services.TryAddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>();
        services.TryAddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));
        services.TryAddSingleton<ExternalChannelRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginToolSourceProvider, ExternalChannelToolProvider>());
        services.TryAddSingleton<ChatBindingResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginToolSourceProvider, SendChannelMessageToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginToolSourceProvider, ReadChannelHistoryToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorldStateSection, ChannelChatSection>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorldStateSection, ClientSurfaceSection>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, WireRuntimeAdditionalContextSystemPromptProvider>());
        services.TryAddSingleton<IAppServerChannelRunnerFactory, DefaultAppServerChannelRunnerFactory>();
        services.TryAddSingleton<IAppServerAutomationRuntimeFactory, DefaultAppServerAutomationRuntimeFactory>();
        services.TryAddSingleton<IWorkspaceRuntimeAppServerFeatureFactory, AppServerWorkspaceRuntimeFeatureFactory>();
    }
}

[HostFactory("app-server")]
public sealed class AppServerHostFactory : IHostFactory
{
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<AppServerHost>(serviceProvider);
    }
}
