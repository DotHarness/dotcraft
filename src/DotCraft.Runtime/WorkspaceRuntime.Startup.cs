using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Context.WorldState;
using DotCraft.Contributions;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Memory.Extraction;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Persistence;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;

namespace DotCraft.Runtime;

public sealed partial class WorkspaceRuntime
{
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

            var startupTimer = System.Diagnostics.Stopwatch.StartNew();
            foreach (var connection in Services.GetServices<IProviderConnectionLifecycle>())
                await connection.InitializeAsync(ct).ConfigureAwait(false);

            LogStartupStage(startupTimer, "provider-connections");

            if (Config.Skills.IncludeBuiltInSkills)
                SkillsLoader.DeployBuiltInSkills();
            Services.GetRequiredService<CustomCommandLoader>().DeployBuiltInCommands();
            SkillsLoader.SetDisabledSkills(Config.Skills.DisabledSkills);

            LogStartupStage(startupTimer, "built-in-skills-and-commands");

            var traceCollector = Services.GetService<TraceCollector>();
            var backgroundTerminalService = Services.GetRequiredService<IBackgroundTerminalService>();
            var chatClientRegistry = Services.GetRequiredService<ChatClientRegistry>();
            var contextPageManager = new ContextPageManager();
            var threadSystemPromptContextProviders = Services.GetServices<IThreadSystemPromptContextProvider>().ToArray();
            var worldStateSections = Services.GetServices<IWorldStateSection>().ToArray();

            var contributionRegistry = Services.GetRequiredService<ContributionRegistry>();
            _contributionScope = new WorkspaceContributionScope(contributionRegistry);
            _contributionScope.RegisterBuiltInCatalogs();
            _contributionScope.SeedContainerContributors(Services);
            _contributionScope.RegisterKernelContributions(
                Services.GetService<ThreadToolDispatchPolicyRegistry>(),
                Services.GetService<CommonToolApprovalEvaluator>(),
                Services.GetService<ToolInvocationRecorderRouter>(),
                Services.GetService<DefaultToolResultNormalizer>());

