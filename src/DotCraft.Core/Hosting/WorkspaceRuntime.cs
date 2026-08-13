using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Persistence;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tools.Sandbox;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotCraft.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;

namespace DotCraft.Hosting;

public sealed class WorkspaceRuntime : IAsyncDisposable
{
    private sealed class StartedState(
        AgentFactory agentFactory,
        WireAcpExtensionProxy wireAcpExtensionProxy,
        WireNodeReplProxy wireNodeReplProxy,
        WireDynamicToolProxy wireDynamicToolProxy,
        ThreadStore threadStore,
        ISessionService sessionService,
        ICommitMessageSuggestService commitMessageSuggestService,
        WelcomeSuggestionService welcomeSuggestionService,
        AgentRunner agentRunner,
        CronService cronService,
        HeartbeatService heartbeatService,
        DreamsService dreamsService,
        IAutomationsRequestHandler? automationsHandler,
        IAppServerChannelListContributor channelListContributor,
        IReadOnlyList<ConfigSchemaSection> configSchema,
        IContextPageManager contextPageManager,
        IWorkspaceRuntimeAppServerFeature? appServerFeature)
    {
        public AgentFactory AgentFactory { get; } = agentFactory;

        public WireAcpExtensionProxy WireAcpExtensionProxy { get; } = wireAcpExtensionProxy;

        public WireNodeReplProxy WireNodeReplProxy { get; } = wireNodeReplProxy;

        public WireDynamicToolProxy WireDynamicToolProxy { get; } = wireDynamicToolProxy;

        public ThreadStore ThreadStore { get; } = threadStore;

        public ISessionService SessionService { get; } = sessionService;

        public ICommitMessageSuggestService CommitMessageSuggestService { get; } = commitMessageSuggestService;

        public WelcomeSuggestionService WelcomeSuggestionService { get; } = welcomeSuggestionService;

        public AgentRunner AgentRunner { get; } = agentRunner;

        public CronService CronService { get; } = cronService;

        public HeartbeatService HeartbeatService { get; } = heartbeatService;

        public DreamsService DreamsService { get; } = dreamsService;

        public IAutomationsRequestHandler? AutomationsHandler { get; } = automationsHandler;

        public IAppServerChannelListContributor ChannelListContributor { get; } = channelListContributor;

        public IReadOnlyList<ConfigSchemaSection> ConfigSchema { get; } = configSchema;

        public IContextPageManager ContextPageManager { get; } = contextPageManager;

        public IWorkspaceRuntimeAppServerFeature? AppServerFeature { get; } = appServerFeature;

    }

    private readonly IAppConfigMonitor _appConfigMonitor;
    private readonly IWorkspaceRuntimeAppServerFeatureFactory? _appServerFeatureFactory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private StartedState? _started;
    private bool _disposed;

    public WorkspaceRuntime(
        IServiceProvider services,
        AppConfig config,
        DotCraftPaths paths,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        PathBlacklist pathBlacklist,
        McpClientManager mcpClientManager,
        LspServerManager lspServerManager)
    {
        Services = services;
        Config = config;
        Paths = paths;
        MemoryStore = memoryStore;
        SkillsLoader = skillsLoader;
        PathBlacklist = pathBlacklist;
        McpClientManager = mcpClientManager;
        LspServerManager = lspServerManager;
        _appConfigMonitor = services.GetRequiredService<IAppConfigMonitor>();
        _appServerFeatureFactory = services.GetService<IWorkspaceRuntimeAppServerFeatureFactory>();
    }

    public IServiceProvider Services { get; }

    public AppConfig Config { get; }

    public DotCraftPaths Paths { get; }

    public MemoryStore MemoryStore { get; }

    public SkillsLoader SkillsLoader { get; }

    public PathBlacklist PathBlacklist { get; }

    public McpClientManager McpClientManager { get; }

    public LspServerManager LspServerManager { get; }

    public bool IsStarted => _started != null;

    public ISessionService SessionService => EnsureStarted().SessionService;

    public ICommitMessageSuggestService CommitMessageSuggestService => EnsureStarted().CommitMessageSuggestService;

    public IWelcomeSuggestionService WelcomeSuggestionService => EnsureStarted().WelcomeSuggestionService;

    public CronService CronService => EnsureStarted().CronService;

    public HeartbeatService HeartbeatService => EnsureStarted().HeartbeatService;

    public DreamsService DreamsService => EnsureStarted().DreamsService;

    public IAutomationsRequestHandler? AutomationsHandler => EnsureStarted().AutomationsHandler;

