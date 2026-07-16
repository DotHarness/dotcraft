using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Auth.OpenAI;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Logging;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Skills;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;

namespace DotCraft.Protocol.AppServer;

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
    public HeartbeatService? HeartbeatService { get; init; }
    public SkillsLoader? SkillsLoader { get; init; }
    public MemoryStore? MemoryStore { get; init; }

    /// <summary>Workspace <c>.craft</c> path bound to this connection.</summary>
    public string? WorkspaceCraftPath { get; init; }

    /// <summary>
    /// When the wire client omits or sends an empty <c>identity.workspacePath</c>, substitute this
    /// host workspace root (AppServer / Gateway process workspace).
    /// </summary>
    public string? HostWorkspacePath { get; init; }

    public IAutomationsRequestHandler? AutomationsHandler { get; init; }
    public Action<CronJobWireInfo, bool>? BroadcastCronStateChanged { get; init; }
    public Action<McpStatusInfoWire>? BroadcastMcpStatusChanged { get; init; }
    public Action<string, string, object?>? NotifyAppPrincipal { get; init; }
    public Action<string, object?>? BroadcastTrustedNotification { get; init; }
    public ICommitMessageSuggestService? CommitMessageSuggest { get; init; }
    public IWelcomeSuggestionService? WelcomeSuggestionService { get; init; }
    public string? DashboardUrl { get; init; }
    public WireAcpExtensionProxy? WireAcpExtensionProxy { get; init; }
    public WireNodeReplProxy? WireNodeReplProxy { get; init; }
    public WireDynamicToolProxy? WireDynamicToolProxy { get; init; }
    public CommandRegistry? CommandRegistry { get; init; }
    public IChannelStatusProvider? ChannelStatusProvider { get; init; }
    public McpClientManager? McpClientManager { get; init; }
    public McpAppTransientContextStore? McpAppTransientContextStore { get; init; }
    public LspServerManager? LspServerManager { get; init; }
    public IEnumerable<IAppServerProtocolExtension>? ProtocolExtensions { get; init; }
    public Func<ExternalChannelEntry, CancellationToken, Task>? OnExternalChannelUpserted { get; init; }
    public Func<string, CancellationToken, Task>? OnExternalChannelRemoved { get; init; }
    public IExternalChannelLogProvider? ExternalChannelLogProvider { get; init; }
    public SessionStreamDebugLogger? StreamDebugLogger { get; init; }
    public IReadOnlyList<ConfigSchemaSection>? ConfigSchema { get; init; }
    public IAppConfigMonitor? AppConfigMonitor { get; init; }
    public ChatClientRegistry? ChatClientRegistry { get; init; }
    public OpenAIClientProvider? OpenAIClientProvider { get; init; }
    public IOpenAIAuthService? OpenAIAuthService { get; init; }
    public IOpenAIUsageService? OpenAIUsageService { get; init; }
    public IBackgroundTerminalService? BackgroundTerminalService { get; init; }
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
    public HookRunner? HookRunner { get; init; }
}
