using System.Diagnostics;
using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Context.WorldState;
using DotCraft.Contributions;
using DotCraft.Hooks;
using DotCraft.Visualizations;
using DotCraft.Tracing;
using DotCraft.Logging;
using DotCraft.Sessions;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Persistence;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Runtime;

public static class ServiceRegistration
{
    public static IServiceCollection AddDotCraftRuntime(
        this IServiceCollection services,
        DotCraftRuntimeOptions options) =>
        AddDotCraftRuntime(services, options, DotCraftPathResolver.Resolve(options));

    internal static IServiceCollection AddDotCraftRuntime(
        this IServiceCollection services,
        DotCraftRuntimeOptions options,
        DotCraftPaths paths)
    {
        var config = options.Config;
        var workspacePath = paths.WorkspacePath;
        var dataPath = paths.Data.RootPath;
        // Provider selection belongs to the application composition root. AddLogging still
        // supplies a no-op-capable ILoggerFactory for embedders that do not configure one.
        services.AddLogging();
        services.AddSingleton(_ =>
        {
            var loggingCfg = config.Logging;
            var streamCfg = config.StreamDebug;
            var logsDir = paths.Data.Resolve(loggingCfg.Directory);
            return SessionStreamDebugLogger.Create(logsDir, new SessionStreamDebugLoggerOptions
            {
                Enabled = streamCfg.Enabled,
                ThreadIdFilter = streamCfg.ThreadIdFilter,
                TurnIdFilter = streamCfg.TurnIdFilter,
                IncludeFullText = streamCfg.IncludeFullText
            });
        });
        services.AddSingleton(config);
        services.TryAddSingleton<ContributionRegistry>();
        services.TryAddSingleton<IContributionView>(sp => sp.GetRequiredService<ContributionRegistry>());
        services.TryAddSingleton<ModuleRegistry>();
        services.AddSingleton<IAppConfigMonitor, AppConfigMonitor>();
        services.AddSingleton<ToolInvocationRecorderRouter>();
        services.AddSingleton(_ => new CommonToolApprovalEvaluator(paths.UserData.RootPath));
        services.AddSingleton<McpAppTransientContextStore>();
        services.AddSingleton<ThreadToolDispatchPolicyRegistry>();
        // The dispatch stages below are the fallback for the window before the workspace starts and
        // registers them into the contribution registry.
        services.AddSingleton(_ => new DefaultToolResultNormalizer(
            config.Tools.ResultLimits.MaxToolResultChars,
            workspacePath,
            dataPath,
            config.Tools.ResultLimits.SpillPreviewLines));
        services.AddSingleton<IToolDispatcher>(sp => new ToolDispatcher(
            policyEvaluator: sp.GetRequiredService<ThreadToolDispatchPolicyRegistry>(),
            hookRunner: new HookRunnerToolDispatchAdapter(sp.GetRequiredService<HookRunner>()),
            approvalEvaluator: sp.GetRequiredService<CommonToolApprovalEvaluator>(),
            recorder: sp.GetRequiredService<ToolInvocationRecorderRouter>(),
            resultNormalizer: sp.GetRequiredService<DefaultToolResultNormalizer>(),
            contributions: sp.GetRequiredService<IContributionView>(),
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger<ToolDispatcher>()));
        services.AddSingleton<ModelProviderRegistry>();
        services.AddSingleton(sp =>
        {
            var resolver = new ModelBackendResolver(sp.GetRequiredService<ModelProviderRegistry>(),
                sp.GetRequiredService<IContributionView>(), paths.UserData.RootPath ?? paths.Data.RootPath,
                sp.GetServices<IModelBackend>());
            config.RuntimeResolver = resolver;
            return resolver;
        });
        services.AddSingleton<IModelRuntimeResolver>(sp => sp.GetRequiredService<ModelBackendResolver>());
        services.AddSingleton<ProviderAuthenticationCoordinator>();
        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredService<ModelBackendResolver>();
            var clients = new ChatClientRegistry(sp.GetRequiredService<ModelProviderRegistry>(), resolver);
            resolver.Changed += clients.InvalidateBackends;
            return clients;
        });
        services.AddSingleton(paths);
        services.TryAddSingleton(sp => new PluginDiscoveryService(paths,
            logger: sp.GetService<ILogger<PluginDiscoveryService>>()));
        services.TryAddSingleton<PluginConfigStore>();
        services.AddSingleton(_ => new WorkspaceStateDatabase(dataPath));
        services.AddSingleton(new PathBlacklist(config.Security.BlacklistedPaths));
        services.AddSingleton<IBackgroundTerminalService>(sp =>
            new BackgroundTerminalService(
                dataPath,
                config.Tools.Shell.Background,
                sp.GetService<ILoggerFactory>()?.CreateLogger<BackgroundTerminalService>()));
        services.AddSingleton(_ => new MemoryStore(dataPath));
        services.AddSingleton(_ => new ApprovalStore(dataPath));
        services.AddSingleton(sp =>
        {
            var timer = Stopwatch.StartNew();
            var skillsLoader = new SkillsLoader(paths, config.Skills.IncludeSharedSkills);
            PluginRuntimeConfigurator.ConfigureSkillsLoader(skillsLoader, config, paths,
                logger: sp.GetService<ILogger<PluginDiscoveryService>>());
            sp.GetService<ILogger<WorkspaceRuntime>>()?.LogInformation(
                "Startup stage {Stage} completed in {ElapsedMs}ms",
                "di-skills-and-plugin-discovery", timer.Elapsed.TotalMilliseconds);
            return skillsLoader;
        });
        services.AddSingleton(sp =>
        {
            var registry = CommandRegistry.CreateDefault(
                Path.GetFileName(paths.Data.RootPath),
                sp.GetServices<IPromptCommandProvider>());
            foreach (var handler in sp.GetServices<ICommandHandler>())
                registry.RegisterHandler(handler);
            return registry;
        });

        services.AddSingleton<IToolSource>(sp => new GoalToolSource(sp.GetRequiredService<AppConfig>()));
        services.AddSingleton<IToolSource, WorktreeToolSource>();
        services.AddSingleton<VisualizationAssetStore>();

        services.AddSingleton(sp =>
        {
            var timer = Stopwatch.StartNew();
            var hooksLoader = new HooksLoader(paths);
            var runner = new HookRunner(hooksLoader.Discover(config, workspacePath), workspacePath);
            sp.GetService<ILogger<WorkspaceRuntime>>()?.LogInformation(
                "Startup stage {Stage} completed in {ElapsedMs}ms",
                "di-hooks-and-plugin-discovery", timer.Elapsed.TotalMilliseconds);
            return runner;
        });

        services.AddSingleton(sp =>
            new McpClientManager(paths, sp.GetService<ILoggerFactory>()?.CreateLogger<McpClientManager>(),
                sp.GetService<IMcpElicitationHandler>()));
        services.AddSingleton<LspServerManager>();
        services.AddSingleton(new SessionGate(config.MaxSessionQueueSize));
        services.AddSingleton(sp => new ThreadStore(
            dataPath,
            sp.GetRequiredService<WorkspaceStateDatabase>(),
            logger: sp.GetService<ILogger<ThreadStore>>()));
        services.AddSingleton<IWorldStateSection>(sp =>
            new VisualizationStateSection(sp.GetRequiredService<ThreadStore>().VisualizationWidgetStates));
        services.AddSingleton(sp => new DreamsRunRegistry(sp.GetRequiredService<ThreadStore>()));
        services.AddSingleton<IToolProfileRegistry>(sp =>
        {
            var reg = new ToolProfileRegistry();
            reg.Register(
                DreamsConstants.ToolProfileName,
                new IToolSource[]
                {
                    new DreamsToolSource(
                        sp.GetRequiredService<DreamsRunRegistry>(),
                        sp.GetRequiredService<AppConfig>(),
                        sp.GetService<PathBlacklist>())
                });
            reg.Register(
                CommitMessageSuggestConstants.ToolProfileName,
                new IToolSource[] { new CommitSuggestToolSource() });
            return reg;
        });

        services.AddConfigurationValidation();

        if (paths.UserData.RootPath is { } userDataPath)
        {
            services.AddSingleton(_ => new UsageStatisticsDatabase(userDataPath));
            services.AddSingleton(sp => new UsageStatisticsStore(
                sp.GetRequiredService<UsageStatisticsDatabase>(), workspacePath, config.Tracing.Enabled));
            services.AddSingleton(sp => new UsageAnalyticsService(sp.GetRequiredService<UsageStatisticsDatabase>()));
        }

        if (config.Tracing.Enabled)
        {
            services.AddSingleton(sp => new TraceSinkDispatcher(
                sp.GetRequiredService<IContributionView>(),
                sp.GetService<ILoggerFactory>()));
            services.AddSingleton(sp => new TraceStore(
                sp.GetRequiredService<WorkspaceStateDatabase>(),
                maxEventsPerSession: 5000,
                sinkDispatcher: sp.GetRequiredService<TraceSinkDispatcher>(),
                statistics: sp.GetService<UsageStatisticsStore>()));
            services.AddSingleton<TraceCollector>();

        }

        services.AddSingleton(sp => new SessionPersistenceService(
            sp.GetRequiredService<ThreadStore>(),
            sp.GetService<TraceStore>(),
            sp.GetService<UsageStatisticsStore>(),
            sp.GetRequiredService<WorkspaceStateDatabase>()));
        services.AddSingleton<IWorkspaceRuntimeFactory, WorkspaceRuntimeFactory>();
        services.AddSingleton(sp => sp.GetRequiredService<IWorkspaceRuntimeFactory>().Create(sp));
        services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<WorkspaceRuntime>().SessionService);
        services.AddSingleton<IHostedService, WorkspaceRuntimeHostedService>();

        // WorkspaceRuntime starts this manager after the kernel and stops it before tearing the
        // kernel down. Keeping one owner also covers custom hosts that do not run IHostedService.
        services.TryAddSingleton(new DotNetPluginRuntimeOptions
        {
            HostSharedAssemblies = PluginHostAssemblies.ValidateHostShared(options.PluginSharedAssemblies)
        });
        services.AddSingleton(sp => new DotNetPluginRuntimeManager(
            sp.GetRequiredService<PluginDiscoveryService>(),
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<DotCraftPaths>(),
            sp,
            sp.GetRequiredService<ContributionRegistry>(),
            sp.GetRequiredService<DotNetPluginRuntimeOptions>(),
            sp.GetService<ILogger<DotNetPluginRuntimeManager>>(),
            pluginConfigStore: sp.GetRequiredService<PluginConfigStore>()));
        services.AddSingleton<IPluginDotnetRuntimeCoordinator>(sp =>
            sp.GetRequiredService<DotNetPluginRuntimeManager>());
        services.AddSingleton<IToolSource>(sp =>
            sp.GetRequiredService<DotNetPluginRuntimeManager>().ToolSource);
        services.AddSingleton<DotNetPluginAuthoringToolSource>();
        services.AddSingleton<IToolSource>(sp =>
            sp.GetRequiredService<DotNetPluginAuthoringToolSource>());

        return services;
    }

    public static bool ValidateConfigurations(
        AppConfig config,
        ModuleRegistry moduleRegistry,
        ILogger? logger = null)
    {
        var validator = new ConfigValidator(moduleRegistry);
        var isValid = validator.ValidateAndLogErrors(config, logger);
        // Startup validation predates plugin activation, so it can only see the host's own catalog.
        var subAgentCatalog = SubAgentProfileCatalog.BuiltIn;
        var subAgentWarnings = SubAgentProfileRegistry.ValidateProfiles(
            config.SubAgentProfiles,
            subAgentCatalog.KnownRuntimeTypes);
        foreach (var warning in subAgentWarnings)
            logger?.LogWarning("SubAgent profile configuration warning: {ValidationError}", warning);

        var subAgentRegistry = subAgentCatalog.CreateRegistry(
            config.SubAgentProfiles,
            disabledProfiles: config.SubAgent.DisabledProfiles);
        var hiddenBuiltInNotes = subAgentRegistry.GetHiddenBuiltInReasons();
        foreach (var note in hiddenBuiltInNotes)
            logger?.LogInformation("SubAgent profile configuration note: {ConfigurationNote}", note);

        return isValid;
    }
}