    public IChannelStatusProvider? ChannelStatusProvider => EnsureStarted().AppServerFeature?.ChannelStatusProvider;

    public IExternalChannelLogProvider? ExternalChannelLogProvider => EnsureStarted().AppServerFeature?.ExternalChannelLogProvider;

    public IAppServerChannelListContributor ChannelListContributor => EnsureStarted().ChannelListContributor;

    public WireAcpExtensionProxy WireAcpExtensionProxy => EnsureStarted().WireAcpExtensionProxy;

    public WireNodeReplProxy WireNodeReplProxy => EnsureStarted().WireNodeReplProxy;

    public WireDynamicToolProxy WireDynamicToolProxy => EnsureStarted().WireDynamicToolProxy;

    public IReadOnlyList<ConfigSchemaSection> ConfigSchema => EnsureStarted().ConfigSchema;

    public IContextPageManager ContextPageManager => EnsureStarted().ContextPageManager;

    public string? DashboardUrl => EnsureStarted().AppServerFeature?.DashboardUrl;

    public PlanStore? PlanStore => EnsureStarted().AgentFactory.PlanStore;

    public event Action<AppConfigChangedEventArgs>? WorkspaceConfigChanged;

    public event Action<McpServerStatusChangedEventArgs>? McpStatusChanged;

    public event Action<string, StructuredPlan>? PlanUpdated;

    public event Action<SessionThread>? ThreadStarted;

    public event Action<SessionThread>? ThreadRenamed;

    public event Action<SessionThread>? ThreadUpdated;

    public event Action<string>? ThreadDeleted;

    public event Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChanged;

    public event Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignal;

    public event Action<ThreadGoal, string?>? ThreadGoalUpdated;

    public event Action<string>? ThreadGoalCleared;

    public event Action<string, string>? SubAgentGraphChanged;

    public event Action<CronJob?, string, bool>? CronStateChanged;

    public event Action<BackgroundJobResult>? BackgroundJobResultProduced;

    public event Action<IAutomationTaskEventPayload>? AutomationTaskUpdated;

    public async Task StartAsync(ModuleRegistry moduleRegistry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_started != null)
                throw new InvalidOperationException("WorkspaceRuntime has already been started.");

            SkillsLoader.SetDisabledSkills(Config.Skills.DisabledSkills);

            var traceCollector = Services.GetService<TraceCollector>();
            var cronTools = Services.GetService<CronTools>();
            var backgroundTerminalService = Services.GetRequiredService<IBackgroundTerminalService>();
            var chatClientRegistry = Services.GetRequiredService<ChatClientRegistry>();
            var mainRuntime = chatClientRegistry.ResolveMainRuntime(Config);
            var mainModel = mainRuntime.Model;
            var contextPageManager = new ContextPageManager();
            var threadSystemPromptContextProviders = Services.GetServices<IThreadSystemPromptContextProvider>().ToArray();
            var runtimeContextContributors = Services.GetServices<IRuntimeContextContributor>().ToArray();

            ToolSourceCollector.ScanToolIcons(moduleRegistry, Config);

            var fallbackApproval = new AutoApproveApprovalService();
            var scopedApproval = new SessionScopedApprovalService(fallbackApproval);
            var planStore = new PlanStore(Paths.CraftPath, Services.GetRequiredService<WorkspaceStateDatabase>());
            var wireAcpExtensionProxy = new WireAcpExtensionProxy();
            var wireNodeReplProxy = new WireNodeReplProxy();
            var wireDynamicToolProxy = Services.GetService<WireDynamicToolProxy>()
                ?? new WireDynamicToolProxy();
            var toolSources = new ToolSourceCollector(moduleRegistry, Services, Config).Collect().ToList();
            toolSources.Add(new CoreToolSource(
                Config,
                chatClientRegistry,
                SkillsLoader,
                scopedApproval,
                backgroundTerminalService,
                PathBlacklist,
                LspServerManager,
                traceCollector,
                Services.GetService<ISkillMutationApplier>(),
                contextPageManager));
            if (Config.Tools.Sandbox.Enabled)
            {
                var sandboxProvider = Services.GetService<ISandboxProvider>()
                    ?? throw new InvalidOperationException(
                        "Sandbox is enabled, but no ISandboxProvider is registered. Register a sandbox backend before starting the workspace runtime.");
                toolSources.Add(new SandboxToolSource(
                    Config,
                    sandboxProvider,
                    chatClientRegistry,
                    SkillsLoader,
                    scopedApproval,
                    PathBlacklist,
                    traceCollector,
                    Services.GetService<ISkillMutationApplier>(),
                    contextPageManager,
                    Services.GetService<ILoggerFactory>()));
            }
            toolSources.Add(new NodeReplPluginToolSource(Config, wireNodeReplProxy, Paths.CraftPath));
            if (!toolSources.Contains(wireDynamicToolProxy))
                toolSources.Add(wireDynamicToolProxy);

