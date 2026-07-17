using DotCraft.Agents;
using DotCraft.Abstractions;
using DotCraft.Auth.OpenAI;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Hooks;
using DotCraft.Tracing;
using DotCraft.Logging;
using DotCraft.Sessions;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.State;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.Hosting;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers all core DotCraft services into the DI container.
    /// </summary>
    public static IServiceCollection AddDotCraft(
        this IServiceCollection services,
        AppConfig config,
        string workspacePath,
        string botPath)
    {
        services.AddLogging(builder =>
        {
            var loggingCfg = config.Logging;
            var minLevel = Enum.TryParse<LogLevel>(loggingCfg.MinLevel, ignoreCase: true, out var lvl)
                ? lvl
                : LogLevel.Information;
            builder.SetMinimumLevel(minLevel);

            if (loggingCfg.Enabled)
            {
                var logsDir = Path.Combine(botPath, loggingCfg.Directory);
                builder.AddProvider(new FileLoggerProvider(logsDir, minLevel, loggingCfg.RetentionDays));
            }

            if (loggingCfg.Console)
            {
                builder.AddConsole();
            }
        });
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
        services.AddSingleton<OpenAITokenStore>(_ => new OpenAITokenStore());
        services.AddSingleton<OpenAIInstallationIdProvider>(_ => new OpenAIInstallationIdProvider());
        services.AddSingleton<IOpenAIAuthService, OpenAIAuthManager>();
        services.AddSingleton<OpenAIUsageClient>();
        services.AddSingleton<OpenAIUsagePoller>();
        services.AddSingleton<IOpenAIUsageService>(sp => sp.GetRequiredService<OpenAIUsagePoller>());
        services.AddSingleton<OpenAIClientProvider>();
        services.AddSingleton<AnthropicClientProvider>();
        services.AddSingleton<ChatClientRegistry>();
        services.AddSingleton(new DotCraftPaths
        {
            WorkspacePath = workspacePath,
            CraftPath = botPath
        });
        services.AddSingleton(new StateRuntime(botPath));
        services.AddSingleton(new PathBlacklist(config.Security.BlacklistedPaths));
        services.AddSingleton<IBackgroundTerminalService>(sp =>
            new BackgroundTerminalService(
                botPath,
                config.Tools.Shell.Background,
                sp.GetService<ILoggerFactory>()?.CreateLogger<BackgroundTerminalService>()));
        services.AddSingleton(new MemoryStore(botPath));
        services.AddSingleton(new DreamStore(botPath));
        services.AddSingleton<DreamsStateStore>();
        services.AddSingleton(new ApprovalStore(botPath));
        var skillsLoader = new SkillsLoader(botPath);
        skillsLoader.DeployBuiltInSkills();
        services.AddSingleton(skillsLoader);
        PluginRuntimeConfigurator.ConfigureSkillsLoader(
            skillsLoader,
            config,
            workspacePath,
            botPath,
            PluginDiagnosticsStore.Shared);
        services.AddSingleton<ISkillMutationApplier>(sp =>
            new WorkspaceFileSkillMutationApplier(sp.GetRequiredService<SkillsLoader>()));

        var customCommandLoader = new CustomCommandLoader(botPath);
        customCommandLoader.DeployBuiltInCommands();
        services.AddSingleton(customCommandLoader);

        var cronStorePath = Path.Combine(botPath, config.Cron.StorePath);
        services.AddSingleton(sp =>
        {
            var cronLogger = sp.GetService<ILoggerFactory>()?.CreateLogger<CronService>();
            return new CronService(cronStorePath, cronLogger);
        });
        services.AddSingleton<CronTools>(sp => new CronTools(sp.GetRequiredService<CronService>()));
        services.AddSingleton<IToolSource>(sp => new CronToolSource(sp.GetRequiredService<CronTools>()));
        services.AddSingleton<IToolSource>(sp => new GoalToolSource(sp.GetRequiredService<AppConfig>()));
        services.AddSingleton<Protocol.InlineVisualizations.InlineVisualizationAssetStore>();
        services.AddSingleton<Protocol.InlineVisualizations.InlineVisualizationRuntimeRegistry>();
        services.AddSingleton<IThreadSystemPromptContextProvider>(sp =>
            sp.GetRequiredService<Protocol.InlineVisualizations.InlineVisualizationRuntimeRegistry>());

        // Hooks
        var hooksLoader = new HooksLoader(botPath);
        var hooksDiscovery = hooksLoader.Discover(config, workspacePath);
        var hookRunner = new HookRunner(hooksDiscovery, workspacePath);
        services.AddSingleton(hookRunner);

        services.AddSingleton(sp =>
            new McpClientManager(sp.GetService<ILoggerFactory>()?.CreateLogger<McpClientManager>()));
        services.AddSingleton<LspServerManager>();
        services.AddSingleton(new SessionGate(config.MaxSessionQueueSize));
        services.AddSingleton<ActiveRunRegistry>();
        services.AddSingleton(sp => new ThreadStore(botPath, sp.GetRequiredService<StateRuntime>()));
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
            var tracingStoragePath = Path.Combine(botPath, "tracing");
            services.AddSingleton(sp =>
            {
                var traceStore = new TraceStore(
                    tracingStoragePath,
                    maxEventsPerSession: 5000,
                    synchronousPersist: false,
                    stateRuntime: sp.GetRequiredService<StateRuntime>());
                traceStore.LoadFromDisk();
                return traceStore;
            });
            services.AddSingleton<TraceCollector>();

            services.AddSingleton(sp =>
            {
                var tokenUsageStore = new TokenUsageStore(
                    tracingStoragePath,
                    stateRuntime: sp.GetRequiredService<StateRuntime>());
                tokenUsageStore.LoadFromDisk();
                return tokenUsageStore;
            });
        }

        services.AddSingleton(sp => new SessionPersistenceService(
            sp.GetRequiredService<ThreadStore>(),
            sp.GetService<TraceStore>(),
            sp.GetService<TokenUsageStore>(),
            sp.GetRequiredService<StateRuntime>()));
        services.AddSingleton<IWorkspaceRuntimeFactory, WorkspaceRuntimeFactory>();
        services.AddSingleton(sp => sp.GetRequiredService<IWorkspaceRuntimeFactory>().Create(sp));

        return services;
    }

    /// <summary>
    /// Validates module configurations and prints diagnostics.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <param name="moduleRegistry">The module registry whose modules provide their own validators.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateConfigurations(AppConfig config, ModuleRegistry moduleRegistry)
    {
        var validator = new ConfigValidator(moduleRegistry);
        var isValid = validator.ValidateAndLogErrors(config);
        var subAgentWarnings = SubAgentProfileRegistry.ValidateProfiles(
            config.SubAgentProfiles,
            SubAgentProfileRegistry.KnownRuntimeTypes);
        foreach (var warning in subAgentWarnings)
            AnsiConsole.MarkupLine($"[yellow][[Config]] Warning: SubAgentProfiles - {Markup.Escape(warning)}[/]");

        var subAgentRegistry = new SubAgentProfileRegistry(
            config.SubAgentProfiles,
            SubAgentProfileRegistry.CreateBuiltInProfiles(),
            SubAgentProfileRegistry.KnownRuntimeTypes,
            config.SubAgent.DisabledProfiles);
        var hiddenBuiltInNotes = subAgentRegistry.GetHiddenBuiltInReasons();
        foreach (var note in hiddenBuiltInNotes)
            AnsiConsole.MarkupLine($"[grey][[Config]] Note: {Markup.Escape(note)}[/]");

        var waitAgentTimeoutErrors = SubAgentWaitAgentTimeoutOptions.Validate(config.SubAgent);
        foreach (var error in waitAgentTimeoutErrors)
            AnsiConsole.MarkupLine($"[red][[Config]] Error: {Markup.Escape(error)}[/]");

        return isValid && waitAgentTimeoutErrors.Count == 0;
    }
}

