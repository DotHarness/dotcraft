using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Configuration;
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

/// <summary>
/// Owns the initialized DotCraft kernel for one workspace.
/// </summary>
public sealed partial class WorkspaceRuntime : IAsyncDisposable
{
    private sealed class StartedState(
        AgentFactory agentFactory,
        ThreadStore threadStore,
        ISessionService sessionService,
        ICommitMessageSuggester commitMessageSuggestService,
        DreamsService dreamsService,
        DotNetExtensionRuntimeManager pluginRuntime,
        IReadOnlyList<ConfigSchemaSection> configSchema,
        IContextPageManager contextPageManager,
        ConfigurationService configuration,
        IAsyncDisposable? idleThreadUnloading)
    {
        public AgentFactory AgentFactory { get; } = agentFactory;

        public ThreadStore ThreadStore { get; } = threadStore;

        public ISessionService SessionService { get; } = sessionService;

        public ICommitMessageSuggester CommitMessageSuggestService { get; } = commitMessageSuggestService;

        public DreamsService DreamsService { get; } = dreamsService;

        public IAsyncDisposable? IdleThreadUnloading { get; } = idleThreadUnloading;

        public DotNetExtensionRuntimeManager PluginRuntime { get; } = pluginRuntime;

        public IReadOnlyList<ConfigSchemaSection> ConfigSchema { get; } = configSchema;

        public IContextPageManager ContextPageManager { get; } = contextPageManager;

        public ConfigurationService Configuration { get; } = configuration;

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


    public DreamsService DreamsService => EnsureStarted().DreamsService;


    public IReadOnlyList<ConfigSchemaSection> ConfigSchema => EnsureStarted().ConfigSchema;

    public IContextPageManager ContextPageManager => EnsureStarted().ContextPageManager;

    public ConfigurationService Configuration => EnsureStarted().Configuration;

    public PlanStore? PlanStore => EnsureStarted().AgentFactory.PlanStore;

    public IRemoteToolHostClient? RemoteToolHostClient => EnsureStarted().AgentFactory.RemoteToolHostClient;

    public event Action<AppConfigChangedEventArgs>? ConfigChanged;

    public event Action<McpServerStatusChangedEventArgs>? McpStatusChanged;

    public event Action<string, McpServerStatusSnapshot>? ThreadMcpStatusChanged;

    public event Action<string, StructuredPlan>? PlanUpdated;

    public event Action<SessionThread>? ThreadStarted;

    public event Action<SessionThread>? ThreadRenamed;

    public event Action<SessionThread>? ThreadUpdated;

    public event Action<string>? ThreadDeleted;

    public event Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChanged;

    public event Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignal;

    public event Action<ThreadGoal, string?>? ThreadGoalUpdated;

    public event Action<string>? ThreadGoalCleared;

    public event Action<string, ThreadReadState>? ThreadReadStateChanged;

    public event Action<string, string>? SubAgentGraphChanged;

    public event Action<string>? SubAgentBoardChanged;

    public event Action<ThreadTokenUsageUpdate>? ThreadTokenUsageUpdated;

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
                if (started.IdleThreadUnloading != null)
                    await started.IdleThreadUnloading.DisposeAsync();
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
                started.SessionService.ThreadCreatedForBroadcast = null;
                started.SessionService.ThreadDeletedForBroadcast = null;
                started.SessionService.ThreadRenamedForBroadcast = null;
                started.SessionService.ThreadUpdatedForBroadcast = null;
                started.SessionService.ThreadStatusChangedForBroadcast = null;
                started.SessionService.ThreadRuntimeSignalForBroadcast = null;
                if (started.SessionService is SessionService sessionService)
                {
                    sessionService.SubAgentGraphChangedForBroadcast = null;
                    sessionService.SubAgentBoardChangedForBroadcast = null;
                    sessionService.ThreadReadStateChangedForBroadcast = null;
                    sessionService.ThreadTokenUsageUpdatedForBroadcast = null;
                    sessionService.ThreadMcpStatusChangedForBroadcast = null;
                    await sessionService.DisposeAsync();
                }
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
        if (_started?.SessionService is IThreadAgentRefreshService refreshService
            && e.Regions.Contains(ConfigChangeRegions.ProviderRegistry))
        {
            refreshService.InvalidateThreadAgents();
        }
        ConfigChanged?.Invoke(e);
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
