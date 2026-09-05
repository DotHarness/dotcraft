using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Cron;
using DotCraft.Hooks;
using DotCraft.Logging;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using DotCraft.InlineVisualizations;
using DotCraft.Plugins;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

/// <summary>
/// Optional collaborator bundle for <see cref="AppServerRequestHandler"/>.
///
/// Collapses the large set of per-connection feature services, broadcast delegates, wire proxies,
/// and configuration inputs into a single init-only object so construction sites express only the
/// dependencies they provide (named property initializers) instead of a long positional/named
/// argument list. The four genuinely required collaborators (session service, connection,
/// transport, channel-list contributor) remain positional constructor parameters.
///
/// Domain handlers receive the subset of services they need from this bundle.
/// </summary>
public sealed record AppServerConnectionServices
{
    /// <summary>Server version reported in the initialize handshake.</summary>
    public string ServerVersion { get; init; } = "0.1.0";

    /// <summary>
    /// Fallback decision used by <see cref="AppServerEventDispatcher"/> when non-interactive
    /// approval resolution cannot be derived from thread policy.
    /// </summary>
    public SessionApprovalDecision DefaultApprovalDecision { get; init; } = SessionApprovalDecision.Reject;

    public CronService? CronService { get; init; }
    public SkillsLoader? SkillsLoader { get; init; }
    public MemoryStore? MemoryStore { get; init; }

    /// <summary>Resolved workspace data path bound to this connection.</summary>
    public string? WorkspaceCraftPath { get; init; }

    /// <summary>
    /// When the wire client omits or sends an empty <c>identity.workspacePath</c>, substitute this
    /// AppServer host workspace root.
    /// </summary>
    public string? HostWorkspacePath { get; init; }

    public IAutomationsRequestHandler? AutomationsHandler { get; init; }
    public Action<Contract.CronJobWireInfo, bool>? BroadcastCronStateChanged { get; init; }
    public Action<McpServerStatusSnapshot>? BroadcastMcpStatusChanged { get; init; }
    public Action<string, string, object?>? NotifyAppPrincipal { get; init; }
    public Action<string, object?>? BroadcastTrustedNotification { get; init; }
    public Action<IAppServerTransport, Contract.PluginSnapshotUpdatedNotification, Task>?
        BroadcastPluginSnapshotUpdated { get; init; }
    public ICommitMessageSuggester? CommitMessageSuggest { get; init; }
    public IWelcomeSuggester? WelcomeSuggestionService { get; init; }
    public string? DashboardUrl { get; init; }
    public WireAcpExtensionProxy? WireAcpExtensionProxy { get; init; }
    public WireNodeReplProxy? WireNodeReplProxy { get; init; }
    public WireDynamicToolProxy? WireDynamicToolProxy { get; init; }
    public CommandRegistry? CommandRegistry { get; init; }
    public IChannelStatusProvider? ChannelStatusProvider { get; init; }
    public McpClientManager? McpClientManager { get; init; }
    public McpAppTransientContextStore? McpAppTransientContextStore { get; init; }
    public InlineVisualizationAssetStore? InlineVisualizationAssetStore { get; init; }
    public InlineVisualizationRuntimeRegistry? InlineVisualizationRuntimeRegistry { get; init; }
    public LspServerManager? LspServerManager { get; init; }
    public IEnumerable<IAppServerProtocolExtension>? ProtocolExtensions { get; init; }
    public Func<ExternalChannelEntry, CancellationToken, Task>? OnExternalChannelUpserted { get; init; }
    public Func<string, CancellationToken, Task>? OnExternalChannelRemoved { get; init; }
    public IExternalChannelLogProvider? ExternalChannelLogProvider { get; init; }
    public SessionStreamDebugLogger? StreamDebugLogger { get; init; }
    public ILoggerFactory? LoggerFactory { get; init; }
    public IReadOnlyList<ConfigSchemaSection>? ConfigSchema { get; init; }
    public IAppConfigMonitor? AppConfigMonitor { get; init; }
    public ChatClientRegistry? ChatClientRegistry { get; init; }
    public ModelProviderRegistry? ModelProviderRegistry { get; init; }
    public IBackgroundTerminalService? BackgroundTerminalService { get; init; }

    public IRemoteToolHostClient? RemoteToolHostClient { get; init; }

    /// <summary>Fans a route change out to every connection that accepts the notification.</summary>
    public Action<Contract.RemoteToolHostRouteChangedNotification>? BroadcastRemoteToolHostRouteChanged { get; init; }
    public IContextPageManager? ContextPageManager { get; init; }
    public DreamStore? DreamStore { get; init; }
    public DreamsService? DreamsService { get; init; }
    public AppBindingService? AppBindingService { get; init; }
    public IReadOnlyList<IThreadOriginPresentationProvider>? ThreadOriginPresentationProviders { get; init; }
    public PlanStore? PlanStore { get; init; }
    public TraceStore? TraceStore { get; init; }
    public IReadOnlyList<string>? BuiltInPluginSourceRoots { get; init; }
    public WireRuntimeAdditionalContextProvider? WireRuntimeAdditionalContextProvider { get; init; }
    public Func<SessionThread, SubAgentCoordinator?>? SubAgentCoordinatorFactory { get; init; }

    /// <summary>Read-only contribution view, so handlers resolve contributions live instead of capturing them per connection.</summary>
    public IContributionView? Contributions { get; init; }
    public HookRunner? HookRunner { get; init; }
    public bool SupportsUltraReasoning { get; init; }
    public IPluginWorkflowSummaryProvider? PluginWorkflowSummaryProvider { get; init; }
    public IPluginDotnetRuntimeCoordinator? PluginDotnetRuntimeCoordinator { get; init; }
    public PluginConfigStore? PluginConfigStore { get; init; }

    /// <summary>Workspace-scoped state used to coordinate plugin management across connections.</summary>
    public AppServerPluginManagementState PluginManagementState { get; init; } = new();
}