            AgentFactory? agentFactory = null;
            HeartbeatService? heartbeatService = null;
            IWorkspaceRuntimeAppServerFeature? appServerFeature = null;
            WelcomeSuggestionService? welcomeSuggestionService = null;
            DreamsService? dreamsService = null;
            try
            {
                agentFactory = new AgentFactory(
                    Paths.CraftPath, Paths.WorkspacePath, Config,
                    MemoryStore, SkillsLoader,
                    approvalService: scopedApproval,
                    PathBlacklist,
                    runtimeContext: new AgentRuntimeContext
                    {
                        Config = Config,
                        ChatClient = chatClientRegistry.GetChatClient(mainRuntime),
                        ChatClientRegistry = chatClientRegistry,
                        EffectiveProviderId = mainRuntime.ProviderId,
                        EffectiveProviderProtocol = mainRuntime.Protocol,
                        EffectiveMainModel = mainModel,
                        WorkspacePath = Paths.WorkspacePath,
                        BotPath = Paths.CraftPath,
                        MemoryStore = MemoryStore,
                        DreamStore = Services.GetRequiredService<DreamStore>(),
                        SkillsLoader = SkillsLoader,
                        ContextPageManager = contextPageManager,
                        ThreadSystemPromptContextProviders = threadSystemPromptContextProviders,
                        RuntimeContextContributors = runtimeContextContributors,
                        ApprovalService = scopedApproval,
                        PathBlacklist = PathBlacklist,
                        BackgroundTerminalService = backgroundTerminalService,
                        CronTools = cronTools,
                        McpClientManager = McpClientManager,
                        LspServerManager = LspServerManager,
                        TraceCollector = traceCollector,
                        AcpExtensionProxy = wireAcpExtensionProxy,
                        NodeReplProxy = wireNodeReplProxy
                    },
                    traceCollector: traceCollector,
                    planStore: planStore,
                    onPlanUpdated: (threadId, plan) => PlanUpdated?.Invoke(threadId, plan),
                    hookRunner: Services.GetService<HookRunner>(),
                    chatClientRegistry: chatClientRegistry,
                    toolDispatcher: Services.GetRequiredService<IToolDispatcher>(),
                    toolSources: toolSources,
                    loggerFactory: Services.GetService<ILoggerFactory>());

                var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
                var sessionService = SessionServiceFactory.Create(agentFactory, agent, Services);
                sessionService.ThreadCreatedForBroadcast = thread => ThreadStarted?.Invoke(thread);
                sessionService.ThreadDeletedForBroadcast = threadId => ThreadDeleted?.Invoke(threadId);
                sessionService.ThreadRenamedForBroadcast = thread => ThreadRenamed?.Invoke(thread);
                sessionService.ThreadUpdatedForBroadcast = thread => ThreadUpdated?.Invoke(thread);
                sessionService.ThreadStatusChangedForBroadcast =
                    (threadId, previousStatus, newStatus) => ThreadStatusChanged?.Invoke(threadId, previousStatus, newStatus);
                var runtimeSignalObservers = Services.GetServices<IThreadRuntimeSignalObserver>().ToArray();
                sessionService.ThreadRuntimeSignalForBroadcast =
                    (threadId, signal) =>
                    {
                        ThreadRuntimeSignal?.Invoke(threadId, signal);
                        foreach (var observer in runtimeSignalObservers)
                            observer.OnThreadRuntimeSignal(Paths.CraftPath, threadId, signal);
                    };
                sessionService.ThreadGoalUpdatedForBroadcast =
                    (goal, turnId) => ThreadGoalUpdated?.Invoke(goal, turnId);
                sessionService.ThreadGoalClearedForBroadcast =
                    threadId => ThreadGoalCleared?.Invoke(threadId);
                sessionService.SubAgentGraphChangedForBroadcast =
                    (parentThreadId, childThreadId) => SubAgentGraphChanged?.Invoke(parentThreadId, childThreadId);

                var loggerFactory = Services.GetService<ILoggerFactory>();
                var commitMessageSuggestService =
                    new CommitMessageSuggestService(
                        sessionService,
                        Paths.WorkspacePath,
                        loggerFactory?.CreateLogger<CommitMessageSuggestService>(),
                        () => _appConfigMonitor.Current.SourceControl);
                welcomeSuggestionService = new WelcomeSuggestionService(
                    sessionService,
                    Services.GetRequiredService<SessionPersistenceService>(),
                    MemoryStore,
                    Paths.WorkspacePath,
                    loggerFactory?.CreateLogger<WelcomeSuggestionService>());
                var cronService = Services.GetRequiredService<CronService>();
                var agentRunner = new AgentRunner(Paths.WorkspacePath, sessionService, quiet: true);
                heartbeatService = new HeartbeatService(
                    Paths.CraftPath,
                    onHeartbeat: async (prompt, sessionKey, threadDisplayName, cancellationToken) =>
                    {
                        try
                        {
                            var run = await agentRunner.RunAsync(
                                prompt,
                                sessionKey,
                                threadDisplayName,
                                cancellationToken);
                            var backgroundJobResult = CreateHeartbeatBackgroundJobResult(run);
                            if (backgroundJobResult != null)
                                BackgroundJobResultProduced?.Invoke(backgroundJobResult);
                            return run;
                        }
                        catch (Exception ex)
                        {
                            loggerFactory?.CreateLogger<WorkspaceRuntime>()
                                .LogError(ex, "Heartbeat run failed");
                            return null;
                        }
                    },
                    intervalSeconds: Config.Heartbeat.IntervalSeconds,
                    enabled: Config.Heartbeat.Enabled,
                    logger: Services.GetService<ILoggerFactory>()
                        ?.CreateLogger<HeartbeatService>());

                var channelListContributor =
                    new ModuleRegistryChannelListContributor(moduleRegistry, cronService, heartbeatService);
                var configSchema = Services.GetService<IConfigSchemaProvider>()?.GetConfigSchema()
                    ?? throw new InvalidOperationException(
                        "IConfigSchemaProvider is not registered. Hosts that use WorkspaceRuntime must register the generated ConfigSchemaRegistrations.CreateSchemaProvider() instance.");
                var dreamsRunner = new DreamsSessionRunner(
                    sessionService,
                    Services.GetRequiredService<SessionPersistenceService>(),
                    chatClientRegistry,
                    Config,
                    Paths.WorkspacePath,
                    Services.GetRequiredService<DreamsRunRegistry>(),
                    Services.GetRequiredService<DreamStore>(),
                    Services.GetService<ILoggerFactory>()
                        ?.CreateLogger<DreamsSessionRunner>());
                dreamsService = new DreamsService(
                    Config,
                    Services.GetRequiredService<DreamsInputCollector>(),
                    dreamsRunner,
                    Services.GetRequiredService<DreamStore>(),
                    Services.GetRequiredService<DreamsStateStore>(),
                    Services.GetService<ILoggerFactory>()
                        ?.CreateLogger<DreamsService>());
                await dreamsService.StartAsync(ct);

                if (_appServerFeatureFactory != null)
                {
                    appServerFeature = _appServerFeatureFactory.Create(Services);
                    appServerFeature.AutomationTaskUpdated += OnAutomationTaskUpdated;
                    await appServerFeature.StartAsync(
                        new WorkspaceRuntimeAppServerFeatureContext(
                            Services,
                            Config,
                            Paths,
                            moduleRegistry,
                            sessionService,
                            agentRunner,
                            cronService,
                            heartbeatService,
                            dreamsService,
                            emitCronStateChanged: (job, id, removed) =>
                                CronStateChanged?.Invoke(job, id, removed),
                            emitBackgroundJobResult: result =>
                                BackgroundJobResultProduced?.Invoke(result)),
                        ct);
                }

                _started = new StartedState(
                    agentFactory,
                    wireAcpExtensionProxy,
                    wireNodeReplProxy,
                    wireDynamicToolProxy,
                    Services.GetRequiredService<ThreadStore>(),
                    sessionService,
                    commitMessageSuggestService,
                    welcomeSuggestionService,
                    agentRunner,
                    cronService,
                    heartbeatService,
                    dreamsService,
                    Services.GetService<IAutomationsRequestHandler>(),
                    channelListContributor,
                    configSchema,
                    contextPageManager,
                    appServerFeature);

                _appConfigMonitor.Changed += OnAppConfigChanged;
                McpClientManager.StatusChanged += OnMcpStatusChanged;
            }
            catch
            {
                if (appServerFeature != null)
                {
                    appServerFeature.AutomationTaskUpdated -= OnAutomationTaskUpdated;
                    try
                    {
                        await appServerFeature.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                if (welcomeSuggestionService != null)
                {
                    try
                    {
                        await welcomeSuggestionService.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                if (dreamsService != null)
                {
                    try
                    {
                        await dreamsService.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                heartbeatService?.Dispose();

                if (agentFactory != null)
                {
                    try
                    {
                        await agentFactory.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal static BackgroundJobResult? CreateHeartbeatBackgroundJobResult(AgentRunResult? run)
    {
        if (run == null)
            return null;

        if (run.Error == null && run.Result != null)
        {
            return new BackgroundJobResult(
                "heartbeat",
                null,
                null,
                run.Result,
                null,
                run.ThreadId,
                run.InputTokens,
                run.OutputTokens);
        }

        if (run.Error != null)
        {
            return new BackgroundJobResult(
                "heartbeat",
                null,
                null,
                null,
                run.Error,
                run.ThreadId,
                run.InputTokens,
                run.OutputTokens);
        }

        return null;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed)
            return;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            var started = _started;
            if (started == null)
                return;

            _appConfigMonitor.Changed -= OnAppConfigChanged;
            McpClientManager.StatusChanged -= OnMcpStatusChanged;

            List<Exception>? errors = null;

            if (started.AppServerFeature != null)
            {
                started.AppServerFeature.AutomationTaskUpdated -= OnAutomationTaskUpdated;
                try
                {
                    await started.AppServerFeature.StopAsync(ct);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }

                try
                {
                    await started.AppServerFeature.DisposeAsync();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            try
            {
                await started.DreamsService.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await started.WelcomeSuggestionService.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.HeartbeatService.Stop();
                started.HeartbeatService.OnResult = null;
                started.HeartbeatService.Dispose();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.CronService.OnJob = null;
                started.CronService.CronJobPersistedAfterExecution = null;
                started.CronService.Stop();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.SessionService.ThreadCreatedForBroadcast = null;
                started.SessionService.ThreadDeletedForBroadcast = null;
                started.SessionService.ThreadRenamedForBroadcast = null;
                started.SessionService.ThreadUpdatedForBroadcast = null;
                started.SessionService.ThreadStatusChangedForBroadcast = null;
                started.SessionService.ThreadRuntimeSignalForBroadcast = null;
                if (started.SessionService is SessionService sessionService)
                    sessionService.SubAgentGraphChangedForBroadcast = null;
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await started.ThreadStore.FlushAndCloseAsync(ct);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await started.AgentFactory.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            _started = null;

            if (errors is { Count: 1 })
                throw errors[0];
            if (errors is { Count: > 1 })
                throw new AggregateException(errors);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default)
    {
        var feature = EnsureStarted().AppServerFeature;
        if (feature != null)
            await feature.ApplyExternalChannelUpsertAsync(entry, ct);
    }

    public async Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default)
    {
        var feature = EnsureStarted().AppServerFeature;
        if (feature != null)
            await feature.ApplyExternalChannelRemoveAsync(channelName, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync();
        _lifecycleLock.Dispose();
        _disposed = true;
    }

    private StartedState EnsureStarted()
    {
        ThrowIfDisposed();
        return _started ?? throw new InvalidOperationException("WorkspaceRuntime has not been started.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkspaceRuntime));
    }

    private void OnAppConfigChanged(object? sender, AppConfigChangedEventArgs e)
    {
        _ = sender;
        WorkspaceConfigChanged?.Invoke(e);
    }

    private void OnMcpStatusChanged(object? sender, McpServerStatusChangedEventArgs e)
    {
        _ = sender;
        McpStatusChanged?.Invoke(e);
    }

    private void OnAutomationTaskUpdated(IAutomationTaskEventPayload task)
    {
        AutomationTaskUpdated?.Invoke(task);
    }

}

public interface IWorkspaceRuntimeFactory
{
    WorkspaceRuntime Create(IServiceProvider services);
}

internal sealed class WorkspaceRuntimeFactory : IWorkspaceRuntimeFactory
{
    public WorkspaceRuntime Create(IServiceProvider services)
    {
        return new WorkspaceRuntime(
            services,
            services.GetRequiredService<AppConfig>(),
            services.GetRequiredService<DotCraftPaths>(),
            services.GetRequiredService<MemoryStore>(),
            services.GetRequiredService<SkillsLoader>(),
            services.GetRequiredService<PathBlacklist>(),
            services.GetRequiredService<McpClientManager>(),
            services.GetRequiredService<LspServerManager>());
    }
}
