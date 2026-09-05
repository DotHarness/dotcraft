using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Cron;
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
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;

namespace DotCraft.Runtime;

/// <summary>
/// Owns the initialized DotCraft kernel for one workspace.
/// </summary>
public sealed class WorkspaceRuntime : IAsyncDisposable
{
    private sealed class StartedState(
        AgentFactory agentFactory,
        ThreadStore threadStore,
        ISessionService sessionService,
        ICommitMessageSuggester commitMessageSuggestService,
        IWelcomeSuggester welcomeSuggestions,
        WelcomeSuggestionService welcomeSuggestionService,
        AgentRunner agentRunner,
        CronService cronService,
        DreamsService dreamsService,
        DotNetPluginRuntimeManager pluginRuntime,
        IReadOnlyList<ConfigSchemaSection> configSchema,
        IContextPageManager contextPageManager)
    {
        public AgentFactory AgentFactory { get; } = agentFactory;

        public ThreadStore ThreadStore { get; } = threadStore;

        public ISessionService SessionService { get; } = sessionService;

        public ICommitMessageSuggester CommitMessageSuggestService { get; } = commitMessageSuggestService;

        public IWelcomeSuggester WelcomeSuggestions { get; } = welcomeSuggestions;

        /// <summary>The runtime-owned built-in, kept for disposal; consumers get the registry-resolving <see cref="WelcomeSuggestions"/>.</summary>
        public WelcomeSuggestionService WelcomeSuggestionService { get; } = welcomeSuggestionService;

        public AgentRunner AgentRunner { get; } = agentRunner;

        public CronService CronService { get; } = cronService;

        public DreamsService DreamsService { get; } = dreamsService;

        public DotNetPluginRuntimeManager PluginRuntime { get; } = pluginRuntime;

        public IReadOnlyList<ConfigSchemaSection> ConfigSchema { get; } = configSchema;

        public IContextPageManager ContextPageManager { get; } = contextPageManager;

    }

    private readonly IAppConfigMonitor _appConfigMonitor;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private WorkspaceContributionScope? _contributionScope;
    private StartedState? _started;
    private bool _disposed;