internal static class ServiceProviderExtensions
{
    internal static async Task InitializeServicesAsync(this IServiceProvider provider)
    {
        var timer = Stopwatch.StartNew();
        var logger = provider.GetService<ILogger<WorkspaceRuntime>>();
        var config = provider.GetRequiredService<AppConfig>();
        var paths = provider.GetRequiredService<DotCraftPaths>();
        var loggerFactory = provider.GetService<ILoggerFactory>();
        var hookDiagnosticsLogger = loggerFactory?.CreateLogger<HookRunner>();
        provider.GetRequiredService<HookRunner>().DebugLogger = message =>
        {
            if (message.Contains("Error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Warning", StringComparison.OrdinalIgnoreCase))
            {
                hookDiagnosticsLogger?.LogWarning("{HookDiagnostic}", message);
            }
            else
            {
                hookDiagnosticsLogger?.LogDebug("{HookDiagnostic}", message);
            }
        };
        LogStage("services-hooks");
        var mcpManager = provider.GetRequiredService<McpClientManager>();
        var lspManager = provider.GetRequiredService<LspServerManager>();
        var effectiveMcpServers = PluginMcpServerResolver.LoadEffectiveServers(
            config,
            paths,
            out var pluginMcpDiagnostics,
            logger: loggerFactory?.CreateLogger<PluginDiscoveryService>());
        PluginDiagnosticsLogger.Write(
            pluginMcpDiagnostics,
            loggerFactory?.CreateLogger("DotCraft.Plugins"));

        LogStage("services-mcp-discovery");
        if (effectiveMcpServers.Count > 0)
        {
            await mcpManager.ConnectAsync(effectiveMcpServers);
        }

        LogStage("services-mcp-connect-scheduling");
        await lspManager.InitializeAsync();
        LogStage("services-lsp-initialize");

        if (provider.GetService<ModelProviderRegistry>() is { } modelProviders)
        {
            foreach (var lifecycle in modelProviders.Protocols
                         .Select(protocol => modelProviders.GetService<IProviderLifecycle>(protocol))
                         .OfType<IProviderLifecycle>()
                         .Distinct())
                lifecycle.Start();
        }
        LogStage("services-provider-protocols");

        void LogStage(string stage)
        {
            logger?.LogInformation("Startup stage {Stage} completed in {ElapsedMs}ms",
                stage, timer.Elapsed.TotalMilliseconds);
            timer.Restart();
        }
    }

}
