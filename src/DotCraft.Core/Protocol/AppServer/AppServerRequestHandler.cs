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
    private AppServerThreadBinder? _threadBinder;
    private AppServerResponseWriter? _responseWriter;
    private AppServerThreadWireProjector? _threadProjector;

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

    private AppServerThreadBinder ThreadBinder => _threadBinder ??=
        new AppServerThreadBinder(
            sessionService,
            connection,
            transport,
            wireAcpExtensionProxy,
            wireNodeReplProxy,
            wireDynamicToolProxy,
            wireRuntimeAdditionalContextProvider,
            contextPageManager);

    private AppServerResponseWriter ResponseWriter => _responseWriter ??= new AppServerResponseWriter(transport);

    private AppServerThreadWireProjector ThreadProjector => _threadProjector ??=
        new AppServerThreadWireProjector(
            sessionService,
            connection,
            appConfigMonitor,
            WorkspaceConfig,
            skillsLoader,
            planStore,
            appBindingService,
            builtInPluginSourceRoots);

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
        new CommandRequestHandler(_commandRegistry, sessionService, connection, services.HeartbeatService, services.CronService, (thread, token) => ThreadProjector.EnrichAsync(thread.ToWire(), thread, token)),
        new ThreadRequestHandler(sessionService, connection, transport, ResponseWriter, ThreadBinder, ThreadProjector, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, services.StreamDebugLogger, _defaultApprovalDecision),
        new TurnRequestHandler(sessionService, connection, transport, ResponseWriter, _commandRegistry, services.SkillsLoader, SkillVariants, services.TraceStore, services.StreamDebugLogger, _defaultApprovalDecision, ThreadProjector),
        new WorktreeRequestHandler(sessionService, ResponseWriter, ThreadBinder, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, ThreadProjector.ProjectAsync),
        new SubAgentRequestHandler(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, RuntimeConfig, (thread, wire, token) => ThreadProjector.EnrichAsync(wire, thread, token), services.SubAgentCoordinatorFactory),
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
            ThreadGoals = ThreadProjector.GoalsCapabilityEnabled(),
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
            SkillVariants = skillsLoader != null && SkillVariants.IsVariantModeEnabled(),
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
