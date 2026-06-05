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
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Plugins;
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
    string serverVersion = "0.1.0",
    SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.Reject,
    CronService? cronService = null,
    HeartbeatService? heartbeatService = null,
    SkillsLoader? skillsLoader = null,
    MemoryStore? memoryStore = null,
    string? workspaceCraftPath = null,
    string? hostWorkspacePath = null,
    IAutomationsRequestHandler? automationsHandler = null,
    Action<CronJobWireInfo, bool>? broadcastCronStateChanged = null,
    Action<McpStatusInfoWire>? broadcastMcpStatusChanged = null,
    ICommitMessageSuggestService? commitMessageSuggest = null,
    IWelcomeSuggestionService? welcomeSuggestionService = null,
    string? dashboardUrl = null,
    WireAcpExtensionProxy? wireAcpExtensionProxy = null,
    WireNodeReplProxy? wireNodeReplProxy = null,
    WireDynamicToolProxy? wireDynamicToolProxy = null,
    CommandRegistry? commandRegistry = null,
    IChannelStatusProvider? channelStatusProvider = null,
    McpClientManager? mcpClientManager = null,
    LspServerManager? lspServerManager = null,
    IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
    Func<ExternalChannelEntry, CancellationToken, Task>? onExternalChannelUpserted = null,
    Func<string, CancellationToken, Task>? onExternalChannelRemoved = null,
    IExternalChannelLogProvider? externalChannelLogProvider = null,
    SessionStreamDebugLogger? streamDebugLogger = null,
    IReadOnlyList<ConfigSchemaSection>? configSchema = null,
    IAppConfigMonitor? appConfigMonitor = null,
    ChatClientRegistry? chatClientRegistry = null,
    OpenAIClientProvider? openAIClientProvider = null,
    IOpenAIAuthService? openAIAuthService = null,
    IOpenAIUsageService? openAIUsageService = null,
    IBackgroundTerminalService? backgroundTerminalService = null,
    IContextPageManager? contextPageManager = null,
    DreamStore? dreamStore = null,
    DreamsService? dreamsService = null,
    AppBindingService? appBindingService = null,
    PlanStore? planStore = null,
    TraceStore? traceStore = null,
    IReadOnlyList<string>? builtInPluginSourceRoots = null,
    WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
    Func<SessionThread, SubAgentCoordinator?>? subAgentCoordinatorFactory = null)
{
    private const string AppBindingAppListUpdatedNotification = "app/list/updated";
    private const string AppBindingThreadBindingsChangedNotification = "thread/appBindings/changed";
    private const int ThreadListDefaultPageLimit = 50;
    private const int ThreadListMaxPageLimit = 100;
    private const int ThreadReadMaxPageLimit = 100;
    private const string ThreadListCursorKind = "thread-list";
    private const string ThreadReadCursorKind = "thread-read";

    private readonly CommandRegistry _commandRegistry = commandRegistry
                                                        ?? CommandRegistry.CreateDefault(
                                                            !string.IsNullOrWhiteSpace(workspaceCraftPath) ? new CustomCommandLoader(workspaceCraftPath) : null);

    private readonly IReadOnlyList<ConfigSchemaSection> _configSchema = configSchema ?? [];

    private readonly ChatClientRegistry _chatClientRegistry = chatClientRegistry ?? new ChatClientRegistry();

    /// <summary>
    /// Fallback decision used by <see cref="AppServerEventDispatcher"/> when non-interactive
    /// approval resolution cannot be derived from thread policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision = defaultApprovalDecision;

    /// <summary>
    /// When the wire client omits or sends an empty <c>identity.workspacePath</c>, substitute this
    /// host workspace root (AppServer / Gateway process workspace).
    /// </summary>
    private readonly string? _hostWorkspacePath = hostWorkspacePath;

    private readonly IReadOnlyDictionary<string, IAppServerMethodHandler> _extensionMethods =
        BuildExtensionMethodMap(protocolExtensions);

    private readonly IReadOnlyList<IAppServerCapabilityContributor> _capabilityContributors =
        protocolExtensions?.Cast<IAppServerCapabilityContributor>().ToArray()
        ?? [];

    private void MarkMemoryContextDirty() =>
        contextPageManager?.MarkDirty(ContextPageKeys.MemoryLongTerm("*"));

    private void MarkSkillsContextDirty() =>
        contextPageManager?.MarkDirty(ContextPageKeys.SkillsWildcard());

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
            // Route to the appropriate handler
            return await (method switch
            {
                AppServerMethods.Initialize => HandleInitializeAsync(msg, ct),
                AppServerMethods.ChannelList => HandleChannelListAsync(msg, ct),
                AppServerMethods.ChannelStatus => HandleChannelStatusAsync(msg, ct),
                AppServerMethods.ProviderList => HandleProviderListAsync(msg, ct),
                AppServerMethods.ProviderCreate => HandleProviderCreateAsync(msg, ct),
                AppServerMethods.ProviderUpdate => HandleProviderUpdateAsync(msg, ct),
                AppServerMethods.ProviderDelete => HandleProviderDeleteAsync(msg, ct),
                AppServerMethods.ProviderTest => HandleProviderTestAsync(msg, ct),
                AppServerMethods.ModelList => HandleModelListAsync(msg, ct),
                AppServerMethods.AuthOpenAiStatus => HandleAuthOpenAiStatusAsync(msg, ct),
                AppServerMethods.AuthOpenAiLogin => HandleAuthOpenAiLoginAsync(msg, ct),
                AppServerMethods.AuthOpenAiLogout => HandleAuthOpenAiLogoutAsync(msg, ct),
                AppServerMethods.AuthOpenAiUsage => HandleAuthOpenAiUsageAsync(msg, ct),
                AppServerMethods.McpList => HandleMcpListAsync(msg, ct),
                AppServerMethods.McpGet => HandleMcpGetAsync(msg, ct),
                AppServerMethods.McpUpsert => HandleMcpUpsertAsync(msg, ct),
                AppServerMethods.McpRemove => HandleMcpRemoveAsync(msg, ct),
                AppServerMethods.ExternalChannelList => HandleExternalChannelListAsync(msg, ct),
                AppServerMethods.ExternalChannelGet => HandleExternalChannelGetAsync(msg, ct),
                AppServerMethods.ExternalChannelUpsert => HandleExternalChannelUpsertAsync(msg, ct),
                AppServerMethods.ExternalChannelRemove => HandleExternalChannelRemoveAsync(msg, ct),
                AppServerMethods.ExternalChannelLogs => HandleExternalChannelLogsAsync(msg, ct),
                AppServerMethods.SubAgentProfileList => HandleSubAgentProfileListAsync(msg, ct),
                AppServerMethods.SubAgentSettingsUpdate => HandleSubAgentSettingsUpdateAsync(msg, ct),
                AppServerMethods.SubAgentProfileSetEnabled => HandleSubAgentProfileSetEnabledAsync(msg, ct),
                AppServerMethods.SubAgentProfileUpsert => HandleSubAgentProfileUpsertAsync(msg, ct),
                AppServerMethods.SubAgentProfileRemove => HandleSubAgentProfileRemoveAsync(msg, ct),
                AppServerMethods.SubAgentChildrenList => HandleSubAgentChildrenListAsync(msg, ct),
                AppServerMethods.SubAgentSendMessage => HandleSubAgentSendMessageAsync(msg, ct),
                AppServerMethods.SubAgentFollowupTask => HandleSubAgentFollowupTaskAsync(msg, ct),
                AppServerMethods.SubAgentClose => HandleSubAgentCloseAsync(msg, ct),
                AppServerMethods.McpStatusList => HandleMcpStatusListAsync(msg, ct),
                AppServerMethods.McpTest => HandleMcpTestAsync(msg, ct),
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
                AppServerMethods.TerminalList => HandleTerminalListAsync(msg, ct),
                AppServerMethods.TerminalRead => HandleTerminalReadAsync(msg, ct),
                AppServerMethods.TerminalWrite => HandleTerminalWriteAsync(msg, ct),
                AppServerMethods.TerminalStop => HandleTerminalStopAsync(msg, ct),
                AppServerMethods.TerminalClean => HandleTerminalCleanAsync(msg, ct),
                AppServerMethods.CronList => HandleCronListAsync(msg, ct),
                AppServerMethods.CronRemove => HandleCronRemoveAsync(msg, ct),
                AppServerMethods.CronEnable => HandleCronEnableAsync(msg, ct),
                AppServerMethods.CronRun => HandleCronRunAsync(msg, ct),
                AppServerMethods.HeartbeatTrigger => HandleHeartbeatTriggerAsync(msg, ct),
                AppServerMethods.SkillsList => HandleSkillsListAsync(msg, ct),
                AppServerMethods.SkillsRead => HandleSkillsReadAsync(msg, ct),
                AppServerMethods.SkillsView => HandleSkillsViewAsync(msg, ct),
                AppServerMethods.SkillsRestoreOriginal => HandleSkillsRestoreOriginalAsync(msg, ct),
                AppServerMethods.SkillsSetEnabled => HandleSkillsSetEnabledAsync(msg, ct),
                AppServerMethods.SkillsUninstall => HandleSkillsUninstallAsync(msg, ct),
                AppServerMethods.PluginList => HandlePluginListAsync(msg, ct),
                AppServerMethods.PluginView => HandlePluginViewAsync(msg, ct),
                AppServerMethods.PluginInstall => HandlePluginInstallAsync(msg, ct),
                AppServerMethods.PluginRemove => HandlePluginRemoveAsync(msg, ct),
                AppServerMethods.PluginSetEnabled => HandlePluginSetEnabledAsync(msg, ct),
                AppServerMethods.CommandList => HandleCommandListAsync(msg, ct),
                AppServerMethods.CommandExecute => HandleCommandExecuteAsync(msg, ct),
                AppServerMethods.AutomationTaskList => RouteAutomation(h => h.HandleTaskListAsync(msg, ct)),
                AppServerMethods.AutomationTaskRead => RouteAutomation(h => h.HandleTaskReadAsync(msg, ct)),
                AppServerMethods.AutomationTaskCreate => RouteAutomation(h => h.HandleTaskCreateAsync(msg, ct)),
                AppServerMethods.AutomationTaskRun => RouteAutomation(h => h.HandleTaskRunAsync(msg, ct)),
                AppServerMethods.AutomationTaskDelete => RouteAutomation(h => h.HandleTaskDeleteAsync(msg, ct)),
                AppServerMethods.AutomationTaskUpdateBinding => RouteAutomation(h => h.HandleTaskUpdateBindingAsync(msg, ct)),
                AppServerMethods.AutomationTemplateList => RouteAutomation(h => h.HandleTemplateListAsync(msg, ct)),
                AppServerMethods.AutomationTemplateSave => RouteAutomation(h => h.HandleTemplateSaveAsync(msg, ct)),
                AppServerMethods.AutomationTemplateDelete => RouteAutomation(h => h.HandleTemplateDeleteAsync(msg, ct)),
                AppServerMethods.WorkspaceCommitMessageSuggest => HandleWorkspaceCommitMessageSuggestAsync(msg, ct),
                AppServerMethods.WelcomeSuggestions => HandleWelcomeSuggestionsAsync(msg, ct),
                AppServerMethods.WorkspaceConfigSchema => HandleWorkspaceConfigSchemaAsync(msg, ct),
                AppServerMethods.WorkspaceConfigUpdate => HandleWorkspaceConfigUpdateAsync(msg, ct),
                AppServerMethods.DreamsStatus => HandleDreamsStatusAsync(msg, ct),
                AppServerMethods.DreamsRun => HandleDreamsRunAsync(msg, ct),
                AppServerMethods.DreamsCreate => HandleDreamsCreateAsync(msg, ct),
                AppServerMethods.DreamsGet => HandleDreamsGetAsync(msg, ct),
                AppServerMethods.DreamsList => HandleDreamsListAsync(msg, ct),
                AppServerMethods.DreamsCancel => HandleDreamsCancelAsync(msg, ct),
                AppServerMethods.DreamsArchive => HandleDreamsArchiveAsync(msg, ct),
                AppServerMethods.DreamsApply => HandleDreamsApplyAsync(msg, ct),
                AppServerMethods.DreamsDiscard => HandleDreamsDiscardAsync(msg, ct),
                AppServerMethods.MemoryReset => HandleMemoryResetAsync(msg, ct),
                AppServerMethods.UsageSummary => HandleUsageSummaryAsync(msg, ct),
                AppServerMethods.UsageTimeseries => HandleUsageTimeseriesAsync(msg, ct),
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
        foreach (var summary in page)
        {
            summary.Goal = await TryGetGoalSnapshotAsync(summary.Id, ct);
            var appBindings = TryGetAppBindingSummaries(summary.Id, summary.WorkspacePath);
            if (appBindings.Count > 0)
                summary.AppBindings = appBindings;
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

    private Task<object?> HandleChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        var channels = new List<ChannelInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string category)
        {
            if (!seen.Add(name))
                return;
            channels.Add(new ChannelInfo { Name = name, Category = category });
        }

        channelListContributor.AppendBaseChannels(channels, seen);

        if (!string.IsNullOrEmpty(workspaceCraftPath))
        {
            var configPath = Path.Combine(workspaceCraftPath, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var cfg = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
                    foreach (var entry in cfg.ExternalChannels)
                    {
                        if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Name))
                            continue;
                        Add(entry.Name, "external");
                    }
                }
                catch
                {
                    // Best-effort: invalid config should not fail channel/list
                }
            }
        }

        static int CategoryOrder(string c) => c switch
        {
            "builtin" => 0,
            "social" => 1,
            "system" => 2,
            "external" => 3,
            _ => 4
        };

        channels.Sort((a, b) =>
        {
            var cmp = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return Task.FromResult<object?>(new ChannelListResult { Channels = channels });
    }

    // -------------------------------------------------------------------------
    // channel/status (spec Section 20)
    // -------------------------------------------------------------------------

    private Task<object?> HandleChannelStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        if (channelStatusProvider == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.ChannelStatus);

        var statuses = channelStatusProvider.GetChannelStatuses();
        return Task.FromResult<object?>(new ChannelStatusResult { Channels = [.. statuses] });
    }

    private Task<object?> HandleProviderListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = GetParams<ProviderListParams>(msg);
        _ = ct;
        EnsureProviderManagementAvailable();

        var config = LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderListResult
        {
            Providers = BuildProviderInfos(config)
        });
    }

    private Task<object?> HandleProviderCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var p = GetParams<ProviderCreateParams>(msg);
        var id = NormalizeProviderId(p.Id);
        var protocol = NormalizeProviderProtocol(p.Protocol);
        ValidateProviderPayload(
            id,
            protocol,
            p.EndPoint,
            p.NetworkTimeoutSeconds,
            p.MaxOutputTokens,
            p.StreamMaxRetries,
            p.StreamIdleTimeoutMs);

        var configPath = GetPersonalConfigPath();
        var root = LoadWorkspaceConfigObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: true)!;
        if (FindCaseInsensitiveKey(providers, id) != null)
            throw AppServerErrors.InvalidParams($"Provider '{id}' already exists.");

        var provider = new JsonObject();
        providers[id] = provider;
        UpsertOrRemoveConfigValue(provider, null, "DisplayName", NormalizeOptionalString(p.DisplayName) ?? id);
        UpsertOrRemoveConfigValue(provider, null, "Protocol", protocol);
        UpsertOrRemoveConfigValue(provider, null, "ApiKey", NormalizeOptionalString(p.ApiKey));
        UpsertOrRemoveConfigValue(provider, null, "EndPoint", NormalizeOptionalString(p.EndPoint));
        UpsertOrRemoveConfigValue(provider, null, "NetworkTimeoutSeconds", NormalizeNetworkTimeout(p.NetworkTimeoutSeconds));
        UpsertOrRemoveConfigValue(provider, null, "MaxOutputTokens", NormalizeMaxOutputTokens(p.MaxOutputTokens));
        UpsertOrRemoveConfigValue(provider, null, "StreamMaxRetries", NormalizeStreamMaxRetries(p.StreamMaxRetries));
        UpsertOrRemoveConfigValue(provider, null, "StreamIdleTimeoutMs", NormalizeStreamIdleTimeoutMs(p.StreamIdleTimeoutMs));
        var createAuthMethod = ModelProviderAuthMethods.Normalize(p.AuthMethod);
        UpsertOrRemoveConfigValue(provider, null, "AuthMethod",
            createAuthMethod == ModelProviderAuthMethods.ApiKey ? null : createAuthMethod);
        WriteConfigObject(configPath, root);

        RefreshCurrentLlmConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderCreate, [ConfigChangeRegions.ProviderRegistry]);

        var current = LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderMutationResult
        {
            Provider = BuildProviderInfo(id, current.Providers[id], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'id' is required.");

        var p = GetParams<ProviderUpdateParams>(msg);
        var id = NormalizeProviderId(p.Id);

        var configPath = GetPersonalConfigPath();
        var root = LoadWorkspaceConfigObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: false)
            ?? throw AppServerErrors.InvalidParams($"Provider '{id}' is not configured.");
        var existingKey = FindCaseInsensitiveKey(providers, id)
            ?? throw AppServerErrors.InvalidParams($"Provider '{id}' is not configured.");
        if (providers[existingKey] is not JsonObject provider)
            throw AppServerErrors.InvalidParams($"Provider '{id}' is not an object.");

        var protocolKey = FindCaseInsensitiveKey(provider, "Protocol");
        var persistedProtocol = ReadConfigStringValue(provider, protocolKey);
        var currentProtocol = NormalizeProviderProtocol(persistedProtocol ?? ModelProviderProtocols.OpenAI);
        var protocol = currentProtocol;
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "protocol", out var protocolEl))
            protocol = NormalizeProviderProtocol(ParseNullableString(protocolEl, "protocol"));
        var endPoint = ReadConfigStringValue(provider, FindCaseInsensitiveKey(provider, "EndPoint"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out var endPointEl))
            endPoint = NormalizeOptionalString(ParseNullableString(endPointEl, "endPoint"));
        int? timeout = ReadConfigIntegerValue(provider, FindCaseInsensitiveKey(provider, "NetworkTimeoutSeconds"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "networkTimeoutSeconds", out var timeoutEl))
            timeout = ParseNullableInteger(timeoutEl, "networkTimeoutSeconds");
        int? maxOutputTokens = ReadConfigIntegerValue(provider, FindCaseInsensitiveKey(provider, "MaxOutputTokens"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "maxOutputTokens", out var maxOutputTokensEl))
            maxOutputTokens = ParseNullableInteger(maxOutputTokensEl, "maxOutputTokens");
        int? streamMaxRetries = ReadConfigIntegerValue(provider, FindCaseInsensitiveKey(provider, "StreamMaxRetries"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamMaxRetries", out var streamMaxRetriesEl))
            streamMaxRetries = ParseNullableInteger(streamMaxRetriesEl, "streamMaxRetries");
        int? streamIdleTimeoutMs = ReadConfigIntegerValue(provider, FindCaseInsensitiveKey(provider, "StreamIdleTimeoutMs"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamIdleTimeoutMs", out var streamIdleTimeoutMsEl))
            streamIdleTimeoutMs = ParseNullableInteger(streamIdleTimeoutMsEl, "streamIdleTimeoutMs");

        ValidateProviderPayload(id, protocol, endPoint, timeout, maxOutputTokens, streamMaxRetries, streamIdleTimeoutMs);

        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "displayName", out var displayNameEl))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "DisplayName"), "DisplayName",
                NormalizeOptionalString(ParseNullableString(displayNameEl, "displayName")) ?? id);
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "protocol", out _)
            || !string.Equals(persistedProtocol, protocol, StringComparison.Ordinal))
        {
            UpsertOrRemoveConfigValue(provider, protocolKey, "Protocol", protocol);
        }
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "apiKey", out var apiKeyEl))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "ApiKey"), "ApiKey",
                NormalizeOptionalString(ParseNullableString(apiKeyEl, "apiKey")));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out _))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "EndPoint"), "EndPoint", endPoint);
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "networkTimeoutSeconds", out _))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "NetworkTimeoutSeconds"), "NetworkTimeoutSeconds",
                NormalizeNetworkTimeout(timeout));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "maxOutputTokens", out _))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "MaxOutputTokens"), "MaxOutputTokens",
                NormalizeMaxOutputTokens(maxOutputTokens));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamMaxRetries", out _))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "StreamMaxRetries"), "StreamMaxRetries",
                NormalizeStreamMaxRetries(streamMaxRetries));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamIdleTimeoutMs", out _))
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "StreamIdleTimeoutMs"), "StreamIdleTimeoutMs",
                NormalizeStreamIdleTimeoutMs(streamIdleTimeoutMs));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "authMethod", out var authMethodEl))
        {
            var updatedAuthMethod = ModelProviderAuthMethods.Normalize(ParseNullableString(authMethodEl, "authMethod"));
            UpsertOrRemoveConfigValue(provider, FindCaseInsensitiveKey(provider, "AuthMethod"), "AuthMethod",
                updatedAuthMethod == ModelProviderAuthMethods.ApiKey ? null : updatedAuthMethod);
        }

        WriteConfigObject(configPath, root);

        RefreshCurrentLlmConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderUpdate, [ConfigChangeRegions.ProviderRegistry]);

        var current = LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderMutationResult
        {
            Provider = BuildProviderInfo(existingKey, current.Providers[existingKey], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var p = GetParams<ProviderDeleteParams>(msg);
        var id = NormalizeProviderId(p.Id);

        var current = LoadCurrentMergedConfig();
        if (string.Equals(current.ProviderId, id, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams($"Provider '{id}' is selected by the active workspace.");

        var configPath = GetPersonalConfigPath();
        var root = LoadWorkspaceConfigObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: false);
        var removed = false;
        if (providers != null && FindCaseInsensitiveKey(providers, id) is { } existingKey)
            removed = providers.Remove(existingKey);

        if (providers is { Count: 0 })
            root.Remove(FindCaseInsensitiveKey(root, "Providers") ?? "Providers");
        if (removed)
            WriteConfigObject(configPath, root);

        if (removed)
        {
            RefreshCurrentLlmConfig();
            InvalidateThreadAgents();
            appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderDelete, [ConfigChangeRegions.ProviderRegistry]);
        }

        return Task.FromResult<object?>(new ProviderDeleteResult { Deleted = removed });
    }

    private async Task<object?> HandleModelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ModelListParams>(msg);

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.ModelList);

        var config = appConfigMonitor?.Current
            ?? AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), GetEffectiveGlobalConfigPath());
        var result = await ModelProviderCatalog.FetchAsync(config, NormalizeOptionalString(p.ProviderId), ct, openAIClientProvider);

        return new ModelListResult
        {
            Success = result.Success,
            ProviderId = result.ProviderId,
            Protocol = result.Protocol,
            Models = [.. result.Models.Select(m => new ModelCatalogItem
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt,
                Reasoning = MapReasoningCapability(ModelThinkingAdapterCatalog.ResolveReasoningCapability(
                    config,
                    result.Protocol,
                    result.EndPoint,
                    m.Id))
            })],
            ErrorCode = result.Success ? null : result.ErrorCode.ToString(),
            ErrorMessage = result.Success ? null : FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint)
        };
    }

    private async Task<object?> HandleProviderTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureProviderManagementAvailable();
        var p = GetParams<ProviderTestParams>(msg);
        var config = LoadCurrentMergedConfig();
        var providerId = NormalizeOptionalString(p.ProviderId);

        ModelCatalogResult result;
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            result = await ModelProviderCatalog.FetchAsync(config, providerId, ct, openAIClientProvider);
        }
        else
        {
            var protocol = NormalizeProviderProtocol(p.Protocol);
            ValidateProviderPayload(
                "__provider_test__",
                protocol,
                p.EndPoint,
                p.NetworkTimeoutSeconds,
                p.MaxOutputTokens,
                p.StreamMaxRetries,
                p.StreamIdleTimeoutMs);
            var draftProviderId = "__provider_test__";
            var draftConfig = new AppConfig
            {
                Model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o-mini" : config.Model,
                NetworkTimeoutSeconds = config.NetworkTimeoutSeconds,
                Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [draftProviderId] = new()
                    {
                        DisplayName = "Provider Test",
                        Protocol = protocol,
                        ApiKey = NormalizeOptionalString(p.ApiKey) ?? string.Empty,
                        EndPoint = NormalizeOptionalString(p.EndPoint) ?? string.Empty,
                        NetworkTimeoutSeconds = NormalizeNetworkTimeout(p.NetworkTimeoutSeconds),
                        MaxOutputTokens = NormalizeMaxOutputTokens(p.MaxOutputTokens),
                        StreamMaxRetries = NormalizeStreamMaxRetries(p.StreamMaxRetries),
                        StreamIdleTimeoutMs = NormalizeStreamIdleTimeoutMs(p.StreamIdleTimeoutMs)
                    }
                }
            };
            result = await ModelProviderCatalog.FetchAsync(draftConfig, draftProviderId, ct, openAIClientProvider);
            result.ProviderId = null;
        }

        return new ProviderTestResult
        {
            Success = result.Success,
            ProviderId = result.ProviderId,
            Protocol = result.Protocol ?? NormalizeProviderProtocol(p.Protocol),
            Models = [.. result.Models.Select(m => new ModelCatalogItem
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt,
                Reasoning = MapReasoningCapability(ModelThinkingAdapterCatalog.ResolveReasoningCapability(
                    config,
                    result.Protocol ?? NormalizeProviderProtocol(p.Protocol),
                    result.EndPoint,
                    m.Id))
            })],
            ErrorCode = result.Success ? null : result.ErrorCode.ToString(),
            ErrorMessage = result.Success ? null : FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint)
        };
    }

    private static string? FormatModelListErrorMessage(string? message, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return message;

        var baseMessage = string.IsNullOrWhiteSpace(message)
            ? "Model list request failed."
            : message.Trim();
        return $"{baseMessage} Endpoint: {endpoint.Trim()}";
    }

    private static ModelReasoningCapability? MapReasoningCapability(
        ModelThinkingAdapterCatalog.ReasoningCapabilityData? capability)
    {
        if (capability == null)
            return null;

        return new ModelReasoningCapability
        {
            SupportsDisable = capability.SupportsDisable,
            SupportedEfforts = [.. capability.SupportedEfforts.Select(option => new ModelReasoningEffortOption
            {
                Effort = option.Effort,
                Label = string.IsNullOrWhiteSpace(option.Label) ? DefaultReasoningEffortLabel(option.Effort) : option.Label!,
                Description = string.IsNullOrWhiteSpace(option.Description)
                    ? DefaultReasoningEffortDescription(option.Effort)
                    : option.Description!
            })],
            DefaultEffort = capability.DefaultEffort,
            SupportedOutputs = [.. capability.SupportedOutputs],
            DefaultOutput = capability.DefaultOutput
        };
    }

    private static string DefaultReasoningEffortLabel(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "Low",
        ReasoningEffort.Medium => "Medium",
        ReasoningEffort.High => "High",
        ReasoningEffort.ExtraHigh => "Extra High",
        _ => effort.ToString()
    };

    private static string DefaultReasoningEffortDescription(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "Faster, lighter reasoning.",
        ReasoningEffort.Medium => "Balanced reasoning.",
        ReasoningEffort.High => "Deeper reasoning.",
        ReasoningEffort.ExtraHigh => "Maximum depth for supported models.",
        _ => string.Empty
    };

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

    private void EnsureProviderManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("provider/*");
    }

    private AppConfig LoadCurrentMergedConfig()
    {
        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
            return AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), GetEffectiveGlobalConfigPath());

        return appConfigMonitor?.Current ?? new AppConfig();
    }

    private string? GetEffectiveGlobalConfigPath() => appConfigMonitor?.Current.GlobalConfigPath;

    private string GetPersonalConfigPath() =>
        !string.IsNullOrWhiteSpace(appConfigMonitor?.Current.GlobalConfigPath)
            ? appConfigMonitor.Current.GlobalConfigPath!
            : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            "config.json");

    private static string NormalizeProviderId(string? id)
    {
        var normalized = NormalizeOptionalString(id);
        if (string.IsNullOrWhiteSpace(normalized))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (normalized.IndexOfAny(['/', '\\', ':']) >= 0)
            throw AppServerErrors.InvalidParams("'id' must not contain '/', '\\', or ':'.");
        return normalized;
    }

    private static void ValidateProviderPayload(
        string id,
        string protocol,
        string? endPoint,
        int? networkTimeoutSeconds,
        int? maxOutputTokens,
        int? streamMaxRetries,
        int? streamIdleTimeoutMs)
    {
        _ = id;
        _ = NormalizeProviderProtocol(protocol);
        if (networkTimeoutSeconds.HasValue && networkTimeoutSeconds.Value < 1)
            throw AppServerErrors.InvalidParams("'networkTimeoutSeconds' must be greater than zero.");
        if (maxOutputTokens.HasValue && maxOutputTokens.Value < 1)
            throw AppServerErrors.InvalidParams("'maxOutputTokens' must be greater than zero.");
        if (streamMaxRetries.HasValue
            && (streamMaxRetries.Value < 0 || streamMaxRetries.Value > ModelProviderDefaults.MaxStreamMaxRetries))
            throw AppServerErrors.InvalidParams("'streamMaxRetries' must be between 0 and 100.");
        if (streamIdleTimeoutMs.HasValue && streamIdleTimeoutMs.Value < 1)
            throw AppServerErrors.InvalidParams("'streamIdleTimeoutMs' must be greater than zero.");
        if (!string.IsNullOrWhiteSpace(endPoint) && !Uri.TryCreate(endPoint, UriKind.Absolute, out _))
            throw AppServerErrors.InvalidParams("'endPoint' must be an absolute URI.");
    }

    private static int? NormalizeNetworkTimeout(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

    private static int? NormalizeMaxOutputTokens(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

    private static int? NormalizeStreamMaxRetries(int? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0, ModelProviderDefaults.MaxStreamMaxRetries) : null;

    private static int? NormalizeStreamIdleTimeoutMs(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

    private static string NormalizeProviderProtocol(string? protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            throw AppServerErrors.InvalidParams($"Unsupported model provider protocol '{protocol}'.");
        }
    }

    private static List<ProviderInfo> BuildProviderInfos(AppConfig config)
    {
        return config.Providers
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => BuildProviderInfo(pair.Key, pair.Value, isImplicit: false))
            .ToList();
    }

    private static ProviderInfo BuildProviderInfo(
        string id,
        AppConfig.ModelProviderConfig provider,
        bool isImplicit)
    {
        var protocol = ModelProviderProtocols.Normalize(provider.Protocol);
        var authMethod = ModelProviderAuthMethods.Normalize(provider.AuthMethod);
        return new ProviderInfo
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? id : provider.DisplayName.Trim(),
            Protocol = protocol,
            ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : "********",
            HasApiKey = !string.IsNullOrWhiteSpace(provider.ApiKey),
            EndPoint = provider.EndPoint?.Trim() ?? string.Empty,
            NetworkTimeoutSeconds = provider.NetworkTimeoutSeconds,
            MaxOutputTokens = provider.MaxOutputTokens,
            StreamMaxRetries = provider.StreamMaxRetries,
            StreamIdleTimeoutMs = provider.StreamIdleTimeoutMs,
            IsImplicit = isImplicit,
            AuthMethod = authMethod,
            ChatGptAccountId = authMethod == ModelProviderAuthMethods.ChatGptOAuth && !string.IsNullOrWhiteSpace(provider.ChatGptAccountId)
                ? provider.ChatGptAccountId.Trim()
                : null,
            ChatGptPlanType = authMethod == ModelProviderAuthMethods.ChatGptOAuth && !string.IsNullOrWhiteSpace(provider.ChatGptPlanType)
                ? provider.ChatGptPlanType.Trim()
                : null,
            Capabilities = MapProviderCapabilities(ModelProviderCapabilities.ForProtocol(protocol))
        };
    }

    private static ProviderCapabilitiesWire MapProviderCapabilities(ModelProviderCapabilities capabilities) => new()
    {
        StreamingChat = capabilities.StreamingChat,
        ToolCalling = capabilities.ToolCalling,
        ModelListing = capabilities.ModelListing,
        TokenUsageReporting = capabilities.TokenUsageReporting,
        CachedInputUsageReporting = capabilities.CachedInputUsageReporting,
        PromptCacheRequestShaping = capabilities.PromptCacheRequestShaping,
        ExtendedThinking = capabilities.ExtendedThinking,
        ToolChoiceControls = capabilities.ToolChoiceControls,
        RawMetadataPassthrough = capabilities.RawMetadataPassthrough,
        ResponsesApi = capabilities.ResponsesApi,
        NativeDeferredToolLoading = capabilities.NativeDeferredToolLoading
    };

    private async Task<object?> HandleMcpListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        EnsureMcpManagementAvailable();
        var servers = await mcpClientManager!.ListConfigsAsync(ct);
        return new McpListResult { Servers = servers.Select(MapMcpConfigToWire).ToList() };
    }

    private async Task<object?> HandleMcpGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpGetParams>(msg);
        EnsureMcpManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var server = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (server == null)
            throw AppServerErrors.McpServerNotFound(p.Name);

        return new McpGetResult { Server = MapMcpConfigToWire(server) };
    }

    private async Task<object?> HandleMcpUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpUpsertParams>(msg);
        EnsureMcpManagementAvailable();
        ValidateMcpConfigWire(p.Server);

        var server = MapWireToMcpConfig(p.Server);
        server.Origin = McpServerOrigin.Workspace();

        var existing = await mcpClientManager!.GetConfigAsync(server.Name, ct);
        if (existing?.ReadOnly == true)
            throw AppServerErrors.McpServerReadOnly(server.Name);

        var workspaceServers = await GetWorkspaceMcpServersAsync(ct);
        var existingIndex = workspaceServers.FindIndex(
            candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            workspaceServers[existingIndex] = server;
        else
            workspaceServers.Add(server);

        await SaveWorkspaceMcpServersAsync(workspaceCraftPath!, workspaceServers, ct);
        SetCurrentWorkspaceMcpServers(workspaceServers);
        await ReconnectEffectiveMcpRuntimeAsync(workspaceServers, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.McpUpsert,
            [ConfigChangeRegions.Mcp]);

        var updated = await mcpClientManager.GetConfigAsync(server.Name, ct) ?? server;
        var status = (await mcpClientManager.ListStatusesAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, updated.Name, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            broadcastMcpStatusChanged?.Invoke(MapMcpStatusToWire(status));

        return new McpUpsertResult { Server = MapMcpConfigToWire(updated) };
    }

    private async Task<object?> HandleMcpRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpRemoveParams>(msg);
        EnsureMcpManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var existing = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (existing == null)
            throw AppServerErrors.McpServerNotFound(p.Name);
        if (existing.ReadOnly)
            throw AppServerErrors.McpServerReadOnly(p.Name);

        var workspaceServers = await GetWorkspaceMcpServersAsync(ct);
        var removed = workspaceServers.RemoveAll(
            candidate => string.Equals(candidate.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.McpServerNotFound(p.Name);

        await SaveWorkspaceMcpServersAsync(workspaceCraftPath!, workspaceServers, ct);
        SetCurrentWorkspaceMcpServers(workspaceServers);
        await ReconnectEffectiveMcpRuntimeAsync(workspaceServers, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.McpRemove,
            [ConfigChangeRegions.Mcp]);
        return new McpRemoveResult { Removed = true };
    }

    private Task<object?> HandleExternalChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        EnsureExternalChannelManagementAvailable();
        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        return Task.FromResult<object?>(new ExternalChannelListResult
        {
            Channels = channels.Select(MapExternalChannelToWire).ToList()
        });
    }

    private Task<object?> HandleExternalChannelGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ExternalChannelGetParams>(msg);
        EnsureExternalChannelManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channel = LoadWorkspaceExternalChannels(workspaceCraftPath!)
            .FirstOrDefault(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (channel == null)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        return Task.FromResult<object?>(new ExternalChannelGetResult
        {
            Channel = MapExternalChannelToWire(channel)
        });
    }

    private async Task<object?> HandleExternalChannelUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ExternalChannelUpsertParams>(msg);
        EnsureExternalChannelManagementAvailable();
        ValidateExternalChannelConfigWire(p.Channel);

        var channel = MapWireToExternalChannelConfig(p.Channel);
        EnsureExternalChannelNameAvailable(channel.Name);

        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        var existingIndex = channels.FindIndex(c => string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            channels[existingIndex] = channel;
        else
            channels.Add(channel);

        SaveWorkspaceExternalChannels(workspaceCraftPath!, channels);
        if (onExternalChannelUpserted != null)
            await onExternalChannelUpserted(channel, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.ExternalChannelUpsert,
            [ConfigChangeRegions.ExternalChannel]);

        return new ExternalChannelUpsertResult
        {
            Channel = MapExternalChannelToWire(channel)
        };
    }

    private async Task<object?> HandleExternalChannelRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ExternalChannelRemoveParams>(msg);
        EnsureExternalChannelManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        var removed = channels.RemoveAll(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        SaveWorkspaceExternalChannels(workspaceCraftPath!, channels);
        if (onExternalChannelRemoved != null)
            await onExternalChannelRemoved(p.Name, ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.ExternalChannelRemove,
            [ConfigChangeRegions.ExternalChannel]);

        return new ExternalChannelRemoveResult { Removed = true };
    }

    private Task<object?> HandleExternalChannelLogsAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ExternalChannelLogsParams>(msg);
        EnsureExternalChannelManagementAvailable();
        if (externalChannelLogProvider == null)
            throw AppServerErrors.InvalidRequest("External channel log retrieval is not available.");
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var lines = externalChannelLogProvider.GetRecentExternalChannelLogs(p.Name.Trim(), p.Tail);
        return Task.FromResult<object?>(new ExternalChannelLogsResult
        {
            Name = p.Name.Trim(),
            Lines = lines.ToList()
        });
    }

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

    private async Task<object?> HandleMcpStatusListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpStatusList);

        var statuses = await mcpClientManager.ListStatusesAsync(ct);
        return new McpStatusListResult { Servers = statuses.Select(MapMcpStatusToWire).ToList() };
    }

    private async Task<object?> HandleMcpTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpTestParams>(msg);
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpTest);

        ValidateMcpConfigWire(p.Server);
        var status = await mcpClientManager.TestAsync(MapWireToMcpConfig(p.Server), ct);
        return new McpTestResult
        {
            Success = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase),
            ErrorCode = status.LastError == null ? null : "McpServerTestFailed",
            ErrorMessage = status.LastError,
            ToolCount = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase) ? status.ToolCount : null
        };
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
            WithContextUsage(FilterToolExecutionItemsForConnection(baseWire), thread.Id),
            thread), thread.Id, ct);
        return new ThreadReadResult
        {
            Thread = await EnrichThreadWireAsync(wire, thread, ct),
            TurnPage = turnPage
        };
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
                WithAppBindingSummaries(
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
        await HydrateThreadGoalAsync(WithAppBindingSummaries(wire, thread.Id, thread.WorkspacePath), ct);

    private SessionWireThread WithAppBindingSummaries(
        SessionWireThread wire,
        string threadId,
        string workspacePath)
    {
        var appBindings = TryGetAppBindingSummaries(threadId, workspacePath);
        return appBindings.Count == 0 ? wire : wire with { AppBindings = appBindings };
    }

    private List<ThreadAppBindingSummaryWire> TryGetAppBindingSummaries(string threadId, string workspacePath)
    {
        if (appBindingService == null || string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(workspacePath))
            return [];

        var craftPath = Path.Combine(workspacePath, ".craft");
        if (!Directory.Exists(craftPath))
            return [];

        var catalog = appBindingService.DiscoverCatalog(
            appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
            workspacePath,
            craftPath,
            skillsLoader,
            builtInPluginSourceRoots);
        return appBindingService.ListThreadBindingSummaries(catalog, craftPath, threadId);
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
            streamDebugLogger: streamDebugLogger);
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
            streamDebugLogger: streamDebugLogger);

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

    private async Task<object?> HandleTerminalListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = GetParams<TerminalListParams>(msg);
        var sessions = await service.ListAsync(p.ThreadId, ct);
        return new { terminals = sessions };
    }

    private async Task<object?> HandleTerminalReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = GetParams<TerminalReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.ReadAsync(p.SessionId, p.WaitMs ?? 0, p.MaxOutputChars, ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalWriteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = GetParams<TerminalWriteParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.WriteStdinAsync(
            p.SessionId,
            p.Input,
            p.YieldTimeMs ?? 1000,
            p.MaxOutputChars,
            ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalStopAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = GetParams<TerminalStopParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.StopAsync(p.SessionId, ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalCleanAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = GetParams<TerminalCleanParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        await sessionService.CleanBackgroundTerminalsAsync(p.ThreadId, ct);
        var terminals = await service.ListAsync(p.ThreadId, ct);
        return new { terminals };
    }

    private IBackgroundTerminalService RequireBackgroundTerminals()
        => backgroundTerminalService ?? throw AppServerErrors.MethodNotFound("terminal/*");

    private async Task<object?> HandleWorkspaceCommitMessageSuggestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (commitMessageSuggest == null)
            throw AppServerErrors.InvalidRequest("Commit message suggestion is not available on this connection.");
        var p = GetParams<WorkspaceCommitMessageSuggestParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (p.Paths is not { Length: > 0 })
            throw AppServerErrors.InvalidParams("'paths' must contain at least one file path.");
        try
        {
            return await commitMessageSuggest.SuggestAsync(p, ct);
        }
        catch (KeyNotFoundException ex)
        {
            throw AppServerErrors.ThreadNotFound(ExtractQuotedId(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private async Task<object?> HandleWelcomeSuggestionsAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (welcomeSuggestionService == null)
            throw AppServerErrors.InvalidRequest("Welcome suggestions are not available on this connection.");

        var p = GetParams<WelcomeSuggestionsParams>(msg);
        p.Identity = NormalizeIdentityWorkspace(p.Identity);
        if (string.IsNullOrWhiteSpace(p.Identity.WorkspacePath))
            throw AppServerErrors.InvalidParams("'identity.workspacePath' is required.");

        try
        {
            return await welcomeSuggestionService.SuggestAsync(p, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private void EnsureMcpManagementAvailable()
    {
        if (mcpClientManager == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("mcp/*");
    }

    private void EnsureExternalChannelManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("externalChannel/*");
    }

    private void EnsureSubAgentManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("subagent/profiles/*");
    }

    private async Task<List<McpServerConfig>> GetWorkspaceMcpServersAsync(CancellationToken ct)
    {
        var source = appConfigMonitor?.Current.McpServers;
        if (source is not { Count: > 0 } && mcpClientManager != null)
            source = (await mcpClientManager.ListConfigsAsync(ct))
                .Where(server => !server.ReadOnly)
                .ToList();

        return (source ?? [])
            .Where(server => !server.ReadOnly)
            .Select(CloneAsWorkspaceMcpServer)
            .ToList();
    }

    private List<McpServerConfig> GetWorkspaceMcpServersSnapshot()
    {
        return (appConfigMonitor?.Current.McpServers ?? [])
            .Where(server => !server.ReadOnly)
            .Select(CloneAsWorkspaceMcpServer)
            .ToList();
    }

    private void SetCurrentWorkspaceMcpServers(IReadOnlyList<McpServerConfig> servers)
    {
        if (appConfigMonitor == null)
            return;

        appConfigMonitor.Current.McpServers = servers
            .Select(CloneAsWorkspaceMcpServer)
            .ToList();
    }

    private List<LspServerConfig> GetWorkspaceLspServersSnapshot()
    {
        return (appConfigMonitor?.Current.LspServers ?? [])
            .Where(server => !server.ReadOnly)
            .Select(CloneAsWorkspaceLspServer)
            .ToList();
    }

    private async Task ReconnectEffectiveMcpRuntimeAsync(
        IReadOnlyList<McpServerConfig> workspaceServers,
        CancellationToken ct)
    {
        if (mcpClientManager == null)
            return;

        var current = appConfigMonitor?.Current ?? new AppConfig();
        current.McpServers = workspaceServers
            .Select(CloneAsWorkspaceMcpServer)
            .ToList();

        var effective = PluginMcpServerResolver.LoadEffectiveServers(
            current,
            ResolveHostWorkspacePath(),
            workspaceCraftPath ?? Path.Combine(ResolveHostWorkspacePath(), ".craft"),
            out var diagnostics);
        PluginDiagnosticsStore.Shared.Append(diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics);

        await mcpClientManager.ConnectAsync(effective, ct);
    }

    private async Task ReconnectEffectiveLspRuntimeAsync(CancellationToken ct)
    {
        if (lspServerManager == null)
            return;

        ct.ThrowIfCancellationRequested();
        await lspServerManager.InitializeAsync(ct);
    }

    private string ResolveHostWorkspacePath() =>
        _hostWorkspacePath
        ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
        ?? Directory.GetCurrentDirectory();

    private static McpServerConfig CloneAsWorkspaceMcpServer(McpServerConfig server)
    {
        var clone = server.Clone();
        clone.Origin = McpServerOrigin.Workspace();
        return clone;
    }

    private static LspServerConfig CloneAsWorkspaceLspServer(LspServerConfig server)
    {
        var clone = server.Clone();
        clone.Origin = LspServerOrigin.Workspace();
        return clone;
    }

    private static McpServerConfigWire MapMcpConfigToWire(McpServerConfig config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = config.NormalizedTransport,
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        Args = config.Arguments.Count > 0 ? [.. config.Arguments] : null,
        Env = config.EnvironmentVariables.Count > 0 ? new Dictionary<string, string>(config.EnvironmentVariables) : null,
        EnvVars = config.EnvVars.Count > 0 ? [.. config.EnvVars] : null,
        Cwd = config.Cwd,
        Url = string.IsNullOrWhiteSpace(config.Url) ? null : config.Url,
        BearerTokenEnvVar = config.BearerTokenEnvVar,
        HttpHeaders = config.Headers.Count > 0 ? new Dictionary<string, string>(config.Headers) : null,
        EnvHttpHeaders = config.EnvHttpHeaders.Count > 0 ? new Dictionary<string, string>(config.EnvHttpHeaders) : null,
        StartupTimeoutSec = config.StartupTimeoutSec,
        ToolTimeoutSec = config.ToolTimeoutSec,
        Origin = MapMcpOriginToWire(config.Origin),
        ReadOnly = config.ReadOnly
    };

    private static McpStatusInfoWire MapMcpStatusToWire(McpServerStatusSnapshot status) => new()
    {
        Name = status.Name,
        Enabled = status.Enabled,
        StartupState = status.StartupState,
        ToolCount = status.ToolCount,
        ResourceCount = status.ResourceCount,
        ResourceTemplateCount = status.ResourceTemplateCount,
        LastError = status.LastError,
        Transport = status.Transport,
        Origin = MapMcpOriginToWire(status.Origin),
        ReadOnly = status.ReadOnly
    };

    private static McpServerConfig MapWireToMcpConfig(McpServerConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeMcpTransport(wire.Transport),
        Command = wire.Command?.Trim() ?? string.Empty,
        Arguments = wire.Args ?? [],
        EnvironmentVariables = wire.Env ?? new Dictionary<string, string>(),
        EnvVars = wire.EnvVars ?? [],
        Cwd = string.IsNullOrWhiteSpace(wire.Cwd) ? null : wire.Cwd.Trim(),
        Url = wire.Url?.Trim() ?? string.Empty,
        BearerTokenEnvVar = string.IsNullOrWhiteSpace(wire.BearerTokenEnvVar) ? null : wire.BearerTokenEnvVar.Trim(),
        Headers = wire.HttpHeaders ?? new Dictionary<string, string>(),
        EnvHttpHeaders = wire.EnvHttpHeaders ?? new Dictionary<string, string>(),
        StartupTimeoutSec = wire.StartupTimeoutSec,
        ToolTimeoutSec = wire.ToolTimeoutSec,
        Origin = McpServerOrigin.Workspace()
    };

    private static McpServerOriginWire MapMcpOriginToWire(McpServerOrigin origin) =>
        new()
        {
            Kind = origin.IsPlugin ? "plugin" : "workspace",
            PluginId = origin.PluginId,
            PluginDisplayName = origin.PluginDisplayName,
            DeclaredName = origin.DeclaredName
        };

    private static string NormalizeMcpTransport(string? transport) =>
        transport?.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("http", StringComparison.OrdinalIgnoreCase) == true
                ? "streamableHttp"
                : "stdio";

    private static ExternalChannelConfigWire MapExternalChannelToWire(ExternalChannelEntry config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = ExternalChannelTransportToWire(config.Transport),
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        BuiltinModule = string.IsNullOrWhiteSpace(config.BuiltinModule) ? null : config.BuiltinModule,
        Args = config.Args is { Count: > 0 } ? [.. config.Args] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
        Env = config.Env is { Count: > 0 } ? new Dictionary<string, string>(config.Env, StringComparer.Ordinal) : null
    };

    private static ExternalChannelEntry MapWireToExternalChannelConfig(ExternalChannelConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeExternalChannelTransport(wire.Transport),
        Command = string.IsNullOrWhiteSpace(wire.Command) ? null : wire.Command.Trim(),
        BuiltinModule = string.IsNullOrWhiteSpace(wire.BuiltinModule) ? null : wire.BuiltinModule.Trim(),
        Args = wire.Args is { Count: > 0 } ? [.. wire.Args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(wire.WorkingDirectory) ? null : wire.WorkingDirectory.Trim(),
        Env = wire.Env is { Count: > 0 } ? new Dictionary<string, string>(wire.Env, StringComparer.Ordinal) : null
    };

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

    private static ExternalChannelTransport NormalizeExternalChannelTransport(string? transport)
    {
        if (transport?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true)
            return ExternalChannelTransport.Websocket;
        if (transport?.Equals("managedWebsocket", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("managed-websocket", StringComparison.OrdinalIgnoreCase) == true)
            return ExternalChannelTransport.ManagedWebsocket;
        return ExternalChannelTransport.Subprocess;
    }

    private static string ExternalChannelTransportToWire(ExternalChannelTransport transport) =>
        transport switch
        {
            ExternalChannelTransport.Websocket => "websocket",
            ExternalChannelTransport.ManagedWebsocket => "managedWebsocket",
            _ => "subprocess"
        };

    private static void ValidateMcpConfigWire(McpServerConfigWire server)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
            throw AppServerErrors.McpServerValidationFailed("'server.name' is required.");

        var transport = NormalizeMcpTransport(server.Transport);

        if (transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw AppServerErrors.McpServerValidationFailed("'server.command' is required for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is not supported for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
                throw AppServerErrors.McpServerValidationFailed("'server.bearerTokenEnvVar' is not supported for stdio transport.");
            if (server.HttpHeaders is { Count: > 0 } || server.EnvHttpHeaders is { Count: > 0 })
                throw AppServerErrors.McpServerValidationFailed("HTTP headers are not supported for stdio transport.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is required for streamableHttp transport.");
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out _))
                throw AppServerErrors.McpServerValidationFailed("'server.url' must be an absolute URL.");
            if (!string.IsNullOrWhiteSpace(server.Command) ||
                server.Args is { Count: > 0 } ||
                server.Env is { Count: > 0 } ||
                server.EnvVars is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(server.Cwd))
            {
                throw AppServerErrors.McpServerValidationFailed("stdio-only fields are not supported for streamableHttp transport.");
            }
        }
    }

    private static void ValidateExternalChannelConfigWire(ExternalChannelConfigWire channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw AppServerErrors.ExternalChannelValidationFailed("'channel.name' is required.");

        var transport = NormalizeExternalChannelTransport(channel.Transport);

        if (transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket)
        {
            if (string.IsNullOrWhiteSpace(channel.Command) && string.IsNullOrWhiteSpace(channel.BuiltinModule))
                throw AppServerErrors.ExternalChannelValidationFailed("'channel.command' or 'channel.builtinModule' is required for subprocess or managedWebsocket transport.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(channel.Command) ||
                !string.IsNullOrWhiteSpace(channel.BuiltinModule) ||
                channel.Args is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(channel.WorkingDirectory) ||
                channel.Env is { Count: > 0 })
            {
                throw AppServerErrors.ExternalChannelValidationFailed("process-launch fields are not supported for websocket transport.");
            }
        }
    }

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

    private static async Task SaveWorkspaceMcpServersAsync(
        string workspaceCraftPath,
        IReadOnlyList<McpServerConfig> servers,
        CancellationToken ct)
    {
        _ = ct;
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var legacyLanguageKey = FindCaseInsensitiveKey(root, "Language");
        var legacyLanguageRemoved = legacyLanguageKey != null && root.Remove(legacyLanguageKey);

        var key = FindCaseInsensitiveKey(root, "McpServers") ?? "McpServers";
        var serverObject = new JsonObject();
        foreach (var server in servers
                     .Where(server => !server.ReadOnly && server.Origin.IsWorkspace)
                     .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Name))
                continue;

            var workspaceServer = CloneAsWorkspaceMcpServer(server);
            var serverNode = JsonSerializer.SerializeToNode(workspaceServer, AppConfig.SerializerOptions);
            if (serverNode != null)
                serverObject[workspaceServer.Name] = serverNode;
        }

        root[key] = serverObject;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static List<ExternalChannelEntry> LoadWorkspaceExternalChannels(string workspaceCraftPath)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        return AppConfig.Load(configPath).ExternalChannels.Select(c => c.Clone()).ToList();
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

    private void RefreshCurrentLlmConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
        appConfigMonitor.Current.ProviderId = mergedConfig.ProviderId;
        appConfigMonitor.Current.Model = mergedConfig.Model;
        appConfigMonitor.Current.NetworkTimeoutSeconds = mergedConfig.NetworkTimeoutSeconds;
        appConfigMonitor.Current.Providers = mergedConfig.Providers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }

    private void RefreshCurrentPermissionsConfig(string? defaultApprovalPolicy)
    {
        if (appConfigMonitor == null)
            return;

        appConfigMonitor.Current.Permissions = new AppConfig.PermissionsConfig
        {
            DefaultApprovalPolicy = ToApprovalPolicy(defaultApprovalPolicy)
        };
    }

    private void RefreshCurrentMemoryConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
        appConfigMonitor.Current.Memory = new MemoryConfig
        {
            AutoConsolidateEnabled = mergedConfig.Memory.AutoConsolidateEnabled,
            ConsolidateEveryNTurns = mergedConfig.Memory.ConsolidateEveryNTurns
        };
    }

    private void RefreshCurrentDreamsConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath);
        appConfigMonitor.Current.Dreams = new DreamsConfig
        {
            Enabled = mergedConfig.Dreams.Enabled,
            Interval = mergedConfig.Dreams.Interval,
            StartupDelay = mergedConfig.Dreams.StartupDelay,
            ThreadLookbackCount = mergedConfig.Dreams.ThreadLookbackCount,
            AutoApply = mergedConfig.Dreams.AutoApply,
            HistoryTailChars = mergedConfig.Dreams.HistoryTailChars,
            MinCompletedTurnsSinceLastRun = mergedConfig.Dreams.MinCompletedTurnsSinceLastRun
        };
    }

    private void RefreshCurrentLspConfig(bool? toolsLspEnabled)
    {
        if (appConfigMonitor == null)
            return;

        if (toolsLspEnabled.HasValue)
        {
            appConfigMonitor.Current.Tools.Lsp.Enabled = toolsLspEnabled.Value;
            return;
        }

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var configPath = Path.Combine(workspaceCraftPath, "config.json");
            var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
            appConfigMonitor.Current.Tools.Lsp = mergedConfig.Tools.Lsp;
            return;
        }

        appConfigMonitor.Current.Tools.Lsp.Enabled = false;
    }

    private void RefreshCurrentReasoningConfig()
    {
        if (appConfigMonitor == null)
            return;

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var configPath = Path.Combine(workspaceCraftPath, "config.json");
            var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, GetEffectiveGlobalConfigPath());
            appConfigMonitor.Current.Reasoning = CloneReasoningConfig(mergedConfig.Reasoning);
            return;
        }

        appConfigMonitor.Current.Reasoning = new AppConfig.ReasoningConfig();
    }

    private static void SaveWorkspaceExternalChannels(string workspaceCraftPath, IReadOnlyCollection<ExternalChannelEntry> channels)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        var key = FindCaseInsensitiveKey(root, "ExternalChannels") ?? "ExternalChannels";
        var channelObject = new JsonObject();
        foreach (var channel in channels.Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            channelObject[channel.Name] = BuildExternalChannelNode(channel);
        }

        root[key] = channelObject;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private void EnsureExternalChannelNameAvailable(string name)
    {
        var nativeChannels = new List<ChannelInfo>();
        channelListContributor.AppendBaseChannels(nativeChannels, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (nativeChannels.Any(c =>
                !string.Equals(c.Category, "external", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw AppServerErrors.ExternalChannelNameConflict(
                $"'{name}' conflicts with a native channel name.");
        }
    }

    private static JsonObject BuildExternalChannelNode(ExternalChannelEntry channel)
    {
        var node = new JsonObject
        {
            ["enabled"] = channel.Enabled,
            ["transport"] = ExternalChannelTransportToWire(channel.Transport)
        };

        if (channel.Transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket)
        {
            if (!string.IsNullOrWhiteSpace(channel.Command))
                node["command"] = channel.Command;
            if (!string.IsNullOrWhiteSpace(channel.BuiltinModule))
                node["builtinModule"] = channel.BuiltinModule;
            if (channel.Args is { Count: > 0 })
                node["args"] = JsonSerializer.SerializeToNode(channel.Args);
            if (!string.IsNullOrWhiteSpace(channel.WorkingDirectory))
                node["workingDirectory"] = channel.WorkingDirectory;
            if (channel.Env is { Count: > 0 })
                node["env"] = JsonSerializer.SerializeToNode(channel.Env);
        }

        return node;
    }

    private async Task<object?> HandleWorkspaceConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        const string requiredFieldMessage =
            "At least one of 'providerId', 'model', 'welcomeSuggestionsEnabled', " +
            "'skillsSelfLearningEnabled', 'memoryAutoConsolidateEnabled', 'dreamsEnabled', 'dreamsInterval', " +
            "'dreamsThreadLookbackCount', 'dreamsAutoApply', 'defaultApprovalPolicy', 'toolsLspEnabled', " +
            "or 'reasoning' is required.";

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.WorkspaceConfigUpdate);
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams(requiredFieldMessage);

        var hasProviderId = TryGetCaseInsensitiveProperty(msg.Params.Value, "providerId", out var providerIdEl);
        var hasModel = TryGetCaseInsensitiveProperty(msg.Params.Value, "model", out var modelEl);
        var hasApiKey = TryGetCaseInsensitiveProperty(msg.Params.Value, "apiKey", out var apiKeyEl);
        var hasEndPoint = TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out var endPointEl);
        if (hasApiKey || hasEndPoint)
            throw AppServerErrors.InvalidParams(
                "'apiKey' and 'endPoint' are no longer accepted by workspace/config/update. Use provider/create or provider/update.");
        var hasWelcomeSuggestionsEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "welcomeSuggestionsEnabled",
            out var welcomeSuggestionsEnabledEl);
        var hasSkillsSelfLearningEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "skillsSelfLearningEnabled",
            out var skillsSelfLearningEnabledEl);
        var hasMemoryAutoConsolidateEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "memoryAutoConsolidateEnabled",
            out var memoryAutoConsolidateEnabledEl);
        var hasDreamsEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsEnabled",
            out var dreamsEnabledEl);
        var hasDreamsInterval = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsInterval",
            out var dreamsIntervalEl);
        var hasDreamsThreadLookbackCount = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsThreadLookbackCount",
            out var dreamsThreadLookbackCountEl);
        var hasDreamsAutoApply = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsAutoApply",
            out var dreamsAutoApplyEl);
        var hasDefaultApprovalPolicy = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "defaultApprovalPolicy",
            out var defaultApprovalPolicyEl);
        var hasToolsLspEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "toolsLspEnabled",
            out var toolsLspEnabledEl);
        var hasReasoning = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "reasoning",
            out var reasoningEl);
        if (!hasProviderId
            && !hasModel
            && !hasWelcomeSuggestionsEnabled
            && !hasSkillsSelfLearningEnabled
            && !hasMemoryAutoConsolidateEnabled
            && !hasDreamsEnabled
            && !hasDreamsInterval
            && !hasDreamsThreadLookbackCount
            && !hasDreamsAutoApply
            && !hasDefaultApprovalPolicy
            && !hasToolsLspEnabled
            && !hasReasoning)
        {
            throw AppServerErrors.InvalidParams(requiredFieldMessage);
        }

        var currentConfig = appConfigMonitor?.Current ?? LoadCurrentMergedConfig();
        var providerId = hasProviderId ? NormalizeOptionalString(ParseNullableString(providerIdEl, "providerId")) : null;
        var model = hasModel ? ParseNullableString(modelEl, "model") : null;
        var welcomeSuggestionsEnabled = hasWelcomeSuggestionsEnabled
            ? ParseNullableBoolean(welcomeSuggestionsEnabledEl, "welcomeSuggestionsEnabled")
            : null;
        var skillsSelfLearningEnabled = hasSkillsSelfLearningEnabled
            ? ParseNullableBoolean(skillsSelfLearningEnabledEl, "skillsSelfLearningEnabled")
            : null;
        var memoryAutoConsolidateEnabled = hasMemoryAutoConsolidateEnabled
            ? ParseNullableBoolean(memoryAutoConsolidateEnabledEl, "memoryAutoConsolidateEnabled")
            : null;
        var dreamsEnabled = hasDreamsEnabled
            ? ParseNullableBoolean(dreamsEnabledEl, "dreamsEnabled")
            : null;
        var dreamsInterval = hasDreamsInterval
            ? ParseNullablePositiveTimeSpan(dreamsIntervalEl, "dreamsInterval")
            : null;
        var dreamsThreadLookbackCount = hasDreamsThreadLookbackCount
            ? ParseNullablePositiveInt32(dreamsThreadLookbackCountEl, "dreamsThreadLookbackCount")
            : null;
        var dreamsAutoApply = hasDreamsAutoApply
            ? ParseNullableBoolean(dreamsAutoApplyEl, "dreamsAutoApply")
            : null;
        var defaultApprovalPolicy = hasDefaultApprovalPolicy
            ? ParseNullableString(defaultApprovalPolicyEl, "defaultApprovalPolicy")
            : null;
        var toolsLspEnabled = hasToolsLspEnabled
            ? ParseNullableBoolean(toolsLspEnabledEl, "toolsLspEnabled")
            : null;
        var reasoning = hasReasoning
            ? ParseNullableReasoningConfig(reasoningEl, "reasoning", currentConfig.Reasoning)
            : null;

        if (hasReasoning && reasoning != null)
        {
            ValidateReasoningForRuntime(
                currentConfig,
                hasProviderId ? providerId : currentConfig.ProviderId,
                hasModel ? NormalizeWorkspaceModel(model) : currentConfig.Model,
                reasoning);
        }

        var saveResult = SaveWorkspaceCoreConfig(
            workspaceCraftPath,
            hasProviderId ? providerId : null,
            hasModel ? NormalizeWorkspaceModel(model) : null,
            welcomeSuggestionsEnabled,
            skillsSelfLearningEnabled,
            memoryAutoConsolidateEnabled,
            dreamsEnabled,
            dreamsInterval,
            dreamsThreadLookbackCount,
            dreamsAutoApply,
            hasDefaultApprovalPolicy ? NormalizeDefaultApprovalPolicy(defaultApprovalPolicy) : null,
            toolsLspEnabled,
            reasoning,
            hasProviderId,
            hasModel,
            hasWelcomeSuggestionsEnabled,
            hasSkillsSelfLearningEnabled,
            hasMemoryAutoConsolidateEnabled,
            hasDreamsEnabled,
            hasDreamsInterval,
            hasDreamsThreadLookbackCount,
            hasDreamsAutoApply,
            hasDefaultApprovalPolicy,
            hasToolsLspEnabled,
            hasReasoning);

        var changedRegions = new List<string>();
        if (saveResult.ProviderIdChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceProvider);
        if (saveResult.ModelChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceModel);
        if (saveResult.ProviderIdChanged
            || saveResult.ModelChanged)
        {
            RefreshCurrentLlmConfig();
            InvalidateThreadAgents();
        }
        if (saveResult.WelcomeSuggestionsChanged)
            changedRegions.Add(ConfigChangeRegions.WelcomeSuggestions);
        if (saveResult.SkillsSelfLearningChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Skills);
            MarkSkillsContextDirty();
        }
        if (saveResult.MemoryAutoConsolidateChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Memory);
            RefreshCurrentMemoryConfig();
        }
        if (saveResult.DreamsChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Memory);
            RefreshCurrentDreamsConfig();
            if (dreamsService != null)
            {
                if (appConfigMonitor?.Current.Dreams.Enabled == true)
                    await dreamsService.StartAsync(ct);
                else
                    await dreamsService.StopAsync(ct);
            }
        }
        if (saveResult.DefaultApprovalPolicyChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);
            RefreshCurrentPermissionsConfig(saveResult.DefaultApprovalPolicy);
        }
        if (saveResult.ToolsLspEnabledChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Lsp);
            RefreshCurrentLspConfig(saveResult.ToolsLspEnabled);
            await ReconnectEffectiveLspRuntimeAsync(ct);
        }
        if (saveResult.ReasoningChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceReasoning);
            RefreshCurrentReasoningConfig();
            InvalidateThreadAgents();
        }
        if (changedRegions.Count > 0)
        {
            appConfigMonitor?.NotifyChanged(
                AppServerMethods.WorkspaceConfigUpdate,
                changedRegions);
        }

        return new WorkspaceConfigUpdateResult
        {
            ProviderId = saveResult.ProviderId,
            Model = saveResult.Model,
            WelcomeSuggestionsEnabled = saveResult.WelcomeSuggestionsEnabled,
            SkillsSelfLearningEnabled = saveResult.SkillsSelfLearningEnabled,
            MemoryAutoConsolidateEnabled = saveResult.MemoryAutoConsolidateEnabled,
            DreamsEnabled = saveResult.DreamsEnabled,
            DreamsInterval = saveResult.DreamsInterval,
            DreamsThreadLookbackCount = saveResult.DreamsThreadLookbackCount,
            DreamsAutoApply = saveResult.DreamsAutoApply,
            DefaultApprovalPolicy = saveResult.DefaultApprovalPolicy,
            ToolsLspEnabled = saveResult.ToolsLspEnabled,
            Reasoning = CloneNullableReasoningConfig(saveResult.Reasoning)
        };
    }

    private Task<object?> HandleWorkspaceConfigSchemaAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        _ = GetParams<WorkspaceConfigSchemaParams>(msg);
        if (_configSchema.Count == 0)
            throw AppServerErrors.MethodNotFound(AppServerMethods.WorkspaceConfigSchema);

        return Task.FromResult<object?>(new WorkspaceConfigSchemaResult
        {
            Sections = [.. _configSchema]
        });
    }

    private Task<object?> HandleDreamsStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(msg, AppServerMethods.DreamsStatus);
        return Task.FromResult<object?>(BuildDreamsStatusResult());
    }

    private async Task<object?> HandleDreamsRunAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        ValidateEmptyObjectParams(msg, AppServerMethods.DreamsRun);
        await dreamsService!.RequestRunAsync(cancellationToken: ct).ConfigureAwait(false);
        return BuildDreamsStatusResult();
    }

    private async Task<object?> HandleDreamsCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = msg.Params.HasValue && msg.Params.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new DreamsCreateParams()
            : GetParams<DreamsCreateParams>(msg);
        if (p.ThreadLookbackCount.HasValue && p.ThreadLookbackCount.Value <= 0)
            throw AppServerErrors.InvalidParams("'threadLookbackCount' must be a positive integer.");

        var state = await dreamsService!.RequestRunAsync(
                new DreamsRunRequest(p.ThreadIds, p.ThreadLookbackCount, p.Instructions, p.Model),
                ct)
            .ConfigureAwait(false);
        return new DreamsRunResult
        {
            Run = ToDreamRunWire(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId()
        };
    }

    private Task<object?> HandleDreamsGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = GetParams<DreamsRunIdParams>(msg);
        var state = dreamsService!.LoadRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult
        {
            Run = ToDreamRunWire(state),
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId(),
            Preview = BuildDreamsRunPreview(state)
        });
    }

    private Task<object?> HandleDreamsListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = msg.Params.HasValue && msg.Params.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new DreamsListParams()
            : GetParams<DreamsListParams>(msg);
        return Task.FromResult<object?>(new DreamsListResult
        {
            Runs = dreamsService!.ListRuns(p.IncludeArchived)
                .Select(ToDreamRunWire)
                .ToList()
        });
    }

    private async Task<object?> HandleDreamsCancelAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureDreamsAvailable();
        var p = GetParams<DreamsRunIdParams>(msg);
        var state = await dreamsService!.CancelRunAsync(NormalizeDreamRunId(p.RunId), ct).ConfigureAwait(false);
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() };
    }

    private Task<object?> HandleDreamsArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = GetParams<DreamsRunIdParams>(msg);
        var state = dreamsService!.ArchiveRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsApplyAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = GetParams<DreamsRunIdParams>(msg);
        DreamsRunState? state;
        try
        {
            state = dreamsService!.ApplyRun(NormalizeDreamRunId(p.RunId));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        MarkMemoryContextDirty();
        appConfigMonitor?.NotifyChanged(AppServerMethods.DreamsApply, [ConfigChangeRegions.Memory]);
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private Task<object?> HandleDreamsDiscardAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureDreamsAvailable();
        var p = GetParams<DreamsRunIdParams>(msg);
        var state = dreamsService!.DiscardRun(NormalizeDreamRunId(p.RunId));
        if (state == null)
            throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(new DreamsRunResult { Run = ToDreamRunWire(state), ActiveDreamStoreId = dreamStore?.GetActiveStoreId() });
    }

    private void EnsureDreamsAvailable()
    {
        if (dreamsService == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.DreamsStatus);
    }

    private DreamsStatusResult BuildDreamsStatusResult()
    {
        var config = appConfigMonitor?.Current.Dreams ?? new DreamsConfig();
        var state = dreamsService!.LoadLatestState();
        var running = state?.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue;
        return new DreamsStatusResult
        {
            Enabled = config.Enabled,
            Interval = FormatTimeSpanForWire(config.Interval),
            ThreadLookbackCount = config.ThreadLookbackCount,
            AutoApply = config.AutoApply,
            HistoryTailChars = config.HistoryTailChars,
            MinCompletedTurnsSinceLastRun = config.MinCompletedTurnsSinceLastRun,
            NextRunAt = state?.NextRunAt,
            Running = running,
            ActiveDreamStoreId = dreamStore?.GetActiveStoreId(),
            LastRun = state == null ? null : ToDreamRunWire(state)
        };
    }

    private static DreamsRunStateWire ToDreamRunWire(DreamsRunState state) => new()
    {
        Id = state.Id,
        Status = state.Status,
        StartedAt = state.StartedAt,
        EndedAt = state.EndedAt,
        ProcessedThreadCount = state.ProcessedThreadCount,
        CandidateThreadCount = state.CandidateThreadCount,
        DreamWritten = state.DreamWritten,
        HistoryWritten = state.HistoryWritten,
        TopicFilesWritten = state.TopicFilesWritten,
        TopicFilesDeleted = state.TopicFilesDeleted,
        EvidenceSearchCount = state.EvidenceSearchCount,
        EvidenceReadCount = state.EvidenceReadCount,
        OutputStoreId = state.OutputStoreId,
        ReviewStatus = state.ReviewStatus,
        AutoApplied = state.AutoApplied,
        ErrorType = state.ErrorType,
        EvidenceThreadIds = state.EvidenceThreadIds,
        WrittenPaths = state.WrittenPaths,
        ThreadId = state.ThreadId,
        TurnId = state.TurnId,
        TurnIds = state.TurnIds,
        Trigger = state.Trigger,
        Message = state.Message,
        Usage = state.Usage,
        InputManifestPath = state.InputManifestPath
    };

    private DreamsRunPreviewWire? BuildDreamsRunPreview(DreamsRunState state)
    {
        if (dreamStore == null || string.IsNullOrWhiteSpace(state.OutputStoreId))
            return null;

        var activeStoreId = dreamStore.GetActiveStoreId();
        return new DreamsRunPreviewWire
        {
            ActiveStoreId = activeStoreId,
            OutputStoreId = state.OutputStoreId,
            ActiveIndexMarkdown = string.IsNullOrWhiteSpace(activeStoreId) ? string.Empty : dreamStore.ReadIndex(activeStoreId),
            OutputIndexMarkdown = dreamStore.ReadIndex(state.OutputStoreId),
            ActiveTopicPaths = string.IsNullOrWhiteSpace(activeStoreId)
                ? []
                : dreamStore.ListTopicFiles(activeStoreId).Select(static topic => topic.Path).ToList(),
            OutputTopicPaths = dreamStore.ListTopicFiles(state.OutputStoreId).Select(static topic => topic.Path).ToList()
        };
    }

    private static string NormalizeDreamRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw AppServerErrors.InvalidParams("'runId' is required.");
        return runId.Trim();
    }

    private static void ValidateEmptyObjectParams(AppServerIncomingMessage msg, string method)
    {
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind is not JsonValueKind.Null
                and not JsonValueKind.Object
                and not JsonValueKind.Undefined)
        {
            throw AppServerErrors.InvalidParams($"{method} accepts omitted, null, or empty-object params.");
        }
    }

    private Task<object?> HandleMemoryResetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (memoryStore == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.MemoryReset);
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind is not JsonValueKind.Null
                and not JsonValueKind.Object
                and not JsonValueKind.Undefined)
        {
            throw AppServerErrors.InvalidParams("memory/reset accepts omitted, null, or empty-object params.");
        }

        try
        {
            memoryStore.ClearAll();
            dreamStore?.ClearAll();
            MarkMemoryContextDirty();
            if (!string.IsNullOrWhiteSpace(_hostWorkspacePath))
                welcomeSuggestionService?.ClearWorkspaceCache(_hostWorkspacePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw AppServerErrors.InternalError($"Failed to reset memory: {ex.Message}");
        }

        appConfigMonitor?.NotifyChanged(
            AppServerMethods.MemoryReset,
            [ConfigChangeRegions.Memory]);

        return Task.FromResult<object?>(new MemoryResetResult());
    }

    // -------------------------------------------------------------------------
    // cron/* methods (spec Section 16)
    // -------------------------------------------------------------------------

    private Task<object?> HandleCronListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronList);
        var p = GetParams<CronListParams>(msg);
        var jobs = cronService.ListJobs(includeDisabled: p.IncludeDisabled);
        return Task.FromResult<object?>(new CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        });
    }

    private Task<object?> HandleCronRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRemove);
        var p = GetParams<CronRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var removed = cronService.RemoveJob(p.JobId);
        if (!removed) throw AppServerErrors.CronJobNotFound(p.JobId);
        broadcastCronStateChanged?.Invoke(new CronJobWireInfo { Id = p.JobId }, true);
        return Task.FromResult<object?>(new CronRemoveResult { Removed = true });
    }

    private Task<object?> HandleCronEnableAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronEnable);
        var p = GetParams<CronEnableParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.EnableJob(p.JobId, p.Enabled);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        broadcastCronStateChanged?.Invoke(MapCronJob(job), false);
        return Task.FromResult<object?>(new CronEnableResult { Job = MapCronJob(job) });
    }

    private Task<object?> HandleCronRunAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRun);
        var p = GetParams<CronRunParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.RunJobNow(p.JobId);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        return Task.FromResult<object?>(new CronRunResult
        {
            Queued = true,
            Job = MapCronJob(job)
        });
    }

    private static CronJobWireInfo MapCronJob(CronJob job) => CronJobWireMapping.ToWire(job);

    // ── usage/summary (spec Section 27A.2) ───────────────────────────────────

    private Task<object?> HandleUsageSummaryAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(AppServerMethods.UsageSummary);
        var s = traceStore.GetSummary();
        return Task.FromResult<object?>(new UsageSummaryResult
        {
            SessionCount = s.SessionCount,
            TotalRequests = s.TotalRequests,
            TotalResponses = s.TotalResponses,
            TotalToolCalls = s.TotalToolCalls,
            TotalErrors = s.TotalErrors,
            TotalContextCompactions = s.TotalContextCompactions,
            TotalInputTokens = s.TotalInputTokens,
            TotalOutputTokens = s.TotalOutputTokens,
            TotalCachedInputTokens = s.TotalCachedInputTokens,
            TotalCacheWriteInputTokens = s.TotalCacheWriteInputTokens,
            TotalFreshInputTokens = s.TotalFreshInputTokens,
            TotalNonCachedInputTokens = s.TotalNonCachedInputTokens,
            TotalReasoningOutputTokens = s.TotalReasoningOutputTokens,
            TotalToolDurationMs = s.TotalToolDurationMs,
            AvgToolDurationMs = s.AvgToolDurationMs,
            MaxToolDurationMs = s.MaxToolDurationMs,
            CacheHitRate = s.CacheHitRate,
            TotalTokens = s.TotalTokens
        });
    }

    // ── usage/timeseries (spec Section 27A.3) ────────────────────────────────

    private Task<object?> HandleUsageTimeseriesAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(AppServerMethods.UsageTimeseries);
        var p = GetParams<UsageTimeseriesParams>(msg);

        var from = ParseUsageDate(p.From, "from");
        var to = ParseUsageDate(p.To, "to");
        var tz = Math.Clamp(p.TzOffsetMinutes ?? 0, -840, 840);

        var buckets = traceStore.GetDailyUsage(from, to, tz);
        return Task.FromResult<object?>(new UsageTimeseriesResult
        {
            TzOffsetMinutes = tz,
            LongestTaskMs = traceStore.GetLongestTurnDurationMs(),
            Days = buckets
                .Select(b => new UsageTimeseriesDay
                {
                    Date = b.Date.ToString("yyyy-MM-dd"),
                    InputTokens = b.InputTokens,
                    OutputTokens = b.OutputTokens,
                    TotalTokens = b.TotalTokens,
                    SessionCount = b.SessionCount
                })
                .ToList()
        });
    }

    private static DateOnly? ParseUsageDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var date))
            throw AppServerErrors.InvalidParams($"'{field}' must be a 'YYYY-MM-DD' date.");
        return date;
    }

    // ── heartbeat/trigger (spec Section 17.2) ────────────────────────────────

    private async Task<object?> HandleHeartbeatTriggerAsync(
        AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (heartbeatService == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.HeartbeatTrigger);

        try
        {
            var result = await heartbeatService.TriggerNowAsync();
            return new HeartbeatTriggerResult { Result = result };
        }
        catch (Exception ex)
        {
            return new HeartbeatTriggerResult { Error = ex.Message };
        }
    }

    // ── skills/* (spec Section 18) ───────────────────────────────────────────

    private Task<object?> HandleSkillsListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsList);
        var p = GetParams<SkillsListParams>(msg);
        var includeUnavailable = p.IncludeUnavailable ?? true;
        var list = skillsLoader.ListSkills(filterUnavailable: !includeUnavailable);
        var wires = list.Select(MapSkillToWire).ToList();
        return Task.FromResult<object?>(new SkillsListResult { Skills = wires });
    }

    private Task<object?> HandleSkillsReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsRead);
        var p = GetParams<SkillsReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        var content = skillsLoader.LoadSkill(p.Name);
        if (content == null)
            throw AppServerErrors.SkillNotFound(p.Name);
        var metadata = skillsLoader.GetSkillMetadata(p.Name);
        return Task.FromResult<object?>(new SkillsReadResult
        {
            Name = p.Name,
            Content = content,
            Metadata = metadata
        });
    }

    private Task<object?> HandleSkillsViewAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsView);
        var p = GetParams<SkillsViewParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var target = BuildSkillVariantTarget();
        var effective = skillsLoader.LoadEffectiveSkill(
            p.Name,
            IsSkillVariantModeEnabled(),
            target);
        if (effective == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        return Task.FromResult<object?>(new SkillsViewResult
        {
            Name = p.Name,
            Content = effective.Content
        });
    }

    private Task<object?> HandleSkillsRestoreOriginalAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsRestoreOriginal);
        var p = GetParams<SkillsRestoreOriginalParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        if (skillsLoader.ResolveSkillInfo(p.Name) == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        var restored = IsSkillVariantModeEnabled()
            && skillsLoader.RestoreOriginalSkill(p.Name, BuildSkillVariantTarget());
        if (restored)
            MarkSkillsContextDirty();
        return Task.FromResult<object?>(new SkillsRestoreOriginalResult
        {
            Name = p.Name,
            Restored = restored
        });
    }

    private Task<object?> HandleSkillsSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsSetEnabled);
        var p = GetParams<SkillsSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var all = skillsLoader.ListSkills(filterUnavailable: false);
        if (all.All(s => !string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            throw AppServerErrors.SkillNotFound(p.Name);

        var disabled = all.Where(s => !s.Enabled).Select(s => s.Name).ToList();
        if (p.Enabled)
            disabled.RemoveAll(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(p.Name);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);
        MarkSkillsContextDirty();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SkillsSetEnabled,
            [ConfigChangeRegions.Skills]);

        var updated = skillsLoader.ListSkills(filterUnavailable: false)
            .First(s => string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SkillsSetEnabledResult { Skill = MapSkillToWire(updated) });
    }

    private Task<object?> HandleSkillsUninstallAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsUninstall);

        var p = GetParams<SkillsUninstallParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var source = skillsLoader.ResolveSkillInfo(p.Name);
        if (source == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        if (!string.Equals(source.Source, "workspace", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Source, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw AppServerErrors.InvalidParams(
                $"Skill '{source.Name}' is {source.Source} and cannot be uninstalled directly.");
        }

        var skillDir = Path.GetDirectoryName(source.Path);
        if (string.IsNullOrWhiteSpace(skillDir))
            throw AppServerErrors.InvalidParams($"Skill '{source.Name}' has an invalid path.");

        var allowedRoot = string.Equals(source.Source, "workspace", StringComparison.OrdinalIgnoreCase)
            ? skillsLoader.WorkspaceSkillsPath
            : skillsLoader.UserSkillsPath;
        if (!IsStrictChildPathOf(skillDir, allowedRoot))
            throw AppServerErrors.InvalidParams($"Skill '{source.Name}' is outside the allowed {source.Source} skill root.");

        var disabled = skillsLoader.ListSkills(filterUnavailable: false)
            .Where(s => !s.Enabled)
            .Select(s => s.Name)
            .ToList();
        disabled.RemoveAll(n => string.Equals(n, source.Name, StringComparison.OrdinalIgnoreCase));

        var removedVariantCount = skillsLoader.VariantStore.DeleteVariantsForSource(source);
        Directory.Delete(skillDir, recursive: true);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);
        skillsLoader.RefreshDescriptors();
        MarkSkillsContextDirty();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SkillsUninstall,
            [ConfigChangeRegions.Skills]);

        return Task.FromResult<object?>(new SkillsUninstallResult
        {
            Name = source.Name,
            Uninstalled = true,
            Source = source.Source,
            RemovedSourcePath = skillDir,
            RemovedVariantCount = removedVariantCount
        });
    }

    private Task<object?> HandlePluginListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginList);

        var p = GetParams<PluginListParams>(msg);
        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugins = discovery.Plugins
            .Where(plugin => p.IncludeDisabled != false || plugin.Enabled)
            .Select(plugin => MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries))
            .OrderBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<object?>(new PluginListResult
        {
            Plugins = plugins,
            Diagnostics = diagnostics.Select(MapPluginDiagnosticToWire).ToList()
        });
    }

    private Task<object?> HandlePluginViewAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginView);

        var p = GetParams<PluginViewParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(
            candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, p.Id));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{p.Id}' was not found.");

        return Task.FromResult<object?>(new PluginViewResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        });
    }

    private async Task<object?> HandlePluginSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginSetEnabled);

        var p = GetParams<PluginSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installed.");

        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        if (p.Enabled)
            disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        else if (!disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            disabled.Add(pluginId);

        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.PluginSetEnabled,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        MarkSkillsContextDirty();

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");

        var result = new PluginSetEnabledResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        var appListUpdate = TryBuildAppListUpdatedNotification(discovery, pluginId, p.Enabled ? "plugin/enable" : "plugin/disable");
        IReadOnlyList<ThreadAppBindingWire> offlineBindings = p.Enabled
            ? []
            : TryMoveActiveAppBindingsOfflineForPlugin(
                TryDiscoverAppBindingCatalog(),
                pluginId,
                "The owning plugin was disabled.",
                "binding.offline.pluginDisabled");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<object?> HandlePluginInstallAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginInstall);

        var p = GetParams<PluginInstallParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (beforePlugin.Installed)
            return new PluginInstallResult { Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeMcpSummaries, beforeLspSummaries) };
        if (!beforePlugin.Installable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installable.");

        var deployDiagnostics = new BuiltInPluginDeployer(Path.Combine(workspaceCraftPath, "plugins"), builtInPluginSourceRoots)
            .DeployPlugin(pluginId);
        PluginDiagnosticsStore.Shared.Append(deployDiagnostics);
        PluginDiagnosticsLogger.Write(deployDiagnostics);
        if (deployDiagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error || d.Code == "BuiltInPluginNotFound"))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.PluginInstall,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        MarkSkillsContextDirty();

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null || !plugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        var result = new PluginInstallResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            TryBuildAppListUpdatedNotification(discovery, pluginId, "plugin/install"),
            [],
            ct);
    }

    private async Task<object?> HandlePluginRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginRemove);

        var p = GetParams<PluginRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            return new PluginRemoveResult { Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeMcpSummaries, beforeLspSummaries) };
        if (!beforePlugin.Removable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");

        var pluginRoot = Path.GetFullPath(beforePlugin.Manifest.RootPath);
        var workspacePluginsRoot = Path.GetFullPath(Path.Combine(workspaceCraftPath, "plugins"));
        if (beforePlugin.SourceKind != PluginDiscoverySourceKind.Workspace
            || !IsStrictPathWithin(pluginRoot, workspacePluginsRoot))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");

        var removalCatalog = TryDiscoverAppBindingCatalog();
        var appListUpdate = TryBuildAppListUpdatedNotification(before, pluginId, "plugin/remove");

        Directory.Delete(pluginRoot, recursive: true);

        var current = appConfigMonitor?.Current ?? new AppConfig();
        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.PluginRemove,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        MarkSkillsContextDirty();

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        var result = new PluginRemoveResult
        {
            Plugin = plugin == null ? null : MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        var offlineBindings = TryMoveActiveAppBindingsOfflineForPlugin(
            removalCatalog,
            pluginId,
            "The owning plugin was removed.",
            "binding.offline.pluginRemoved");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<object?> MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
        AppServerIncomingMessage msg,
        object result,
        object? appListUpdatedParams,
        IReadOnlyList<ThreadAppBindingWire> offlineBindings,
        CancellationToken ct)
    {
        if (appListUpdatedParams == null && offlineBindings.Count == 0)
            return result;

        ReleaseAppContextPagesForBindings(offlineBindings);
        await transport.WriteMessageAsync(BuildResponse(msg.Id, result), ct);
        if (appListUpdatedParams != null)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = AppBindingAppListUpdatedNotification,
                @params = appListUpdatedParams
            }, ct);
        }

        foreach (var binding in offlineBindings)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = AppBindingThreadBindingsChangedNotification,
                @params = new
                {
                    threadId = binding.ThreadId,
                    bindingId = binding.BindingId,
                    appId = binding.AppId,
                    state = binding.State,
                    previousState = AppBindingStates.Active,
                    changeKind = "offline"
                }
            }, ct);
        }

        return null;
    }

    private void ReleaseAppContextPagesForBindings(IReadOnlyList<ThreadAppBindingWire> bindings)
    {
        if (contextPageManager == null || bindings.Count == 0)
            return;

        foreach (var threadId in bindings
                     .Select(binding => binding.ThreadId)
                     .Where(threadId => !string.IsNullOrWhiteSpace(threadId))
                     .Distinct(StringComparer.Ordinal))
        {
            contextPageManager.ReleaseStablePage(threadId, ContextPageKeys.AppContextBlocks());
        }
    }

    private object? TryBuildAppListUpdatedNotification(
        PluginDiscoveryResult discovery,
        string pluginId,
        string reason)
    {
        if (appBindingService == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath)
            || string.IsNullOrWhiteSpace(_hostWorkspacePath))
        {
            return null;
        }

        var pluginHasAppContribution = discovery.Plugins.Any(plugin =>
            PluginIds.EqualsCanonical(plugin.Manifest.Id, pluginId)
            && !string.IsNullOrWhiteSpace(plugin.Manifest.AppsPath));

        var appIds = TryResolveAppBindingAppIdsForPlugin(pluginId);
        if (!pluginHasAppContribution && appIds.Count == 0)
            return null;

        return new
        {
            pluginId,
            reason,
            appIds
        };
    }

    private IReadOnlyList<ThreadAppBindingWire> TryMoveActiveAppBindingsOfflineForPlugin(
        AppCatalogSnapshot? catalog,
        string pluginId,
        string diagnostic,
        string auditEvent)
    {
        if (appBindingService == null
            || catalog == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            return [];
        }

        var appIds = ResolveAppBindingAppIdsForPlugin(catalog, pluginId);
        return appIds.Count == 0
            ? []
            : appBindingService.MoveActiveBindingsOfflineForApps(catalog, workspaceCraftPath, appIds, diagnostic, auditEvent);
    }

    private List<string> TryResolveAppBindingAppIdsForPlugin(string pluginId)
    {
        var catalog = TryDiscoverAppBindingCatalog();
        return catalog == null ? [] : ResolveAppBindingAppIdsForPlugin(catalog, pluginId);
    }

    private AppCatalogSnapshot? TryDiscoverAppBindingCatalog()
    {
        if (appBindingService == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath)
            || string.IsNullOrWhiteSpace(_hostWorkspacePath))
        {
            return null;
        }

        try
        {
            return appBindingService.DiscoverCatalog(
                appConfigMonitor?.Current ?? LoadCurrentMergedConfig(),
                _hostWorkspacePath,
                workspaceCraftPath,
                skillsLoader,
                builtInPluginSourceRoots);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ResolveAppBindingAppIdsForPlugin(AppCatalogSnapshot catalog, string pluginId) =>
        catalog.Entries
            .Where(entry => PluginIds.EqualsCanonical(entry.Plugin.Manifest.Id, pluginId))
            .Select(entry => entry.Descriptor.AppId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(appId => appId, StringComparer.Ordinal)
            .ToList();

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

    private PluginDiscoveryResult RefreshPluginRuntime()
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        return PluginRuntimeConfigurator.ConfigureSkillsLoader(
            skillsLoader,
            current,
            _hostWorkspacePath
            ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
            ?? Directory.GetCurrentDirectory(),
            workspaceCraftPath ?? Path.Combine(Directory.GetCurrentDirectory(), ".craft"),
            PluginDiagnosticsStore.Shared,
            builtInPluginSourceRoots);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> BuildPluginMcpSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics) =>
        PluginMcpServerResolver.BuildPluginMcpServerSummaries(
            discovery.Plugins,
            GetWorkspaceMcpServersSnapshot(),
            diagnostics);

    private IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> BuildPluginLspSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics) =>
        PluginLspServerResolver.BuildPluginLspServerSummaries(
            discovery.Plugins,
            GetWorkspaceLspServersSnapshot(),
            diagnostics,
            lspToolEnabled: appConfigMonitor?.Current.Tools.Lsp.Enabled ?? false);

    private PluginInfoWire MapPluginToWire(
        DiscoveredPlugin plugin,
        IReadOnlyList<PluginDiagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> mcpSummaries,
        IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> lspSummaries)
    {
        var manifest = plugin.Manifest;
        var appDiagnostics = new List<PluginDiagnostic>();
        var apps = MapPluginAppsToWire(plugin, appDiagnostics);
        var desktopExtensionDiagnostics = new List<PluginDiagnostic>();
        var desktopExtensions = MapPluginDesktopExtensionsToWire(plugin, desktopExtensionDiagnostics);
        var pluginDiagnostics = diagnostics
            .Concat(appDiagnostics)
            .Concat(desktopExtensionDiagnostics)
            .Where(d => string.Equals(d.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            .Select(MapPluginDiagnosticToWire)
            .ToList();
        return new PluginInfoWire
        {
            Id = manifest.Id,
            DisplayName = manifest.Interface?.DisplayName ?? manifest.DisplayName,
            Description = manifest.Interface?.ShortDescription ?? manifest.Description,
            Version = manifest.Version,
            Enabled = plugin.Enabled,
            Installed = plugin.Installed,
            Installable = plugin.Installable,
            Removable = plugin.Removable,
            Source = plugin.SourceKind.ToString().ToLowerInvariant(),
            RootPath = manifest.RootPath,
            Interface = MapPluginInterfaceToWire(manifest.Interface),
            Functions = [],
            Skills = MapPluginSkillsToWire(plugin),
            Apps = apps,
            DesktopExtensions = desktopExtensions,
            McpServers = mcpSummaries.TryGetValue(manifest.Id, out var servers)
                ? servers.Select(MapPluginMcpServerToWire).ToList()
                : [],
            LspServers = lspSummaries.TryGetValue(manifest.Id, out var lspServers)
                ? lspServers.Select(MapPluginLspServerToWire).ToList()
                : [],
            Diagnostics = pluginDiagnostics
        };
    }

    private static List<PluginDesktopExtensionInfoWire> MapPluginDesktopExtensionsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        PluginDesktopExtensionCatalog.LoadPluginDesktopExtensions(plugin, diagnostics)
            .Select(extension => new PluginDesktopExtensionInfoWire
            {
                Id = extension.Id,
                DisplayName = extension.DisplayName,
                Description = extension.Description,
                Entry = extension.Entry,
                Styles = extension.Styles.ToList(),
                RequiredAppIds = extension.RequiredAppIds.ToList(),
                ConnectOrigins = extension.ConnectOrigins.ToList(),
                SurfaceWriteScopes = extension.SurfaceWriteScopes.ToList(),
                Surfaces = extension.Surfaces
                    .Select(surface => new PluginDesktopExtensionSurfaceWire
                    {
                        Type = surface.Type,
                        ViewId = surface.ViewId,
                        Label = surface.Label,
                        Placement = surface.Placement,
                        Order = surface.Order,
                        Title = surface.Title,
                        Description = surface.Description,
                        Slot = surface.Slot,
                        RendererId = surface.RendererId,
                        ActionId = surface.ActionId,
                        SettingsId = surface.SettingsId
                    })
                    .ToList()
            })
            .ToList();

    private static List<PluginAppInfoWire> MapPluginAppsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        AppBindingCatalog.LoadPluginAppDescriptors(plugin, diagnostics)
            .Select(app => new PluginAppInfoWire
            {
                AppId = app.AppId,
                ToolNamespace = app.ToolNamespace,
                DisplayName = app.DisplayName,
                DeveloperName = app.DeveloperName,
                Description = app.Description,
                Category = app.Category,
                Icon = TryReadDataUrl(app.Icon) ?? app.Icon,
                ReleasePage = app.ReleasePage,
                NativeApplication = app.NativeApplication == null
                    ? null
                    : new PluginAppNativeApplicationWire
                    {
                        DisplayName = app.NativeApplication.DisplayName,
                        Protocol = app.NativeApplication.Protocol,
                        InstallUrl = app.NativeApplication.InstallUrl
                    },
                ToolCatalog = app.ToolCatalog
                    .Select(tool => new PluginAppToolInfoWire
                    {
                        Name = tool.Name,
                        Scope = tool.Scope,
                        Risk = tool.Risk,
                        DefaultExposure = tool.DefaultExposure,
                        Description = tool.Description
                    })
                    .ToList(),
                DynamicToolCatalog = new PluginAppDynamicToolCatalogWire
                {
                    Enabled = app.DynamicToolCatalog.Enabled,
                    Description = app.DynamicToolCatalog.Description
                }
            })
            .ToList();

    private static PluginMcpServerInfoWire MapPluginMcpServerToWire(PluginMcpServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            ShadowedBy = server.ShadowedBy
        };

    private static PluginLspServerInfoWire MapPluginLspServerToWire(PluginLspServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            Extensions = [.. server.Extensions],
            ShadowedBy = server.ShadowedBy
        };

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsStrictPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !string.IsNullOrWhiteSpace(relative)
               && !relative.Equals(".", StringComparison.Ordinal)
               && IsPathWithin(path, root);
    }

    private PluginInterfaceWire? MapPluginInterfaceToWire(PluginInterfaceMetadata? metadata)
    {
        if (metadata == null)
            return null;

        return new PluginInterfaceWire
        {
            DisplayName = metadata.DisplayName,
            ShortDescription = metadata.ShortDescription,
            LongDescription = metadata.LongDescription,
            DeveloperName = metadata.DeveloperName,
            Category = metadata.Category,
            Capabilities = metadata.Capabilities.ToList(),
            DefaultPrompt = metadata.DefaultPrompt,
            BrandColor = metadata.BrandColor,
            ComposerIconDataUrl = TryReadDataUrl(metadata.ComposerIcon),
            LogoDataUrl = TryReadDataUrl(metadata.Logo),
            WebsiteUrl = metadata.WebsiteUrl,
            PrivacyPolicyUrl = metadata.PrivacyPolicyUrl,
            TermsOfServiceUrl = metadata.TermsOfServiceUrl
        };
    }

    private List<PluginSkillInfoWire> MapPluginSkillsToWire(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.SkillsPath) || !Directory.Exists(manifest.SkillsPath))
            return [];

        var allSkills = skillsLoader?.ListSkills(filterUnavailable: false) ?? new List<SkillsLoader.SkillInfo>();
        return Directory.GetDirectories(manifest.SkillsPath)
            .Where(dir => File.Exists(Path.Combine(dir, "SKILL.md")))
            .Select(dir =>
            {
                var name = Path.GetFileName(dir);
                var skill = allSkills.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                var interfaceInfo = skillsLoader?.GetSkillInterface(name);
                return new PluginSkillInfoWire
                {
                    Name = name,
                    Description = skillsLoader?.GetSkillDescription(name) ?? name,
                    DisplayName = interfaceInfo?.DisplayName,
                    ShortDescription = interfaceInfo?.ShortDescription,
                    Enabled = plugin.Installed
                              && plugin.Enabled
                              && (skill?.Enabled
                              ?? (!string.Equals(manifest.Id, PluginIds.Browser, StringComparison.OrdinalIgnoreCase)
                                  || (appConfigMonitor?.Current ?? new AppConfig()).Plugins.IsPluginEnabled(PluginIds.Browser, true)))
                };
            })
            .ToList();
    }

    private static PluginDiagnosticWire MapPluginDiagnosticToWire(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = diagnostic.PluginId,
            Path = diagnostic.Path
        };

    private static string? TryReadDataUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
        if (mimeType == null)
            return null;

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > 512 * 1024)
            return null;

        return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private SkillInfoWire MapSkillToWire(SkillsLoader.SkillInfo s)
    {
        var metadata = skillsLoader!.GetSkillMetadata(s.Name);
        var interfaceInfo = skillsLoader.GetSkillInterface(s.Name);
        return new SkillInfoWire
        {
            Name = s.Name,
            Description = skillsLoader.GetSkillDescription(s.Name),
            DisplayName = interfaceInfo?.DisplayName,
            ShortDescription = interfaceInfo?.ShortDescription,
            Source = s.Source,
            PluginId = s.PluginId,
            PluginDisplayName = s.PluginDisplayName,
            Available = s.Available,
            UnavailableReason = s.UnavailableReason,
            Enabled = s.Enabled,
            Path = s.Path,
            HasVariant = HasCurrentSkillVariant(s),
            IconSmallDataUrl = interfaceInfo?.IconSmallDataUrl,
            IconLargeDataUrl = interfaceInfo?.IconLargeDataUrl,
            DefaultPrompt = interfaceInfo?.DefaultPrompt,
            Metadata = metadata
        };
    }

    private bool HasCurrentSkillVariant(SkillsLoader.SkillInfo source)
    {
        if (skillsLoader == null || !IsSkillVariantModeEnabled())
            return false;

        var effectivePath = skillsLoader.ResolveEffectiveSkillFile(source, true, BuildSkillVariantTarget());
        return effectivePath != null
               && !string.Equals(effectivePath, source.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictChildPathOf(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSkillVariantModeEnabled()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        return string.Equals(
            config.Skills.SelfLearning.VariantMode,
            "enabled",
            StringComparison.OrdinalIgnoreCase);
    }

    private bool GoalsCapabilityEnabled()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        return config.Goals.Enabled;
    }

    private SkillVariantTarget BuildSkillVariantTarget()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        var model = ResolveMainModelOrFallback(config);
        return SkillVariantStore.CreateTarget(
            model,
            _hostWorkspacePath ?? workspaceCraftPath ?? string.Empty,
            config.Tools.Sandbox.Enabled,
            config.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
    }

    private string ResolveMainModelOrFallback(AppConfig config)
    {
        try
        {
            return _chatClientRegistry.ResolveMainModel(config);
        }
        catch (ArgumentException)
        {
            return config.Model;
        }
    }

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

    private static string? NormalizeWorkspaceModel(string? rawModel)
    {
        var trimmed = rawModel?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string? NormalizeDefaultApprovalPolicy(string? rawPolicy)
    {
        var trimmed = rawPolicy?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed switch
        {
            "default" or "Default" => "default",
            "autoApprove" or "AutoApprove" => "autoApprove",
            _ => throw AppServerErrors.InvalidParams("'defaultApprovalPolicy' must be 'default', 'autoApprove', or null.")
        };
    }

    private static ApprovalPolicy ToApprovalPolicy(string? rawPolicy)
    {
        return rawPolicy switch
        {
            "autoApprove" => ApprovalPolicy.AutoApprove,
            _ => ApprovalPolicy.Default
        };
    }

    private static WorkspaceConfigSaveResult SaveWorkspaceCoreConfig(
        string workspaceCraftPath,
        string? providerId,
        string? model,
        bool? welcomeSuggestionsEnabled,
        bool? skillsSelfLearningEnabled,
        bool? memoryAutoConsolidateEnabled,
        bool? dreamsEnabled,
        TimeSpan? dreamsInterval,
        int? dreamsThreadLookbackCount,
        bool? dreamsAutoApply,
        string? defaultApprovalPolicy,
        bool? toolsLspEnabled,
        AppConfig.ReasoningConfig? reasoning,
        bool updateProviderId,
        bool updateModel,
        bool updateWelcomeSuggestionsEnabled,
        bool updateSkillsSelfLearningEnabled,
        bool updateMemoryAutoConsolidateEnabled,
        bool updateDreamsEnabled,
        bool updateDreamsInterval,
        bool updateDreamsThreadLookbackCount,
        bool updateDreamsAutoApply,
        bool updateDefaultApprovalPolicy,
        bool updateToolsLspEnabled,
        bool updateReasoning)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var legacyLanguageKey = FindCaseInsensitiveKey(root, "Language");
        var legacyLanguageRemoved = legacyLanguageKey != null && root.Remove(legacyLanguageKey);

        var providerIdKey = FindCaseInsensitiveKey(root, "ProviderId");
        var modelKey = FindCaseInsensitiveKey(root, "Model");
        var welcomeSection = GetOrCreateConfigSection(root, "WelcomeSuggestions", createIfMissing: updateWelcomeSuggestionsEnabled);
        var welcomeEnabledKey = welcomeSection == null ? null : FindCaseInsensitiveKey(welcomeSection, "Enabled");
        var skillsSection = GetOrCreateConfigSection(root, "Skills", createIfMissing: updateSkillsSelfLearningEnabled);
        var selfLearningSection = skillsSection == null
            ? null
            : GetOrCreateConfigSection(skillsSection, "SelfLearning", createIfMissing: updateSkillsSelfLearningEnabled);
        var selfLearningEnabledKey = selfLearningSection == null ? null : FindCaseInsensitiveKey(selfLearningSection, "Enabled");
        var memorySection = GetOrCreateConfigSection(root, "Memory", createIfMissing: updateMemoryAutoConsolidateEnabled);
        var memoryAutoConsolidateEnabledKey = memorySection == null ? null : FindCaseInsensitiveKey(memorySection, "AutoConsolidateEnabled");
        var dreamsSection = GetOrCreateConfigSection(
            root,
            "Dreams",
            createIfMissing: updateDreamsEnabled || updateDreamsInterval || updateDreamsThreadLookbackCount || updateDreamsAutoApply);
        var dreamsEnabledKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "Enabled");
        var dreamsIntervalKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "Interval");
        var dreamsThreadLookbackCountKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "ThreadLookbackCount");
        var dreamsAutoApplyKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "AutoApply");
        var permissionsSection = GetOrCreateConfigSection(root, "Permissions", createIfMissing: updateDefaultApprovalPolicy);
        var defaultApprovalPolicyKey = permissionsSection == null ? null : FindCaseInsensitiveKey(permissionsSection, "DefaultApprovalPolicy");
        var toolsSection = GetOrCreateConfigSection(root, "Tools", createIfMissing: updateToolsLspEnabled);
        var lspSection = toolsSection == null
            ? null
            : GetOrCreateConfigSection(toolsSection, "Lsp", createIfMissing: updateToolsLspEnabled);
        var toolsLspEnabledKey = lspSection == null ? null : FindCaseInsensitiveKey(lspSection, "Enabled");
        var reasoningSection = GetOrCreateConfigSection(root, "Reasoning", createIfMissing: updateReasoning && reasoning != null);

        var existingProviderId = NormalizeOptionalString(ReadConfigStringValue(root, providerIdKey));
        var existingModel = NormalizeWorkspaceModel(ReadConfigStringValue(root, modelKey));
        var existingWelcomeSuggestionsEnabled = ReadConfigBooleanValue(welcomeSection, welcomeEnabledKey);
        var existingSkillsSelfLearningEnabled = ReadConfigBooleanValue(selfLearningSection, selfLearningEnabledKey);
        var existingMemoryAutoConsolidateEnabled = ReadConfigBooleanValue(memorySection, memoryAutoConsolidateEnabledKey);
        var existingDreamsEnabled = ReadConfigBooleanValue(dreamsSection, dreamsEnabledKey);
        var existingDreamsInterval = ReadConfigTimeSpanValue(dreamsSection, dreamsIntervalKey);
        var existingDreamsThreadLookbackCount = ReadConfigIntegerValue(dreamsSection, dreamsThreadLookbackCountKey);
        var existingDreamsAutoApply = ReadConfigBooleanValue(dreamsSection, dreamsAutoApplyKey);
        var existingDefaultApprovalPolicy = NormalizeDefaultApprovalPolicy(ReadConfigStringValue(permissionsSection, defaultApprovalPolicyKey));
        var existingToolsLspEnabled = ReadConfigBooleanValue(lspSection, toolsLspEnabledKey);
        var existingReasoning = ReadConfigReasoningValue(reasoningSection);

        var providerIdChanged = updateProviderId && !string.Equals(existingProviderId, providerId, StringComparison.Ordinal);
        var modelChanged = updateModel && !string.Equals(existingModel, model, StringComparison.Ordinal);
        var welcomeSuggestionsChanged = updateWelcomeSuggestionsEnabled
            && existingWelcomeSuggestionsEnabled != welcomeSuggestionsEnabled;
        var skillsSelfLearningChanged = updateSkillsSelfLearningEnabled
            && existingSkillsSelfLearningEnabled != skillsSelfLearningEnabled;
        var memoryAutoConsolidateChanged = updateMemoryAutoConsolidateEnabled
            && existingMemoryAutoConsolidateEnabled != memoryAutoConsolidateEnabled;
        var dreamsEnabledChanged = updateDreamsEnabled
            && existingDreamsEnabled != dreamsEnabled;
        var dreamsIntervalChanged = updateDreamsInterval
            && existingDreamsInterval != dreamsInterval;
        var dreamsThreadLookbackCountChanged = updateDreamsThreadLookbackCount
            && existingDreamsThreadLookbackCount != dreamsThreadLookbackCount;
        var dreamsAutoApplyChanged = updateDreamsAutoApply
            && existingDreamsAutoApply != dreamsAutoApply;
        var defaultApprovalPolicyChanged = updateDefaultApprovalPolicy
            && !string.Equals(existingDefaultApprovalPolicy, defaultApprovalPolicy, StringComparison.Ordinal);
        var toolsLspEnabledChanged = updateToolsLspEnabled
            && existingToolsLspEnabled != toolsLspEnabled;
        var reasoningChanged = updateReasoning
            && !ReasoningConfigEquals(existingReasoning, reasoning);

        if (updateProviderId)
            UpsertOrRemoveConfigValue(root, providerIdKey, "ProviderId", providerId);
        if (updateModel)
            UpsertOrRemoveConfigValue(root, modelKey, "Model", model);
        if (updateWelcomeSuggestionsEnabled)
        {
            var section = GetOrCreateConfigSection(root, "WelcomeSuggestions", createIfMissing: true)!;
            var sectionEnabledKey = FindCaseInsensitiveKey(section, "Enabled");
            UpsertOrRemoveConfigValue(section, sectionEnabledKey, "Enabled", welcomeSuggestionsEnabled);
            RemoveConfigSectionIfEmpty(root, "WelcomeSuggestions");
        }
        if (updateSkillsSelfLearningEnabled)
        {
            var skills = GetOrCreateConfigSection(root, "Skills", createIfMissing: true)!;
            var selfLearning = GetOrCreateConfigSection(skills, "SelfLearning", createIfMissing: true)!;
            var selfLearningEnabledExistingKey = FindCaseInsensitiveKey(selfLearning, "Enabled");
            UpsertOrRemoveConfigValue(selfLearning, selfLearningEnabledExistingKey, "Enabled", skillsSelfLearningEnabled);
            RemoveConfigSectionIfEmpty(skills, "SelfLearning");
            RemoveConfigSectionIfEmpty(root, "Skills");
        }
        if (updateMemoryAutoConsolidateEnabled)
        {
            var memory = GetOrCreateConfigSection(root, "Memory", createIfMissing: true)!;
            var autoConsolidateExistingKey = FindCaseInsensitiveKey(memory, "AutoConsolidateEnabled");
            UpsertOrRemoveConfigValue(memory, autoConsolidateExistingKey, "AutoConsolidateEnabled", memoryAutoConsolidateEnabled);
            RemoveConfigSectionIfEmpty(root, "Memory");
        }
        if (updateDreamsEnabled || updateDreamsInterval || updateDreamsThreadLookbackCount || updateDreamsAutoApply)
        {
            var dreams = GetOrCreateConfigSection(root, "Dreams", createIfMissing: true)!;
            if (updateDreamsEnabled)
            {
                var enabledExistingKey = FindCaseInsensitiveKey(dreams, "Enabled");
                UpsertOrRemoveConfigValue(dreams, enabledExistingKey, "Enabled", dreamsEnabled);
            }
            if (updateDreamsInterval)
            {
                var intervalExistingKey = FindCaseInsensitiveKey(dreams, "Interval");
                UpsertOrRemoveConfigValue(dreams, intervalExistingKey, "Interval", FormatTimeSpanForConfig(dreamsInterval));
            }
            if (updateDreamsThreadLookbackCount)
            {
                var threadLookbackExistingKey = FindCaseInsensitiveKey(dreams, "ThreadLookbackCount");
                UpsertOrRemoveConfigValue(dreams, threadLookbackExistingKey, "ThreadLookbackCount", dreamsThreadLookbackCount);
            }
            if (updateDreamsAutoApply)
            {
                var autoApplyExistingKey = FindCaseInsensitiveKey(dreams, "AutoApply");
                UpsertOrRemoveConfigValue(dreams, autoApplyExistingKey, "AutoApply", dreamsAutoApply);
            }
            RemoveConfigSectionIfEmpty(root, "Dreams");
        }
        if (updateDefaultApprovalPolicy)
        {
            var permissions = GetOrCreateConfigSection(root, "Permissions", createIfMissing: true)!;
            var defaultApprovalPolicyExistingKey = FindCaseInsensitiveKey(permissions, "DefaultApprovalPolicy");
            UpsertOrRemoveConfigValue(permissions, defaultApprovalPolicyExistingKey, "DefaultApprovalPolicy", defaultApprovalPolicy);
            RemoveConfigSectionIfEmpty(root, "Permissions");
        }
        if (updateToolsLspEnabled)
        {
            var tools = GetOrCreateConfigSection(root, "Tools", createIfMissing: true)!;
            var lsp = GetOrCreateConfigSection(tools, "Lsp", createIfMissing: true)!;
            var enabledExistingKey = FindCaseInsensitiveKey(lsp, "Enabled");
            UpsertOrRemoveConfigValue(lsp, enabledExistingKey, "Enabled", toolsLspEnabled);
            RemoveConfigSectionIfEmpty(tools, "Lsp");
            RemoveConfigSectionIfEmpty(root, "Tools");
        }
        if (updateReasoning)
        {
            if (reasoning == null)
            {
                RemoveConfigSection(root, "Reasoning");
            }
            else
            {
                var section = GetOrCreateConfigSection(root, "Reasoning", createIfMissing: true)!;
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Enabled"), "Enabled", reasoning.Enabled);
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Effort"), "Effort", reasoning.Effort.ToString());
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Output"), "Output", reasoning.Output.ToString());
            }
        }

        if (providerIdChanged
            || modelChanged
            || welcomeSuggestionsChanged
            || skillsSelfLearningChanged
            || memoryAutoConsolidateChanged
            || dreamsEnabledChanged
            || dreamsIntervalChanged
            || dreamsThreadLookbackCountChanged
            || dreamsAutoApplyChanged
            || defaultApprovalPolicyChanged
            || toolsLspEnabledChanged
            || reasoningChanged
            || legacyLanguageRemoved)
        {
            WriteConfigObject(configPath, root);
        }

        return new WorkspaceConfigSaveResult
        {
            ProviderId = updateProviderId ? providerId : existingProviderId,
            Model = updateModel ? model : existingModel,
            WelcomeSuggestionsEnabled = updateWelcomeSuggestionsEnabled
                ? welcomeSuggestionsEnabled
                : existingWelcomeSuggestionsEnabled,
            SkillsSelfLearningEnabled = updateSkillsSelfLearningEnabled
                ? skillsSelfLearningEnabled
                : existingSkillsSelfLearningEnabled,
            MemoryAutoConsolidateEnabled = updateMemoryAutoConsolidateEnabled
                ? memoryAutoConsolidateEnabled
                : existingMemoryAutoConsolidateEnabled,
            DreamsEnabled = updateDreamsEnabled
                ? dreamsEnabled
                : existingDreamsEnabled,
            DreamsInterval = updateDreamsInterval
                ? FormatTimeSpanForConfig(dreamsInterval)
                : FormatTimeSpanForConfig(existingDreamsInterval),
            DreamsThreadLookbackCount = updateDreamsThreadLookbackCount
                ? dreamsThreadLookbackCount
                : existingDreamsThreadLookbackCount,
            DreamsAutoApply = updateDreamsAutoApply
                ? dreamsAutoApply
                : existingDreamsAutoApply,
            DefaultApprovalPolicy = updateDefaultApprovalPolicy
                ? defaultApprovalPolicy
                : existingDefaultApprovalPolicy,
            ToolsLspEnabled = updateToolsLspEnabled
                ? toolsLspEnabled
                : existingToolsLspEnabled,
            Reasoning = updateReasoning
                ? CloneNullableReasoningConfig(reasoning)
                : CloneNullableReasoningConfig(existingReasoning),
            ProviderIdChanged = providerIdChanged,
            ModelChanged = modelChanged,
            WelcomeSuggestionsChanged = welcomeSuggestionsChanged,
            SkillsSelfLearningChanged = skillsSelfLearningChanged,
            MemoryAutoConsolidateChanged = memoryAutoConsolidateChanged,
            DreamsChanged = dreamsEnabledChanged || dreamsIntervalChanged || dreamsThreadLookbackCountChanged || dreamsAutoApplyChanged,
            DefaultApprovalPolicyChanged = defaultApprovalPolicyChanged,
            ToolsLspEnabledChanged = toolsLspEnabledChanged,
            ReasoningChanged = reasoningChanged
        };
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }

        return null;
    }

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value;
    }

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        bool? value)
    {
        if (!value.HasValue)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value.Value;
    }

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        int? value)
    {
        if (!value.HasValue)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value.Value;
    }

    private static void WriteConfigObject(string configPath, JsonObject root)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
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

    private static AppConfig.ReasoningConfig? ParseNullableReasoningConfig(
        JsonElement element,
        string fieldName,
        AppConfig.ReasoningConfig fallback)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an object or null.");

        var hasEnabled = TryGetCaseInsensitiveProperty(element, "enabled", out var enabledEl);
        var hasEffort = TryGetCaseInsensitiveProperty(element, "effort", out var effortEl);
        var hasOutput = TryGetCaseInsensitiveProperty(element, "output", out var outputEl);
        if (!hasEnabled && !hasEffort && !hasOutput)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must contain at least one of 'enabled', 'effort', or 'output'.");

        var enabled = hasEnabled ? ParseNullableBoolean(enabledEl, $"{fieldName}.enabled") : null;
        var effort = hasEffort ? ParseNullableReasoningEffort(effortEl, $"{fieldName}.effort") : null;
        var output = hasOutput ? ParseNullableReasoningOutput(outputEl, $"{fieldName}.output") : null;

        return new AppConfig.ReasoningConfig
        {
            Enabled = enabled ?? (hasEffort ? true : fallback.Enabled),
            Effort = effort ?? fallback.Effort,
            Output = output ?? fallback.Output
        };
    }

    private static ReasoningEffort? ParseNullableReasoningEffort(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a reasoning effort string or null.");

        var raw = element.GetString();
        if (!ModelThinkingAdapterCatalog.TryParseReasoningEffort(raw, out var effort))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be one of 'low', 'medium', 'high', or 'extraHigh'.");

        return effort;
    }

    private static ReasoningOutput? ParseNullableReasoningOutput(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a reasoning output string or null.");

        var raw = element.GetString();
        if (!ModelThinkingAdapterCatalog.TryParseReasoningOutput(raw, out var output))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be one of 'none', 'summary', or 'full'.");

        return output;
    }

    private static int? ParseNullablePositiveInt32(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value)
            || value <= 0)
        {
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive integer or null.");
        }

        return value;
    }

    private static TimeSpan? ParseNullablePositiveTimeSpan(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive TimeSpan string or null.");

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw)
            || !TryParseWireTimeSpan(raw.Trim(), out var value)
            || value <= TimeSpan.Zero)
        {
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive TimeSpan string or null.");
        }

        return value;
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

    private static string? ReadConfigStringValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<string>(out var result) ? result : null;
    }

    private static bool? ReadConfigBooleanValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<bool>(out var result) ? result : null;
    }

    private static AppConfig.ReasoningConfig? ReadConfigReasoningValue(JsonObject? root)
    {
        if (root == null)
            return null;

        var enabled = ReadConfigBooleanValue(root, FindCaseInsensitiveKey(root, "Enabled"));
        var effortRaw = ReadConfigStringValue(root, FindCaseInsensitiveKey(root, "Effort"));
        var outputRaw = ReadConfigStringValue(root, FindCaseInsensitiveKey(root, "Output"));
        var hasEffort = ModelThinkingAdapterCatalog.TryParseReasoningEffort(effortRaw, out var effort);
        var hasOutput = ModelThinkingAdapterCatalog.TryParseReasoningOutput(outputRaw, out var output);
        if (!enabled.HasValue && !hasEffort && !hasOutput)
            return null;

        return new AppConfig.ReasoningConfig
        {
            Enabled = enabled ?? false,
            Effort = hasEffort ? effort : ReasoningEffort.Medium,
            Output = hasOutput ? output : ReasoningOutput.Full
        };
    }

    private static AppConfig.ReasoningConfig CloneReasoningConfig(AppConfig.ReasoningConfig source) => new()
    {
        Enabled = source.Enabled,
        Effort = source.Effort,
        Output = source.Output
    };

    private static AppConfig.ReasoningConfig? CloneNullableReasoningConfig(AppConfig.ReasoningConfig? source) =>
        source == null ? null : CloneReasoningConfig(source);

    private static bool ReasoningConfigEquals(AppConfig.ReasoningConfig? left, AppConfig.ReasoningConfig? right)
    {
        if (left == null || right == null)
            return left == null && right == null;

        return left.Enabled == right.Enabled
               && left.Effort == right.Effort
               && left.Output == right.Output;
    }

    private static int? ReadConfigIntegerValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<int>(out var result) ? result : null;
    }

    private static TimeSpan? ReadConfigTimeSpanValue(JsonObject? root, string? key)
    {
        var raw = ReadConfigStringValue(root, key);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return TryParseWireTimeSpan(raw.Trim(), out var result) && result > TimeSpan.Zero
            ? result
            : null;
    }

    private static string? FormatTimeSpanForConfig(TimeSpan? value) =>
        value.HasValue ? value.Value.ToString("c", CultureInfo.InvariantCulture) : null;

    private static string FormatTimeSpanForWire(TimeSpan value)
    {
        var normalized = value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;
        var totalSeconds = (long)Math.Round(normalized.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    private static bool TryParseWireTimeSpan(string raw, out TimeSpan value)
    {
        if (TryParseTotalHoursTimeSpan(raw, out value))
            return true;
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseTotalHoursTimeSpan(string raw, out TimeSpan value)
    {
        value = default;
        var parts = raw.Split(':');
        if (parts.Length != 3)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || hours < 0
            || minutes is < 0 or > 59
            || seconds is < 0 or > 59)
        {
            return false;
        }

        value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static JsonObject? GetOrCreateConfigSection(JsonObject root, string canonicalKey, bool createIfMissing)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey != null)
        {
            if (root[existingKey] is JsonObject existingObject)
                return existingObject;
            if (!createIfMissing)
                return null;

            var replacement = new JsonObject();
            root[existingKey] = replacement;
            return replacement;
        }

        if (!createIfMissing)
            return null;

        var section = new JsonObject();
        root[canonicalKey] = section;
        return section;
    }

    private static void RemoveConfigSectionIfEmpty(JsonObject root, string canonicalKey)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey == null)
            return;

        if (root[existingKey] is JsonObject obj && obj.Count == 0)
            root.Remove(existingKey);
    }

    private static void RemoveConfigSection(JsonObject root, string canonicalKey)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey != null)
            root.Remove(existingKey);
    }

    private sealed class WorkspaceConfigSaveResult
    {
        public string? ProviderId { get; init; }

        public string? Model { get; init; }

        public bool? WelcomeSuggestionsEnabled { get; init; }

        public bool? SkillsSelfLearningEnabled { get; init; }

        public bool? MemoryAutoConsolidateEnabled { get; init; }

        public bool? DreamsEnabled { get; init; }

        public string? DreamsInterval { get; init; }

        public int? DreamsThreadLookbackCount { get; init; }

        public bool? DreamsAutoApply { get; init; }

        public string? DefaultApprovalPolicy { get; init; }

        public bool? ToolsLspEnabled { get; init; }

        public AppConfig.ReasoningConfig? Reasoning { get; init; }

        public bool ProviderIdChanged { get; init; }

        public bool ModelChanged { get; init; }

        public bool WelcomeSuggestionsChanged { get; init; }

        public bool SkillsSelfLearningChanged { get; init; }

        public bool MemoryAutoConsolidateChanged { get; init; }

        public bool DreamsChanged { get; init; }

        public bool DefaultApprovalPolicyChanged { get; init; }

        public bool ToolsLspEnabledChanged { get; init; }

        public bool ReasoningChanged { get; init; }
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

    private Task<object?> RouteAutomation(Func<IAutomationsRequestHandler, Task<object?>> action)
    {
        if (automationsHandler == null)
            throw AppServerErrors.MethodNotFound("automation/*");
        return action(automationsHandler);
    }

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    // ── auth/openai/* ────────────────────────────────────────────────

    private Task<object?> HandleAuthOpenAiStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        return Task.FromResult<object?>(BuildAuthStatusResult(auth.GetStatus(), providerId: null));
    }

    private async Task<object?> HandleAuthOpenAiLoginAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = msg.Params is null ? new AuthOpenAiLoginParams() : GetParams<AuthOpenAiLoginParams>(msg);
        var providerId = string.IsNullOrWhiteSpace(p.ProviderId) ? "openai" : p.ProviderId.Trim();
        var openBrowser = p.OpenBrowser ?? true;

        OpenAIAuthStatus status;
        try
        {
            status = await auth.LoginAsync(
                openBrowser,
                onAuthorizationUrl: url =>
                {
                    // Fire-and-forget notification so the renderer can render "Copy URL" affordance.
                    _ = transport.WriteMessageAsync(new
                    {
                        jsonrpc = "2.0",
                        method = AppServerMethods.AuthOpenAiAuthorizeUrl,
                        @params = new AuthOpenAiAuthorizeUrlNotification
                        {
                            Url = url,
                            CallbackPort = OpenAIAuthConstants.RedirectPortPrimary
                        }
                    }, ct);
                },
                ct).ConfigureAwait(false);
        }
        catch (OpenAIAuthException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }

        try
        {
            // Pin the write to the same global config the AppConfig loader is reading from —
            // if it differs from the default, BindProviderToOAuth would otherwise write to a
            // path the in-memory merged config never re-reads, and turn/start would keep seeing
            // a stale (or absent) Providers[id] entry.
            OpenAIAuthBindingPersistence.BindProviderToOAuth(providerId, status, GetEffectiveGlobalConfigPath());
        }
        catch (Exception ex)
        {
            throw AppServerErrors.InvalidRequest($"Login succeeded but provider config could not be updated: {ex.Message}");
        }

        // BindProviderToOAuth wrote new Provider data to the global config. Refresh the
        // in-memory merged config so the next turn/start sees AuthMethod=chatgptOAuth instead of
        // the stale (or absent) entry — otherwise the resolver would still see the old apiKey
        // shape and throw "API key must be configured." Mirror what provider/create does.
        RefreshCurrentLlmConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(AppServerMethods.AuthOpenAiLogin, [ConfigChangeRegions.ProviderRegistry]);

        return BuildAuthStatusResult(status, providerId);
    }

    private async Task<object?> HandleAuthOpenAiLogoutAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = msg.Params is null ? new AuthOpenAiLogoutParams() : GetParams<AuthOpenAiLogoutParams>(msg);
        var providerId = string.IsNullOrWhiteSpace(p.ProviderId) ? "openai" : p.ProviderId.Trim();

        await auth.LogoutAsync(ct).ConfigureAwait(false);
        try
        {
            OpenAIAuthBindingPersistence.UnbindProvider(providerId, GetEffectiveGlobalConfigPath());
        }
        catch (Exception)
        {
            // Best-effort — the OAuth tokens are already deleted, which is the user's primary intent.
        }

        // Mirror the login path: refresh in-memory config so the resolver picks up the reverted
        // AuthMethod immediately instead of continuing to think the provider is OAuth-bound.
        RefreshCurrentLlmConfig();
        InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(AppServerMethods.AuthOpenAiLogout, [ConfigChangeRegions.ProviderRegistry]);

        return new AuthOpenAiStatusResult { LoggedIn = false, ProviderId = providerId };
    }

    private static AuthOpenAiStatusResult BuildAuthStatusResult(OpenAIAuthStatus status, string? providerId) =>
        new()
        {
            LoggedIn = status.LoggedIn,
            AccountId = status.AccountId,
            PlanType = status.PlanType,
            Email = status.Email,
            LastRefresh = status.LastRefresh,
            AccessTokenExpiresAt = status.AccessTokenExpiresAt,
            ProviderId = providerId
        };

    private async Task<object?> HandleAuthOpenAiUsageAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var usage = openAIUsageService;
        if (usage is null)
            throw AppServerErrors.InvalidRequest("ChatGPT usage telemetry is not available in this server build.");

        var snapshot = usage.CurrentSnapshot;
        if (snapshot is null)
        {
            // First call from a freshly-connected client: trigger an inline refresh so the
            // renderer can render a value on initial paint.
            snapshot = await usage.RefreshAsync(ct).ConfigureAwait(false);
        }

        return OpenAIUsageMapping.ToWire(snapshot);
    }

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
