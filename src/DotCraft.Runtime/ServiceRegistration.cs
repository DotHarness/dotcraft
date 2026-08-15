using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Commands.Custom;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Hooks;
using DotCraft.InlineVisualizations;
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
    /// <summary>
    /// Registers all core DotCraft services into the DI container.
    /// </summary>
    public static IServiceCollection AddDotCraftRuntime(
        this IServiceCollection services,
        DotCraftRuntimeOptions options)
    {
        var config = options.Config;
        var workspacePath = Path.GetFullPath(options.WorkspacePath);
        var botPath = Path.GetFullPath(options.CraftPath);
        // Provider selection belongs to the application composition root. AddLogging still
        // supplies a no-op-capable ILoggerFactory for embedders that do not configure one.
        services.AddLogging();
        services.AddSingleton(_ =>
        {
            var loggingCfg = config.Logging;
            var streamCfg = config.StreamDebug;
            var logsDir = Path.Combine(botPath, loggingCfg.Directory);
            return SessionStreamDebugLogger.Create(logsDir, new SessionStreamDebugLoggerOptions
            {
                Enabled = streamCfg.Enabled,
                ThreadIdFilter = streamCfg.ThreadIdFilter,
                TurnIdFilter = streamCfg.TurnIdFilter,
                IncludeFullText = streamCfg.IncludeFullText
            });
        });
        services.AddSingleton(config);
        services.TryAddSingleton<ModuleRegistry>();
        services.AddSingleton(PluginDiagnosticsStore.Shared);
        services.AddSingleton<IAppConfigMonitor, AppConfigMonitor>();
        services.AddSingleton<ToolInvocationRecorderRouter>();
        services.AddSingleton<CommonToolApprovalEvaluator>();
        services.AddSingleton<McpAppTransientContextStore>();
        services.AddSingleton<ThreadToolDispatchPolicyRegistry>();
        services.AddSingleton<IToolDispatcher>(sp => new ToolDispatcher(
            policyEvaluator: sp.GetRequiredService<ThreadToolDispatchPolicyRegistry>(),
            hookRunner: new HookRunnerToolDispatchAdapter(sp.GetRequiredService<HookRunner>()),
            approvalEvaluator: sp.GetRequiredService<CommonToolApprovalEvaluator>(),
            recorder: sp.GetRequiredService<ToolInvocationRecorderRouter>(),
            resultNormalizer: new DefaultToolResultNormalizer(
                config.Tools.ResultLimits.MaxToolResultChars,
                workspacePath,
                config.Tools.ResultLimits.SpillPreviewLines)));
        services.AddSingleton<ModelProviderRegistry>();
        services.AddSingleton(sp => new ChatClientRegistry(sp.GetRequiredService<ModelProviderRegistry>()));
        services.AddSingleton(new WorkspacePaths
        {
            WorkspacePath = workspacePath,
            CraftPath = botPath
        });
        services.AddSingleton(_ => new WorkspaceStateDatabase(botPath));
        services.AddSingleton(new PathBlacklist(config.Security.BlacklistedPaths));
        services.AddSingleton<IBackgroundTerminalService>(sp =>
            new BackgroundTerminalService(
                botPath,
                config.Tools.Shell.Background,
                sp.GetService<ILoggerFactory>()?.CreateLogger<BackgroundTerminalService>()));
        services.AddSingleton(_ => new MemoryStore(botPath));
        services.AddSingleton(_ => new DreamStore(botPath));
        services.AddSingleton<DreamsStateStore>();
        services.AddSingleton(_ => new ApprovalStore(botPath));
        services.AddSingleton(_ =>
        {
            var skillsLoader = new SkillsLoader(botPath);
            PluginRuntimeConfigurator.ConfigureSkillsLoader(
                skillsLoader,
                config,
                workspacePath,
                botPath,
                PluginDiagnosticsStore.Shared);
            return skillsLoader;
        });
        services.AddSingleton<ISkillMutationApplier>(sp =>
            new WorkspaceFileSkillMutationApplier(sp.GetRequiredService<SkillsLoader>()));

        services.AddSingleton(_ => new CustomCommandLoader(botPath));
        services.AddSingleton(sp => CommandRegistry.CreateDefault(
            sp.GetRequiredService<CustomCommandLoader>(),
            sp.GetServices<IPromptCommandProvider>()));

        var cronStorePath = Path.Combine(botPath, config.Cron.StorePath);
        services.AddSingleton(sp =>
        {
            var cronLogger = sp.GetService<ILoggerFactory>()?.CreateLogger<CronService>();
            return new CronService(cronStorePath, cronLogger);
        });
        services.AddSingleton<CronTools>(sp => new CronTools(sp.GetRequiredService<CronService>()));
        services.AddSingleton<IToolSource>(sp => new CronToolSource(sp.GetRequiredService<CronTools>()));
        services.AddSingleton<IToolSource>(sp => new GoalToolSource(sp.GetRequiredService<AppConfig>()));
        services.AddSingleton<InlineVisualizationAssetStore>();

        // Hooks
        services.AddSingleton(_ =>
        {
            var hooksLoader = new HooksLoader(botPath);
            return new HookRunner(hooksLoader.Discover(config, workspacePath), workspacePath);
        });

        services.AddSingleton(sp =>
            new McpClientManager(sp.GetService<ILoggerFactory>()?.CreateLogger<McpClientManager>()));
        services.AddSingleton<LspServerManager>();
        services.AddSingleton(new SessionGate(config.MaxSessionQueueSize));
        services.AddSingleton(sp => new ThreadStore(botPath, sp.GetRequiredService<WorkspaceStateDatabase>()));
        services.AddSingleton(sp => new DreamsInputCollector(
            sp.GetRequiredService<AppConfig>(),
            workspacePath,
            sp.GetRequiredService<MemoryStore>(),
            sp.GetRequiredService<DreamStore>(),
            sp.GetRequiredService<ThreadStore>()));
        services.AddSingleton<DreamsRunRegistry>();
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
            reg.Register(
                WelcomeSuggestionConstants.ToolProfileName,
                new[]
                {
                    new WelcomeSuggestionToolSource(sp.GetRequiredService<MemoryStore>())
                });
            return reg;
        });

        // Register configuration validation
        services.AddConfigurationValidation();

        if (config.Tracing.Enabled)
        {
            services.AddSingleton(sp => new TraceStore(
                sp.GetRequiredService<WorkspaceStateDatabase>(),
                maxEventsPerSession: 5000));
            services.AddSingleton<TraceCollector>();

            services.AddSingleton(sp => new TokenUsageStore(
                sp.GetRequiredService<WorkspaceStateDatabase>()));
        }

        services.AddSingleton(sp => new SessionPersistenceService(
            sp.GetRequiredService<ThreadStore>(),
            sp.GetService<TraceStore>(),
            sp.GetService<TokenUsageStore>(),
            sp.GetRequiredService<WorkspaceStateDatabase>()));
        services.AddSingleton<IWorkspaceRuntimeFactory, WorkspaceRuntimeFactory>();
        services.AddSingleton(sp => sp.GetRequiredService<IWorkspaceRuntimeFactory>().Create(sp));
        services.AddSingleton<IHostedService, WorkspaceRuntimeHostedService>();

        return services;
    }

    /// <summary>
    /// Validates module configurations and prints diagnostics.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <param name="moduleRegistry">The module registry whose modules provide their own validators.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateConfigurations(
        AppConfig config,
        ModuleRegistry moduleRegistry,
        ILogger? logger = null)
    {
        var validator = new ConfigValidator(moduleRegistry);
        var isValid = validator.ValidateAndLogErrors(config, logger);
        var subAgentWarnings = SubAgentProfileRegistry.ValidateProfiles(
            config.SubAgentProfiles,
            SubAgentProfileRegistry.KnownRuntimeTypes);
        foreach (var warning in subAgentWarnings)
            logger?.LogWarning("SubAgent profile configuration warning: {ValidationError}", warning);

        var subAgentRegistry = new SubAgentProfileRegistry(
            config.SubAgentProfiles,
            SubAgentProfileRegistry.CreateBuiltInProfiles(),
            SubAgentProfileRegistry.KnownRuntimeTypes,
            config.SubAgent.DisabledProfiles);
        var hiddenBuiltInNotes = subAgentRegistry.GetHiddenBuiltInReasons();
        foreach (var note in hiddenBuiltInNotes)
            logger?.LogInformation("SubAgent profile configuration note: {ConfigurationNote}", note);

        var waitAgentTimeoutErrors = SubAgentWaitAgentTimeoutOptions.Validate(config.SubAgent);
        foreach (var error in waitAgentTimeoutErrors)
            logger?.LogError("SubAgent wait timeout configuration error: {ValidationError}", error);

        return isValid && waitAgentTimeoutErrors.Count == 0;
    }
}

