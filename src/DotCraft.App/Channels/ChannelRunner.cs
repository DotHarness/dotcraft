using DotCraft.Workspaces;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Hosting;
using DotCraft.Logging;
using DotCraft.Modules;
using DotCraft.Runtime;
using DotCraft.Security;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Sessions;
using DotCraft.Agents;

namespace DotCraft.Channels;

/// <summary>Owns workspace channel processes and shared web listeners.</summary>
public sealed partial class ChannelRunner : IAsyncDisposable, IChannelStatusProvider, IChannelRuntimeProvider
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly ChannelHostRegistry _channelHostRegistry;
    private readonly MessageRouter _router;
    private readonly List<IChannelService> _nativeChannels;
    private readonly List<IChannelService> _allChannels;
    private readonly object _channelsLock = new();

    private ISessionService? _sessionService;
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
        ChannelHostRegistry channelHostRegistry,
        MessageRouter router,
        List<IChannelService> nativeChannels)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _moduleRegistry = moduleRegistry;
        _channelHostRegistry = channelHostRegistry;
        _router = router;
        _nativeChannels = nativeChannels;
        _allChannels = new List<IChannelService>(nativeChannels);
        ChannelPlugins = new ChannelPluginWorkspace(paths, config, AppVersion.Informational, WebSocketEnabled, ReadChannelSelections);
    }

    public static ChannelRunner CreateForAppServer(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry registry)
    {
        var traceStore = sp.GetService<TraceStore>();
        var native = CollectNativeChannels(sp, config, registry);
        var wantDashboard = !ManagedAppServerEnvironment.IsManaged && config.DashBoard.Enabled && traceStore != null;

        ValidateAppServerPortConflict(config, native, wantDashboard);

        var router = sp.GetRequiredService<MessageRouter>();
        var channelHosts = sp.GetRequiredService<ChannelHostRegistry>();

        foreach (var ch in native)
            router.RegisterChannel(ch);

        return new ChannelRunner(sp, config, paths, registry, channelHosts, router, native);
    }

    private static List<IChannelService> CollectNativeChannels(
        IServiceProvider sp,
        AppConfig config,
        ModuleRegistry registry)
    {
        return registry
            .GetEnabledModules(config)
            .OfType<IChannelServiceModule>()
            .Select(m => m.CreateChannelService(sp))
            .OfType<IChannelService>()
            .ToList();
    }

    /// <summary>
    /// Ensures DashBoard / native HTTP channels do not bind the same (host, port) as the AppServer WebSocket listener.
    /// </summary>
    private static void ValidateAppServerPortConflict(
        AppConfig config, IReadOnlyList<IChannelService> nativeChannels, bool wantDashboard)
    {
        var appServer = config.GetSection<AppServerConfig>("AppServer");
        if (appServer.Mode is not (AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket))
            return;

        var wsPort = appServer.WebSocket.Port;
        var wsHost = NormalizeHost(appServer.WebSocket.Host);

        if (wantDashboard && config.DashBoard.Port == wsPort
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

        if (!ManagedAppServerEnvironment.IsManaged && _config.DashBoard.Enabled && traceStore != null)
        {
            var dashHost = _config.DashBoard.Host;
            var dashPort = _config.DashBoard.Port;

            var dashStandalone = !_nativeChannels.OfType<IWebHostingChannel>()
                .Any(wc => wc.ListenScheme == "http" &&
                           wc.ListenHost == dashHost &&
                           wc.ListenPort == dashPort);

            var dashBuilder = _pool.GetOrCreateBuilder("http", dashHost, dashPort);
            AppServerDashboard.ConfigureServices(dashBuilder.Services, _sp);
            if (dashStandalone)
                dashBuilder.Logging.ClearProviders();
        }

        _pool.BuildAll();
    }

    /// <summary>
    /// Configures Kestrel routes once session services are available.
    /// </summary>
    public void CompleteAfterSession(
        ISessionService sessionService,
        DreamsService? dreamsService = null)
    {
        if (_pool == null)
            throw new InvalidOperationException("Call BuildPoolThroughBuildAll first.");

        var traceStore = _sp.GetService<TraceStore>();
        _sessionService = sessionService;
        _pathBlacklist = _sp.GetRequiredService<PathBlacklist>();

        foreach (var ch in _allChannels.OfType<ISessionServiceConsumer>())
            ch.SetSessionService(sessionService);

        _pool.ConfigureApps();

        if (!ManagedAppServerEnvironment.IsManaged && _config.DashBoard.Enabled && traceStore != null)
        {
            var dashApp = _pool.GetApp("http", _config.DashBoard.Host, _config.DashBoard.Port);
            AppServerDashboard.MapWorkspace(
                dashApp, _sp.GetRequiredService<WorkspaceRuntime>(), traceStore, sessionService, dreamsService);

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
    /// Builds the web pool and maps DashBoard routes.
    /// </summary>
    public void Initialize(
        ISessionService sessionService,
        DreamsService dreamsService)
    {
        BuildPoolThroughBuildAll();
        CompleteAfterSession(sessionService, dreamsService);
    }

    private ChannelHost CreateChannelHost_NoLock(ChannelEntry channel)
    {
        var channelServiceMap = _allChannels
            .Where(ch => ch.ApprovalService != null)
            .ToDictionary(ch => ch.Name, ch => ch.ApprovalService!);
        var approvalService = new SessionScopedApprovalService(
            new ChannelRoutingApprovalService(channelServiceMap, new ConsoleApprovalService(new SpectreApprovalPrompt())));

        return new ChannelHost(
            channel,
            _sessionService!,
            AppVersion.Informational,
            _moduleRegistry,
            _paths,
            _sp.GetRequiredService<ChatClientRegistry>(),
            _sp.GetRequiredService<ModelProviderRegistry>(),
            _pathBlacklist,
            approvalService,
            streamDebugLogger: _sp.GetService<SessionStreamDebugLogger>(),
            appConfigMonitor: _sp.GetService<IAppConfigMonitor>(),
            protocolExtensions: _sp.GetServices<IAppServerProtocolExtension>(),
            originPresentationProviders: _sp.GetServices<IThreadOriginPresentationProvider>(),
            loggerFactory: _sp.GetService<ILoggerFactory>(),
            wireRuntimeAdditionalContextProvider: _sp.GetRequiredService<WireRuntimeAdditionalContextProvider>(),
            contextPageManager: _sp.GetService<IContextPageManager>(),
            contributions: _sp.GetService<DotCraft.Contributions.IContributionView>(),
            commandRegistry: _sp.GetRequiredService<DotCraft.Commands.Core.CommandRegistry>());
    }

    private void UnregisterChannelHost_NoLock(ChannelHost host)
    {
        _allChannels.Remove(host);
        _router.UnregisterChannel(host.Name);
        _channelHostRegistry.Unregister(host.Name);
    }

    private void UpdateChannelSelection(ChannelEntry entry)
    {
        lock (_channelsLock)
        {
            _config.Channels.RemoveAll(c => string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            _config.Channels.Add(entry with { });
        }
    }

    /// <summary>
    /// Starts all Kestrel listeners before automation and channel loops.
    /// </summary>
    public async Task StartWebPoolAsync()
    {
        if (_pool == null)
            throw new InvalidOperationException("Call Initialize before StartWebPoolAsync.");

        await _pool.StartAllAsync();
    }

    /// <summary>
    /// Starts <see cref="IChannelService.StartAsync"/> for every channel (fire-and-forget tasks).
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
        var pluginToken = _channelCts.Token;
        _pluginReconciliation = Task.Run(() => WatchPluginChannelsAsync(pluginToken), pluginToken);
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

        if (_pluginReconciliation != null)
        {
            try { await _pluginReconciliation; }
            catch (OperationCanceledException) { }
            _pluginReconciliation = null;
        }
        foreach (var name in _pluginProcesses.Keys.ToArray())
            await StopPluginChannelAsync(name);

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
        List<ChannelEntry> channelSelections;
        Dictionary<string, string> pluginFailures;
        lock (_channelsLock)
        {
            channelSnapshot = _allChannels.ToList();
            channelSelections = _config.Channels.ToList();
            pluginFailures = new Dictionary<string, string>(_pluginFailures, StringComparer.OrdinalIgnoreCase);
        }

        // Native social channels: discovered via modules with "social" category entries.
        var nativeRunningNames = _nativeChannels
            .Select(ch => ch.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in _moduleRegistry.Modules.OfType<ISessionChannelModule>())
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

        var selectedChannels = ChannelEntryMap.ToDictionaryByNameLastWins(channelSelections);

        // Build a lookup of running external hosts by name.
        var externalHosts = channelSnapshot
            .OfType<ChannelHost>()
            .ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, channelEntry) in selectedChannels)
        {
            externalHosts.TryGetValue(name, out var host);
            result.Add(new ChannelStatusSnapshot
            {
                Name = name,
                Category = "social",
                PluginId = channelEntry.PluginId,
                Enabled = channelEntry.Enabled,
                Running = host?.IsAdapterConnected ?? false,
                RuntimeState = host?.RuntimeState
                    ?? (pluginFailures.ContainsKey(name) ? ChannelRuntimeStates.Failed
                        : channelEntry.Enabled ? ChannelRuntimeStates.Starting : ChannelRuntimeStates.Stopped),
                FailureCode = host?.FailureCode ?? pluginFailures.GetValueOrDefault(name)
            });
        }

        return result.OrderBy(c => c.Category, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> GetRecentChannelLogs(string channelName, int? tail = null)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return [];

        lock (_channelsLock)
        {
            var host = _allChannels
                .OfType<ChannelHost>()
                .FirstOrDefault(ch => string.Equals(ch.Name, channelName, StringComparison.OrdinalIgnoreCase));
            return host?.GetRecentLogs(tail) ?? [];
        }
    }

    public Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(string channelName, string key, CancellationToken ct)
    {
        ChannelHost? host;
        lock (_channelsLock)
        {
            host = _allChannels
                .OfType<ChannelHost>()
                .FirstOrDefault(ch => string.Equals(ch.Name, channelName, StringComparison.OrdinalIgnoreCase));
        }
        return host?.GetConfigOptionAvailabilityAsync(key, ct) ?? Task.FromResult<IReadOnlyList<string>?>(null);
    }

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
