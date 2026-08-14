using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Dreams;
using DotCraft.ExternalChannel;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Logging;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Sessions;
using DotCraft.Agents;

namespace DotCraft.Channels;

/// <summary>
/// Manages native channels, external channels, <see cref="WebHostPool"/>, and DashBoard mounting
/// for AppServer mode with the shared workspace session.
/// Also implements <see cref="IChannelStatusProvider"/> for the <c>channel/status</c> wire method
/// (spec Section 20).
/// </summary>
public sealed class ChannelRunner : IAsyncDisposable, IChannelStatusProvider, IExternalChannelLogProvider
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly ExternalChannelRegistry _externalChannelRegistry;
    private readonly MessageRouter _router;
    private readonly List<IChannelService> _nativeChannels;
    private readonly List<IChannelService> _allChannels;
    private readonly object _channelsLock = new();

    private ISessionService? _sessionService;
    private HeartbeatService? _heartbeatService;
    private CronService? _cronService;
    private DreamsService? _dreamsService;
    private PathBlacklist? _pathBlacklist;

    private WebHostPool? _pool;
    private List<Task>? _channelTasks;
    private CancellationTokenSource? _channelCts;
    private bool _stopped;

    /// <summary>
    /// Public URL of the DashBoard UI (…/dashboard), or null when DashBoard is not hosted.
    /// </summary>
    public string? DashBoardUrl { get; private set; }

    private ChannelRunner(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry moduleRegistry,
        ExternalChannelRegistry externalChannelRegistry,
        MessageRouter router,
        List<IChannelService> nativeChannels)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _moduleRegistry = moduleRegistry;
        _externalChannelRegistry = externalChannelRegistry;
        _router = router;
        _nativeChannels = nativeChannels;
        _allChannels = new List<IChannelService>(nativeChannels);
    }

    /// <summary>
    /// Creates a runner when native channels, external channels, and/or DashBoard should be hosted in-process.
    /// </summary>
    public static ChannelRunner? TryCreateForAppServer(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry registry)
    {
        var traceStore = sp.GetService<TraceStore>();
        var native = CollectNativeChannels(sp, config, registry);
        var hasExternal = ExternalChannelManager.HasEnabledChannels(config);
        var wantDashboard = config.DashBoard.Enabled && traceStore != null;

        if (native.Count == 0 && !hasExternal && !wantDashboard)
            return null;

        ValidateAppServerPortConflict(config, native);

        var router = sp.GetRequiredService<MessageRouter>();
        var extReg = sp.GetRequiredService<ExternalChannelRegistry>();

        foreach (var ch in native)
            router.RegisterChannel(ch);

        return new ChannelRunner(sp, config, paths, registry, extReg, router, native);
    }

    private static List<IChannelService> CollectNativeChannels(
        IServiceProvider sp,
        AppConfig config,
        ModuleRegistry registry)
    {
        return registry
            .GetEnabledModules(config)
            .Where(m => m.Name != "app-server")
            .Select(m => m.CreateChannelService(sp))
            .OfType<IChannelService>()
            .ToList();
    }

    /// <summary>
    /// Ensures DashBoard / native HTTP channels do not bind the same (host, port) as the AppServer WebSocket listener.
    /// </summary>
    private static void ValidateAppServerPortConflict(AppConfig config, IReadOnlyList<IChannelService> nativeChannels)
    {
        var appServer = config.GetSection<AppServerConfig>("AppServer");
        if (appServer.Mode is not (AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket))
            return;

        var wsPort = appServer.WebSocket.Port;
        var wsHost = NormalizeHost(appServer.WebSocket.Host);

        if (config.DashBoard.Enabled && config.DashBoard.Port == wsPort
            && NormalizeHost(config.DashBoard.Host) == wsHost)
        {
            throw new InvalidOperationException(
                $"DashBoard cannot use the same address as AppServer WebSocket ({wsHost}:{wsPort}). Change DashBoard.Port or AppServer.WebSocket.Port.");
        }

        foreach (var ch in nativeChannels)
        {
            if (ch is not IWebHostingChannel wc)
                continue;
            if (wc.ListenPort != wsPort || NormalizeHost(wc.ListenHost) != wsHost)
                continue;
            if (string.Equals(wc.ListenScheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Native channel '{ch.Name}' HTTP listener conflicts with AppServer WebSocket on {wsHost}:{wsPort}.");
            }
        }
    }

    private static string NormalizeHost(string host)
    {
        if (host is "::1" or "[::1]")
            return "127.0.0.1";
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";
        return host;
    }

    /// <summary>
    /// Registers native web channels, optional DashBoard builder, and calls <see cref="WebHostPool.BuildAll"/>.
    /// Call <see cref="CompleteAfterSession"/> after <see cref="ISessionService"/> exists.
    /// </summary>
    public void BuildPoolThroughBuildAll()
    {
        var traceStore = _sp.GetService<TraceStore>();

        _pool = new WebHostPool();

        foreach (var wc in _nativeChannels.OfType<IWebHostingChannel>())
            _pool.Register(wc);

        var dashboardEnabled = _config.DashBoard.Enabled && traceStore != null;
        if (dashboardEnabled)
        {
            var dashHost = _config.DashBoard.Host;
            var dashPort = _config.DashBoard.Port;

            var dashStandalone = !_nativeChannels.OfType<IWebHostingChannel>()
                .Any(wc => wc.ListenScheme == "http" &&
                           wc.ListenHost == dashHost &&
                           wc.ListenPort == dashPort);

            var dashBuilder = _pool.GetOrCreateBuilder("http", dashHost, dashPort);
            if (dashStandalone)
                dashBuilder.Logging.ClearProviders();
        }

        _pool.BuildAll();
    }

    /// <summary>
    /// External channels, session injection, Kestrel route configuration, and DashBoard routes.
    /// </summary>
    public void CompleteAfterSession(
        ISessionService sessionService,
        HeartbeatService heartbeatService,
        CronService cronService,
        DreamsService? dreamsService = null)
    {
        if (_pool == null)
            throw new InvalidOperationException("Call BuildPoolThroughBuildAll first.");

        var traceStore = _sp.GetService<TraceStore>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();
        var orchestratorProviders = _sp.GetServices<IOrchestratorSnapshotProvider>().ToList();
        _sessionService = sessionService;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _dreamsService = dreamsService;
        _pathBlacklist = _sp.GetRequiredService<PathBlacklist>();

        if (ExternalChannelManager.HasEnabledChannels(_config))
        {
            var nativeNames = _nativeChannels.Select(ch => ch.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var channelServiceMap = _allChannels
                .Where(ch => ch.ApprovalService != null)
                .ToDictionary(ch => ch.Name, ch => ch.ApprovalService!);
            var approvalService = new SessionScopedApprovalService(
                new ChannelRoutingApprovalService(channelServiceMap, new ConsoleApprovalService(new SpectreApprovalPrompt())));
            var streamDebugLogger = _sp.GetService<SessionStreamDebugLogger>();
            var appConfigMonitor = _sp.GetService<IAppConfigMonitor>();
            var ecManager = new ExternalChannelManager(
                _config,
                sessionService,
                nativeNames,
                _moduleRegistry,
                _paths.WorkspacePath,
                _sp.GetRequiredService<ChatClientRegistry>(),
                _sp.GetRequiredService<ModelProviderRegistry>(),
                _pathBlacklist,
                approvalService,
                _externalChannelRegistry,
                streamDebugLogger,
                appConfigMonitor,
                _sp.GetServices<IAppServerProtocolExtension>(),
                _sp.GetService<AppBindingService>(),
                _sp.GetServices<IThreadOriginPresentationProvider>(),
                _sp.GetService<ILoggerFactory>());

            foreach (var extCh in ecManager.Channels)
            {
                lock (_channelsLock)
                {
                    _allChannels.Add(extCh);
                }
                _router.RegisterChannel(extCh);
            }
        }

        foreach (var ch in _allChannels.OfType<ISessionServiceConsumer>())
            ch.SetSessionService(sessionService);

        foreach (var ch in _allChannels)
        {
            ch.HeartbeatService = heartbeatService;
            ch.CronService = cronService;
        }

        _pool.ConfigureApps();

        var dashboardEnabled = _config.DashBoard.Enabled && traceStore != null;
        if (dashboardEnabled && traceStore != null)
        {
            var capturedOrchestrators = orchestratorProviders.Count > 0 ? orchestratorProviders : null;
            var dashApp = _pool.GetApp("http", _config.DashBoard.Host, _config.DashBoard.Port);
            dashApp.MapDashBoardAuth(_config);
            dashApp.UseDashBoardAuth(_config);
            var capturedSvc = sessionService;
            var persistence = _sp.GetRequiredService<SessionPersistenceService>();
            var dreamStore = _sp.GetService<DreamStore>();
            dashApp.MapDashBoard(traceStore, _paths, tokenUsageStore,
                orchestratorProviders: capturedOrchestrators,
                configSchema: ConfigSchemaRegistrations.GetConfigSchema(),
                persistence: persistence,
                deleteThreadAsync: (threadId, cancellationToken) => capturedSvc.DeleteThreadPermanentlyAsync(threadId, cancellationToken),
                sessionHandler: new DelegateDashBoardSessionHandler(id => capturedSvc.DeleteThreadPermanentlyAsync(id)),
                refreshTraceFromDiskBeforeRead: true,
                dreamStore: dreamStore,
                dreamsService: _dreamsService);

            var baseUrl = $"http://{_config.DashBoard.Host}:{_config.DashBoard.Port}";
            DashBoardUrl = $"{baseUrl}/dashboard";
            AnsiConsole.MarkupLine(
                $"[green]DashBoard started at[/] [link={DashBoardUrl}]{DashBoardUrl}[/]");
        }
        else
        {
            DashBoardUrl = null;
        }
    }

    /// <summary>
    /// Builds the web pool, attaches external channels, maps DashBoard, and prepares channel tasks (not yet started).
    /// </summary>
    public void Initialize(
        ISessionService sessionService,
        HeartbeatService heartbeatService,
        CronService cronService,
        DreamsService dreamsService)
    {
        BuildPoolThroughBuildAll();
        CompleteAfterSession(sessionService, heartbeatService, cronService, dreamsService);
    }

    /// <summary>
    /// Applies a single external-channel upsert to in-memory runtime state so WebSocket adapter
    /// routing and <c>channel/status</c> observe changes immediately without host restart.
    /// </summary>
    public async Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        UpsertExternalChannelConfig(entry);

        if (_sessionService == null || _heartbeatService == null || _cronService == null || _pathBlacklist == null)
            return;

        ExternalChannelHost? replacedHost = null;
        Task? nextHostTask = null;

        lock (_channelsLock)
        {
            replacedHost = RemoveExternalChannelHost_NoLock(entry.Name);
            if (!entry.Enabled)
                goto done;

            if (_nativeChannels.Any(ch => string.Equals(ch.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow][[ExternalChannel]][/] Skipping runtime upsert for [yellow]{entry.Name}[/]: " +
                    "name conflicts with a native channel.");
                goto done;
            }

            if (entry.Transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket
                && string.IsNullOrWhiteSpace(entry.Command)
                && string.IsNullOrWhiteSpace(entry.BuiltinModule))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow][[ExternalChannel]][/] Skipping runtime upsert for [yellow]{entry.Name}[/]: " +
                    $"{entry.Transport} channel requires a non-empty command or built-in module.");
                goto done;
            }

            if (entry.Transport is ExternalChannelTransport.Websocket or ExternalChannelTransport.ManagedWebsocket)
            {
                var appServerConfig = _config.GetSection<AppServerConfig>("AppServer");
                var wsEnabled = appServerConfig.Mode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket;
                if (!wsEnabled)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow][[ExternalChannel]][/] Skipping runtime upsert for [yellow]{entry.Name}[/]: " +
                        $"{entry.Transport} transport requires AppServer WebSocket mode.");
                    goto done;
                }
            }

            var createdHost = CreateExternalChannelHost_NoLock(entry);
            _allChannels.Add(createdHost);
            _router.RegisterChannel(createdHost);
            _externalChannelRegistry.Register(entry.Name, createdHost);

            if (_channelTasks != null && _channelCts is { IsCancellationRequested: false } cts)
            {
                nextHostTask = RunChannelAsync(createdHost, cts.Token);
                _channelTasks.Add(nextHostTask);
            }
        }

    done:
        if (replacedHost != null)
            await replacedHost.DisposeAsync();

        if (nextHostTask != null)
            await Task.Yield();
    }

    /// <summary>
    /// Applies a single external-channel removal to in-memory runtime state.
    /// </summary>
    public async Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(channelName))
            return;

        RemoveExternalChannelConfig(channelName);

        ExternalChannelHost? removedHost;
        lock (_channelsLock)
        {
            removedHost = RemoveExternalChannelHost_NoLock(channelName);
        }

        if (removedHost != null)
            await removedHost.DisposeAsync();
    }

    private ExternalChannelHost CreateExternalChannelHost_NoLock(ExternalChannelEntry source)
    {
        var channel = source.Clone();
        var channelServiceMap = _allChannels
            .Where(ch => ch.ApprovalService != null)
            .ToDictionary(ch => ch.Name, ch => ch.ApprovalService!);
        var approvalService = new SessionScopedApprovalService(
            new ChannelRoutingApprovalService(channelServiceMap, new ConsoleApprovalService(new SpectreApprovalPrompt())));

        return new ExternalChannelHost(
            channel,
            _sessionService!,
            AppVersion.Informational,
            _moduleRegistry,
            _paths.WorkspacePath,
            _sp.GetRequiredService<ChatClientRegistry>(),
            _sp.GetRequiredService<ModelProviderRegistry>(),
            _pathBlacklist,
            approvalService,
            streamDebugLogger: _sp.GetService<SessionStreamDebugLogger>(),
            appConfigMonitor: _sp.GetService<IAppConfigMonitor>(),
            protocolExtensions: _sp.GetServices<IAppServerProtocolExtension>(),
            appBindingService: _sp.GetService<AppBindingService>(),
            originPresentationProviders: _sp.GetServices<IThreadOriginPresentationProvider>(),
            loggerFactory: _sp.GetService<ILoggerFactory>());
    }

    private ExternalChannelHost? RemoveExternalChannelHost_NoLock(string channelName)
    {
        var host = _allChannels
            .OfType<ExternalChannelHost>()
            .FirstOrDefault(ch => string.Equals(ch.Name, channelName, StringComparison.OrdinalIgnoreCase));
        if (host == null)
            return null;

        _allChannels.Remove(host);
        _router.UnregisterChannel(channelName);
        _externalChannelRegistry.Unregister(channelName);
        return host;
    }

    private void UpsertExternalChannelConfig(ExternalChannelEntry entry)
    {
        lock (_channelsLock)
        {
            var existingIndex = _config.ExternalChannels.FindIndex(c =>
                string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                _config.ExternalChannels[existingIndex] = entry.Clone();
            else
                _config.ExternalChannels.Add(entry.Clone());
        }
    }

    private void RemoveExternalChannelConfig(string channelName)
    {
        lock (_channelsLock)
        {
            _config.ExternalChannels.RemoveAll(c =>
                string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Starts all Kestrel listeners before cron and channel loops.
    /// </summary>
    public async Task StartWebPoolAsync()
    {
        if (_pool == null)
            throw new InvalidOperationException("Call Initialize before StartWebPoolAsync.");

        await _pool.StartAllAsync();
    }

    /// <summary>
    /// Starts <see cref="IChannelService.StartAsync"/> for every channel (fire-and-forget tasks).
    /// Call after shared Cron/Heartbeat services have been started when applicable.
    /// </summary>
    public void BeginChannelLoops(CancellationToken cancellationToken)
    {
        if (_pool == null)
            throw new InvalidOperationException("Call Initialize before BeginChannelLoops.");

        _channelCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<IChannelService> snapshot;
        lock (_channelsLock)
        {
            snapshot = _allChannels.ToList();
        }
        _channelTasks = snapshot
            .Select(ch => RunChannelAsync(ch, _channelCts.Token))
            .ToList();
    }

    /// <summary>
    /// Cancels channel tasks and stops the web pool.
    /// </summary>
    public async Task StopAsync()
    {
        if (_stopped)
            return;

        if (_channelCts != null)
        {
            try
            {
                await _channelCts.CancelAsync();
            }
            catch
            {
                // ignored
            }
        }

        if (_channelTasks is { Count: > 0 })
        {
            try
            {
                await Task.WhenAll(_channelTasks);
            }
            catch
            {
                // ignored
            }

            _channelTasks = null;
        }

        _channelCts?.Dispose();
        _channelCts = null;

        if (_pool != null)
        {
            await _pool.DisposeAsync();
            _pool = null;
        }

        _stopped = true;
    }

    // -------------------------------------------------------------------------
    // IChannelStatusProvider (spec Section 20 — channel/status)
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses()
    {
        var result = new List<ChannelStatusSnapshot>();
        List<IChannelService> channelSnapshot;
        List<ExternalChannelEntry> externalChannelConfigSnapshot;
        lock (_channelsLock)
        {
            channelSnapshot = _allChannels.ToList();
            externalChannelConfigSnapshot = _config.ExternalChannels.Select(c => c.Clone()).ToList();
        }

        // Native social channels: discovered via modules with "social" category entries.
        var nativeRunningNames = _nativeChannels
            .Select(ch => ch.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in _moduleRegistry.Modules)
        {
            foreach (var entry in module.GetSessionChannelListEntries())
            {
                if (!string.Equals(entry.Category, "social", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new ChannelStatusSnapshot
                {
                    Name = entry.Name,
                    Category = "social",
                    Enabled = module.IsEnabled(_config),
                    Running = nativeRunningNames.Contains(entry.Name),
                    RuntimeState = nativeRunningNames.Contains(entry.Name)
                        ? ChannelRuntimeStates.Running
                        : ChannelRuntimeStates.Stopped
                });
            }
        }

        // External adapter channels: read all entries from config (including disabled ones).
        var externalChannels = ExternalChannelEntryMap.ToDictionaryByNameLastWins(externalChannelConfigSnapshot);

        // Build a lookup of running external hosts by name.
        var externalHosts = channelSnapshot
            .OfType<ExternalChannelHost>()
            .ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, channelEntry) in externalChannels)
        {
            externalHosts.TryGetValue(name, out var host);
            result.Add(new ChannelStatusSnapshot
            {
                Name = name,
                Category = "external",
                Enabled = channelEntry.Enabled,
                Running = host?.IsAdapterConnected ?? false,
                RuntimeState = host?.RuntimeState
                    ?? (channelEntry.Enabled ? ChannelRuntimeStates.Starting : ChannelRuntimeStates.Stopped),
                FailureCode = host?.FailureCode
            });
        }

        // Sort: social first, then external; within each group sort by name.
        result.Sort((a, b) =>
        {
            var catOrder = GetCategoryOrder(a.Category).CompareTo(GetCategoryOrder(b.Category));
            return catOrder != 0 ? catOrder : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    public IReadOnlyList<string> GetRecentExternalChannelLogs(string channelName, int? tail = null)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return [];

        lock (_channelsLock)
        {
            var host = _allChannels
                .OfType<ExternalChannelHost>()
                .FirstOrDefault(ch => string.Equals(ch.Name, channelName, StringComparison.OrdinalIgnoreCase));
            return host?.GetRecentLogs(tail) ?? [];
        }
    }

    private static int GetCategoryOrder(string category) => category switch
    {
        "social" => 0,
        "external" => 1,
        _ => 2
    };

    private static async Task RunChannelAsync(IChannelService channel, CancellationToken ct)
    {
        try
        {
            await channel.StartAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[Channels]][/] [red]Channel '{Markup.Escape(channel.Name)}' failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
            await StopAsync();

        List<IChannelService> snapshot;
        lock (_channelsLock)
        {
            snapshot = _allChannels.ToList();
        }

        foreach (var ch in snapshot)
            await ch.DisposeAsync();
    }
}
