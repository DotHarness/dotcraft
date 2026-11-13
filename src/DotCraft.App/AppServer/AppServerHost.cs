using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.AppBinding;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Text;
using DotCraft.Logging;
using DotCraft.Hosting;
using DotCraft.Runtime;
using DotCraft.Hooks;
using DotCraft.GenerativeUi;
using DotCraft.Visualizations;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Automations.Protocol;
using DotCraft.Automations;
using DotCraft.Tracing;
using DotCraft.ExternalChannel;
using DotCraft.DynamicWorkflows;
using DotCraft.SessionImport;
using Contract = DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;
using ThreadGoalSnapshot = DotCraft.Sessions.ThreadGoalSnapshot;

namespace DotCraft.AppServer;

/// <summary>
/// Host for AppServer mode.
/// Runs a stdio JSON-RPC 2.0 server that exposes <see cref="ISessionService"/> over the
/// Session Wire Protocol. When <see cref="WebSocketServerConfig"/> is present in configuration,
/// additionally starts a WebSocket listener that accepts multiple concurrent connections,
/// each with an isolated <see cref="AppServerConnection"/> sharing the same session service.
/// </summary>
public sealed partial class AppServerHost(
    WorkspaceRuntime runtime,
    ExternalChannelRegistry? externalChannelRegistry = null) : IDotCraftHost
{
    private readonly IServiceProvider _services = runtime.Services;
    private readonly ILoggerFactory _loggerFactory = runtime.Services.GetRequiredService<ILoggerFactory>();
    private readonly ILogger<AppServerHost> _logger = runtime.Services.GetRequiredService<ILogger<AppServerHost>>();
    private IWorkspaceRuntimeAppServerFeature? _appServerFeature;
    private RemoteToolHostRouteBroadcaster? _remoteToolHostRouteBroadcaster;

    private WireAcpExtensionProxy WireAcpExtensionProxy =>
        _services.GetRequiredService<WireAcpExtensionProxy>();

    private WireNodeReplProxy WireNodeReplProxy =>
        _services.GetRequiredService<WireNodeReplProxy>();

    private WireDynamicToolProxy WireDynamicToolProxy =>
        _services.GetRequiredService<WireDynamicToolProxy>();

    private IAppServerChannelListContributor ChannelListContributor =>
        new ModuleRegistryChannelListContributor(
            _services.GetRequiredService<ModuleRegistry>(), runtime.Config);

    /// <summary>
    /// Thread-safe set of currently connected transports. Used to broadcast
    /// out-of-band notifications (e.g. <c>plan/updated</c>) to all clients.
    /// </summary>
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _activeTransports = new();
    private readonly ConcurrentDictionary<IAppServerTransport, Lazy<OrderedAppServerNotificationQueue>> _terminalNotificationQueues = new();
    private readonly ConcurrentDictionary<IAppServerTransport, Lazy<OrderedAppServerNotificationQueue>> _pluginNotificationQueues = new();
    private readonly ThreadRuntimeNotificationCoordinator _threadRuntimeNotifications = new();
    private readonly HiddenThreadAudience _hiddenThreads = new(threadId => runtime.SessionService.IsHiddenThread(threadId));

    private IReadOnlyList<IAppServerProtocolExtension> ProtocolExtensions =>
        runtime.Services.GetServices<IAppServerProtocolExtension>().ToArray();

    private AppServerLockInfo CreateLockInfo(AppServerConfig appServerConfig)
        => new(
            Pid: Environment.ProcessId,
            WorkspacePath: runtime.Paths.WorkspacePath,
            ManagedByHub: ManagedAppServerEnvironment.IsManaged,
            HubApiBaseUrl: Environment.GetEnvironmentVariable(ManagedAppServerEnvironment.HubApiBaseUrl),
            StartedAt: DateTimeOffset.UtcNow,
            Version: AppVersion.Informational,
            Endpoints: BuildEndpointDictionary(appServerConfig));

    private IReadOnlyDictionary<string, string> BuildEndpointDictionary(AppServerConfig appServerConfig)
    {
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if ((appServerConfig.Mode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket)
            && appServerConfig.WebSocket.Port != 0)
        {
            endpoints["appServerWebSocket"] =
                BuildWebSocketEndpoint(appServerConfig.WebSocket.Host, appServerConfig.WebSocket.Port, appServerConfig.WebSocket.Token);
        }

        if (runtime.Config.DashBoard.Enabled && runtime.Config.Tracing.Enabled)
        {
            endpoints["dashboard"] =
                $"http://{runtime.Config.DashBoard.Host}:{runtime.Config.DashBoard.Port}/dashboard";
        }

        return endpoints;
    }

    private static string BuildWebSocketEndpoint(string host, int port, string? token)
    {
        var url = $"ws://{host}:{port}/ws";
        return string.IsNullOrEmpty(token) ? url : $"{url}?token={Uri.EscapeDataString(token)}";
    }

    private void SubscribeRuntimeEvents()
    {
        runtime.ConfigChanged += BroadcastConfigChanged;
        runtime.McpStatusChanged += OnRuntimeMcpStatusChanged;
        runtime.ThreadMcpStatusChanged += BroadcastThreadMcpStatusChanged;
        runtime.PlanUpdated += BroadcastPlanUpdated;
        runtime.ThreadStarted += BroadcastThreadStarted;
        runtime.ThreadRenamed += BroadcastThreadRenamed;
        runtime.ThreadUpdated += BroadcastThreadUpdated;
        runtime.ThreadDeleted += BroadcastThreadDeleted;
        runtime.ThreadStatusChanged += BroadcastThreadStatusChanged;
        runtime.ThreadRuntimeSignal += OnThreadRuntimeSignal;
        runtime.ThreadGoalUpdated += BroadcastThreadGoalUpdated;
        runtime.ThreadGoalCleared += BroadcastThreadGoalCleared;
        runtime.ThreadReadStateChanged += BroadcastThreadReadStateChanged;
        runtime.SubAgentGraphChanged += BroadcastSubAgentGraphChanged;
        runtime.SubAgentBoardChanged += BroadcastSubAgentBoardChanged;
        runtime.ThreadTokenUsageUpdated += BroadcastThreadTokenUsageUpdated;
        if (_services.GetService<DynamicWorkflowService>() is { } workflows)
            workflows.RunChanged += BroadcastWorkflowRunUpdated;
        if (_services.GetService<SessionImportService>() is { } imports)
        {
            imports.Progress += BroadcastImportProgress;
            imports.Completed += BroadcastImportCompleted;
        }
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent += BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged += BroadcastOpenAiUsageChanged;
        if (_services.GetService<AppBindingCoordinator>() is { } bindings)
            bindings.BindingStatusChanged += BroadcastAppBindingStatusChanged;
        if (_services.GetService<IDotNetExtensionRuntimeCoordinator>() is { } plugins)
            plugins.SnapshotChanged += BroadcastPluginRuntimeSnapshotChanged;
        if (runtime.RemoteToolHostClient is { } remoteToolHostClient)
        {
            _remoteToolHostRouteBroadcaster = new RemoteToolHostRouteBroadcaster(
                remoteToolHostClient,
                BroadcastRemoteToolHostRouteChanged);
        }
    }

    private void UnsubscribeRuntimeEvents()
    {
        runtime.ConfigChanged -= BroadcastConfigChanged;
        runtime.McpStatusChanged -= OnRuntimeMcpStatusChanged;
        runtime.ThreadMcpStatusChanged -= BroadcastThreadMcpStatusChanged;
        runtime.PlanUpdated -= BroadcastPlanUpdated;
        runtime.ThreadStarted -= BroadcastThreadStarted;
        runtime.ThreadRenamed -= BroadcastThreadRenamed;
        runtime.ThreadUpdated -= BroadcastThreadUpdated;
        runtime.ThreadDeleted -= BroadcastThreadDeleted;
        runtime.ThreadStatusChanged -= BroadcastThreadStatusChanged;
        runtime.ThreadRuntimeSignal -= OnThreadRuntimeSignal;
        runtime.ThreadGoalUpdated -= BroadcastThreadGoalUpdated;
        runtime.ThreadGoalCleared -= BroadcastThreadGoalCleared;
        runtime.ThreadReadStateChanged -= BroadcastThreadReadStateChanged;
        runtime.SubAgentGraphChanged -= BroadcastSubAgentGraphChanged;
        runtime.SubAgentBoardChanged -= BroadcastSubAgentBoardChanged;
        runtime.ThreadTokenUsageUpdated -= BroadcastThreadTokenUsageUpdated;
        if (_services.GetService<DynamicWorkflowService>() is { } workflows)
            workflows.RunChanged -= BroadcastWorkflowRunUpdated;
        if (_services.GetService<SessionImportService>() is { } imports)
        {
            imports.Progress -= BroadcastImportProgress;
            imports.Completed -= BroadcastImportCompleted;
        }
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent -= BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged -= BroadcastOpenAiUsageChanged;
        if (_services.GetService<AppBindingCoordinator>() is { } bindings)
            bindings.BindingStatusChanged -= BroadcastAppBindingStatusChanged;
        if (_services.GetService<IDotNetExtensionRuntimeCoordinator>() is { } plugins)
            plugins.SnapshotChanged -= BroadcastPluginRuntimeSnapshotChanged;
        _remoteToolHostRouteBroadcaster?.Dispose();
        _remoteToolHostRouteBroadcaster = null;
    }

    private AppServerRequestHandler CreateRequestHandler(
        IAppServerTransport transport,
        AppServerConnection connection)
    {
        var threadSubscribers = _services.GetRequiredService<AppServerThreadSubscribers>();
        threadSubscribers.Register(transport, connection);
        return new AppServerRequestHandler(
            runtime.SessionService,
            connection,
            transport,
            ChannelListContributor,
            new AppServerConnectionServices
            {
                CommandRegistry = runtime.Services.GetService<DotCraft.Commands.Core.CommandRegistry>(),
                SupportsUltraReasoning = runtime.Services
                    .GetServices<DotCraft.Modules.IRuntimeCapabilityProvider>()
                    .Any(static capability => capability.Capability == "dynamicWorkflows" && capability.IsAvailable),
                PluginWorkflowSummaryProvider = runtime.Services.GetService<DotCraft.Plugins.IPluginWorkflowSummaryProvider>(),
                DotNetExtensionRuntimeCoordinator = runtime.Services
                    .GetService<DotCraft.Plugins.IDotNetExtensionRuntimeCoordinator>(),
                PluginConfigStore = runtime.Services.GetService<DotCraft.Plugins.PluginConfigStore>(),
                PluginManagementState = _services.GetRequiredService<AppServerPluginManagementState>(),
                BroadcastPluginSnapshotUpdated = BroadcastPluginSnapshotUpdated,
                RemoteToolHostClient = runtime.RemoteToolHostClient,
                ServerVersion = AppVersion.Informational,
                SkillsLoader = runtime.SkillsLoader,
                MemoryStore = runtime.MemoryStore,
                WorkspaceCraftPath = runtime.Paths.Data.RootPath,
                WorkspaceTempPath = runtime.Paths.WorkspaceTempPath,
                HostWorkspacePath = runtime.Paths.WorkspacePath,
                AutomationsHandler = _services.GetService<IAutomationsRequestHandler>(),
                CommitMessageSuggest = runtime.CommitMessageSuggestService,
                DashboardUrl = _appServerFeature?.DashboardUrl,
                WireAcpExtensionProxy = WireAcpExtensionProxy,
                WireNodeReplProxy = WireNodeReplProxy,
                WireDynamicToolProxy = WireDynamicToolProxy,
                ChannelStatusProvider = _appServerFeature?.ChannelStatusProvider,
                McpClientManager = runtime.McpClientManager,
                McpAppTransientContextStore = _services.GetService<McpAppTransientContextStore>(),
                VisualizationAssetStore = _services.GetService<VisualizationAssetStore>(),
                VisualizationRuntimeRegistry = _services.GetService<VisualizationRuntimeRegistry>(),
                GenerativeUiRuntimeRegistry = _services.GetService<GenerativeUiRuntimeRegistry>(),
                ThreadSubscribers = threadSubscribers,
                LspServerManager = runtime.LspServerManager,
                BroadcastMcpStatusChanged = BroadcastMcpStatusChanged,
                NotifyAppPrincipal = NotifyAppPrincipal,
                BroadcastTrustedNotification = BroadcastTrustedNotification,
                ProtocolExtensions = ProtocolExtensions,
                OnExternalChannelUpserted = _appServerFeature is null
                    ? null
                    : (entry, ct) => _appServerFeature.ApplyExternalChannelUpsertAsync(entry, ct),
                OnExternalChannelRemoved = _appServerFeature is null
                    ? null
                    : (name, ct) => _appServerFeature.ApplyExternalChannelRemoveAsync(name, ct),
                ExternalChannelRuntimeProvider = _appServerFeature?.ExternalChannelRuntimeProvider,
                StreamDebugLogger = _services.GetService<SessionStreamDebugLogger>(),
                LoggerFactory = _services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
                ConfigSchema = runtime.ConfigSchema,
                Configuration = runtime.Configuration,
                AppConfigMonitor = _services.GetRequiredService<IAppConfigMonitor>(),
                RuntimeRefreshers = _services.GetServices<ISessionRuntimeRefresher>().ToArray(),
                ChatClientRegistry = _services.GetRequiredService<ChatClientRegistry>(),
                ModelProviderRegistry = _services.GetRequiredService<ModelProviderRegistry>(),
                ProviderAuthentication = _services.GetService<ProviderAuthenticationCoordinator>(),
                BackgroundTerminalService = _services.GetService<IBackgroundTerminalService>(),
                ContextPageManager = runtime.ContextPageManager,
                DreamsService = runtime.DreamsService,
                AppBindingService = _services.GetService<AppBindingService>(),
                ThreadOriginPresentationProviders = _services.GetServices<IThreadOriginPresentationProvider>().ToArray(),
                PlanStore = runtime.PlanStore,
                TraceStore = _services.GetService<TraceStore>(),
                UsageAnalytics = _services.GetService<UsageAnalyticsService>(),
                WireRuntimeAdditionalContextProvider = _services.GetService<WireRuntimeAdditionalContextProvider>(),
                HookRunner = _services.GetService<HookRunner>(),
                Contributions = runtime.Services.GetService<DotCraft.Contributions.IContributionView>(),
            });
    }

    private async Task RunStdioOnlyAsync(CancellationToken cancellationToken)
    {
        var connection = new AppServerConnection();
        await using var transport = StdioTransport.CreateStdio(_logger, connection.ConnectionId);
        transport.Start();
        _activeTransports.TryAdd(transport, connection);

        var handler = CreateRequestHandler(transport, connection);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");
        _logger.LogInformation("AppServer started with stdio transport");
        LogStartupStage("stdio-transport-ready");

        try
        {
            await RunLoopAsync(
                transport, connection, handler,
                WireAcpExtensionProxy, WireNodeReplProxy, WireDynamicToolProxy,
                _services.GetService<WireRuntimeAdditionalContextProvider>(),
                _services.GetService<VisualizationRuntimeRegistry>(),
                _services.GetService<GenerativeUiRuntimeRegistry>(),
                runtime.ContextPageManager,
                runtime.SessionService as IThreadAgentRefreshService,
                _logger,
                cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
            RemoveTerminalNotificationQueue(transport);
            RemovePluginNotificationQueue(transport);
            _threadRuntimeNotifications.RemoveTransport(transport);
        }
    }

    private static async Task RunLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        WireAcpExtensionProxy? wireAcpProxy,
        WireNodeReplProxy? wireNodeReplProxy,
        WireDynamicToolProxy? wireDynamicToolProxy,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider,
        VisualizationRuntimeRegistry? visualizationRuntimeRegistry,
        GenerativeUiRuntimeRegistry? generativeUiRuntimeRegistry,
        IContextPageManager? contextPageManager,
        IThreadAgentRefreshService? threadAgentRefreshService,
        ILogger logger,
        CancellationToken ct)
    {
        await AppServerRequestDispatcher.RunAsync(transport, connection, handler, logger, ct);

        // Active persisted turns are drained independently by the request handler.
        connection.CancelAllSubscriptions();
        wireAcpProxy?.UnbindTransport(transport);
        wireNodeReplProxy?.UnbindTransport(transport);
        wireDynamicToolProxy?.UnbindTransport(transport);
        if (visualizationRuntimeRegistry != null)
        {
            foreach (var threadId in visualizationRuntimeRegistry.UnbindTransport(transport))
            {
                contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.Visualization());
                if (threadAgentRefreshService != null)
                    await RefreshThreadAgentAfterCapabilityCleanupAsync(threadAgentRefreshService, threadId, logger);
            }
        }
        if (generativeUiRuntimeRegistry != null)
        {
            foreach (var threadId in generativeUiRuntimeRegistry.UnbindConnection(connection))
            {
                contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.GenerativeUi());
                if (threadAgentRefreshService != null)
                    await RefreshThreadAgentAfterCapabilityCleanupAsync(threadAgentRefreshService, threadId, logger);
            }
        }
        if (wireRuntimeAdditionalContextProvider != null)
        {
            foreach (var threadId in wireRuntimeAdditionalContextProvider.UnbindTransport(transport))
                contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.RuntimeAdditionalContext());
        }
    }

    internal static async Task RefreshThreadAgentAfterCapabilityCleanupAsync(
        IThreadAgentRefreshService refreshService,
        string threadId,
        ILogger logger)
    {
        try
        {
            await refreshService.RefreshThreadAgentAsync(threadId, CancellationToken.None);
        }
        catch (KeyNotFoundException ex) when (
            ex.Message.Contains($"Thread '{threadId}' not found", StringComparison.Ordinal))
        {
            logger.LogDebug(ex, "Skipped capability cleanup refresh for deleted thread {ThreadId}.", threadId);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("permanently deleted", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(ex, "Skipped capability cleanup refresh while thread {ThreadId} is being deleted.", threadId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var transport in _terminalNotificationQueues.Keys.ToArray())
            RemoveTerminalNotificationQueue(transport);
        foreach (var transport in _pluginNotificationQueues.Keys.ToArray())
            RemovePluginNotificationQueue(transport);
        await _threadRuntimeNotifications.CompleteAsync();
        await runtime.DisposeAsync();
    }

    private void BroadcastPluginRuntimeSnapshotChanged(
        object? sender,
        PluginRuntimeSnapshotChangedEventArgs args)
    {
        var management = _services.GetRequiredService<AppServerPluginManagementState>();
        if (management.IsMutationInProgress)
            return;

        BroadcastPluginSnapshotUpdated(new Contract.PluginSnapshotUpdatedNotification
        {
            SnapshotRevision = management.AdvanceSnapshotRevision(args.Snapshot.Revision),
            PluginIds = args.PluginIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static pluginId => pluginId, StringComparer.Ordinal)
                .ToArray()
        });
    }

    private void BroadcastPluginSnapshotUpdated(Contract.PluginSnapshotUpdatedNotification parameters)
        => BroadcastPluginSnapshotUpdated(sourceTransport: null, parameters, Task.CompletedTask);

    private void StartPluginRegistrySync(CancellationToken cancellationToken)
    {
        var craftHome = runtime.Paths.UserData.RootPath;
        if (string.IsNullOrWhiteSpace(craftHome))
            return;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var sync = new PluginRegistrySyncService(
                        craftHome,
                        diagnostic => _logger.LogWarning(
                            "Plugin registry sync: {Code}: {Message}",
                            diagnostic.Code,
                            diagnostic.Message));
                    if (!await sync.SyncDueAsync(runtime.Config.Plugins, force: false, cancellationToken)
                            .ConfigureAwait(false))
                        return;

                    var management = _services.GetRequiredService<AppServerPluginManagementState>();
                    BroadcastPluginSnapshotUpdated(new Contract.PluginSnapshotUpdatedNotification
                    {
                        SnapshotRevision = management.AdvanceSnapshotRevision(0),
                        PluginIds = Array.Empty<string>()
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Plugin registry sync failed");
                }
            },
            cancellationToken);
    }

    private void BroadcastPluginSnapshotUpdated(
        IAppServerTransport? sourceTransport,
        Contract.PluginSnapshotUpdatedNotification parameters,
        Task sourceResponseCompleted)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.PluginSnapshotUpdated.Name))
                continue;

            var queue = _pluginNotificationQueues.GetOrAdd(
                transport,
                candidate => new Lazy<OrderedAppServerNotificationQueue>(
                    () => new OrderedAppServerNotificationQueue(
                        candidate,
                        () =>
                        {
                            _activeTransports.TryRemove(candidate, out _);
                            RemovePluginNotificationQueue(candidate);
                        }),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            if (ReferenceEquals(transport, sourceTransport))
            {
                queue.Value.Enqueue(
                    Contract.AppServerRpc.PluginSnapshotUpdated.Name,
                    parameters,
                    sourceResponseCompleted);
            }
            else
            {
                queue.Value.Enqueue(Contract.AppServerRpc.PluginSnapshotUpdated.Name, parameters);
            }
        }
    }

    private void BroadcastRemoteToolHostRouteChanged(Contract.RemoteToolHostRouteChangedNotification parameters)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.RemoteToolHostRouteChanged.Name))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(
                        Contract.AppServerRpc.RemoteToolHostRouteChanged,
                        parameters,
                        CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void RemovePluginNotificationQueue(IAppServerTransport transport)
    {
        if (_pluginNotificationQueues.TryRemove(transport, out var queue) && queue.IsValueCreated)
            queue.Value.Complete();
    }

    private void OnBackgroundJobResultProduced(BackgroundJobResult result)
    {
        BroadcastJobResult(
            result.Source,
            result.JobId,
            result.JobName,
            result.Result,
            result.Error,
            result.ThreadId,
            result.InputTokens,
            result.OutputTokens);
    }

    /// <summary>
    /// Broadcasts a <c>system/jobResult</c> JSON-RPC notification to all connected transports.
    /// Called when a server-managed background job completes and the job was created from
    /// a CLI (non-social-channel) context. See spec Section 6.9.
    /// </summary>
    private void BroadcastJobResult(
        string source,
        string? jobId,
        string? jobName,
        string? result,
        string? error,
        string? threadId = null,
        int? inputTokens = null,
        int? outputTokens = null)
    {
        Contract.SystemJobTokenUsage? tokenUsage = null;
        if (inputTokens.HasValue || outputTokens.HasValue)
        {
            tokenUsage = new Contract.SystemJobTokenUsage
            {
                InputTokens = inputTokens ?? 0,
                OutputTokens = outputTokens ?? 0
            };
        }

        var parameters = new Contract.SystemJobResultNotification
        {
            Source = source,
            JobId = jobId,
            JobName = jobName,
            ThreadId = threadId,
            Result = result,
            Error = error,
            TokenUsage = tokenUsage
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.SystemJobResult))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.SystemJobResult, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void NotifyAppPrincipal(string appId, string method, object? parameters)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.IsAppPrincipalAuthenticated
                || !string.Equals(connection.AppPrincipalAppId, appId, StringComparison.Ordinal))
                continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(method, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

    private void BroadcastTrustedNotification(string method, object? parameters)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (connection.IsAppPrincipalAuthenticated || connection.IsChannelAdapter) continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(method, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

    private void BroadcastAppBindingStatusChanged(AppBindingStatusChanged change) =>
        BroadcastTrustedNotification(
            Contract.AppServerRpc.ThreadAppBindingsChanged.Name,
            new Contract.ThreadAppBindingsChangedNotification
            {
                ThreadId = change.ThreadId,
                BindingId = change.BindingId,
                AppId = change.AppId,
                State = change.State,
                FailureReason = change.FailureReason,
                AuthorityRevision = change.AuthorityRevision
            });

    private void BroadcastOpenAiUsageChanged(DotCraft.Auth.OpenAI.OpenAIUsageSnapshot? snapshot)
    {
        var notifications = ProviderUsageMapping.ToChatGptNotifications(
            _services.GetRequiredService<IAppConfigMonitor>().Current,
            DotCraft.Auth.OpenAI.OpenAIProviderProjection.ToProviderUsage(snapshot));
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ProviderAuthUsageUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var notification in notifications)
                        await transport.NotifyContractAsync(Contract.AppServerRpc.ProviderAuthUsageUpdated, notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastConfigChanged(AppConfigChangedEventArgs change)
    {
        var parameters = new Contract.ConfigChangedParams
        {
            Source = change.Source,
            Regions = change.Regions.ToArray(),
            ChangedAt = change.ChangedAt
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsConfigChange || !connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ConfigChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastBackgroundTerminalEvent(BackgroundTerminalEvent evt)
    {
        var method = ResolveBackgroundTerminalNotificationMethod(evt.EventType);
        if (method is null)
            return;

        var parameters = new Contract.TerminalLifecycleNotification
        {
            Terminal = TerminalContractMapper.ToContract(evt.Terminal),
            Delta = evt.Delta
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsBackgroundTerminals || !connection.ShouldSendNotification(method))
                continue;

            var queue = _terminalNotificationQueues.GetOrAdd(
                transport,
                candidate => new Lazy<OrderedAppServerNotificationQueue>(
                    () => new OrderedAppServerNotificationQueue(
                        candidate,
                        () =>
                        {
                            _activeTransports.TryRemove(candidate, out _);
                            RemoveTerminalNotificationQueue(candidate);
                        }),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            queue.Value.Enqueue(method, parameters);
        }
    }

    internal static string? ResolveBackgroundTerminalNotificationMethod(string eventType) =>
        eventType switch
        {
            "started" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStarted,
            "outputDelta" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta,
            "completed" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCompleted,
            "stalled" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStalled,
            "cleaned" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCleaned,
            _ => null
        };

    private void RemoveTerminalNotificationQueue(IAppServerTransport transport)
    {
        if (_terminalNotificationQueues.TryRemove(transport, out var queue) && queue.IsValueCreated)
            queue.Value.Complete();
    }

    /// <summary>
    /// Broadcasts <c>thread/started</c> to all connected transports when any channel creates a thread
    /// in the shared <see cref="SessionService"/> (so Desktop sidebar updates without polling).
    /// </summary>
    private void BroadcastThreadStarted(SessionThread thread)
    {
        _hiddenThreads.Observe(thread);
        var wire = thread.ToWire() with
        {
            Runtime = runtime.SessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState()
        };
        var parameters = new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(wire)
        };

        var skipTransport = !IsSubAgentThread(thread)
                            && (string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, StringComparison.Ordinal)
                                || string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndStart, StringComparison.Ordinal))
            ? AppServerRequestContext.CurrentTransport
            : null;

        _threadRuntimeNotifications.BroadcastThreadStarted(parameters, _activeTransports,
            (transport, connection) => !ReferenceEquals(transport, skipTransport) && _hiddenThreads.Accepts(thread.Id, connection));
    }

    private void BroadcastSubAgentGraphChanged(string parentThreadId, string childThreadId)
    {
        var parameters = new Contract.SubAgentGraphChangedNotification
        {
            ParentThreadId = parentThreadId,
            ChildThreadId = childThreadId
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentGraphChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.SubAgentGraphChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastThreadTokenUsageUpdated(ThreadTokenUsageUpdate update) =>
        _services.GetRequiredService<AppServerThreadSubscribers>().NotifyThreadTokenUsageUpdated(update);

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private void BroadcastThreadGoalUpdated(ThreadGoal goal, string? turnId)
    {
        var wireGoal = AppServerContractMapper.ToContract(ThreadGoalSnapshot.FromGoal(goal));
        var parameters = new Contract.ThreadGoalUpdatedNotification
        {
            ThreadId = goal.ThreadId,
            Goal = DotCraft.Protocol.Optional<Contract.ThreadGoal?>.FromValue(wireGoal),
            TurnId = turnId is null
                ? default
                : DotCraft.Protocol.Optional<string?>.FromValue(turnId)
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadGoalUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastThreadReadStateChanged(string threadId, ThreadReadState readState)
    {
        var parameters = new Contract.ThreadReadStateChangedNotification
        {
            ThreadId = threadId,
            ReadState = AppServerContractMapper.ToContract(readState)
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadReadStateChanged.Name))
                continue;

            // Started synchronously so the write is queued ahead of the Turn's terminal notification.
            _ = transport.NotifyContractAsync(Contract.AppServerRpc.ThreadReadStateChanged, parameters, CancellationToken.None)
                .ContinueWith(
                    task => _activeTransports.TryRemove(transport, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }
    }

    private void BroadcastThreadGoalCleared(string threadId)
    {
        var parameters = new Contract.ThreadGoalClearedNotification { ThreadId = threadId };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalCleared))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadGoalCleared, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void OnThreadRuntimeSignal(string threadId, SessionThreadRuntimeSignal signal, SessionTurn? turn)
    {
        var thread = runtime.SessionService.TryGetLoadedThread(threadId);
        if (thread is not null && _threadRuntimeNotifications.ApplySnapshot(
                threadId,
                runtime.SessionService.GetThreadRuntimeSnapshot(thread),
                _activeTransports,
                connection => _hiddenThreads.Accepts(threadId, connection)))
            RequestHubTurnNotification(threadId, signal);
    }

    private void RequestHubTurnNotification(string threadId, SessionThreadRuntimeSignal signal)
    {
        var spec = HubTurnNotificationPolicy.GetSpec(signal);
        if (spec is null)
            return;

        _ = Task.Run(async () =>
        {
            var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(runtime.SessionService, threadId);
            if (!decision.ShouldNotify)
                return;

            var actionUrl = decision.OpenDesktopOnClick && !string.IsNullOrWhiteSpace(decision.ThreadId)
                ? HubTurnNotificationPolicy.BuildDesktopOpenActionUrl(runtime.Paths.WorkspacePath, decision.ThreadId)
                : null;

            await HubNotificationClient.RequestAsync(
                runtime.Paths.WorkspacePath,
                spec.Kind,
                spec.TitleKey,
                spec.TitleKey is null ? decision.DisplayName : FallbackText.Format(spec.TitleKey),
                spec.BodyKey,
                FallbackText.Format(spec.BodyKey, decision.DisplayName),
                new { name = decision.DisplayName },
                spec.Severity,
                decision.ThreadId,
                actionUrl,
                decision.OpenDesktopOnClick);
        });
    }

    private void BroadcastThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
    {
        var lifecycle = ThreadLifecycleNotification.For(threadId, newStatus);

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(lifecycle.Method)
                || !_hiddenThreads.Accepts(threadId, connection))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(lifecycle.Method, lifecycle.Params, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    /// <summary>
    /// Broadcasts <c>thread/deleted</c> to all connected transports after permanent thread removal
    /// (Wire <c>thread/delete</c>, DashBoard, etc.).
    /// </summary>
    private void BroadcastThreadDeleted(string threadId)
    {
        _threadRuntimeNotifications.RemoveThread(threadId);

        var parameters = new Contract.ThreadDeletedNotification { ThreadId = threadId };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadDeleted)
                || !_hiddenThreads.Accepts(threadId, connection))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyAsync(Contract.AppServerRpc.ThreadDeleted, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }

        _hiddenThreads.Forget(threadId);
    }

    /// <summary>
    /// Broadcasts an <c>automation/updated</c> JSON-RPC notification to all connected transports.
    /// Called by <see cref="AutomationsEventDispatcher"/> when a task status changes.
    /// </summary>
    private void BroadcastAutomationUpdated(Contract.AutomationUpdatedNotification parameters) =>
        BroadcastContractNotification(Contract.AppServerRpc.AutomationUpdated, parameters);

    private void BroadcastAutomationRunUpdated(Contract.AutomationRunUpdatedNotification parameters) =>
        BroadcastContractNotification(Contract.AppServerRpc.AutomationRunUpdated, parameters);

    private void BroadcastImportProgress(Contract.ImportProgressNotification parameters) =>
        BroadcastContractNotification(Contract.AppServerRpc.ImportProgress, parameters);

    private void BroadcastImportCompleted(Contract.ImportCompletedNotification parameters) =>
        BroadcastContractNotification(Contract.AppServerRpc.ImportCompleted, parameters);

    private void BroadcastSubAgentBoardChanged(string rootThreadId) =>
        BroadcastContractNotification(Contract.AppServerRpc.SubAgentBoardChanged,
            new Contract.SubAgentBoardChangedNotification { RootThreadId = rootThreadId });

    private void BroadcastContractNotification<T>(DotCraft.Protocol.RpcNotification<T> descriptor, T parameters) where T : class
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(descriptor.Name)) continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(descriptor, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

    private void BroadcastWorkflowRunUpdated(DynamicWorkflowRunChanged update)
    {
        var parameters = new Contract.WorkflowRunUpdatedNotification
        {
            ThreadId = update.ThreadId,
            RunId = update.RunId,
            Reason = update.Reason
        };
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.WorkflowRunUpdated.Name)) continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(Contract.AppServerRpc.WorkflowRunUpdated, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

}