    /// <summary>
    /// Creates a runtime from the services registered by <see cref="ServiceRegistration.AddDotCraftRuntime"/>.
    /// </summary>
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
    }

    /// <summary>Gets the service graph owned by the embedding host.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Gets the effective runtime configuration.</summary>
    public AppConfig Config { get; }

    /// <summary>Gets the workspace paths owned by this runtime.</summary>
    public DotCraftPaths Paths { get; }

    public MemoryStore MemoryStore { get; }

    public SkillsLoader SkillsLoader { get; }

    public PathBlacklist PathBlacklist { get; }

    public McpClientManager McpClientManager { get; }

    public LspServerManager LspServerManager { get; }

    /// <summary>Gets whether startup completed successfully.</summary>
    public bool IsStarted => _started != null;

    public ISessionService SessionService => EnsureStarted().SessionService;

    /// <summary>Gets the session kernel owned by this runtime.</summary>
    public ISessionService Sessions => SessionService;

    public ICommitMessageSuggester CommitMessageSuggestService => EnsureStarted().CommitMessageSuggestService;

    public IWelcomeSuggester WelcomeSuggestionService => EnsureStarted().WelcomeSuggestions;

    public CronService CronService => EnsureStarted().CronService;

    public DreamsService DreamsService => EnsureStarted().DreamsService;

    public AgentRunner AgentRunner => EnsureStarted().AgentRunner;

    public IReadOnlyList<ConfigSchemaSection> ConfigSchema => EnsureStarted().ConfigSchema;

    public IContextPageManager ContextPageManager => EnsureStarted().ContextPageManager;

    public PlanStore? PlanStore => EnsureStarted().AgentFactory.PlanStore;

    public IRemoteToolHostClient? RemoteToolHostClient => EnsureStarted().AgentFactory.RemoteToolHostClient;

    public event Action<AppConfigChangedEventArgs>? WorkspaceConfigChanged;

    public event Action<McpServerStatusChangedEventArgs>? McpStatusChanged;

    public event Action<string, StructuredPlan>? PlanUpdated;

    public event Action<SessionThread>? ThreadStarted;

    public event Action<SessionThread>? ThreadRenamed;

    public event Action<SessionThread>? ThreadUpdated;

    public event Action<string>? ThreadDeleted;

    public event Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChanged;

    public event Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignal;

    public event Action<ThreadGoal, string?>? ThreadGoalUpdated;

    public event Action<string>? ThreadGoalCleared;

    public event Action<string, string>? SubAgentGraphChanged;

    /// <summary>
    /// Provisions and starts the workspace kernel with the supplied compiled modules.
    /// </summary>
    public async Task StartAsync(ModuleRegistry moduleRegistry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_started != null)
                throw new InvalidOperationException("WorkspaceRuntime has already been started.");

            SkillsLoader.DeployBuiltInSkills();
            Services.GetRequiredService<CustomCommandLoader>().DeployBuiltInCommands();
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

            var contributionRegistry = Services.GetRequiredService<ContributionRegistry>();
            _contributionScope = new WorkspaceContributionScope(contributionRegistry);
            _contributionScope.RegisterBuiltInCatalogs();
            _contributionScope.SeedContainerContributors(Services);
            _contributionScope.RegisterKernelContributions(
                Services.GetService<ThreadToolDispatchPolicyRegistry>(),
                Services.GetService<CommonToolApprovalEvaluator>(),
                Services.GetService<ToolInvocationRecorderRouter>(),
                Services.GetService<DefaultToolResultNormalizer>());

            ToolSourceCollector.ScanToolIcons(moduleRegistry, Config);

            var fallbackApproval = new AutoApproveApprovalService();
            var scopedApproval = new SessionScopedApprovalService(fallbackApproval);
            var remoteToolHostClient = Services
                .GetService<IRemoteToolHostClientFactory>()?
                .Create(scopedApproval);
            var planStore = new PlanStore(Paths.Data.RootPath, Services.GetRequiredService<WorkspaceStateDatabase>());
            var acpExtensionProxy = Services.GetService<IAcpExtensionProxy>();
            var nodeReplProxy = Services.GetService<INodeReplProxy>();
            var toolSources = new ToolSourceCollector(moduleRegistry, Services, Config)
                .CollectWithOrigins()
                .ToList();
            toolSources.Add(new CollectedToolSource(new CoreToolSource(
                Config,
                chatClientRegistry,
                SkillsLoader,
                scopedApproval,
                backgroundTerminalService,
                PathBlacklist,
                LspServerManager,
                traceCollector,
                Services.GetService<ISkillMutationApplier>(),
                contextPageManager,
                Paths.UserData.RootPath,
                contributionRegistry), ContributionOrigin.Builtin));
            if (Config.Tools.Sandbox.Enabled)
            {
                var sandboxProvider = Services.GetService<ISandboxProvider>()
                    ?? throw new InvalidOperationException(
                        "Sandbox is enabled, but no ISandboxProvider is registered. Register a sandbox backend before starting the workspace runtime.");
                toolSources.Add(new CollectedToolSource(new SandboxToolSource(
                    Config,
                    sandboxProvider,
                    chatClientRegistry,
                    SkillsLoader,
                    scopedApproval,
                    Path.GetFileName(Paths.Data.RootPath),
                    PathBlacklist,
                    traceCollector,
                    Services.GetService<ISkillMutationApplier>(),
                    contextPageManager,
                    Services.GetService<ILoggerFactory>(),
                    contributionRegistry), ContributionOrigin.Builtin));
            }
            if (nodeReplProxy != null)
            {
                toolSources.Add(new CollectedToolSource(
                    new NodeReplPluginToolSource(Config, nodeReplProxy, Paths.Data.RootPath),
                    ContributionOrigin.Builtin));
            }

            var effectiveToolSources = _contributionScope.RegisterToolSources(toolSources);

            AgentFactory? agentFactory = null;
            WelcomeSuggestionService? welcomeSuggestionService = null;
            DreamsService? dreamsService = null;
            DotNetPluginRuntimeManager? pluginRuntime = null;
            var pluginRuntimeStarted = false;
            try
            {
                await Services.InitializeServicesAsync();
                agentFactory = new AgentFactory(
                    Paths.Data.RootPath, Paths.WorkspacePath, Config,
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
                        BotPath = Paths.Data.RootPath,
                        UserDataPath = Paths.UserData.RootPath,
                        MemoryStore = MemoryStore,
                        DreamStore = Services.GetRequiredService<DreamStore>(),
                        SkillsLoader = SkillsLoader,
                        ContextPageManager = contextPageManager,
                        ThreadSystemPromptContextProviders = threadSystemPromptContextProviders,
                        RuntimeContextContributors = runtimeContextContributors,
                        Contributions = contributionRegistry,
                        ApprovalService = scopedApproval,
                        PathBlacklist = PathBlacklist,
                        BackgroundTerminalService = backgroundTerminalService,
                        CronTools = cronTools,
                        McpClientManager = McpClientManager,
                        LspServerManager = LspServerManager,
                        TraceCollector = traceCollector,
                        AcpExtensionProxy = acpExtensionProxy,
                        NodeReplProxy = nodeReplProxy
                    },
                    traceCollector: traceCollector,
                    planStore: planStore,
                    onPlanUpdated: (threadId, plan) => PlanUpdated?.Invoke(threadId, plan),
                    hookRunner: Services.GetService<HookRunner>(),
                    chatClientRegistry: chatClientRegistry,
                    toolDispatcher: Services.GetRequiredService<IToolDispatcher>(),
                    toolSources: effectiveToolSources,
                    loggerFactory: Services.GetService<ILoggerFactory>(),
                    remoteToolHostClient: remoteToolHostClient);

                _contributionScope.RegisterAgentContributions(agentFactory);

                var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
                var sessionService = SessionServiceFactory.Create(agentFactory, agent, Services);
                _contributionScope.AttachPropagation(sessionService, contextPageManager);
                sessionService.ThreadCreatedForBroadcast = thread => ThreadStarted?.Invoke(thread);
                sessionService.ThreadDeletedForBroadcast = threadId => ThreadDeleted?.Invoke(threadId);
                sessionService.ThreadRenamedForBroadcast = thread => ThreadRenamed?.Invoke(thread);
                sessionService.ThreadUpdatedForBroadcast = thread => ThreadUpdated?.Invoke(thread);
                sessionService.ThreadStatusChangedForBroadcast =
                    (threadId, previousStatus, newStatus) => ThreadStatusChanged?.Invoke(threadId, previousStatus, newStatus);
                var runtimeSignalObservers = Services.GetServices<IThreadRuntimeSignalObserver>().ToArray();
                var runtimeSignalDispatcher =
                    _contributionScope.AttachRuntimeSignals(Services.GetService<ILoggerFactory>());
                sessionService.ThreadRuntimeSignalForBroadcast =
                    (threadId, signal, turn) =>
                    {
                        // Enqueued first: the contribution point must not depend on a host observer returning.
                        runtimeSignalDispatcher.Publish(threadId, signal);
                        ThreadRuntimeSignal?.Invoke(threadId, signal, turn);
                        foreach (var observer in runtimeSignalObservers)
                            observer.OnThreadRuntimeSignal(Paths.Data.RootPath, threadId, signal);
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
                    Config,
                    Paths.Data.RootPath,
                    loggerFactory?.CreateLogger<WelcomeSuggestionService>());
                _contributionScope.RegisterSessionContributions(commitMessageSuggestService, welcomeSuggestionService);
                // Handed out instead of the built-ins so a caller that captures one — an open AppServer
                // connection, say — still observes a replacement registered after it captured.
                var commitMessageSuggest =
                    new ContributedCommitMessageSuggestService(contributionRegistry, commitMessageSuggestService);
                var welcomeSuggestions =
                    new ContributedWelcomeSuggestionService(contributionRegistry, welcomeSuggestionService);
                var cronService = Services.GetRequiredService<CronService>();
                var agentRunner = new AgentRunner(Paths.WorkspacePath, sessionService, quiet: true);

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

                pluginRuntime = Services.GetRequiredService<DotNetPluginRuntimeManager>();
                await pluginRuntime.StartAsync(ct);
                pluginRuntimeStarted = true;

                _started = new StartedState(
                    agentFactory,
                    Services.GetRequiredService<ThreadStore>(),
                    sessionService,
                    commitMessageSuggest,
                    welcomeSuggestions,
                    welcomeSuggestionService,
                    agentRunner,
                    cronService,
                    dreamsService,
                    pluginRuntime,
                    configSchema,
                    contextPageManager);

                _appConfigMonitor.Changed += OnAppConfigChanged;
                McpClientManager.StatusChanged += OnMcpStatusChanged;
            }
            catch
            {
                if (pluginRuntimeStarted && pluginRuntime != null)
                {
                    try
                    {
                        await pluginRuntime.StopAsync(CancellationToken.None);
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

    /// <summary>
    /// Stops active work and flushes runtime-owned state.
    /// </summary>
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

            // Plugin generations must stop while the kernel registries and provider services they
            // contribute into are still alive. WorkspaceRuntime owns this lifecycle for every host,
            // including custom DotCraft hosts that do not run Generic Host IHostedService entries.
            try
            {
                await started.PluginRuntime.StopAsync(ct);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            // Drop the registry subscriptions before tearing agents down, so the disposals below
            // cannot schedule an invalidation against a session service going away.
            try
            {
                _contributionScope?.Dispose();
                _contributionScope = null;
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
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

    /// <inheritdoc />
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

}

internal interface IWorkspaceRuntimeFactory
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
