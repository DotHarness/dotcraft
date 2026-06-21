using DotCraft.Abstractions;
using DotCraft.AppServer;
using DotCraft.AppBinding;
using DotCraft.Automations;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Gateway;

/// <summary>
/// Gateway module that enables running multiple channel modules concurrently (WeCom, automations, external channels).
/// Priority: 100 (highest) — overrides all single-channel modules when enabled.
/// </summary>
[DotCraftModule("gateway", Priority = 100, Description = "Gateway mode: runs all enabled channel modules concurrently", CanBePrimaryHost = true)]
public sealed partial class GatewayModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => HasGatewayChannelsEnabled(config);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register MessageRouter as a singleton for cross-channel delivery (TryAdd: app-server may register first)
        services.TryAddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>();
        services.TryAddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));

        // Register ExternalChannelRegistry as a singleton (all external hosts + WebSocket routing)
        services.TryAddSingleton<ExternalChannelRegistry>();
        services.TryAddSingleton<ChannelToolRegistrationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadPluginFunctionProvider, ExternalChannelToolProvider>());
        services.AddSingleton<IManagedAppBindingRuntime>(sp => new SocialChannelAppBindingRuntime(
            "qq",
            "QQ",
            "Continue this thread in a QQ group or private chat.",
            sp.GetRequiredService<IChannelRuntimeRegistry>(),
            sp.GetRequiredService<ChannelToolRegistrationService>()));
        services.AddSingleton<IManagedAppBindingRuntime>(sp => new SocialChannelAppBindingRuntime(
            "wecom",
            "WeCom",
            "Continue this thread in a WeCom conversation.",
            sp.GetRequiredService<IChannelRuntimeRegistry>(),
            sp.GetRequiredService<ChannelToolRegistrationService>()));
        services.AddSingleton<IManagedAppBindingRuntime>(sp => new SocialChannelAppBindingRuntime(
            "telegram",
            "Telegram",
            "Continue this thread in a Telegram chat.",
            sp.GetRequiredService<IChannelRuntimeRegistry>(),
            sp.GetRequiredService<ChannelToolRegistrationService>()));
        services.AddSingleton<IManagedAppBindingRuntime>(sp => new SocialChannelAppBindingRuntime(
            "feishu",
            "Feishu",
            "Continue this thread in a Feishu chat.",
            sp.GetRequiredService<IChannelRuntimeRegistry>(),
            sp.GetRequiredService<ChannelToolRegistrationService>()));
        services.AddSingleton<IManagedAppBindingRuntime>(sp => new SocialChannelAppBindingRuntime(
            "weixin",
            "Weixin",
            "Continue this thread in a Weixin conversation.",
            sp.GetRequiredService<IChannelRuntimeRegistry>(),
            sp.GetRequiredService<ChannelToolRegistrationService>()));
        services.TryAddSingleton<AppBindingService>();
        services.TryAddSingleton<WireRuntimeAdditionalContextProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, WireDynamicToolProxy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, AppBindingRuntimeToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, WireRuntimeAdditionalContextSystemPromptProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, AppBindingThreadSystemPromptContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, DotCraft.Agents.ProfileBuilderSystemPromptProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadRuntimeToolProvider, ThreadPluginFunctionToolProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, AppBindingProtocolExtension>());
        services.TryAddSingleton<IChannelRuntimeToolProvider, CompositeChannelRuntimeToolProvider>();
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        if (!HasGatewayChannelsEnabled(config))
            errors.Add("Gateway mode is enabled but no channel modules are enabled.");
        return errors;
    }

    private static bool HasGatewayChannelsEnabled(AppConfig config)
        => config.IsSectionEnabled("WeComBot")
           || config.GetSection<AutomationsConfig>("Automations").Enabled
           || ExternalChannelManager.HasEnabledChannels(config);
}

/// <summary>
/// Host factory for Gateway mode.
/// </summary>
[HostFactory("gateway")]
public sealed class GatewayHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider sp, ModuleContext context)
    {
        var registry = sp.GetRequiredService<ModuleRegistry>();
        var config = sp.GetRequiredService<AppConfig>();

        // Collect channel services from all enabled non-gateway modules
        var channelServices = registry
            .GetEnabledModules(config)
            .Where(m => m.Name != "gateway")
            .Select(m => m.CreateChannelService(sp))
            .OfType<IChannelService>()
            .ToList();

        var router = sp.GetRequiredService<MessageRouter>();
        var externalChannelRegistry = sp.GetRequiredService<ExternalChannelRegistry>();

        return new GatewayHost(
            sp,
            config,
            context.Paths,
            sp.GetRequiredService<Skills.SkillsLoader>(),
            sp.GetRequiredService<Cron.CronService>(),
            channelServices,
            router,
            registry,
            externalChannelRegistry);
    }
}
