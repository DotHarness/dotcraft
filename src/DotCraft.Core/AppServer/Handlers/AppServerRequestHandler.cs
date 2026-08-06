using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.AppServer;

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
    private readonly CommandRegistry _commandRegistry = services.CommandRegistry
                                                        ?? CommandRegistry.CreateDefault(
                                                            !string.IsNullOrWhiteSpace(services.WorkspaceCraftPath) ? new CustomCommandLoader(services.WorkspaceCraftPath) : null);

    private readonly IReadOnlyList<ConfigSchemaSection> _configSchema = services.ConfigSchema ?? [];

    private readonly ChatClientRegistry _chatClientRegistry = services.ChatClientRegistry
                                                              ?? new ChatClientRegistry(
                                                                  services.ModelProviderRegistry
                                                                  ?? new ModelProviderRegistry([]));

    /// <summary>
    /// Fallback decision used by <see cref="AppServerEventDispatcher"/> when non-interactive
    /// approval resolution cannot be derived from thread policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision = services.DefaultApprovalDecision;

    private readonly IReadOnlyDictionary<string, IAppServerMethodHandler> _extensionMethods =
        BuildExtensionMethodMap(services.ProtocolExtensions);

    private static readonly IReadOnlyDictionary<string, IRpcMethodDescriptor> BundledExtensionRequests =
        Contract.AppServerRpcCatalog.All
            .Where(static descriptor => descriptor.Module != "core" &&
                                        descriptor.Kind == "request" &&
                                        descriptor.Direction == RpcDirection.ClientToServer)
            .ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

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
        new SkillVariantContext(services.AppConfigMonitor, _chatClientRegistry, services.HostWorkspacePath, services.WorkspaceCraftPath);

    private WorkspaceConfigEditor WorkspaceConfig => _workspaceConfig ??=
        new WorkspaceConfigEditor(services.AppConfigMonitor, services.WorkspaceCraftPath);

    private AppServerMcpConfigService McpConfig => _mcpConfig ??=
        new AppServerMcpConfigService(services.AppConfigMonitor, services.McpClientManager, services.HostWorkspacePath, services.WorkspaceCraftPath);

    private ExternalChannelConfigService ExternalChannelConfig => _externalChannelConfig ??=
        new ExternalChannelConfigService(channelListContributor, services.WorkspaceCraftPath);

    private AppServerRuntimeConfigRefresher RuntimeConfig => _runtimeConfig ??=
        new AppServerRuntimeConfigRefresher(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, WorkspaceConfig);

    private AppServerThreadBinder ThreadBinder => _threadBinder ??=
        new AppServerThreadBinder(
            sessionService,
            connection,
            transport,
            services.WireAcpExtensionProxy,
            services.WireNodeReplProxy,
            services.WireDynamicToolProxy,
            services.WireRuntimeAdditionalContextProvider,
            services.InlineVisualizationRuntimeRegistry);

    private AppServerResponseWriter ResponseWriter => _responseWriter ??= new AppServerResponseWriter(transport);

    private AppServerThreadWireProjector ThreadProjector => _threadProjector ??=
        new AppServerThreadWireProjector(
            sessionService,
            connection,
            services.AppConfigMonitor,
            WorkspaceConfig,
            services.SkillsLoader,
            services.PlanStore,
            services.AppBindingService,
            services.ThreadOriginPresentationProviders,
            services.BuiltInPluginSourceRoots,
            sessionService as IThreadToolSnapshotService,
            sessionService as IThreadMcpRuntimeService);

    private AppServerMethodTable? _domainMethods;
    private AppServerNotificationTable? _clientNotifications;

    /// <summary>
    /// Methods owned by built-in domain handlers. The dispatcher resolves a request against this
    /// table before protocol extensions. Built lazily so domain handlers can capture the
    /// connection-scoped services initialized above.
    /// </summary>
    private AppServerMethodTable DomainMethods => _domainMethods ??= BuildDomainMethods();

    private AppServerNotificationTable ClientNotifications => _clientNotifications ??= BuildClientNotifications();

    private AppServerMethodTable BuildDomainMethods()
    {
        var table = new AppServerMethodTable();
        foreach (var handler in BuildDomainHandlers())
            handler.RegisterMethods(table);
        return table;
    }

    private AppServerNotificationTable BuildClientNotifications()
    {
        var table = new AppServerNotificationTable();
        table.Map(Contract.AppServerRpc.Initialized, _ => connection.MarkClientReady());
        return table;
    }

    private IEnumerable<IAppServerDomainHandler> BuildDomainHandlers() =>
    [
        new InitializeRequestHandler(connection, services, _capabilityContributors, SkillVariants, ThreadProjector),
        new CronRequestHandler(services.CronService, services.HeartbeatService, services.BroadcastCronStateChanged),
        new TerminalRequestHandler(services.BackgroundTerminalService, sessionService),
        new DreamsRequestHandler(services.DreamsService, services.DreamStore, services.AppConfigMonitor, services.WorkspaceCraftPath, services.ContextPageManager),
        new SkillsRequestHandler(services.SkillsLoader, services.ContextPageManager, services.AppConfigMonitor, services.WorkspaceCraftPath, SkillVariants),
        new ToolRequestHandler(),
        new McpRequestHandler(services.McpClientManager, McpConfig, transport, services.AppConfigMonitor, services.BroadcastMcpStatusChanged, sessionService as IThreadToolDispatchService, sessionService as IThreadMcpRuntimeService, sessionService as IThreadAgentRefreshService),
        new McpAppRequestHandler(
            sessionService,
            connection,
            transport,
            sessionService as IThreadToolDispatchService,
            sessionService as IThreadToolSnapshotService,
            sessionService as IThreadMcpRuntimeService,
            services.McpAppTransientContextStore),
        new InlineVisualizationRequestHandler(
            sessionService,
            connection,
            services.InlineVisualizationAssetStore,
            services.InlineVisualizationRuntimeRegistry),
        new ChannelRequestHandler(channelListContributor, services.ChannelStatusProvider, WorkspaceConfig, ExternalChannelConfig, services.WorkspaceCraftPath, services.AppConfigMonitor, services.OnExternalChannelUpserted, services.OnExternalChannelRemoved, services.ExternalChannelLogProvider),
        new ProviderRequestHandler(
            transport,
            WorkspaceConfig,
            RuntimeConfig,
            services.WorkspaceCraftPath,
            services.AppConfigMonitor,
            services.ModelProviderRegistry),
        new WorkspaceRequestHandler(services.CommitMessageSuggest, services.WelcomeSuggestionService, _configSchema, services.MemoryStore, services.DreamStore, services.DreamsService, services.LspServerManager, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, services.ContextPageManager, WorkspaceConfig, RuntimeConfig),
        new SourceControlRequestHandler(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath),
        new HookRequestHandler(services.HookRunner, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, WorkspaceConfig, RuntimeConfig, services.BuiltInPluginSourceRoots),
        new PluginRequestHandler(transport, services.SkillsLoader, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, services.BuiltInPluginSourceRoots, McpConfig, services.LspServerManager, services.ContextPageManager, services.AppBindingService, WorkspaceConfig, RuntimeConfig, services.HookRunner),
        new AgentProfileRequestHandler(sessionService, services.WorkspaceCraftPath, services.HostWorkspacePath, services.AppConfigMonitor),
        new CommandRequestHandler(_commandRegistry, sessionService, connection, services.HeartbeatService, services.CronService, services.WorkspaceCraftPath, (thread, token) => ThreadProjector.EnrichAsync(thread.ToWire(), thread, token)),
        new ThreadRequestHandler(sessionService, connection, transport, ResponseWriter, ThreadBinder, ThreadProjector, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, services.WorkspaceCraftPath, services.StreamDebugLogger, _defaultApprovalDecision, services.LoggerFactory?.CreateLogger<ThreadRequestHandler>()),
        new TurnRequestHandler(sessionService, connection, transport, ResponseWriter, _commandRegistry, services.SkillsLoader, SkillVariants, services.TraceStore, services.StreamDebugLogger, _defaultApprovalDecision, ThreadProjector),
        new WorktreeRequestHandler(sessionService, ResponseWriter, ThreadBinder, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, ThreadProjector.ProjectAsync),
        new SubAgentRequestHandler(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, RuntimeConfig, (thread, wire, token) => ThreadProjector.EnrichAsync(wire, thread, token), services.SubAgentCoordinatorFactory),
        new UsageRequestHandler(services.TraceStore, services.SkillsLoader, sessionService, services.HostWorkspacePath),
        new AutomationRequestHandler(services.AutomationsHandler),
    ];

    private static readonly HashSet<string> ReservedMethodNames =
        Contract.AppServerRpcCatalog.All
            .Where(static descriptor => descriptor is
            {
                Kind: "request",
                Direction: RpcDirection.ClientToServer,
                Module: "core" or "automations" or "external-channel"
            })
            .Select(static descriptor => descriptor.Name)
            .ToHashSet(StringComparer.Ordinal);

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
        if (method != Contract.AppServerRpc.Initialize.Name && !connection.IsInitialized)
            throw AppServerErrors.NotInitialized();

        // After initialize response, block all requests until the client sends the
        // `initialized` notification (IsClientReady). This prevents premature operations
        // before the client has finished processing server capabilities.
        if (method != Contract.AppServerRpc.Initialize.Name && connection.IsInitialized && !connection.IsClientReady)
            throw AppServerErrors.InvalidRequest("Server is awaiting the 'initialized' notification before handling requests.");

        if (connection.IsAppPrincipalAuthenticated && !IsAppPrincipalMethod(method))
            throw AppServerErrors.AppPrincipalUnauthorized("App-principal connections may call only App Binding app-role methods.");

        try
        {
            // Extracted domain handlers register their methods in the table; protocol extensions
            // are the only fallback once built-in domains have had a chance to resolve the method.
            if (DomainMethods.TryGet(method, out var domainHandler))
                return await domainHandler(msg, ct);

            return await TryHandleExtensionAsync(method, msg, ct);
        }
        catch (KeyNotFoundException ex)
        {
            // Thread or turn not found in persistence or in-memory state
            throw AppServerErrors.ThreadNotFound(AppServerExceptionMapper.ExtractQuotedId(ex.Message));
        }
        catch (WorktreeHandoffConflictException ex)
        {
            throw AppServerErrors.WorktreeHandoffConflict(ex.ConflictPaths);
        }
        catch (ThreadHistoryUnavailableException ex)
        {
            throw AppServerErrors.ThreadHistoryUnavailable(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerExceptionMapper.MapOperationException(ex);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
    }

    private static readonly HashSet<string> AppPrincipalMethods =
    [
        Contract.AppServerRpc.AppConnectionAuthenticate.Name,
        Contract.AppServerRpc.AppConnectionRefresh.Name,
        Contract.AppServerRpc.AppConnectionStatus.Name,
        Contract.AppServerRpc.AppConnectionRevoke.Name,
        Contract.AppServerRpc.AppSurfacePublish.Name,
        Contract.AppServerRpc.AppBindingRequestGet.Name,
        Contract.AppServerRpc.AppBindingActivate.Name,
        Contract.AppServerRpc.AppBindingRebind.Name,
        Contract.AppServerRpc.AppBindingsList.Name,
        Contract.AppServerRpc.AppThreadInputEnqueue.Name
    ];

    private static bool IsAppPrincipalMethod(string method) => AppPrincipalMethods.Contains(method);

    /// <summary>
    /// Handles the <c>initialized</c> client notification (no response required).
    /// </summary>
    public void HandleInitializedNotification()
    {
        connection.MarkClientReady();
    }

    /// <summary>Dispatches a client notification through the typed notification catalog.</summary>
    public bool HandleNotification(AppServerIncomingMessage message) => ClientNotifications.TryHandle(message);

    private async Task<object?> TryHandleExtensionAsync(string method, AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!_extensionMethods.TryGetValue(method, out var handler))
            throw AppServerErrors.MethodNotFound(method);

        BundledExtensionRequests.TryGetValue(method, out var descriptor);
        var context = new AppServerExtensionContext(
                connection,
                transport,
                sessionService,
                services.WorkspaceCraftPath,
                services.HostWorkspacePath,
                services.ContextPageManager,
                services.NotifyAppPrincipal,
                services.BroadcastTrustedNotification,
                ct);

        var result = handler is IAppServerContractExtension contractExtension && descriptor is not null
            ? await contractExtension.HandleContractAsync(
                descriptor,
                AppServerTypedParams.Deserialize(descriptor.ParamsType, msg),
                msg,
                context)
            : await handler.HandleAsync(msg, context);

        if (descriptor is not null && result is not null && !descriptor.ResultType.IsInstanceOfType(result))
        {
            throw new InvalidOperationException(
                $"Extension method '{descriptor.Name}' returned {result.GetType().FullName}, " +
                $"expected {descriptor.ResultType.FullName} from the Contracts assembly.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IAppServerMethodHandler> BuildExtensionMethodMap(
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions)
    {
        if (protocolExtensions == null)
            return new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);

        var methods = new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);
        foreach (var extension in protocolExtensions)
        {
            if (extension is IAppServerContractExtension contractExtension)
            {
                var declaredMethods = contractExtension.ContractMethods
                    .Select(static descriptor => descriptor.Name)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var descriptor in contractExtension.ContractMethods)
                {
                    if (descriptor is not { Kind: "request", Direction: RpcDirection.ClientToServer } ||
                        descriptor.Module == "core")
                    {
                        throw new InvalidOperationException(
                            $"Bundled extension descriptor '{descriptor.Name}' is not a client request owned by an extension module.");
                    }
                    if (!extension.Methods.Contains(descriptor.Name, StringComparer.Ordinal))
                        throw new InvalidOperationException($"Bundled extension descriptor '{descriptor.Name}' has no runtime route.");
                }

                var undeclaredMethod = extension.Methods.FirstOrDefault(method => !declaredMethods.Contains(method));
                if (undeclaredMethod is not null)
                {
                    throw new InvalidOperationException(
                        $"Bundled extension method '{undeclaredMethod}' has no executable contract descriptor.");
                }
            }

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
