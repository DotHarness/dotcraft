using DotCraft.Channels;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Logging;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Reads external channel configuration and creates <see cref="ExternalChannelHost"/> instances.
/// Invoked by AppServer's channel runner to merge external channels with native channels.
/// </summary>
public sealed class ExternalChannelManager
{
    private readonly List<ExternalChannelHost> _hosts = [];
    private readonly ExternalChannelRegistry _registry;
    private readonly ILogger<ExternalChannelManager> _logger;

    /// <summary>
    /// The <see cref="ExternalChannelRegistry"/> holding all external channel hosts for tool
    /// discovery and WebSocket adapter routing for transports that accept attach.
    /// </summary>
    public ExternalChannelRegistry Registry => _registry;

    /// <summary>
    /// All created external channel hosts (both subprocess and WebSocket modes).
    /// These are merged into the AppServer channel runner's channel list.
    /// </summary>
    public IReadOnlyList<IChannelService> Channels => _hosts;

    /// <summary>
    /// Creates an <see cref="ExternalChannelManager"/> that reads the <c>"ExternalChannels"</c>
    /// configuration section and creates host instances.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <param name="sessionService">Shared session service for all external channels.</param>
    /// <param name="nativeChannelNames">Names of native channels (for conflict detection).</param>
    /// <param name="moduleRegistry">Registered DotCraft modules (for <c>channel/list</c> discovery).</param>
    /// <param name="hostWorkspacePath">Host workspace root; passed to wire handlers for empty <c>identity.workspacePath</c>.</param>
    /// <param name="chatClientRegistry">Workspace-scoped chat client registry shared with the main AppServer.</param>
    /// <param name="modelProviderRegistry">Workspace-scoped model provider registry shared with the main AppServer.</param>
    /// <param name="registry">
    /// External channel registry for WebSocket routing. If null, a new instance is created.
    /// </param>
    public ExternalChannelManager(
        AppConfig config,
        ISessionService sessionService,
        IReadOnlyCollection<string> nativeChannelNames,
        ModuleRegistry moduleRegistry,
        string hostWorkspacePath,
        ChatClientRegistry chatClientRegistry,
        ModelProviderRegistry modelProviderRegistry,
        PathBlacklist? pathBlacklist = null,
        IApprovalService? approvalService = null,
        ExternalChannelRegistry? registry = null,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null,
        IEnumerable<IThreadOriginPresentationProvider>? originPresentationProviders = null,
        ILoggerFactory? loggerFactory = null,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
        IContextPageManager? contextPageManager = null)
    {
        _registry = registry ?? new ExternalChannelRegistry();
        _logger = loggerFactory?.CreateLogger<ExternalChannelManager>() ?? NullLogger<ExternalChannelManager>.Instance;

        var channels = ExternalChannelEntryMap.ToDictionaryByNameLastWins(config.ExternalChannels);

        if (channels.Count == 0)
            return;

        var appServerConfig = config.GetSection<AppServerConfig>("AppServer");
        var serverVersion = AppVersion.Informational;

        foreach (var (name, entry) in channels)
        {
            if (!entry.Enabled)
                continue;

            // Validate channel name doesn't conflict with native channels
            if (nativeChannelNames.Contains(name))
            {
                _logger.LogError("External channel {Channel} conflicts with a native channel and was skipped", name);
                continue;
            }

            // Validate managed process config.
            if (entry.Transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket
                && string.IsNullOrWhiteSpace(entry.Command)
                && string.IsNullOrWhiteSpace(entry.BuiltinModule))
            {
                _logger.LogError(
                    "External channel {Channel} using {Transport} has no command or built-in module and was skipped",
                    name,
                    entry.Transport);
                continue;
            }

            // Validate WebSocket config — requires AppServer WebSocket to be enabled
            if (entry.Transport is ExternalChannelTransport.Websocket or ExternalChannelTransport.ManagedWebsocket)
            {
                var wsEnabled = appServerConfig.Mode is AppServerMode.WebSocket
                    or AppServerMode.StdioAndWebSocket;

                if (!wsEnabled)
                {
                    _logger.LogError(
                        "External channel {Channel} using {Transport} requires AppServer WebSocket mode and was skipped",
                        name,
                        entry.Transport);
                    continue;
                }
            }

            var host = new ExternalChannelHost(
                entry,
                sessionService,
                serverVersion,
                moduleRegistry,
                hostWorkspacePath,
                chatClientRegistry,
                modelProviderRegistry,
                pathBlacklist,
                approvalService,
                streamDebugLogger: streamDebugLogger,
                appConfigMonitor: appConfigMonitor,
                protocolExtensions: protocolExtensions,
                appBindingService: appBindingService,
                originPresentationProviders: originPresentationProviders,
                loggerFactory: loggerFactory,
                wireRuntimeAdditionalContextProvider: wireRuntimeAdditionalContextProvider,
                contextPageManager: contextPageManager);
            _hosts.Add(host);

            // Register all hosts for unified channel runtime tool discovery and WebSocket routing.
            // AppServerHost only attaches WebSocket clients to attach-capable transports.
            _registry.Register(name, host);

            _logger.LogInformation(
                "Registered external channel {Channel} using {Transport}",
                name,
                entry.Transport);
        }
    }

    /// <summary>
    /// Returns true if any external channels are configured and enabled.
    /// Used to determine whether AppServer should initialize external channel hosting.
    /// </summary>
    public static bool HasEnabledChannels(AppConfig config)
    {
        return config.ExternalChannels.Any(c => !string.IsNullOrWhiteSpace(c.Name) && c.Enabled);
    }
}
