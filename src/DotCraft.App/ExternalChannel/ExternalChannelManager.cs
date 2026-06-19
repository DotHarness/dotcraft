using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Logging;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using Spectre.Console;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Reads external channel configuration and creates <see cref="ExternalChannelHost"/> instances.
/// Invoked during <c>GatewayHostFactory.CreateHost()</c> to merge external channels into
/// the gateway's channel list alongside native channels.
/// </summary>
public sealed class ExternalChannelManager
{
    private readonly List<ExternalChannelHost> _hosts = [];
    private readonly ExternalChannelRegistry _registry;

    /// <summary>
    /// The <see cref="ExternalChannelRegistry"/> holding all external channel hosts for tool
    /// discovery and WebSocket adapter routing for transports that accept attach.
    /// </summary>
    public ExternalChannelRegistry Registry => _registry;

    /// <summary>
    /// All created external channel hosts (both subprocess and WebSocket modes).
    /// These should be merged into <c>GatewayHost</c>'s channel list.
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
    /// <param name="registry">
    /// External channel registry for WebSocket routing. If null, a new instance is created.
    /// </param>
    public ExternalChannelManager(
        AppConfig config,
        ISessionService sessionService,
        IReadOnlyCollection<string> nativeChannelNames,
        ModuleRegistry moduleRegistry,
        string hostWorkspacePath,
        PathBlacklist? pathBlacklist = null,
        IApprovalService? approvalService = null,
        ExternalChannelRegistry? registry = null,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null)
    {
        _registry = registry ?? new ExternalChannelRegistry();

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
                AnsiConsole.MarkupLine(
                    $"[red][[ExternalChannel]][/] Channel name [yellow]{name}[/] conflicts with " +
                    "a native channel. Skipping.");
                continue;
            }

            // Validate managed process config.
            if (entry.Transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket
                && string.IsNullOrWhiteSpace(entry.Command)
                && string.IsNullOrWhiteSpace(entry.BuiltinModule))
            {
                AnsiConsole.MarkupLine(
                    $"[red][[ExternalChannel]][/] {entry.Transport} channel [yellow]{name}[/] has no " +
                    "'command' or 'builtinModule' configured. Skipping.");
                continue;
            }

            // Validate WebSocket config — requires AppServer WebSocket to be enabled
            if (entry.Transport is ExternalChannelTransport.Websocket or ExternalChannelTransport.ManagedWebsocket)
            {
                var wsEnabled = appServerConfig.Mode is AppServerMode.WebSocket
                    or AppServerMode.StdioAndWebSocket;

                if (!wsEnabled)
                {
                    AnsiConsole.MarkupLine(
                        $"[red][[ExternalChannel]][/] {entry.Transport} channel [yellow]{name}[/] requires " +
                        "AppServer WebSocket mode to be enabled. Skipping.");
                    continue;
                }
            }

            var host = new ExternalChannelHost(
                entry,
                sessionService,
                serverVersion,
                moduleRegistry,
                hostWorkspacePath,
                pathBlacklist,
                approvalService,
                streamDebugLogger: streamDebugLogger,
                appConfigMonitor: appConfigMonitor,
                protocolExtensions: protocolExtensions,
                appBindingService: appBindingService);
            _hosts.Add(host);

            // Register all hosts for unified channel runtime tool discovery and WebSocket routing.
            // AppServerHost only attaches WebSocket clients to attach-capable transports.
            _registry.Register(name, host);

            AnsiConsole.MarkupLine(
                $"[green][[ExternalChannel]][/] Registered external channel [yellow]{name}[/] " +
                $"({entry.Transport})");
        }
    }

    /// <summary>
    /// Returns true if any external channels are configured and enabled.
    /// Used by <see cref="Gateway.GatewayModule"/> to include external channels in the enabled check.
    /// </summary>
    public static bool HasEnabledChannels(AppConfig config)
    {
        return config.ExternalChannels.Any(c => !string.IsNullOrWhiteSpace(c.Name) && c.Enabled);
    }
}