/// <summary>
/// Extension methods for IServiceProvider.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Initializes async services.
    /// </summary>
    public static async Task InitializeServicesAsync(this IServiceProvider provider)
    {
        var config = provider.GetRequiredService<AppConfig>();
        var paths = provider.GetRequiredService<DotCraftPaths>();
        var mcpManager = provider.GetRequiredService<McpClientManager>();
        var lspManager = provider.GetRequiredService<LspServerManager>();
        var effectiveMcpServers = PluginMcpServerResolver.LoadEffectiveServers(
            config,
            paths.WorkspacePath,
            paths.CraftPath,
            out var pluginMcpDiagnostics);
        PluginDiagnosticsStore.Shared.Append(pluginMcpDiagnostics);
        PluginDiagnosticsLogger.Write(pluginMcpDiagnostics);

        if (effectiveMcpServers.Count > 0)
        {
            await mcpManager.ConnectAsync(effectiveMcpServers);
        }

        await lspManager.InitializeAsync();

        // Start the ChatGPT usage poller if an account is already signed in. The poller is
        // self-quiescing when no OAuth credentials exist so this is safe regardless of auth state.
        provider.GetService<OpenAIUsagePoller>()?.Start();
    }

    /// <summary>
    /// Disposes async services.
    /// </summary>
    public static async ValueTask DisposeServicesAsync(this IServiceProvider provider)
    {
        var cronService = provider.GetRequiredService<CronService>();
        cronService.Stop();
        cronService.Dispose();

        var mcpManager = provider.GetRequiredService<McpClientManager>();
        await mcpManager.DisposeAsync();

        var lspManager = provider.GetRequiredService<LspServerManager>();
        await lspManager.DisposeAsync();

        if (provider.GetService<IBackgroundTerminalService>() is IAsyncDisposable terminals)
            await terminals.DisposeAsync();

        if (provider.GetService<OpenAIUsagePoller>() is { } usagePoller)
            await usagePoller.DisposeAsync();
    }
}