/// <summary>
/// Extension methods for IServiceProvider.
/// </summary>
internal static class ServiceProviderExtensions
{
    /// <summary>
    /// Initializes async services.
    /// </summary>
    internal static async Task InitializeServicesAsync(this IServiceProvider provider)
    {
        var config = provider.GetRequiredService<AppConfig>();
        var paths = provider.GetRequiredService<WorkspacePaths>();
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
        var mcpManager = provider.GetRequiredService<McpClientManager>();
        var lspManager = provider.GetRequiredService<LspServerManager>();
        var effectiveMcpServers = PluginMcpServerResolver.LoadEffectiveServers(
            config,
            paths.WorkspacePath,
            paths.CraftPath,
            out var pluginMcpDiagnostics);
        PluginDiagnosticsStore.Shared.Append(pluginMcpDiagnostics);
        PluginDiagnosticsLogger.Write(
            pluginMcpDiagnostics,
            loggerFactory?.CreateLogger("DotCraft.Plugins"));

        if (effectiveMcpServers.Count > 0)
        {
            await mcpManager.ConnectAsync(effectiveMcpServers);
        }

        await lspManager.InitializeAsync();

        if (provider.GetService<ModelProviderRegistry>() is { } modelProviders)
        {
            foreach (var lifecycle in modelProviders.Protocols
                         .Select(protocol => modelProviders.GetService<IProviderLifecycle>(protocol))
                         .OfType<IProviderLifecycle>()
                         .Distinct())
                lifecycle.Start();
        }
    }

}
