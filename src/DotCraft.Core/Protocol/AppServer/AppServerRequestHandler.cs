using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using DotCraft.Agents;
using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.Auth.OpenAI;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Logging;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Skills;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Dispatches incoming JSON-RPC requests to the appropriate <see cref="ISessionService"/>
/// method and returns a JSON-RPC response object ready for serialization.
///
/// Each public Handle* method maps directly to one of the wire protocol methods
/// defined in the Session Wire Protocol Specification.
/// </summary>
public sealed class AppServerRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    IAppServerChannelListContributor channelListContributor,
    AppServerConnectionServices services)
{
    // Optional collaborators are supplied via the AppServerConnectionServices bundle. They are
    // aliased to private fields below so the (hundreds of) method-body references that previously
    // named the constructor parameters directly continue to resolve unchanged. The four required
    // collaborators above remain positional constructor parameters.
    private readonly string serverVersion = services.ServerVersion;
    private readonly CronService? cronService = services.CronService;
    private readonly HeartbeatService? heartbeatService = services.HeartbeatService;
    private readonly SkillsLoader? skillsLoader = services.SkillsLoader;
    private readonly MemoryStore? memoryStore = services.MemoryStore;
    private readonly string? workspaceCraftPath = services.WorkspaceCraftPath;
    private readonly IAutomationsRequestHandler? automationsHandler = services.AutomationsHandler;
    private readonly IWelcomeSuggestionService? welcomeSuggestionService = services.WelcomeSuggestionService;
    private readonly string? dashboardUrl = services.DashboardUrl;
    private readonly WireAcpExtensionProxy? wireAcpExtensionProxy = services.WireAcpExtensionProxy;
    private readonly WireNodeReplProxy? wireNodeReplProxy = services.WireNodeReplProxy;
    private readonly WireDynamicToolProxy? wireDynamicToolProxy = services.WireDynamicToolProxy;
    private readonly IChannelStatusProvider? channelStatusProvider = services.ChannelStatusProvider;
    private readonly McpClientManager? mcpClientManager = services.McpClientManager;
    private readonly SessionStreamDebugLogger? streamDebugLogger = services.StreamDebugLogger;
    private readonly IAppConfigMonitor? appConfigMonitor = services.AppConfigMonitor;
    private readonly IOpenAIAuthService? openAIAuthService = services.OpenAIAuthService;
    private readonly IOpenAIUsageService? openAIUsageService = services.OpenAIUsageService;
    private readonly IBackgroundTerminalService? backgroundTerminalService = services.BackgroundTerminalService;
    private readonly IContextPageManager? contextPageManager = services.ContextPageManager;
    private readonly DreamsService? dreamsService = services.DreamsService;
    private readonly AppBindingService? appBindingService = services.AppBindingService;
    private readonly PlanStore? planStore = services.PlanStore;
    private readonly TraceStore? traceStore = services.TraceStore;
    private readonly IReadOnlyList<string>? builtInPluginSourceRoots = services.BuiltInPluginSourceRoots;
    private readonly WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = services.WireRuntimeAdditionalContextProvider;
    private readonly Func<SessionThread, SubAgentCoordinator?>? subAgentCoordinatorFactory = services.SubAgentCoordinatorFactory;

    private const int ThreadListDefaultPageLimit = 50;
    private const int ThreadListMaxPageLimit = 100;
    private const int ThreadReadMaxPageLimit = 100;
    private const int MaxWidgetStateBytes = 8 * 1024;
    private const string ThreadListCursorKind = "thread-list";
    private const string ThreadReadCursorKind = "thread-read";

    private readonly CommandRegistry _commandRegistry = services.CommandRegistry
                                                        ?? CommandRegistry.CreateDefault(
                                                            !string.IsNullOrWhiteSpace(services.WorkspaceCraftPath) ? new CustomCommandLoader(services.WorkspaceCraftPath) : null);

    private readonly IReadOnlyList<ConfigSchemaSection> _configSchema = services.ConfigSchema ?? [];

    private readonly ChatClientRegistry _chatClientRegistry = services.ChatClientRegistry ?? new ChatClientRegistry();

    /// <summary>
    /// Fallback decision used by <see cref="AppServerEventDispatcher"/> when non-interactive
    /// approval resolution cannot be derived from thread policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision = services.DefaultApprovalDecision;

    /// <summary>
    /// When the wire client omits or sends an empty <c>identity.workspacePath</c>, substitute this
    /// host workspace root (AppServer / Gateway process workspace).
    /// </summary>
    private readonly string? _hostWorkspacePath = services.HostWorkspacePath;

    private readonly IReadOnlyDictionary<string, IAppServerMethodHandler> _extensionMethods =
        BuildExtensionMethodMap(services.ProtocolExtensions);

    private readonly IReadOnlyList<IAppServerCapabilityContributor> _capabilityContributors =
        services.ProtocolExtensions?.Cast<IAppServerCapabilityContributor>().ToArray()
        ?? [];

    private SkillVariantContext? _skillVariants;
    private WorkspaceConfigEditor? _workspaceConfig;
    private AppServerMcpConfigService? _mcpConfig;
    private ExternalChannelConfigService? _externalChannelConfig;
    private AppServerRuntimeConfigRefresher? _runtimeConfig;

    /// <summary>
    /// Shared skill-variant resolver (variant mode + per-connection target), consulted by the
    /// initialize capability report, turn start, and the extracted <see cref="SkillsRequestHandler"/>.
    /// </summary>
    private SkillVariantContext SkillVariants => _skillVariants ??=
        new SkillVariantContext(appConfigMonitor, _chatClientRegistry, _hostWorkspacePath, workspaceCraftPath);

    private WorkspaceConfigEditor WorkspaceConfig => _workspaceConfig ??=
        new WorkspaceConfigEditor(appConfigMonitor, workspaceCraftPath);

    private AppServerMcpConfigService McpConfig => _mcpConfig ??=
        new AppServerMcpConfigService(appConfigMonitor, mcpClientManager, _hostWorkspacePath, workspaceCraftPath);

    private ExternalChannelConfigService ExternalChannelConfig => _externalChannelConfig ??=
        new ExternalChannelConfigService(channelListContributor, workspaceCraftPath);

    private AppServerRuntimeConfigRefresher RuntimeConfig => _runtimeConfig ??=
        new AppServerRuntimeConfigRefresher(sessionService, appConfigMonitor, workspaceCraftPath, WorkspaceConfig);

    private AppServerMethodTable? _domainMethods;

    /// <summary>
    /// Methods owned by built-in domain handlers extracted from this class (refactor M3/M4). The
    /// dispatcher resolves a request against this table first, then the in-class switch for
    /// not-yet-extracted domains, then protocol extensions. Built lazily so domain handlers can
    /// capture the connection-scoped services initialized above.
    /// </summary>
    private AppServerMethodTable DomainMethods => _domainMethods ??= BuildDomainMethods();

    private AppServerMethodTable BuildDomainMethods()
    {
        var table = new AppServerMethodTable();
        foreach (var handler in BuildDomainHandlers())
            handler.RegisterMethods(table);
        return table;
    }

    private IEnumerable<IAppServerDomainHandler> BuildDomainHandlers() =>
    [
        new CronRequestHandler(services.CronService, services.HeartbeatService, services.BroadcastCronStateChanged),
        new TerminalRequestHandler(services.BackgroundTerminalService, sessionService),
        new DreamsRequestHandler(services.DreamsService, services.DreamStore, services.AppConfigMonitor, services.WorkspaceCraftPath, services.ContextPageManager),
        new SkillsRequestHandler(services.SkillsLoader, services.ContextPageManager, services.AppConfigMonitor, services.WorkspaceCraftPath, SkillVariants),
        new McpRequestHandler(services.McpClientManager, McpConfig, services.AppConfigMonitor, services.BroadcastMcpStatusChanged),
        new ChannelRequestHandler(channelListContributor, services.ChannelStatusProvider, WorkspaceConfig, ExternalChannelConfig, services.WorkspaceCraftPath, services.AppConfigMonitor, services.OnExternalChannelUpserted, services.OnExternalChannelRemoved, services.ExternalChannelLogProvider),
        new ProviderRequestHandler(transport, WorkspaceConfig, RuntimeConfig, services.WorkspaceCraftPath, services.AppConfigMonitor, services.OpenAIClientProvider, services.OpenAIAuthService, services.OpenAIUsageService),
        new WorkspaceRequestHandler(services.CommitMessageSuggest, services.WelcomeSuggestionService, _configSchema, services.MemoryStore, services.DreamStore, services.DreamsService, services.LspServerManager, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, services.ContextPageManager, WorkspaceConfig, RuntimeConfig),
        new PluginRequestHandler(transport, services.SkillsLoader, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, services.BuiltInPluginSourceRoots, McpConfig, services.LspServerManager, services.ContextPageManager, services.AppBindingService, WorkspaceConfig),
        new UsageRequestHandler(services.TraceStore, services.SkillsLoader, sessionService, services.HostWorkspacePath),
        new AutomationRequestHandler(services.AutomationsHandler),
    ];

    private void MarkSkillsContextDirty() =>
        AppServerContextInvalidation.MarkSkills(contextPageManager);

    private static readonly HashSet<string> ReservedMethodNames =
    [
        AppServerMethods.Initialize,
        AppServerMethods.ChannelList,
        AppServerMethods.ChannelStatus,
        AppServerMethods.ProviderList,
        AppServerMethods.ProviderCreate,
        AppServerMethods.ProviderUpdate,
        AppServerMethods.ProviderDelete,
        AppServerMethods.ProviderTest,
        AppServerMethods.ModelList,
        AppServerMethods.ThreadStart,
        AppServerMethods.ThreadFork,
        AppServerMethods.WorktreeCreateAndFork,
        AppServerMethods.WorktreeList,
        AppServerMethods.WorktreeStatus,
        AppServerMethods.ThreadResume,
        AppServerMethods.ThreadList,
        AppServerMethods.ThreadRead,
        AppServerMethods.ThreadGoalGet,
        AppServerMethods.ThreadGoalSet,
        AppServerMethods.ThreadGoalClear,
        AppServerMethods.ItemWidgetStateSet,
        AppServerMethods.ThreadRollback,
        AppServerMethods.ThreadSubscribe,
        AppServerMethods.ThreadUnsubscribe,
        AppServerMethods.ThreadPause,
        AppServerMethods.ThreadArchive,
        AppServerMethods.ThreadUnarchive,
        AppServerMethods.ThreadDelete,
        AppServerMethods.ThreadRename,
        AppServerMethods.ThreadModeSet,
        AppServerMethods.ThreadConfigUpdate,
        AppServerMethods.TurnStart,
        AppServerMethods.TurnEnqueue,
        AppServerMethods.TurnQueueRemove,
        AppServerMethods.TurnQueueReorder,
        AppServerMethods.TurnSteer,
        AppServerMethods.TurnInterrupt,
        AppServerMethods.TerminalList,
        AppServerMethods.TerminalRead,
        AppServerMethods.TerminalWrite,
        AppServerMethods.TerminalStop,
        AppServerMethods.TerminalClean,
        AppServerMethods.WorkspaceCommitMessageSuggest,
        AppServerMethods.WelcomeSuggestions,
        AppServerMethods.WorkspaceConfigSchema,
        AppServerMethods.WorkspaceConfigUpdate,
        AppServerMethods.DreamsStatus,
        AppServerMethods.DreamsRun,
        AppServerMethods.DreamsCreate,
        AppServerMethods.DreamsGet,
        AppServerMethods.DreamsList,
        AppServerMethods.DreamsCancel,
        AppServerMethods.DreamsArchive,
        AppServerMethods.DreamsApply,
        AppServerMethods.DreamsDiscard,
        AppServerMethods.MemoryReset,
        AppServerMethods.UsageSummary,
        AppServerMethods.UsageTimeseries,
        AppServerMethods.ProfileInsights,
        AppServerMethods.CronList,
        AppServerMethods.CronRemove,
        AppServerMethods.CronEnable,
        AppServerMethods.CronRun,
        AppServerMethods.HeartbeatTrigger,
        AppServerMethods.SkillsList,
        AppServerMethods.SkillsRead,
        AppServerMethods.SkillsView,
        AppServerMethods.SkillsRestoreOriginal,
        AppServerMethods.SkillsSetEnabled,
        AppServerMethods.SkillsUninstall,
        AppServerMethods.PluginList,
        AppServerMethods.PluginView,
        AppServerMethods.PluginInstall,
        AppServerMethods.PluginRemove,
        AppServerMethods.PluginSetEnabled,
        AppServerMethods.CommandList,
        AppServerMethods.CommandExecute,
        AppServerMethods.AutomationTaskList,
        AppServerMethods.AutomationTaskRead,
        AppServerMethods.AutomationTaskCreate,
        AppServerMethods.AutomationTaskRun,
        AppServerMethods.AutomationTaskDelete,
        AppServerMethods.AutomationTaskUpdateBinding,
        AppServerMethods.AutomationTemplateList,
        AppServerMethods.AutomationTemplateSave,
        AppServerMethods.AutomationTemplateDelete,
        AppServerMethods.McpList,
        AppServerMethods.McpGet,
        AppServerMethods.McpUpsert,
        AppServerMethods.McpRemove,
        AppServerMethods.McpStatusList,
        AppServerMethods.McpTest,
        AppServerMethods.ExternalChannelList,
        AppServerMethods.ExternalChannelGet,
        AppServerMethods.ExternalChannelUpsert,
        AppServerMethods.ExternalChannelRemove,
        AppServerMethods.ExternalChannelLogs,
        AppServerMethods.SubAgentProfileList,
        AppServerMethods.SubAgentSettingsUpdate,
        AppServerMethods.SubAgentProfileSetEnabled,
        AppServerMethods.SubAgentProfileUpsert,
        AppServerMethods.SubAgentProfileRemove,
        AppServerMethods.SubAgentChildrenList,
        AppServerMethods.SubAgentSendMessage,
        AppServerMethods.SubAgentFollowupTask,
        AppServerMethods.SubAgentClose
    ];

    // -------------------------------------------------------------------------
    // Main dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dispatches an incoming request to the appropriate handler.
    /// Returns the JSON-RPC response to send to the client.
    /// Throws <see cref="AppServerException"/> for protocol errors.
    /// Domain exceptions from <see cref="ISessionService"/> are translated to spec-defined
    /// error codes (Section 8.3): -32010 ThreadNotFound, -32011 ThreadNotActive, -32012 TurnInProgress.
    /// </summary>
    public async Task<object?> HandleRequestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var method = msg.Method ?? string.Empty;

        // initialize is the only method allowed before the handshake
        if (method != AppServerMethods.Initialize && !connection.IsInitialized)
            throw AppServerErrors.NotInitialized();

        // After initialize response, block all requests until the client sends the
        // `initialized` notification (IsClientReady). This prevents premature operations
        // before the client has finished processing server capabilities.
        if (method != AppServerMethods.Initialize && connection.IsInitialized && !connection.IsClientReady)
            throw AppServerErrors.InvalidRequest("Server is awaiting the 'initialized' notification before handling requests.");

        try
        {
            // Extracted domain handlers register their methods in the table; resolve them first,
            // then fall through to the in-class switch for not-yet-extracted domains.
            if (DomainMethods.TryGet(method, out var domainHandler))
                return await domainHandler(msg, ct);

            // Route to the appropriate handler
            return await (method switch
            {
                AppServerMethods.Initialize => HandleInitializeAsync(msg, ct),
                AppServerMethods.SubAgentProfileList => HandleSubAgentProfileListAsync(msg, ct),
                AppServerMethods.SubAgentSettingsUpdate => HandleSubAgentSettingsUpdateAsync(msg, ct),
                AppServerMethods.SubAgentProfileSetEnabled => HandleSubAgentProfileSetEnabledAsync(msg, ct),
                AppServerMethods.SubAgentProfileUpsert => HandleSubAgentProfileUpsertAsync(msg, ct),
                AppServerMethods.SubAgentProfileRemove => HandleSubAgentProfileRemoveAsync(msg, ct),
                AppServerMethods.SubAgentChildrenList => HandleSubAgentChildrenListAsync(msg, ct),
                AppServerMethods.SubAgentSendMessage => HandleSubAgentSendMessageAsync(msg, ct),
                AppServerMethods.SubAgentFollowupTask => HandleSubAgentFollowupTaskAsync(msg, ct),
                AppServerMethods.SubAgentClose => HandleSubAgentCloseAsync(msg, ct),
                AppServerMethods.ThreadStart => HandleThreadStartAsync(msg, ct),
                AppServerMethods.ThreadFork => HandleThreadForkAsync(msg, ct),
                AppServerMethods.WorktreeCreateAndFork => HandleWorktreeCreateAndForkAsync(msg, ct),
                AppServerMethods.WorktreeCreateAndStart => HandleWorktreeCreateAndStartAsync(msg, ct),
                AppServerMethods.ThreadWorktreeHandoff => HandleThreadWorktreeHandoffAsync(msg, ct),
                AppServerMethods.WorktreeList => HandleWorktreeListAsync(msg, ct),
                AppServerMethods.WorktreeStatus => HandleWorktreeStatusAsync(msg, ct),
                AppServerMethods.ThreadResume => HandleThreadResumeAsync(msg, ct),
                AppServerMethods.ThreadList => HandleThreadListAsync(msg, ct),
                AppServerMethods.ThreadRead => HandleThreadReadAsync(msg, ct),
                AppServerMethods.ThreadGoalGet => HandleThreadGoalGetAsync(msg, ct),
                AppServerMethods.ThreadGoalSet => HandleThreadGoalSetAsync(msg, ct),
                AppServerMethods.ThreadGoalClear => HandleThreadGoalClearAsync(msg, ct),
                AppServerMethods.ItemWidgetStateSet => HandleItemWidgetStateSetAsync(msg, ct),
                AppServerMethods.ThreadCompactStart => HandleThreadCompactStartAsync(msg, ct),
                AppServerMethods.ThreadMemoryConsolidateStart => HandleThreadMemoryConsolidateStartAsync(msg, ct),
                AppServerMethods.ThreadMaintenanceInterrupt => HandleThreadMaintenanceInterruptAsync(msg, ct),
                AppServerMethods.ThreadRollback => HandleThreadRollbackAsync(msg, ct),
                AppServerMethods.ThreadSubscribe => HandleThreadSubscribeAsync(msg, ct),
                AppServerMethods.ThreadUnsubscribe => HandleThreadUnsubscribeAsync(msg, ct),
                AppServerMethods.ThreadPause => HandleThreadPauseAsync(msg, ct),
                AppServerMethods.ThreadArchive => HandleThreadArchiveAsync(msg, ct),
                AppServerMethods.ThreadUnarchive => HandleThreadUnarchiveAsync(msg, ct),
                AppServerMethods.ThreadDelete => HandleThreadDeleteAsync(msg, ct),
                AppServerMethods.ThreadRename => HandleThreadRenameAsync(msg, ct),
                AppServerMethods.ThreadModeSet => HandleThreadModeSetAsync(msg, ct),
                AppServerMethods.ThreadConfigUpdate => HandleThreadConfigUpdateAsync(msg, ct),
                AppServerMethods.TurnStart => HandleTurnStartAsync(msg, ct),
                AppServerMethods.TurnEnqueue => HandleTurnEnqueueAsync(msg, ct),
                AppServerMethods.TurnQueueRemove => HandleTurnQueueRemoveAsync(msg, ct),
                AppServerMethods.TurnQueueReorder => HandleTurnQueueReorderAsync(msg, ct),
                AppServerMethods.TurnSteer => HandleTurnSteerAsync(msg, ct),
                AppServerMethods.TurnInterrupt => HandleTurnInterruptAsync(msg, ct),
                AppServerMethods.CommandList => HandleCommandListAsync(msg, ct),
                AppServerMethods.CommandExecute => HandleCommandExecuteAsync(msg, ct),
                _ => TryHandleExtensionAsync(method, msg, ct)
            });
        }
        catch (KeyNotFoundException ex)
        {
            // Thread or turn not found in persistence or in-memory state
            throw AppServerErrors.ThreadNotFound(ExtractQuotedId(ex.Message));
        }
        catch (WorktreeHandoffConflictException ex)
        {
            throw AppServerErrors.WorktreeHandoffConflict(ex.ConflictPaths);
        }
        catch (InvalidOperationException ex)
        {
            throw MapOperationException(ex);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
    }

    /// <summary>
    /// Handles the <c>initialized</c> client notification (no response required).
    /// </summary>
    public void HandleInitializedNotification()
    {
        connection.MarkClientReady();
    }

    // -------------------------------------------------------------------------
    // initialize (spec Section 3.2)
    // -------------------------------------------------------------------------

    private Task<object?> HandleInitializeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AppServerInitializeParams>(msg);
        if (!connection.TryMarkInitialized(p.ClientInfo, p.Capabilities))
            throw AppServerErrors.AlreadyInitialized();

        var capabilities = new AppServerServerCapabilities
        {
            ThreadManagement = true,
            ThreadSubscriptions = true,
            ThreadGoals = GoalsCapabilityEnabled(),
            ThreadFork = true,
            GitWorktrees = true,
            ManualCompaction = true,
            ManualMemoryConsolidation = memoryStore != null,
            DynamicToolRebind = wireDynamicToolProxy != null,
            RuntimeAdditionalContext = wireRuntimeAdditionalContextProvider != null,
            ThreadMaintenanceInterrupt = true,
            ApprovalFlow = true,
            RequestUserInput = true,
            ModeSwitch = true,
            ConfigOverride = true,
            BackgroundTerminals = backgroundTerminalService != null,
            CronManagement = cronService != null,
            HeartbeatManagement = heartbeatService != null,
            SkillsManagement = skillsLoader != null,
            PluginManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            SkillVariants = skillsLoader != null && IsSkillVariantModeEnabled(),
            CommandManagement = true,
            Automations = automationsHandler != null,
            ChannelStatus = channelStatusProvider != null,
            ProviderManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            ModelCatalogManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            WorkspaceConfigManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            MemoryManagement = memoryStore != null,
            Dreams = dreamsService != null && !string.IsNullOrWhiteSpace(workspaceCraftPath),
            McpManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath) && mcpClientManager != null,
            McpServerOrigins = mcpClientManager != null,
            ExternalChannelManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            SubAgentManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            AuthOpenAiOAuth = openAIAuthService != null,
            AuthOpenAiUsage = openAIUsageService != null,
            SubAgentSessions = true,
            McpStatus = mcpClientManager != null,
            UsageTelemetry = traceStore != null
        };

        var capabilityBuilder = new AppServerCapabilityBuilder(capabilities, workspaceCraftPath);
        foreach (var contributor in _capabilityContributors)
            contributor.ContributeCapabilities(capabilityBuilder);
        if (welcomeSuggestionService != null)
            capabilityBuilder.SetExtension("welcomeSuggestions", true);

        var result = new AppServerInitializeResult
        {
            ServerInfo = new AppServerServerInfo
            {
                Name = "dotcraft",
                Version = serverVersion,
                ProtocolVersion = "1"
            },
            Capabilities = capabilities,
            DashboardUrl = dashboardUrl
        };

        return Task.FromResult<object?>(result);
    }

    // -------------------------------------------------------------------------
    // thread/* methods (spec Section 4)
    // -------------------------------------------------------------------------

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(_hostWorkspacePath))
            return identity with { WorkspacePath = _hostWorkspacePath };
        return identity;
    }

    private async Task<object?> HandleThreadStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadStartParams>(msg);
        if (!WireDynamicToolProxy.TryValidateSpecs(p.DynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(p.AdditionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (p.AdditionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");

        var identity = NormalizeIdentityWorkspace(p.Identity);

        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;

        if (p.Config?.Reasoning != null)
        {
            ValidateReasoningForRuntime(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                p.Config.ProviderId,
                p.Config.Model,
                p.Config.Reasoning);
        }

        var spawnSource = !string.IsNullOrWhiteSpace(p.SpawnedFromThreadId)
            ? ThreadSource.SpawnedFromThread(p.SpawnedFromThreadId.Trim())
            : null;

        var thread = await sessionService.CreateThreadAsync(
            identity,
            p.Config,
            historyMode,
            displayName: p.DisplayName,
            ct: ct,
            source: spawnSource);

        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);
        var shouldRefreshAgent = false;
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (p.DynamicTools is { Count: > 0 } && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, p.DynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, p.AdditionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);

        // Fix 8: The host sends the thread/start response first, then emits the
        // thread/started notification as required by spec Section 4.1.
        var startedWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(), thread.Id), thread),
            thread,
            ct);
        await SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = startedWire },
            AppServerMethods.ThreadStarted,
            new { thread = startedWire },
            ct);

        // Return null to signal the response will be sent inline by SendNotificationAfterResponseAsync
        return null;
    }

    private async Task<object?> HandleThreadForkAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadForkParams>(msg);
        if (!WireDynamicToolProxy.TryValidateSpecs(p.DynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(p.AdditionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (p.AdditionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");

        if (p.Config?.Reasoning != null)
        {
            ValidateReasoningForRuntime(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                p.Config.ProviderId,
                p.Config.Model,
                p.Config.Reasoning);
        }

        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var thread = await sessionService.ForkThreadAsync(
            p.ThreadId,
            new ThreadForkOptions
            {
                Path = p.Path,
                ForkPoint = p.ForkPoint,
                Identity = identity,
                Config = p.Config,
                DisplayName = p.DisplayName,
                Ephemeral = p.Ephemeral ?? false
            },
            ct);

        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);
        var shouldRefreshAgent = false;
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (p.DynamicTools is { Count: > 0 } && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, p.DynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, p.AdditionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);

        var includeTurns = p.ExcludeTurns != true;
        var responseWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(
                WithContextUsage(FilterToolExecutionItemsForConnection(thread.ToWire(includeTurns)), thread.Id),
                thread),
            thread,
            ct);
        var notificationWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(includeTurns: false), thread.Id), thread),
            thread,
            ct);

        await SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = responseWire },
            AppServerMethods.ThreadStarted,
            new { thread = notificationWire },
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeCreateAndForkAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<WorktreeCreateAndForkParams>(msg);
        if (!WireDynamicToolProxy.TryValidateSpecs(p.DynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(p.AdditionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (p.AdditionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");

        if (p.Config?.Reasoning != null)
        {
            ValidateReasoningForRuntime(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                p.Config.ProviderId,
                p.Config.Model,
                p.Config.Reasoning);
        }

        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var result = await sessionService.CreateWorktreeAndForkAsync(
            new WorktreeCreateAndForkOptions
            {
                SourceThreadId = p.SourceThreadId,
                ForkPoint = p.ForkPoint,
                Identity = identity,
                Config = p.Config,
                DisplayName = p.DisplayName,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);
        var thread = result.Thread;

        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);
        var shouldRefreshAgent = false;
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (p.DynamicTools is { Count: > 0 } && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, p.DynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, p.AdditionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);

        var includeTurns = p.ExcludeTurns != true;
        var responseWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(
                WithContextUsage(FilterToolExecutionItemsForConnection(thread.ToWire(includeTurns)), thread.Id),
                thread),
            thread,
            ct);
        var notificationWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(includeTurns: false), thread.Id), thread),
            thread,
            ct);

        await SendNotificationAfterResponseAsync(
            msg.Id,
            new WorktreeCreateAndForkResponse { Thread = responseWire, Worktree = result.Worktree },
            AppServerMethods.ThreadStarted,
            new { thread = notificationWire },
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeCreateAndStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<WorktreeCreateAndStartParams>(msg);
        if (!WireDynamicToolProxy.TryValidateSpecs(p.DynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(p.AdditionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (p.AdditionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");

        if (p.Config?.Reasoning != null)
        {
            ValidateReasoningForRuntime(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                p.Config.ProviderId,
                p.Config.Model,
                p.Config.Reasoning);
        }

        var identity = NormalizeIdentityWorkspace(p.Identity);
        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;
        var result = await sessionService.CreateWorktreeAndStartAsync(
            new WorktreeCreateAndStartOptions
            {
                Identity = identity,
                Config = p.Config,
                HistoryMode = historyMode,
                DisplayName = p.DisplayName,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);
        var thread = result.Thread;

        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);
        var shouldRefreshAgent = false;
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (p.DynamicTools is { Count: > 0 } && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, p.DynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, p.AdditionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);

        var startedWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(), thread.Id), thread),
            thread,
            ct);
        await SendNotificationAfterResponseAsync(
            msg.Id,
            new WorktreeCreateAndStartResponse { Thread = startedWire, Worktree = result.Worktree },
            AppServerMethods.ThreadStarted,
            new { thread = startedWire },
            ct);

        return null;
    }

    private async Task<object?> HandleThreadWorktreeHandoffAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadWorktreeHandoffParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var result = await sessionService.HandoffThreadWorktreeAsync(
            new WorktreeHandoffOptions
            {
                ThreadId = p.ThreadId,
                Mode = p.Mode,
                BranchName = p.BranchName,
                BaseRef = p.BaseRef,
                Path = p.Path,
                CopyDirtyChanges = p.CopyDirtyChanges ?? true
            },
            ct);
        var thread = result.Thread;
        var wire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(includeTurns: false), thread.Id), thread),
            thread,
            ct);

        var response = new ThreadWorktreeHandoffResponse
        {
            Thread = wire,
            Mode = result.Mode,
            Worktree = result.Worktree,
            DirtyHandoff = result.DirtyHandoff
        };

        await SendNotificationAfterResponseAsync(
            msg.Id,
            response,
            AppServerMethods.ThreadUpdated,
            new { thread = wire },
            ct);

        return null;
    }

    private async Task<object?> HandleWorktreeListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<WorktreeListParams>(msg);
        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var data = await sessionService.ListWorktreesAsync(identity, ct);
        return new WorktreeListResult { Data = data.ToList() };
    }

    private async Task<object?> HandleWorktreeStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<WorktreeStatusParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        return new WorktreeStatusResult
        {
            Status = await sessionService.GetWorktreeStatusAsync(p.ThreadId, ct)
        };
    }

    private async Task<object?> HandleThreadResumeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadResumeParams>(msg);
        if (!WireDynamicToolProxy.TryValidateSpecs(p.DynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(p.AdditionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (p.AdditionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");

        var thread = await sessionService.ResumeThreadAsync(p.ThreadId, ct);

        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);
        var shouldRefreshAgent = false;
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (p.DynamicTools is { Count: > 0 } && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, p.DynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, p.AdditionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);

        // Gap D: use the client's declared name from initialize instead of hardcoded "appserver".
        var resumedBy = connection.ClientInfo?.Name ?? "appserver";
        var resumedWire = await EnrichThreadWireAsync(
            WithRuntimeSnapshot(WithContextUsage(thread.ToWire(), thread.Id), thread),
            thread,
            ct);
        var responseResult = new { thread = resumedWire };
        var notifParams = new { thread = resumedWire, resumedBy };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: connection has a passive subscription — the broker/dispatcher path will
            // emit thread/resumed. Send only the response to avoid duplicating the notification.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, responseResult), ct);
            SchedulePendingInteractiveRequestReplay(p.ThreadId);
            return null;
        }

        // No subscription: send response then notification inline.
        await SendNotificationAfterResponseAsync(msg.Id, responseResult, AppServerMethods.ThreadResumed, notifParams, ct);
        SchedulePendingInteractiveRequestReplay(p.ThreadId);
        return null;
    }

    private async Task<object?> HandleThreadListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadListParams>(msg);
        var identity = NormalizeIdentityWorkspace(p.Identity);
        var crossOrigins = ResolveCrossChannelOriginsForThreadList(p);
        var threads = await sessionService.FindThreadsAsync(
            identity,
            p.IncludeArchived ?? false,
            crossOrigins,
            ct,
            p.IncludeSubAgents ?? false);

        if (p.IncludeInternal != true)
        {
            threads = threads
                .Where(t => !ThreadVisibility.IsInternal(t))
                .ToList();
        }

        if (!string.IsNullOrEmpty(p.ChannelName))
        {
            threads = threads
                .Where(t => string.Equals(t.OriginChannel, p.ChannelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(p.Query))
        {
            var query = p.Query.Trim();
            threads = threads
                .Where(t => MatchesThreadListQuery(t, query))
                .ToList();
        }

        var totalMatched = threads.Count;
        var isPaged = p.Limit.HasValue || !string.IsNullOrWhiteSpace(p.Cursor);
        var limit = isPaged
            ? NormalizePageLimit(p.Limit, ThreadListDefaultPageLimit, ThreadListMaxPageLimit, "limit")
            : totalMatched;
        var offset = DecodeCursorOffset(p.Cursor, ThreadListCursorKind);
        if (!isPaged && offset != 0)
            throw AppServerErrors.InvalidParams("'cursor' requires pagination.");

        var page = isPaged
            ? threads.Skip(offset).Take(limit).ToList()
            : threads.ToList();
        var nextOffset = offset + page.Count;
        var nextCursor = isPaged && nextOffset < totalMatched
            ? EncodeCursor(ThreadListCursorKind, nextOffset)
            : null;

        var data = new List<ThreadSummary>();
        var catalogByWorkspace = new Dictionary<string, AppCatalogSnapshot?>(StringComparer.Ordinal);
        foreach (var summary in page)
        {
            summary.Goal = await TryGetGoalSnapshotAsync(summary.Id, ct);
            if (!catalogByWorkspace.TryGetValue(summary.WorkspacePath, out var catalog))
            {
                catalog = TryGetAppCatalog(summary.WorkspacePath);
                catalogByWorkspace[summary.WorkspacePath] = catalog;
            }
            if (catalog is not null)
            {
                var appBindings = appBindingService!.ListThreadBindingSummaries(
                    catalog, Path.Combine(summary.WorkspacePath, ".craft"), summary.Id);
                if (appBindings.Count > 0)
                    summary.AppBindings = appBindings;
                summary.OriginApp = appBindingService.ResolveOriginApp(catalog, summary.OriginChannel, summary.ChannelContext);
            }
            data.Add(summary);
        }

        return new ThreadListResult
        {
            Data = data,
            NextCursor = nextCursor,
            TotalMatched = isPaged || !string.IsNullOrWhiteSpace(p.Query) ? totalMatched : null
        };
    }

    private async Task<object?> HandleSubAgentChildrenListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<SubAgentChildrenListParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId))
            throw AppServerErrors.InvalidParams("'parentThreadId' is required.");

        var edges = await sessionService.ListSubAgentChildrenAsync(
            p.ParentThreadId.Trim(),
            p.IncludeClosed ?? false,
            ct);
        var data = new List<SubAgentChildWire>();
        foreach (var edge in edges)
        {
            SessionWireThread? thread = null;
            if (p.IncludeThreads == true)
            {
                var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
                thread = await EnrichThreadWireAsync(child.ToWire(includeTurns: false), child, ct);
            }

            data.Add(new SubAgentChildWire { Edge = edge, Thread = thread });
        }

        return new SubAgentChildrenListResult { Data = data };
    }

    private async Task<object?> HandleSubAgentSendMessageAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<SubAgentTargetMessageParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target) || string.IsNullOrWhiteSpace(p.Message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.SendMessageAsync(context, p.Target.Trim(), p.Message, ct);
    }

    private async Task<object?> HandleSubAgentFollowupTaskAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<SubAgentTargetMessageParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target) || string.IsNullOrWhiteSpace(p.Message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.FollowupTaskAsync(
            context,
            p.Target.Trim(),
            p.Message,
            CreateSubAgentCoordinator(context.ParentThread),
            ct,
            p.DeliveryMode);
    }

    private async Task<object?> HandleSubAgentCloseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<SubAgentTargetParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target))
            throw AppServerErrors.InvalidParams("'parentThreadId' and 'target' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.CloseAgentAsync(context, p.Target.Trim(), ct);
    }

    private async Task<SubAgentSessionContext> BuildSubAgentControlContextAsync(string parentThreadId, CancellationToken ct)
    {
        var parent = await sessionService.GetThreadAsync(parentThreadId, ct);
        var source = parent.Source.SubAgent;
        return new SubAgentSessionContext
        {
            SessionService = sessionService,
            ParentThread = parent,
            ParentTurnId = string.Empty,
            RootThreadId = string.IsNullOrWhiteSpace(source?.RootThreadId) ? parent.Id : source.RootThreadId,
            Depth = source?.Depth ?? 0
        };
    }

    private SubAgentCoordinator? CreateSubAgentCoordinator(SessionThread thread)
    {
        if (subAgentCoordinatorFactory != null)
            return subAgentCoordinatorFactory(thread);

        var config = appConfigMonitor?.Current ?? new AppConfig();
        var workspaceRoot = !string.IsNullOrWhiteSpace(thread.WorkspacePath)
            ? thread.WorkspacePath
            : _hostWorkspacePath ?? Environment.CurrentDirectory;
        return new SubAgentCoordinator(
            workspaceRoot,
            [new CliOneshotRuntime()],
            config.SubAgentProfiles,
            disabledProfiles: config.SubAgent.DisabledProfiles,
            externalCliSessionStore: new ThreadExternalCliSessionStore(thread),
            enableExternalCliSessionResume: config.SubAgent.EnableExternalCliSessionResume);
    }

    private async Task BindNodeReplThreadAndRefreshAgentAsync(string threadId, CancellationToken ct)
    {
        wireNodeReplProxy!.BindThread(threadId, transport, connection);
        if (sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(threadId, ct);
    }

    /// <summary>
    /// Passes through <c>crossChannelOrigins</c> from the client; when omitted or null, no cross-channel list is applied.
    /// </summary>
    private static IReadOnlyList<string>? ResolveCrossChannelOriginsForThreadList(ThreadListParams p) =>
        p.CrossChannelOrigins;

    private static bool MatchesThreadListQuery(ThreadSummary summary, string query)
    {
        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);

        return Contains(summary.Id, query)
               || Contains(summary.DisplayName, query)
               || Contains(summary.OriginChannel, query)
               || Contains(summary.ChannelContext, query)
               || Contains(summary.Status.ToString(), query);
    }

    private static int NormalizePageLimit(int? limit, int defaultLimit, int maxLimit, string fieldName)
    {
        var value = limit ?? defaultLimit;
        if (value <= 0)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive integer.");
        if (value > maxLimit)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be at most {maxLimit}.");
        return value;
    }

    private static string EncodeCursor(string kind, int offset)
    {
        var raw = $"{kind}:{offset.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursorOffset(string? cursor, string expectedKind)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var prefix = expectedKind + ":";
            if (!raw.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(raw[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0)
            {
                throw AppServerErrors.InvalidParams("'cursor' is invalid.");
            }

            return offset;
        }
        catch (AppServerException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }
    }

    private static void ValidateReasoningForRuntime(
        AppConfig config,
        string? providerId,
        string? model,
        AppConfig.ReasoningConfig reasoning)
    {
        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveMain(config, providerId, model);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (ModelProviderConfigurationException)
        {
            return;
        }

        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            config,
            runtime.Protocol,
            runtime.EndPoint,
            runtime.Model);
        if (capability == null)
            return;

        if (!reasoning.Enabled)
        {
            if (!capability.SupportsDisable)
            {
                throw AppServerErrors.InvalidParams(
                    $"Model '{runtime.Model}' does not support disabling reasoning.");
            }

            return;
        }

        if (capability.SupportedEfforts.All(option => option.Effort != reasoning.Effort))
        {
            throw AppServerErrors.InvalidParams(
                $"Model '{runtime.Model}' does not support reasoning effort '{reasoning.Effort}'.");
        }
    }

    private AppConfig LoadCurrentMergedConfig()
        => WorkspaceConfig.LoadCurrentMergedConfig();

    private string? GetEffectiveGlobalConfigPath() => WorkspaceConfig.EffectiveGlobalConfigPath;

    private string GetPersonalConfigPath() => WorkspaceConfig.PersonalConfigPath;

    private Task<object?> HandleSubAgentProfileListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        EnsureSubAgentManagementAvailable();

        var listResult = BuildSubAgentProfileListResult();
        return Task.FromResult<object?>(listResult);
    }

    private Task<object?> HandleSubAgentSettingsUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = GetParams<SubAgentSettingsUpdateParams>(msg);
        JsonElement modelEl = default;
        JsonElement minWaitTimeoutMsEl = default;
        JsonElement defaultWaitTimeoutMsEl = default;
        JsonElement maxWaitTimeoutMsEl = default;
        var hasModel = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "model", out modelEl);
        var hasMinWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "minWaitTimeoutMs", out minWaitTimeoutMsEl);
        var hasDefaultWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "defaultWaitTimeoutMs", out defaultWaitTimeoutMsEl);
        var hasMaxWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "maxWaitTimeoutMs", out maxWaitTimeoutMsEl);
        if (!p.ExternalCliSessionResumeEnabled.HasValue
            && !hasModel
            && !hasMinWaitTimeoutMs
            && !hasDefaultWaitTimeoutMs
            && !hasMaxWaitTimeoutMs)
        {
            throw AppServerErrors.InvalidParams("At least one of 'externalCliSessionResumeEnabled', 'model', 'minWaitTimeoutMs', 'defaultWaitTimeoutMs', or 'maxWaitTimeoutMs' is required.");
        }

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var nextResumeEnabled = p.ExternalCliSessionResumeEnabled ?? state.EnableExternalCliSessionResume;
        var nextModel = hasModel
            ? NormalizeOptionalString(ParseNullableString(modelEl, "model")) ?? string.Empty
            : state.Model;
        var nextWaitAgentTimeouts = new SubAgentWaitAgentTimeoutOptions(
            hasMinWaitTimeoutMs ? ParseInteger(minWaitTimeoutMsEl, "minWaitTimeoutMs") : state.WaitAgentTimeouts.MinTimeoutMs,
            hasDefaultWaitTimeoutMs ? ParseInteger(defaultWaitTimeoutMsEl, "defaultWaitTimeoutMs") : state.WaitAgentTimeouts.DefaultTimeoutMs,
            hasMaxWaitTimeoutMs ? ParseInteger(maxWaitTimeoutMsEl, "maxWaitTimeoutMs") : state.WaitAgentTimeouts.MaxTimeoutMs);
        var waitAgentTimeoutErrors = SubAgentWaitAgentTimeoutOptions.Validate(nextWaitAgentTimeouts);
        if (waitAgentTimeoutErrors.Count > 0)
            throw AppServerErrors.InvalidParams(string.Join(" ", waitAgentTimeoutErrors));

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            state.DisabledProfiles,
            nextResumeEnabled,
            nextModel,
            nextWaitAgentTimeouts,
            state.Profiles);
        RefreshCurrentSubAgentConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentSettingsUpdate,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult<object?>(new SubAgentSettingsUpdateResult
        {
            Settings = new SubAgentSettingsWire
            {
                ExternalCliSessionResumeEnabled = nextResumeEnabled,
                Model = string.IsNullOrWhiteSpace(nextModel) ? null : nextModel,
                MinWaitTimeoutMs = nextWaitAgentTimeouts.MinTimeoutMs,
                DefaultWaitTimeoutMs = nextWaitAgentTimeouts.DefaultTimeoutMs,
                MaxWaitTimeoutMs = nextWaitAgentTimeouts.MaxTimeoutMs
            }
        });
    }

    private Task<object?> HandleSubAgentProfileSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = GetParams<SubAgentProfileSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        if (string.Equals(p.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase) && !p.Enabled)
            throw AppServerErrors.SubAgentProfileProtected($"'{SubAgentCoordinator.DefaultProfileName}' cannot be disabled.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var builtIns = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var registry = new SubAgentProfileRegistry(
            state.Profiles,
            builtIns,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            state.DisabledProfiles);
        if (!registry.TryGet(p.Name, out _))
            throw AppServerErrors.SubAgentProfileNotFound(p.Name);

        var disabled = state.DisabledProfiles.ToList();
        if (p.Enabled)
            disabled.RemoveAll(name => string.Equals(name, p.Name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(p.Name);

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            disabled,
            state.EnableExternalCliSessionResume,
            state.Model,
            state.WaitAgentTimeouts,
            state.Profiles);
        RefreshCurrentSubAgentConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileSetEnabled,
            [ConfigChangeRegions.SubAgent]);

        var updated = BuildSubAgentProfileListResult().Profiles
            .First(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SubAgentProfileSetEnabledResult { Profile = updated });
    }

    private Task<object?> HandleSubAgentProfileUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = GetParams<SubAgentProfileUpsertParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var profile = MapWireToSubAgentProfile(p.Name, p.Definition);
        ValidateSubAgentProfileWire(profile);

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var profiles = state.Profiles.Select(existing => existing.Clone()).ToList();
        var existingIndex = profiles.FindIndex(existing => string.Equals(existing.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            profiles[existingIndex] = profile;
        else
            profiles.Add(profile);

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            state.DisabledProfiles,
            state.EnableExternalCliSessionResume,
            state.Model,
            state.WaitAgentTimeouts,
            profiles);
        RefreshCurrentSubAgentConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileUpsert,
            [ConfigChangeRegions.SubAgent]);

        var updated = BuildSubAgentProfileListResult().Profiles
            .First(entry => string.Equals(entry.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SubAgentProfileUpsertResult { Profile = updated });
    }

    private Task<object?> HandleSubAgentProfileRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = GetParams<SubAgentProfileRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var workspaceProfiles = state.Profiles.Select(profile => profile.Clone()).ToList();
        var existingIndex = workspaceProfiles.FindIndex(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
            throw AppServerErrors.SubAgentProfileNotFound(p.Name);

        workspaceProfiles.RemoveAt(existingIndex);
        var disabled = state.DisabledProfiles.ToList();
        var isBuiltIn = SubAgentProfileRegistry.CreateBuiltInProfiles()
            .Any(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (!isBuiltIn)
            disabled.RemoveAll(name => string.Equals(name, p.Name, StringComparison.OrdinalIgnoreCase));

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            disabled,
            state.EnableExternalCliSessionResume,
            state.Model,
            state.WaitAgentTimeouts,
            workspaceProfiles);
        RefreshCurrentSubAgentConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileRemove,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult<object?>(new SubAgentProfileRemoveResult { Removed = true });
    }

    private async Task<object?> HandleThreadReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadReadParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var isPaged = p.TurnLimit.HasValue || !string.IsNullOrWhiteSpace(p.Cursor);
        var includeTurns = (p.IncludeTurns ?? false) || isPaged;
        ThreadReadTurnPage? turnPage = null;
        SessionWireThread baseWire;
        if (isPaged)
        {
            var limit = NormalizePageLimit(p.TurnLimit, ThreadListDefaultPageLimit, ThreadReadMaxPageLimit, "turnLimit");
            var offset = DecodeCursorOffset(p.Cursor, ThreadReadCursorKind);
            var totalTurns = thread.Turns.Count;
            var startIndex = Math.Max(0, totalTurns - offset - limit);
            var count = Math.Max(0, totalTurns - offset - startIndex);
            var pageTurns = count == 0
                ? new List<SessionWireTurn>()
                : thread.Turns.Skip(startIndex).Take(count).Select(t => t.ToWire(includeItems: true)).ToList();
            var nextOffset = offset + count;
            var hasMore = startIndex > 0;
            turnPage = new ThreadReadTurnPage
            {
                Limit = limit,
                TotalTurns = totalTurns,
                StartOrdinal = count == 0 ? 0 : startIndex + 1,
                EndOrdinal = count == 0 ? 0 : startIndex + count,
                NextCursor = hasMore ? EncodeCursor(ThreadReadCursorKind, nextOffset) : null,
                HasMore = hasMore
            };
            baseWire = thread.ToWire(includeTurns: false) with { Turns = pageTurns };
        }
        else
        {
            baseWire = thread.ToWire(includeTurns);
        }

        var wire = await WithPlanAsync(WithRuntimeSnapshot(
            WithWidgetState(WithContextUsage(FilterToolExecutionItemsForConnection(baseWire), thread.Id), thread.Id),
            thread), thread.Id, ct);
        return new ThreadReadResult
        {
            Thread = await EnrichThreadWireAsync(wire, thread, ct),
            TurnPage = turnPage
        };
    }

    /// <summary>
    /// Surfaces the UI-only Interactive Tool UI <c>widgetState</c> (M-iv) onto each
    /// <c>dynamicToolCall</c> wire item from the mutable side store, so the host can restore the
    /// iframe's persisted state on <c>thread/read</c>. Never reaches the model.
    /// </summary>
    private SessionWireThread WithWidgetState(SessionWireThread wire, string threadId)
    {
        if (wire.Turns is not { Count: > 0 } turns)
            return wire;
        var states = sessionService.GetItemWidgetStates(threadId);
        if (states.Count == 0)
            return wire;

        foreach (var turn in turns)
        {
            if (turn.Items is not { Count: > 0 } items)
                continue;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Payload is not DynamicToolCallPayload payload
                    || string.IsNullOrEmpty(payload.CallId)
                    || !states.TryGetValue(payload.CallId, out var json))
                {
                    continue;
                }

                try
                {
                    if (System.Text.Json.Nodes.JsonNode.Parse(json) is { } node)
                        items[i] = items[i] with { Payload = payload with { WidgetState = node } };
                }
                catch (System.Text.Json.JsonException)
                {
                    // Skip a corrupt stored state rather than failing the whole read.
                }
            }
        }

        return wire;
    }

    private Task<object?> HandleItemWidgetStateSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ItemWidgetStateSetParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.CallId))
            throw AppServerErrors.InvalidParams("'callId' is required.");

        var json = p.WidgetState?.ToJsonString();
        var cleared = string.IsNullOrEmpty(json);
        if (!cleared && System.Text.Encoding.UTF8.GetByteCount(json!) > MaxWidgetStateBytes)
            throw AppServerErrors.InvalidParams($"widgetState exceeds the {MaxWidgetStateBytes}-byte limit.");

        sessionService.SetItemWidgetState(p.ThreadId, p.CallId, cleared ? null : json);
        return Task.FromResult<object?>(new ItemWidgetStateSetResult { Cleared = cleared });
    }

    private async Task<object?> HandleThreadGoalGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalGet);

        var p = GetParams<ThreadGoalGetParams>(msg);
        var goal = await sessionService.GetThreadGoalAsync(p.ThreadId, ct);
        return new ThreadGoalGetResult { Goal = goal };
    }

    private async Task<object?> HandleThreadGoalSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalSet);

        var p = GetParams<ThreadGoalSetParams>(msg);
        var update = new ThreadGoalUpdate
        {
            Objective = p.Objective,
            Status = ParseThreadGoalStatus(p.Status),
            HasTokenBudget = p.TokenBudget.HasValue,
            TokenBudget = ParseThreadGoalBudget(p.TokenBudget)
        };
        var mode = ParseGoalSetMode(p.Mode);
        ThreadGoal goal;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            goal = await sessionService.SetThreadGoalAsync(p.ThreadId, update, mode, ct);
        }

        var result = new ThreadGoalSetResult { Goal = goal };
        await SendNotificationAfterResponseAsync(
            msg.Id,
            result,
            AppServerMethods.ThreadGoalUpdated,
            new { threadId = goal.ThreadId, goal, turnId = (string?)null },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadGoalClearAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalClear);

        var p = GetParams<ThreadGoalClearParams>(msg);
        ThreadGoalClearResult result;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            result = await sessionService.ClearThreadGoalAsync(p.ThreadId, ct);
        }

        var wireResult = new ThreadGoalClearResultWire { Cleared = result.Cleared };
        if (!result.Cleared)
            return wireResult;

        await SendNotificationAfterResponseAsync(
            msg.Id,
            wireResult,
            AppServerMethods.ThreadGoalCleared,
            new { threadId = p.ThreadId },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadCompactStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadCompactStartParams>(msg);
        var result = await sessionService.CompactThreadAsync(p.ThreadId, ct);
        return new ThreadCompactStartResponse
        {
            Outcome = result.Outcome,
            Message = result.Message,
            ContextUsage = result.ContextUsage
        };
    }

    private async Task<object?> HandleThreadMemoryConsolidateStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadMemoryConsolidateStartParams>(msg);
        var result = await sessionService.ConsolidateThreadMemoryAsync(p.ThreadId, ct);
        return new ThreadMemoryConsolidateStartResponse
        {
            Outcome = result.Outcome,
            Message = result.Message,
            MemoryWritten = result.MemoryWritten,
            HistoryWritten = result.HistoryWritten
        };
    }

    private async Task<object?> HandleThreadMaintenanceInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadMaintenanceInterruptParams>(msg);
        await sessionService.CancelThreadMaintenanceAsync(p.ThreadId, ct);
        return new { };
    }

    private async Task<object?> HandleThreadRollbackAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadRollbackParams>(msg);
        if (p.NumTurns <= 0)
            throw AppServerErrors.InvalidParams("'numTurns' must be >= 1.");

        var thread = await sessionService.RollbackThreadAsync(p.ThreadId, p.NumTurns, ct);
        return new ThreadRollbackResponse
        {
            Thread = await HydrateThreadGoalAsync(
                WithAppBindingAttribution(
                    WithRuntimeSnapshot(
                        WithContextUsage(FilterToolExecutionItemsForConnection(thread.ToWire(includeTurns: true)), thread.Id),
                        thread),
                    thread.Id,
                    thread.WorkspacePath),
                ct)
        };
    }

    private SessionWireThread FilterToolExecutionItemsForConnection(SessionWireThread wire)
    {
        if (connection.SupportsToolExecutionLifecycle || wire.Turns is null)
            return wire;

        foreach (var turn in wire.Turns)
            turn.Items?.RemoveAll(item => item.Type == ItemType.ToolExecution);
        return wire;
    }

    private SessionWireThread WithContextUsage(SessionWireThread wire, string threadId)
    {
        var snapshot = sessionService.TryGetContextUsageSnapshot(threadId);
        return snapshot is null ? wire : wire with { ContextUsage = snapshot };
    }

    private SessionWireThread WithRuntimeSnapshot(SessionWireThread wire, SessionThread thread) =>
        wire with { Runtime = sessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState() };

    private async Task<SessionWireThread> WithPlanAsync(
        SessionWireThread wire,
        string threadId,
        CancellationToken ct)
    {
        if (planStore == null)
            return wire;

        var plan = await planStore.LoadStructuredPlanAsync(threadId);
        ct.ThrowIfCancellationRequested();
        return wire with { Plan = plan == null ? null : DotCraft.Protocol.SessionWireMapper.ToWire(plan) };
    }

    private async Task<SessionWireThread> EnrichThreadWireAsync(
        SessionWireThread wire,
        SessionThread thread,
        CancellationToken ct) =>
        await HydrateThreadGoalAsync(WithAppBindingAttribution(wire, thread.Id, thread.WorkspacePath), ct);

    /// <summary>
    /// Attaches App Binding attribution to a thread wire: lightweight app-binding summaries plus, when the
    /// thread's origin channel matches an installed app's declared origin channel, the origin-app branding
    /// badge. Discovers the workspace catalog once so <c>thread/started</c>, <c>thread/updated</c>,
    /// <c>thread/fork</c>, <c>thread/read</c>, and <c>thread/rollback</c> carry the same attribution as
    /// <c>thread/list</c> — without it, event-delivered threads fall back to the generic channel icon.
    /// </summary>
    private SessionWireThread WithAppBindingAttribution(
        SessionWireThread wire,
        string threadId,
        string workspacePath)
    {
        if (appBindingService is null || string.IsNullOrWhiteSpace(threadId))
            return wire;
        var catalog = TryGetAppCatalog(workspacePath);
        if (catalog is null)
            return wire;
        var appBindings = appBindingService.ListThreadBindingSummaries(
            catalog, Path.Combine(workspacePath, ".craft"), threadId);
        var originApp = appBindingService.ResolveOriginApp(catalog, wire.OriginChannel, wire.ChannelContext);
        if (appBindings.Count == 0 && originApp is null)
            return wire;
        return wire with
        {
            AppBindings = appBindings.Count > 0 ? appBindings : wire.AppBindings,
            OriginApp = originApp ?? wire.OriginApp
        };
    }

    /// <summary>
    /// Synchronous thread-wire enricher handed to <see cref="AppServerEventDispatcher"/> so
    /// <c>thread/started</c> (ThreadCreated) and <c>thread/resumed</c> notifications carry the same
    /// App Binding / origin-app attribution as <c>thread/list</c>. Used for server-originated threads
    /// (e.g. Teams mission member threads) that reach the client only via the event stream.
    /// </summary>
    private SessionWireThread EnrichThreadWireForNotification(SessionWireThread wire) =>
        WithAppBindingAttribution(wire, wire.Id, wire.WorkspacePath);

    /// <summary>
    /// Discovers the App Binding catalog for a workspace, or null when App Binding is unavailable or
    /// the workspace has no <c>.craft</c> directory. Shared by thread-summary enrichment (app bindings
    /// + origin-app attribution) so a single <c>thread/list</c> projection discovers each workspace once.
    /// </summary>
    private AppCatalogSnapshot? TryGetAppCatalog(string workspacePath)
    {
        if (appBindingService == null || string.IsNullOrWhiteSpace(workspacePath))
            return null;
        var craftPath = Path.Combine(workspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return null;
        return appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
            workspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
    }

    private void RevokeAppBindingsForDeletedThread(SessionThread thread)
    {
        if (appBindingService == null)
            return;

        var craftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return;

        var catalog = appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
            thread.WorkspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
        _ = appBindingService.RevokeBindingsForDeletedThread(catalog, craftPath, thread.Id);
    }

    private async Task<SessionWireThread> HydrateThreadGoalAsync(SessionWireThread wire, CancellationToken ct)
    {
        var goal = await TryGetGoalSnapshotAsync(wire.Id, ct);
        return goal is null ? wire : wire with { Goal = goal };
    }

    private async Task<ThreadGoal?> TryGetGoalSnapshotAsync(string threadId, CancellationToken ct)
    {
        if (!GoalsCapabilityEnabled())
            return null;

        try
        {
            return await sessionService.GetThreadGoalAsync(threadId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static ThreadGoalStatus? ParseThreadGoalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim() switch
        {
            "active" => ThreadGoalStatus.Active,
            "paused" => ThreadGoalStatus.Paused,
            "budgetLimited" => ThreadGoalStatus.BudgetLimited,
            "complete" => ThreadGoalStatus.Complete,
            _ => throw AppServerErrors.InvalidParams("'status' must be active, paused, budgetLimited, or complete.")
        };
    }

    private static GoalSetMode ParseGoalSetMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GoalSetMode.UpsertOrUpdate;

        return value.Trim() switch
        {
            "upsertOrUpdate" => GoalSetMode.UpsertOrUpdate,
            "createOnly" => GoalSetMode.CreateOnly,
            "updateOnly" => GoalSetMode.UpdateOnly,
            "replaceExisting" => GoalSetMode.ReplaceExisting,
            _ => throw AppServerErrors.InvalidParams("'mode' must be upsertOrUpdate, createOnly, updateOnly, or replaceExisting.")
        };
    }

    private static long? ParseThreadGoalBudget(JsonElement? value)
    {
        if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetInt64(out var budget))
            throw AppServerErrors.InvalidParams("'tokenBudget' must be a positive integer or null.");
        if (budget <= 0)
            throw AppServerErrors.InvalidParams("'tokenBudget' must be a positive integer or null.");
        return budget;
    }

    private async Task<object?> HandleThreadSubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadSubscribeParams>(msg);

        // Start a background subscription that fans out thread events to this connection
        var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!connection.TryAddSubscription(p.ThreadId, subCts))
        {
            // Already subscribed — idempotent, just return success
            subCts.Dispose();
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            SchedulePendingInteractiveRequestReplay(p.ThreadId);
            return null;
        }

        var events = sessionService.SubscribeThreadAsync(
            p.ThreadId,
            p.ReplayRecent ?? false,
            subCts.Token);

        var dispatcher = new AppServerEventDispatcher(
            events, connection, transport, sessionService,
            defaultApprovalDecision: _defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger,
            enrichThreadWire: EnrichThreadWireForNotification);
        _ = dispatcher.RunAsync(subCts.Token)
            .ContinueWith(t =>
            {
                connection.TryCancelSubscription(p.ThreadId);
                if (t.IsFaulted)
                    _ = Console.Error.WriteLineAsync(
                        $"[AppServer] Subscription error for thread {p.ThreadId}: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.ExecuteSynchronously);

        await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
        SchedulePendingInteractiveRequestReplay(p.ThreadId);
        return null;
    }

    private Task<object?> HandleThreadUnsubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadUnsubscribeParams>(msg);
        connection.TryCancelSubscription(p.ThreadId);
        return Task.FromResult<object?>(new { });
    }

    private void SchedulePendingInteractiveRequestReplay(string threadId)
    {
        _ = ReplayPendingInteractiveRequestsAsync(threadId);
    }

    private async Task ReplayPendingInteractiveRequestsAsync(string threadId)
    {
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, CancellationToken.None);
            ReplayPendingInteractiveRequests(thread);
        }
        catch (KeyNotFoundException)
        {
            // The thread may have been deleted between subscribe/resume and replay.
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending interactive request replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private void ReplayPendingInteractiveRequests(SessionThread thread)
    {
        var sender = CreateInteractiveRequestSender();
        foreach (var turn in thread.Turns)
        {
            if (turn.Status == TurnStatus.WaitingApproval
                && TryFindPendingApprovalRequest(turn, out var approvalItem, out var approvalRequest))
            {
                _ = ReplayApprovalRequestAsync(sender, thread.Id, turn.Id, approvalItem.Id, approvalRequest);
            }
            else if (turn.Status == TurnStatus.WaitingInput
                && TryFindPendingUserInputRequest(turn, out var userInputItem, out var userInputRequest))
            {
                _ = ReplayUserInputRequestAsync(sender, thread.Id, turn.Id, userInputItem.Id, userInputRequest);
            }
        }
    }

    private AppServerInteractiveRequestSender CreateInteractiveRequestSender() =>
        new(
            connection,
            transport,
            sessionService,
            _defaultApprovalDecision);

    private static async Task ReplayApprovalRequestAsync(
        AppServerInteractiveRequestSender sender,
        string threadId,
        string turnId,
        string itemId,
        ApprovalRequestPayload request)
    {
        try
        {
            await sender.SendApprovalRequestAsync(threadId, turnId, itemId, request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending approval replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private static async Task ReplayUserInputRequestAsync(
        AppServerInteractiveRequestSender sender,
        string threadId,
        string turnId,
        string itemId,
        UserInputRequestPayload request)
    {
        try
        {
            await sender.SendUserInputRequestAsync(threadId, turnId, itemId, request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending user-input replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private static bool TryFindPendingApprovalRequest(
        SessionTurn turn,
        out SessionItem item,
        out ApprovalRequestPayload request)
    {
        foreach (var candidate in turn.Items.AsEnumerable().Reverse())
        {
            if (candidate.Payload is not ApprovalRequestPayload approvalRequest)
                continue;
            if (HasApprovalResponse(turn, approvalRequest.RequestId))
                continue;

            item = candidate;
            request = approvalRequest;
            return true;
        }

        item = null!;
        request = null!;
        return false;
    }

    private static bool TryFindPendingUserInputRequest(
        SessionTurn turn,
        out SessionItem item,
        out UserInputRequestPayload request)
    {
        foreach (var candidate in turn.Items.AsEnumerable().Reverse())
        {
            if (candidate.Payload is not UserInputRequestPayload userInputRequest)
                continue;
            if (HasUserInputResponse(turn, userInputRequest.RequestId))
                continue;

            item = candidate;
            request = userInputRequest;
            return true;
        }

        item = null!;
        request = null!;
        return false;
    }

    private static bool HasApprovalResponse(SessionTurn turn, string requestId) =>
        turn.Items.Any(item => item.Payload is ApprovalResponsePayload response
            && string.Equals(response.RequestId, requestId, StringComparison.Ordinal));

    private static bool HasUserInputResponse(SessionTurn turn, string requestId) =>
        turn.Items.Any(item => item.Payload is UserInputResponsePayload response
            && string.Equals(response.RequestId, requestId, StringComparison.Ordinal));

    private async Task<object?> HandleThreadPauseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadPauseParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.PauseThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already paused, no status change occurred.
        if (previousStatus == ThreadStatus.Paused)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Paused },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadArchiveParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.ArchiveThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already archived, no status change occurred.
        if (previousStatus == ThreadStatus.Archived)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Archived },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadUnarchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadUnarchiveParams>(msg);

        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.UnarchiveThreadAsync(p.ThreadId, ct);

        if (previousStatus == ThreadStatus.Active)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Active },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadDeleteParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        await sessionService.DeleteThreadPermanentlyAsync(p.ThreadId, ct);
        RevokeAppBindingsForDeletedThread(thread);
        return new { };
    }

    private async Task<object?> HandleThreadRenameAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadRenameParams>(msg);
        if (string.IsNullOrWhiteSpace(p.DisplayName))
            throw AppServerErrors.InvalidParams("'displayName' must not be empty.");
        await sessionService.RenameThreadAsync(p.ThreadId, p.DisplayName, ct);
        return new { };
    }

    private async Task<object?> HandleThreadModeSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadModeSetParams>(msg);
        await sessionService.SetThreadModeAsync(p.ThreadId, p.Mode, ct);
        return new { };
    }

    private async Task<object?> HandleThreadConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadConfigUpdateParams>(msg);
        if (p.Config.Reasoning != null)
        {
            ValidateReasoningForRuntime(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                p.Config.ProviderId,
                p.Config.Model,
                p.Config.Reasoning);
        }

        await sessionService.UpdateThreadConfigurationAsync(p.ThreadId, p.Config, ct);
        return new { };
    }

    // -------------------------------------------------------------------------
    // turn/* methods (spec Section 5)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records a forward-only <see cref="TraceEventType.SkillReferenced"/> event for each skill
    /// (<c>$name</c>) referenced in a turn's input, keyed by the thread's trace session
    /// (the trace session key equals the threadId for main turns). Powers the Profile skill
    /// metrics (spec §27A.5). Best-effort: a no-op when tracing is disabled.
    /// </summary>
    private void RecordSkillReferences(string threadId, IReadOnlyList<SessionWireInputPart> nativeParts)
    {
        if (traceStore == null || string.IsNullOrWhiteSpace(threadId))
            return;

        foreach (var part in nativeParts)
        {
            if (!string.Equals(part.Type, "skillRef", StringComparison.Ordinal))
                continue;

            var name = part.Name?.Trim().TrimStart('/', '$').Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            traceStore.Record(new TraceEvent
            {
                Type = TraceEventType.SkillReferenced,
                SessionKey = threadId,
                ToolName = name
            });
        }
    }

    private async Task<object?> HandleTurnStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnStartParams>(msg);

        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var inputMaterialization = new InputMaterializationService(
            _commandRegistry,
            skillsLoader,
            IsSkillVariantModeEnabled(),
            BuildSkillVariantTarget());
        var normalizedInput = InputMaterializationService.NormalizeInputParts(p.Input);
        ValidateTurnStartInput(normalizedInput);

        var materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
        var content = await ResolveInputPartsAsync(materializedInput.MaterializedInputParts.ToList(), ct);
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

        // Set ChannelSessionScope so that SessionService.ResolveApprovalSource returns the correct
        // channel name for approval routing, and CronTools captures the right delivery target.
        // For CLI clients: "cli" channel, adapter client name as userId.
        // For external adapters: use the real platform user/chat IDs from SenderContext so that
        // cron payloads store a usable delivery target (e.g. the Telegram chat_id) rather than
        // the adapter's process-level client name.
        var channelScopeInfo = connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = p.Sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = p.Sender?.GroupId,
                DefaultDeliveryTarget = p.Sender?.GroupId,
            }
            : new ChannelSessionInfo
            {
                Channel = connection.HasAcpExtensions ? "acp" : "cli",
                UserId = connection.ClientInfo?.Name ?? "anonymous"
            };

        // Fix 5: Deserialize client-provided history for historyMode=client threads.
        ChatMessage[]? messages = null;
        if (p.Messages.HasValue && p.Messages.Value.ValueKind != JsonValueKind.Null)
        {
            try
            {
                messages = JsonSerializer.Deserialize<ChatMessage[]>(
                    p.Messages.Value.GetRawText(),
                    SessionWireJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw AppServerErrors.InvalidParams($"Failed to deserialize 'messages': {ex.Message}");
            }
        }

        // TCS to receive the initial turn from the event dispatcher
        var initialTurnTcs = new TaskCompletionSource<SessionWireTurn>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // onTurnStarted: send the turn/start response, then signal the dispatcher to
        // proceed with the turn/started notification — guaranteeing correct ordering.
        async Task OnTurnStarted(SessionWireTurn initialTurn)
        {
            // Build and send the turn/start JSON-RPC response before the notification
            var responsePayload = new { turn = initialTurn };
            try
            {
                await transport.WriteMessageAsync(BuildResponse(msg.Id, responsePayload), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Client disconnected before the response could be delivered; the
                // turn has already started and must keep draining in the background.
            }
            catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
            {
                // Client disconnected before the response could be delivered; the
                // turn has already started and must keep draining in the background.
            }

            // Signal that the response phase is complete even if the client is gone.
            initialTurnTcs.TrySetResult(initialTurn);
        }

        using var channelScope = channelScopeInfo != null ? ChannelSessionScope.Set(channelScopeInfo) : null;

        // Ensure thread is loaded into memory (may only exist on disk after server restart)
        // and per-thread Configuration agent/MCP is hydrated (GetThreadAsync alone does not rebuild agents).
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);

        var events = sessionService.SubmitInputAsync(
            p.ThreadId,
            content,
            p.Sender,
            messages,
            CancellationToken.None,
            new SessionInputSnapshot
            {
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText
            });

        // Spec §6.10 (at-most-once delivery guarantee): when the connection already holds an active
        // thread/subscribe subscription for this thread, the subscription dispatcher is the sole
        // notification delivery path. Creating a second AppServerEventDispatcher here would send
        // every turn event twice on the same transport. Instead, we read only the first TurnStarted
        // event from the turn channel (needed to build the turn/start response), send the response,
        // and then drain the turn channel in the background so disconnects cannot strand approvals.
        if (connection.HasSubscription(p.ThreadId))
        {
            var subscribedEnumerator = events.GetAsyncEnumerator(CancellationToken.None);
            var drainOwnsEnumerator = false;
            try
            {
                while (await subscribedEnumerator.MoveNextAsync())
                {
                    var evt = subscribedEnumerator.Current;
                    if (evt.EventType == SessionEventType.TurnStarted && evt.TurnPayload is { } startedTurn)
                    {
                        var wireTurn = startedTurn.ToWire(includeItems: false) with { Items = [] };
                        try
                        {
                            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { turn = wireTurn }), ct);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // The response is best-effort after disconnect; keep draining below.
                        }
                        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
                        {
                            // The response is best-effort after disconnect; keep draining below.
                        }

                        _ = DrainSubscribedTurnEventsAsync(subscribedEnumerator, connection, CancellationToken.None);
                        drainOwnsEnumerator = true;
                        break;
                    }
                }
            }
            finally
            {
                if (!drainOwnsEnumerator)
                    await subscribedEnumerator.DisposeAsync();
            }

            return null;
        }

        var dispatcher = new AppServerEventDispatcher(
            events, connection, transport, sessionService, OnTurnStarted,
            defaultApprovalDecision: _defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger,
            enrichThreadWire: EnrichThreadWireForNotification);

        var dispatchTask = dispatcher.RunAsync(CancellationToken.None);

        // Propagate dispatch failures to the TCS so we don't hang indefinitely
        _ = dispatchTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                initialTurnTcs.TrySetException(t.Exception!.GetBaseException());
            else if (t.IsCanceled)
                initialTurnTcs.TrySetCanceled(ct);
            else
                initialTurnTcs.TrySetException(new AppServerException(
                    AppServerErrors.InternalErrorCode,
                    "Event dispatch completed without emitting a TurnStarted event."));
        }, TaskContinuationOptions.ExecuteSynchronously);

        // Wait until the response has been sent (or dispatch failed)
        await initialTurnTcs.Task;

        // Return null to signal the host that the response has already been sent inline
        return null;
    }

    private async Task DrainSubscribedTurnEventsAsync(
        IAsyncEnumerator<SessionEvent> events,
        AppServerConnection owningConnection,
        CancellationToken ct)
    {
        try
        {
            while (await events.MoveNextAsync())
            {
                ct.ThrowIfCancellationRequested();
                var evt = events.Current;
                if (evt.EventType == SessionEventType.ApprovalRequested)
                    await ScheduleDisconnectedApprovalFallbackAsync(evt, owningConnection, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown or explicit drain cancellation.
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Subscribed turn drain error for thread {ex.Message}");
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    private Task ScheduleDisconnectedApprovalFallbackAsync(
        SessionEvent evt,
        AppServerConnection owningConnection,
        CancellationToken ct)
    {
        var item = evt.ItemPayload;
        if (item?.Payload is not ApprovalRequestPayload req)
            return Task.CompletedTask;
        if (evt.TurnId == null)
            return Task.CompletedTask;

        if (owningConnection.IsClosed)
            return CreateInteractiveRequestSender().ResolveApprovalWithFallbackAsync(
                evt.ThreadId,
                evt.TurnId,
                req.RequestId,
                ct);

        _ = Task.Run(async () =>
        {
            try
            {
                await owningConnection.Closed;
                await CreateInteractiveRequestSender().ResolveApprovalWithFallbackAsync(
                    evt.ThreadId,
                    evt.TurnId,
                    req.RequestId,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[AppServer] Disconnected approval fallback failed: {ex.Message}");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private async Task<object?> HandleTurnEnqueueAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnEnqueueParams>(msg);
        var materializedInput = await PrepareTurnInputAsync(p.Input, ct);
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

        using var channelScope = CreateChannelScope(p.Sender);
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var queued = await sessionService.EnqueueTurnInputAsync(
            p.ThreadId,
            materializedInput.Content,
            p.Sender,
            ct,
            new SessionInputSnapshot
            {
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText
            });
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        return new TurnEnqueueResponse
        {
            QueuedInput = queued,
            QueuedInputs = thread.QueuedInputs.ToList()
        };
    }

    private async Task<object?> HandleTurnQueueRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnQueueRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.QueuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");
        var queuedInputs = await sessionService.RemoveQueuedTurnInputAsync(p.ThreadId, p.QueuedInputId, ct);
        return new TurnQueueRemoveResponse { QueuedInputs = queuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnQueueReorderAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnQueueReorderParams>(msg);
        if (p.OrderedQueuedInputIds == null)
            throw AppServerErrors.InvalidParams("'orderedQueuedInputIds' is required.");
        var queuedInputs = await sessionService.ReorderQueuedTurnInputsAsync(p.ThreadId, p.OrderedQueuedInputIds, ct);
        return new TurnQueueReorderResponse { QueuedInputs = queuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnSteerAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnSteerParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ExpectedTurnId))
            throw AppServerErrors.InvalidParams("'expectedTurnId' is required.");
        if (string.IsNullOrWhiteSpace(p.QueuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");

        using var channelScope = CreateChannelScope(p.Sender);
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var result = await sessionService.SteerTurnAsync(
            p.ThreadId,
            p.ExpectedTurnId,
            p.QueuedInputId,
            ct,
            p.Sender);
        return new TurnSteerResponse { TurnId = result.TurnId, QueuedInputs = result.QueuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnInterruptParams>(msg);

        // Issue E: validate thread and turn existence/status before cancelling.
        // GetThreadAsync throws KeyNotFoundException → mapped to -32010 by the outer catch.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);

        var turn = thread.Turns.FirstOrDefault(t => t.Id == p.TurnId);
        if (turn == null)
            throw AppServerErrors.TurnNotFound(p.TurnId);

        if (turn.Status != TurnStatus.Running
            && turn.Status != TurnStatus.WaitingApproval
            && turn.Status != TurnStatus.WaitingInput)
            throw AppServerErrors.TurnNotRunning(p.TurnId);

        await sessionService.CancelTurnAsync(p.ThreadId, p.TurnId, ct);
        return new { };
    }

    // terminal/* methods live in TerminalRequestHandler.

    private void EnsureSubAgentManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("subagent/profiles/*");
    }

    private static SubAgentProfileWriteWire MapSubAgentProfileToWire(SubAgentProfile profile) => new()
    {
        Runtime = profile.Runtime,
        Bin = profile.Bin,
        Args = profile.Args is { Count: > 0 } ? [.. profile.Args] : null,
        Env = profile.Env is { Count: > 0 } ? new Dictionary<string, string>(profile.Env, StringComparer.Ordinal) : null,
        EnvPassthrough = profile.EnvPassthrough is { Count: > 0 } ? [.. profile.EnvPassthrough] : null,
        WorkingDirectoryMode = profile.WorkingDirectoryMode,
        SupportsStreaming = profile.SupportsStreaming,
        SupportsResume = profile.SupportsResume,
        SupportsModelSelection = profile.SupportsModelSelection,
        InputFormat = profile.InputFormat,
        OutputFormat = profile.OutputFormat,
        InputMode = profile.InputMode,
        InputArgTemplate = profile.InputArgTemplate,
        InputEnvKey = profile.InputEnvKey,
        ResumeArgTemplate = profile.ResumeArgTemplate,
        ResumeSessionIdJsonPath = profile.ResumeSessionIdJsonPath,
        ResumeSessionIdRegex = profile.ResumeSessionIdRegex,
        OutputJsonPath = profile.OutputJsonPath,
        OutputInputTokensJsonPath = profile.OutputInputTokensJsonPath,
        OutputOutputTokensJsonPath = profile.OutputOutputTokensJsonPath,
        OutputTotalTokensJsonPath = profile.OutputTotalTokensJsonPath,
        OutputFileArgTemplate = profile.OutputFileArgTemplate,
        ReadOutputFile = profile.ReadOutputFile,
        DeleteOutputFileAfterRead = profile.DeleteOutputFileAfterRead,
        MaxOutputBytes = profile.MaxOutputBytes,
        Timeout = profile.Timeout,
        TrustLevel = profile.TrustLevel,
        PermissionModeMapping = profile.PermissionModeMapping is { Count: > 0 }
            ? new Dictionary<string, string>(profile.PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
            : null,
        SanitizationRules = profile.SanitizationRules?.DeepClone() as JsonObject
    };

    private static SubAgentProfile MapWireToSubAgentProfile(string name, SubAgentProfileWriteWire wire) => new()
    {
        Name = name.Trim(),
        Runtime = wire.Runtime?.Trim() ?? string.Empty,
        Bin = string.IsNullOrWhiteSpace(wire.Bin) ? null : wire.Bin.Trim(),
        Args = wire.Args is { Count: > 0 } ? [.. wire.Args.Where(arg => !string.IsNullOrWhiteSpace(arg)).Select(arg => arg.Trim())] : null,
        Env = wire.Env is { Count: > 0 } ? new Dictionary<string, string>(wire.Env, StringComparer.Ordinal) : null,
        EnvPassthrough = wire.EnvPassthrough is { Count: > 0 }
            ? [.. wire.EnvPassthrough.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim())]
            : null,
        WorkingDirectoryMode = string.IsNullOrWhiteSpace(wire.WorkingDirectoryMode) ? "workspace" : wire.WorkingDirectoryMode.Trim(),
        SupportsStreaming = wire.SupportsStreaming,
        SupportsResume = wire.SupportsResume,
        SupportsModelSelection = wire.SupportsModelSelection,
        InputFormat = string.IsNullOrWhiteSpace(wire.InputFormat) ? null : wire.InputFormat.Trim(),
        OutputFormat = string.IsNullOrWhiteSpace(wire.OutputFormat) ? null : wire.OutputFormat.Trim(),
        InputMode = string.IsNullOrWhiteSpace(wire.InputMode) ? null : wire.InputMode.Trim(),
        InputArgTemplate = string.IsNullOrWhiteSpace(wire.InputArgTemplate) ? null : wire.InputArgTemplate,
        InputEnvKey = string.IsNullOrWhiteSpace(wire.InputEnvKey) ? null : wire.InputEnvKey.Trim(),
        ResumeArgTemplate = string.IsNullOrWhiteSpace(wire.ResumeArgTemplate) ? null : wire.ResumeArgTemplate,
        ResumeSessionIdJsonPath = string.IsNullOrWhiteSpace(wire.ResumeSessionIdJsonPath) ? null : wire.ResumeSessionIdJsonPath.Trim(),
        ResumeSessionIdRegex = string.IsNullOrWhiteSpace(wire.ResumeSessionIdRegex) ? null : wire.ResumeSessionIdRegex,
        OutputJsonPath = string.IsNullOrWhiteSpace(wire.OutputJsonPath) ? null : wire.OutputJsonPath.Trim(),
        OutputInputTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputInputTokensJsonPath) ? null : wire.OutputInputTokensJsonPath.Trim(),
        OutputOutputTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputOutputTokensJsonPath) ? null : wire.OutputOutputTokensJsonPath.Trim(),
        OutputTotalTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputTotalTokensJsonPath) ? null : wire.OutputTotalTokensJsonPath.Trim(),
        OutputFileArgTemplate = string.IsNullOrWhiteSpace(wire.OutputFileArgTemplate) ? null : wire.OutputFileArgTemplate,
        ReadOutputFile = wire.ReadOutputFile,
        DeleteOutputFileAfterRead = wire.DeleteOutputFileAfterRead,
        MaxOutputBytes = wire.MaxOutputBytes,
        Timeout = wire.Timeout,
        TrustLevel = string.IsNullOrWhiteSpace(wire.TrustLevel) ? null : wire.TrustLevel.Trim(),
        PermissionModeMapping = wire.PermissionModeMapping is { Count: > 0 }
            ? new Dictionary<string, string>(wire.PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
            : null,
        SanitizationRules = wire.SanitizationRules?.DeepClone() as JsonObject
    };

    private static void ValidateSubAgentProfileWire(SubAgentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw AppServerErrors.SubAgentProfileValidationFailed("'name' is required.");

        if (profile.SupportsResume == true)
        {
            if (string.IsNullOrWhiteSpace(profile.ResumeArgTemplate))
            {
                throw AppServerErrors.SubAgentProfileValidationFailed(
                    "resumeArgTemplate is required when supportsResume is true.");
            }

            if (string.IsNullOrWhiteSpace(profile.ResumeSessionIdJsonPath)
                && string.IsNullOrWhiteSpace(profile.ResumeSessionIdRegex))
            {
                throw AppServerErrors.SubAgentProfileValidationFailed(
                    "resumeSessionIdJsonPath or resumeSessionIdRegex is required when supportsResume is true.");
            }
        }

        var warnings = SubAgentProfileRegistry.ValidateProfiles(
            [profile],
            SubAgentProfileRegistry.KnownRuntimeTypes,
            []);
        if (warnings.Count > 0)
            throw AppServerErrors.SubAgentProfileValidationFailed(string.Join(" ", warnings));
    }

    private SubAgentProfileListResult BuildSubAgentProfileListResult()
    {
        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var workspaceOverrideNames = state.Profiles
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInProfiles = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var builtInMap = builtInProfiles.ToDictionary(profile => profile.Name, profile => profile.Clone(), StringComparer.OrdinalIgnoreCase);
        var registry = new SubAgentProfileRegistry(
            state.Profiles,
            builtInProfiles,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            state.DisabledProfiles);
        var diagnostics = BuildSubAgentDiagnostics(registry)
            .ToDictionary(diagnostic => diagnostic.Name, diagnostic => diagnostic, StringComparer.OrdinalIgnoreCase);

        var profiles = registry.Profiles
            .OrderBy(profile => string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile =>
            {
                var diagnostic = diagnostics[profile.Name];
                return new SubAgentProfileEntryWire
                {
                    Name = profile.Name,
                    IsBuiltIn = registry.IsBuiltInProfile(profile.Name),
                    IsTemplate = registry.IsTemplateProfile(profile.Name),
                    HasWorkspaceOverride = workspaceOverrideNames.Contains(profile.Name),
                    IsDefault = string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase),
                    Enabled = registry.IsEnabled(profile.Name),
                    Definition = MapSubAgentProfileToWire(profile),
                    BuiltInDefaults = builtInMap.TryGetValue(profile.Name, out var builtInDefault)
                        ? MapSubAgentProfileToWire(builtInDefault)
                        : null,
                    Diagnostic = new SubAgentProfileDiagnosticWire
                    {
                        Enabled = diagnostic.Enabled,
                        BinaryResolved = diagnostic.BinaryResolved,
                        HiddenFromPrompt = diagnostic.HiddenFromPrompt,
                        HiddenReason = diagnostic.HiddenReason,
                        Warnings = [.. diagnostic.Warnings]
                    }
                };
            })
            .ToList();

        return new SubAgentProfileListResult
        {
            DefaultName = SubAgentCoordinator.DefaultProfileName,
            Profiles = profiles,
            Settings = new SubAgentSettingsWire
            {
                ExternalCliSessionResumeEnabled = state.EnableExternalCliSessionResume,
                Model = string.IsNullOrWhiteSpace(state.Model) ? null : state.Model,
                MinWaitTimeoutMs = state.WaitAgentTimeouts.MinTimeoutMs,
                DefaultWaitTimeoutMs = state.WaitAgentTimeouts.DefaultTimeoutMs,
                MaxWaitTimeoutMs = state.WaitAgentTimeouts.MaxTimeoutMs
            }
        };
    }

    private static IReadOnlyList<SubAgentProfileDiagnostic> BuildSubAgentDiagnostics(SubAgentProfileRegistry registry)
    {
        var diagnostics = new List<SubAgentProfileDiagnostic>();
        foreach (var profile in registry.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            var warnings = registry.GetValidationWarningsForProfile(profile.Name);
            var enabled = registry.IsEnabled(profile.Name);
            var runtimeRegistered = SubAgentProfileRegistry.KnownRuntimeTypes.Contains(profile.Runtime, StringComparer.OrdinalIgnoreCase);

            string? resolvedBinary = null;
            var hiddenReasons = new List<string>();
            if (!enabled)
                hiddenReasons.Add("disabled by workspace configuration");

            if (!runtimeRegistered)
                hiddenReasons.Add("runtime not registered");

            if (registry.IsTemplateProfile(profile.Name))
                hiddenReasons.Add("template profile");

            if (warnings.Count > 0)
                hiddenReasons.Add("configuration warnings present");

            if (string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(profile.Bin))
                {
                    hiddenReasons.Add("missing required field 'bin'");
                }
                else if (runtimeRegistered)
                {
                    if (CliOneshotRuntime.TryResolveExecutablePath(profile.Bin, out var resolved))
                        resolvedBinary = resolved;
                    else
                        hiddenReasons.Add($"binary '{profile.Bin}' was not found on PATH");
                }
            }

            diagnostics.Add(new SubAgentProfileDiagnostic
            {
                Name = profile.Name,
                Runtime = profile.Runtime,
                WorkingDirectoryMode = profile.WorkingDirectoryMode,
                IsBuiltIn = registry.IsBuiltInProfile(profile.Name),
                Enabled = enabled,
                Bin = profile.Bin,
                ResolvedBinary = resolvedBinary,
                BinaryResolved = resolvedBinary != null,
                RuntimeRegistered = runtimeRegistered,
                HiddenFromPrompt = hiddenReasons.Count > 0,
                HiddenReason = hiddenReasons.Count > 0
                    ? string.Join("; ", hiddenReasons.Distinct(StringComparer.Ordinal))
                    : null,
                Warnings = warnings
            });
        }

        return diagnostics;
    }

    private void RefreshCurrentSubAgentConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
        appConfigMonitor.Current.SubAgent = new AppConfig.SubAgentConfig
        {
            DisabledProfiles = [.. mergedConfig.SubAgent.DisabledProfiles],
            EnableExternalCliSessionResume = mergedConfig.SubAgent.EnableExternalCliSessionResume,
            Model = mergedConfig.SubAgent.Model,
            MinWaitTimeoutMs = mergedConfig.SubAgent.MinWaitTimeoutMs,
            DefaultWaitTimeoutMs = mergedConfig.SubAgent.DefaultWaitTimeoutMs,
            MaxWaitTimeoutMs = mergedConfig.SubAgent.MaxWaitTimeoutMs,
            MaxDepth = mergedConfig.SubAgent.MaxDepth,
            Roles = [.. mergedConfig.SubAgent.Roles.Select(role => role.Clone())]
        };
        appConfigMonitor.Current.SubAgentProfiles = mergedConfig.SubAgentProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Clone())
            .ToList();
    }

    private void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }

    // dreams/* methods live in DreamsRequestHandler.

    // cron/* and heartbeat/trigger live in CronRequestHandler.

    // usage/* and profile/insights live in UsageRequestHandler.

    // skills/* methods live in SkillsRequestHandler.

    private Task<object?> HandleCommandListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<CommandListParams>(msg);

        var commands = _commandRegistry.ListCommands()
            .Where(c => p.IncludeBuiltins != false ||
                !string.Equals(c.Category, "builtin", StringComparison.OrdinalIgnoreCase))
            .Where(c =>
            {
                var reg = _commandRegistry.GetRegistration(c.Name);
                return reg == null || IsServiceAvailableForRegistration(reg);
            })
            .Select(c => new CommandInfoWire
            {
                Name = c.Name,
                Aliases = c.Aliases,
                DescriptionKey = c.DescriptionKey,
                FallbackDescription = c.FallbackDescription,
                Description = c.Description,
                Category = c.Category,
                RequiresAdmin = c.RequiresAdmin
            })
            .ToList();

        return Task.FromResult<object?>(new CommandListResult { Commands = commands });
    }

    private async Task<object?> HandleCommandExecuteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<CommandExecuteParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.Command))
            throw AppServerErrors.InvalidParams("'command' is required.");

        var commandName = ExtractCommandName(p.Command);
        var registration = _commandRegistry.GetRegistration(commandName);

        if (registration != null && !IsSenderAllowed(registration, p.Sender))
            throw AppServerErrors.CommandPermissionDenied(commandName);

        if (registration != null && !IsServiceAvailableForRegistration(registration))
            throw AppServerErrors.CommandServiceUnavailable(commandName);

        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var rawText = BuildRawText(p.Command, p.Arguments);
        var senderId = p.Sender?.SenderId ?? thread.UserId ?? connection.ClientInfo?.Name ?? "anonymous";
        var senderName = p.Sender?.SenderName ?? thread.UserId ?? connection.ClientInfo?.Name ?? "anonymous";
        var source = string.IsNullOrWhiteSpace(thread.OriginChannel)
            ? (connection.IsChannelAdapter ? connection.ChannelAdapterName ?? "external" : "appserver")
            : thread.OriginChannel;

        var context = new CommandContext
        {
            SessionId = p.ThreadId,
            RawText = rawText,
            UserId = senderId,
            UserName = senderName,
            IsAdmin = string.Equals(p.Sender?.SenderRole, "admin", StringComparison.OrdinalIgnoreCase),
            Source = source,
            GroupId = p.Sender?.GroupId,
            ChannelContext = thread.ChannelContext,
            WorkspacePath = thread.WorkspacePath,
            SessionService = sessionService,
            HeartbeatService = heartbeatService,
            CronService = cronService,
            CommandRegistry = _commandRegistry
        };

        var responder = new BufferedCommandResponder();
        var result = await _commandRegistry.TryExecuteAsync(rawText, context, responder);
        SessionWireThread? resetThread = null;
        if (!string.IsNullOrWhiteSpace(result.NewThreadId))
        {
            try
            {
                var freshThread = await sessionService.GetThreadAsync(result.NewThreadId, ct);
                resetThread = await EnrichThreadWireAsync(freshThread.ToWire(), freshThread, ct);
            }
            catch
            {
                // Best-effort enrichment for command/execute response.
            }
        }

        return new CommandExecuteResult
        {
            Handled = result.Handled,
            Message = responder.Message ?? result.Message,
            IsMarkdown = responder.IsMarkdown || result.IsMarkdown,
            ExpandedPrompt = result.ExpandedPrompt,
            SessionReset = result.SessionReset,
            Thread = resetThread,
            ArchivedThreadIds = result.ArchivedThreadIds?.ToList(),
            CreatedLazily = result.CreatedLazily
        };
    }

    private bool IsSkillVariantModeEnabled() => SkillVariants.IsVariantModeEnabled();

    private bool GoalsCapabilityEnabled()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        return config.Goals.Enabled;
    }

    private SkillVariantTarget BuildSkillVariantTarget() => SkillVariants.BuildTarget();

    private bool IsServiceAvailableForRegistration(CommandRegistration registration)
    {
        return registration.RequiredService?.ToLowerInvariant() switch
        {
            "cron" => cronService != null,
            "heartbeat" => heartbeatService != null,
            _ => true
        };
    }

    private static bool IsSenderAllowed(CommandRegistration registration, SenderContext? sender)
    {
        if (!registration.RequiresAdmin)
            return true;
        return string.Equals(sender?.SenderRole, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateTurnStartInput(IReadOnlyList<SessionWireInputPart> input)
    {
        foreach (var part in input)
        {
            if (!string.Equals(part.Type, "commandRef", StringComparison.Ordinal))
                continue;

            var commandName = !string.IsNullOrWhiteSpace(part.Name)
                ? part.Name
                : ExtractCommandName(part.RawText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(commandName))
                continue;

            var registration = _commandRegistry.GetRegistration(commandName);
            if (registration == null ||
                string.Equals(registration.Category, "custom", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw AppServerErrors.InvalidParams(
                $"Built-in slash command '{registration.Name}' cannot be sent as 'commandRef' in 'turn/start'. Use 'command/execute' or dedicated UI instead.");
        }
    }

    private async Task<PreparedTurnInput> PrepareTurnInputAsync(
        IReadOnlyList<SessionWireInputPart> input,
        CancellationToken ct)
    {
        if (input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var inputMaterialization = new InputMaterializationService(
            _commandRegistry,
            skillsLoader,
            IsSkillVariantModeEnabled(),
            BuildSkillVariantTarget());
        var normalizedInput = InputMaterializationService.NormalizeInputParts(input);
        ValidateTurnStartInput(normalizedInput);
        var materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
        var content = await ResolveInputPartsAsync(materializedInput.MaterializedInputParts.ToList(), ct);
        return new PreparedTurnInput
        {
            NativeInputParts = materializedInput.NativeInputParts,
            MaterializedInputParts = materializedInput.MaterializedInputParts,
            DisplayText = materializedInput.DisplayText,
            Content = content
        };
    }

    private IDisposable? CreateChannelScope(SenderContext? sender)
    {
        var channelScopeInfo = connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = sender?.GroupId,
                DefaultDeliveryTarget = sender?.GroupId,
            }
            : new ChannelSessionInfo
            {
                Channel = connection.HasAcpExtensions ? "acp" : "cli",
                UserId = connection.ClientInfo?.Name ?? "anonymous"
            };

        return ChannelSessionScope.Set(channelScopeInfo);
    }

    private sealed class PreparedTurnInput
    {
        public IReadOnlyList<SessionWireInputPart> NativeInputParts { get; init; } = [];

        public IReadOnlyList<SessionWireInputPart> MaterializedInputParts { get; init; } = [];

        public string DisplayText { get; init; } = string.Empty;

        public List<AIContent> Content { get; init; } = [];
    }

    private static string ExtractCommandName(string rawCommand)
    {
        var trimmed = rawCommand.Trim();
        if (trimmed.Length == 0)
            return rawCommand;

        var whitespaceIndex = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespaceIndex >= 0 ? trimmed[..whitespaceIndex] : trimmed;
    }

    private static string BuildRawText(string command, List<string>? arguments)
    {
        var normalized = command.StartsWith('/') ? command : $"/{command}";
        if (arguments == null || arguments.Count == 0)
            return normalized;
        return $"{normalized} {string.Join(" ", arguments)}";
    }

    private sealed class BufferedCommandResponder : ICommandResponder
    {
        private readonly List<(string Text, bool IsMarkdown)> _segments = [];

        /// <summary>
        /// All non-empty segments joined with newlines, or null if nothing was sent.
        /// </summary>
        public string? Message
        {
            get
            {
                var parts = _segments
                    .Where(s => !string.IsNullOrWhiteSpace(s.Text))
                    .Select(s => s.Text)
                    .ToList();
                return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
            }
        }

        /// <summary>
        /// True if any segment was sent as markdown.
        /// </summary>
        public bool IsMarkdown => _segments.Any(s => s.IsMarkdown);

        public Task SendTextAsync(string message)
        {
            _segments.Add((message, false));
            return Task.CompletedTask;
        }

        public Task SendMarkdownAsync(string markdown)
        {
            _segments.Add((markdown, true));
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Fix 10: Shared HttpClient for image URL fetch (best-effort, text-only is the primary path).
    private static readonly HttpClient ImageHttpClient = new();
    internal const string LocalImagePathMetadataKey = "localImage.path";
    internal const string LocalImageMimeTypeMetadataKey = "localImage.mimeType";
    internal const string LocalImageFileNameMetadataKey = "localImage.fileName";

    /// <summary>
    /// Converts wire input parts to <see cref="AIContent"/>, resolving image and localImage parts
    /// to <see cref="DataContent"/> by reading file bytes or fetching URL bytes.
    /// Falls back to a text placeholder on any I/O error so the turn is not blocked.
    /// </summary>
    private static async Task<List<AIContent>> ResolveInputPartsAsync(
        List<SessionWireInputPart> parts,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            AIContent content;
            switch (part.Type)
            {
                case "localImage" when part.Path is { } path:
                    content = await ResolveLocalImageAsync(path, part.MimeType, part.FileName, ct);
                    break;
                case "image" when part.Url is { } url:
                    content = await ResolveRemoteImageAsync(url, ct);
                    break;
                default:
                    content = part.ToAIContent();
                    break;
            }
            result.Add(content);
        }
        return result;
    }

    private static async Task<AIContent> ResolveLocalImageAsync(
        string path,
        string? mimeTypeHint,
        string? fileNameHint,
        CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var mediaType = InferMediaType(path);
            var data = new DataContent(bytes, mediaType);
            data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            data.AdditionalProperties[LocalImagePathMetadataKey] = path;
            if (!string.IsNullOrWhiteSpace(mimeTypeHint))
                data.AdditionalProperties[LocalImageMimeTypeMetadataKey] = mimeTypeHint.Trim();
            if (!string.IsNullOrWhiteSpace(fileNameHint))
                data.AdditionalProperties[LocalImageFileNameMetadataKey] = fileNameHint.Trim();
            return data;
        }
        catch
        {
            // Best-effort: return placeholder if file cannot be read
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static async Task<AIContent> ResolveRemoteImageAsync(string url, CancellationToken ct)
    {
        try
        {
            // Security: Validate URL scheme to prevent SSRF attacks
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new TextContent($"[image:invalid-url]");
            }

            // Only allow http and https schemes to prevent file://, ftp://, etc.
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return new TextContent($"[image:blocked-scheme:{uri.Scheme}]");
            }

            // Block requests to localhost/internal networks (basic SSRF protection)
            if (IsInternalAddress(uri.Host))
            {
                return new TextContent($"[image:blocked-internal]");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await ImageHttpClient.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType
                ?? InferMediaType(url);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if URL cannot be fetched
            return new TextContent($"[image:{url}]");
        }
    }

    /// <summary>
    /// Checks if a host points to an internal/reserved address.
    /// This is a basic SSRF protection - blocks localhost, loopback, and private ranges.
    /// </summary>
    private static bool IsInternalAddress(string host)
    {
        // Block localhost variants
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            host == "::1" ||
            host.StartsWith("127.", StringComparison.Ordinal) ||
            host.StartsWith("0.", StringComparison.Ordinal))
        {
            return true;
        }

        // Block common internal hostnames
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("localhost"))
        {
            return true;
        }

        // Try to parse as IP address and check for private ranges
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();

            // IPv4 private ranges
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 (link-local)
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }

            // IPv6 loopback and link-local
            if (bytes.Length == 16)
            {
                // ::1 (loopback)
                if (bytes.AsSpan().ToArray().All(b => b == 0) && bytes[15] == 1) return true;
                // fe80::/10 (link-local)
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
            }
        }

        return false;
    }

    private static string InferMediaType(string pathOrUrl)
    {
        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    // -------------------------------------------------------------------------
    // Domain exception → wire error code translation (Gap A, spec Section 8.3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Translates an <see cref="InvalidOperationException"/> from the domain layer into the
    /// appropriate <see cref="AppServerException"/> with a spec-defined error code.
    /// </summary>
    private static AppServerException MapOperationException(InvalidOperationException ex)
    {
        var msg = ex.Message;
        var id = ExtractQuotedId(msg);

        if (msg.Contains("archived and cannot be resumed") || msg.Contains("is not Active"))
            return AppServerErrors.ThreadNotActive(id);

        if (msg.Contains("already has a running Turn")
            || msg.Contains("has a running Turn")
            || msg.Contains("has active thread maintenance"))
            return AppServerErrors.TurnInProgress(id);

        // historyMode contract violations are caller errors → InvalidParams (-32602)
        if (msg.Contains("client-managed history")
            || msg.Contains("server-managed history")
            || msg.Contains("SubAgent child thread")
            || msg.Contains("deliveryMode")
            || msg.Contains("has no goal")
            || msg.Contains("already has a goal")
            || msg.Contains("has no history")
            || msg.Contains("has no completed turn")
            || msg.Contains("has no model-visible history"))
            return AppServerErrors.InvalidParams(msg);

        return AppServerErrors.InternalError(msg);
    }

    /// <summary>
    /// Extracts the first single-quoted identifier from an exception message.
    /// For example: "Thread 'thread_001' not found." → "thread_001".
    /// </summary>
    private static string ExtractQuotedId(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return string.Empty;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : string.Empty;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? ParseNullableString(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a string or null.")
        };
    }

    private static bool? ParseNullableBoolean(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a boolean or null.")
        };
    }

    private static int? ParseNullableInteger(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer or null.")
        };
    }

    private static int ParseInteger(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer.")
        };
    }

    private static bool TryGetCaseInsensitiveProperty(JsonElement obj, string expectedName, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private Task<object?> TryHandleExtensionAsync(string method, AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!_extensionMethods.TryGetValue(method, out var handler))
            throw AppServerErrors.MethodNotFound(method);

        return handler.HandleAsync(
            msg,
            new AppServerExtensionContext(
                connection,
                transport,
                sessionService,
                workspaceCraftPath,
                _hostWorkspacePath,
                contextPageManager,
                ct));
    }

    private static IReadOnlyDictionary<string, IAppServerMethodHandler> BuildExtensionMethodMap(
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions)
    {
        if (protocolExtensions == null)
            return new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);

        var methods = new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);
        foreach (var extension in protocolExtensions)
        {
            foreach (var method in extension.Methods)
            {
                if (string.IsNullOrWhiteSpace(method))
                    throw new InvalidOperationException("AppServer extension method names must be non-empty.");
                if (ReservedMethodNames.Contains(method))
                    throw new InvalidOperationException($"AppServer extension method '{method}' conflicts with a Core protocol method.");
                if (!methods.TryAdd(method, extension))
                    throw new InvalidOperationException($"Duplicate AppServer extension method registration: '{method}'.");
            }
        }

        return methods;
    }

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
        => AppServerParams.Get<T>(msg);

    /// <summary>
    /// Sends a JSON-RPC response followed immediately by a notification on the same connection.
    /// Used by thread lifecycle handlers (Fix 8) to guarantee response-before-notification ordering.
    /// The caller must return null from its handle method to signal the host that the response
    /// has already been sent.
    /// </summary>
    private async Task SendNotificationAfterResponseAsync(
        JsonElement? requestId,
        object responseResult,
        string notificationMethod,
        object notificationParams,
        CancellationToken ct)
    {
        await transport.WriteMessageAsync(BuildResponse(requestId, responseResult), ct);
        await transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = notificationMethod,
            @params = notificationParams
        }, ct);
    }

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 success response.
    /// </summary>
    public static object BuildResponse(JsonElement? id, object? result) => new
    {
        jsonrpc = "2.0",
        id,
        result
    };

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 error response.
    /// </summary>
    public static object BuildErrorResponse(JsonElement? id, AppServerError error) => new
    {
        jsonrpc = "2.0",
        id,
        error
    };
}