            var fallbackApproval = new AutoApproveApprovalService();
            var scopedApproval = new SessionScopedApprovalService(fallbackApproval);
            var remoteToolHostClient = Services
                .GetService<IRemoteToolHostClientFactory>()?
                .Create();
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
                Paths.UserData.RootPath,
                contributionRegistry,
                Services.GetService<IProviderHttpTransport>()), ContributionOrigin.Builtin));
            toolSources.Add(new CollectedToolSource(new ImageGenerationToolSource(
                Config,
                chatClientRegistry,
                scopedApproval,
                PathBlacklist,
                Paths.UserData.RootPath,
                remoteToolHostClient,
                Services.GetService<ILoggerFactory>()?.CreateLogger<ImageGenerationToolSource>()), ContributionOrigin.Builtin));
            toolSources.Add(new CollectedToolSource(
                new WebSearchToolSource(Config, chatClientRegistry),
                ContributionOrigin.Builtin));
            if (nodeReplProxy != null)
            {
                toolSources.Add(new CollectedToolSource(
                    new NodeReplPluginToolSource(Config, nodeReplProxy, Paths.Data.RootPath),
                    ContributionOrigin.Builtin));
            }

            var effectiveToolSources = _contributionScope.RegisterToolSources(toolSources);

            AgentFactory? agentFactory = null;
            DreamsService? dreamsService = null;
            DotNetPluginRuntimeManager? pluginRuntime = null;
            var pluginRuntimeStarted = false;
            try
            {
                LogStartupStage(startupTimer, "lazy-services-and-tool-composition");
                await Services.InitializeServicesAsync();
                LogStartupStage(startupTimer, "initialize-services");
                agentFactory = new AgentFactory(
                    Paths.Data.RootPath, Paths.WorkspacePath, Config,
                    MemoryStore, SkillsLoader,
                    approvalService: scopedApproval,
                    PathBlacklist,
                    runtimeContext: new AgentRuntimeContext
                    {
                        Config = Config,
                        ChatClientRegistry = chatClientRegistry,
                        WorkspacePath = Paths.WorkspacePath,
                        BotPath = Paths.Data.RootPath,
                        UserDataPath = Paths.UserData.RootPath,
                        MemoryStore = MemoryStore,
                        SkillsLoader = SkillsLoader,
                        ContextPageManager = contextPageManager,
                        ThreadSystemPromptContextProviders = threadSystemPromptContextProviders,
                        WorldStateSections = worldStateSections,
                        Contributions = contributionRegistry,
                        ApprovalService = scopedApproval,
                        PathBlacklist = PathBlacklist,
                        BackgroundTerminalService = backgroundTerminalService,
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

                var sessionService = SessionServiceFactory.Create(agentFactory, null, Services);
                _contributionScope.AttachPropagation(sessionService, contextPageManager, Services.GetRequiredService<ModelBackendResolver>());
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
                        if (turn is not null)
                            remoteToolHostClient?.ReportTurn(threadId, turn.Id, turn.Status);
                        ThreadRuntimeSignal?.Invoke(threadId, signal, turn);
                        foreach (var observer in runtimeSignalObservers)
                            observer.OnThreadRuntimeSignal(Paths.Data.RootPath, threadId, signal);
                    };
                sessionService.ThreadGoalUpdatedForBroadcast =
                    (goal, turnId) => ThreadGoalUpdated?.Invoke(goal, turnId);
                sessionService.ThreadGoalClearedForBroadcast =
                    threadId => ThreadGoalCleared?.Invoke(threadId);
                sessionService.ThreadReadStateChangedForBroadcast =
                    (threadId, readState) => ThreadReadStateChanged?.Invoke(threadId, readState);
                sessionService.SubAgentGraphChangedForBroadcast =
                    (parentThreadId, childThreadId) => SubAgentGraphChanged?.Invoke(parentThreadId, childThreadId);
                sessionService.SubAgentBoardChangedForBroadcast = rootThreadId => SubAgentBoardChanged?.Invoke(rootThreadId);
                sessionService.ThreadTokenUsageUpdatedForBroadcast =
                    update => ThreadTokenUsageUpdated?.Invoke(update);

                var loggerFactory = Services.GetService<ILoggerFactory>();
                var commitMessageSuggestService =
                    new CommitMessageSuggestService(
                        sessionService,
                        Paths.WorkspacePath,
                        loggerFactory?.CreateLogger<CommitMessageSuggestService>(),
                        () => _appConfigMonitor.Current.SourceControl);
                var memoryExtraction = new MemoryExtractionScheduler(
                    sessionService.GetThreadAsync,
                    (thread, ct) => sessionService.ReadThreadTurnsAsync(
                        thread.Id, ThreadHistorySortDirection.Descending, ct: ct),
                    new MemoryExtractionRunner(
                        sessionService,
                        contributionRegistry,
                        MemoryStore,
                        Paths.Data.RootPath,
                        traceCollector,
                        loggerFactory?.CreateLogger<MemoryExtractionRunner>()),
                    loggerFactory?.CreateLogger<MemoryExtractionScheduler>());
                _contributionScope.RegisterSessionContributions(
                    commitMessageSuggestService,
                    memoryExtraction);
                // Handed out instead of the built-in so a caller that captures it — an open AppServer
                // connection, say — still observes a replacement registered after it captured.
                var commitMessageSuggest =
                    new ContributedCommitMessageSuggestService(contributionRegistry, commitMessageSuggestService);

                var configSchema = Services.GetService<IConfigSchemaProvider>()?.GetConfigSchema()
                    ?? throw new InvalidOperationException(
                        "IConfigSchemaProvider is not registered. Hosts that use WorkspaceRuntime must register the generated ConfigSchemaRegistrations.CreateSchemaProvider() instance.");
                var dreamsRunner = new DreamsSessionRunner(
                    sessionService,
                    Services.GetRequiredService<SessionPersistenceService>(),
                    chatClientRegistry,
                    Config,
                    Services.GetRequiredService<DreamsRunRegistry>(),
                    loggerFactory?.CreateLogger<DreamsSessionRunner>());
                dreamsService = new DreamsService(
                    Config,
                    Paths,
                    Services.GetRequiredService<ThreadStore>(),
                    dreamsRunner,
                    _appConfigMonitor,
                    loggerFactory?.CreateLogger<DreamsService>());

                var descriptors = Services.GetService<IConfigDescriptorRegistry>()
                    ?? throw new InvalidOperationException(
                        "IConfigDescriptorRegistry is not registered. Hosts that use WorkspaceRuntime must register the generated ConfigSchemaRegistrations.CreateDescriptorRegistry() instance.");
                var configuration = new ConfigurationService(
                    descriptors,
                    _appConfigMonitor,
                    _appConfigMonitor.Current.GlobalConfigPath
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft", "config.json"),
                    Path.Combine(Paths.Data.RootPath, "config.json"));
                ConfigurationSubsystems.Register(
                    configuration,
                    _appConfigMonitor,
                    sessionService as IThreadAgentRefreshService,
                    SkillsLoader,
                    LspServerManager,
                    dreamsService,
                    contextPageManager);

                LogStartupStage(startupTimer, "session-kernel-and-configuration");
                pluginRuntime = Services.GetRequiredService<DotNetPluginRuntimeManager>();
                await pluginRuntime.StartAsync(ct);
                pluginRuntimeStarted = true;
                LogStartupStage(startupTimer, "dotnet-plugin-activation");
                await dreamsService.StartAsync(ct);

                _started = new StartedState(
                    agentFactory,
                    Services.GetRequiredService<ThreadStore>(),
                    sessionService,
                    commitMessageSuggest,
                    dreamsService,
                    pluginRuntime,
                    configSchema,
                    contextPageManager,
                    configuration,
                    (sessionService as SessionService)?.StartIdleThreadUnloading(),
                    Services.GetRequiredService<ThreadStore>().StartBackgroundUpgrade());

                _appConfigMonitor.Changed += OnAppConfigChanged;
                McpClientManager.StatusChanged += OnMcpStatusChanged;
                LogStartupStage(startupTimer, "runtime-finalize");
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

    private void LogStartupStage(System.Diagnostics.Stopwatch timer, string stage)
    {
        Services.GetService<ILogger<WorkspaceRuntime>>()?.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms", stage, timer.Elapsed.TotalMilliseconds);
        timer.Restart();
    }
}
