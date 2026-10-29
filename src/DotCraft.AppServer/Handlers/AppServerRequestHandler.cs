using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.AppServer;

public sealed class AppServerRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    IAppServerChannelListContributor channelListContributor,
    AppServerConnectionServices services)
{
    private readonly CommandRegistry _commandRegistry = services.CommandRegistry
                                                        ?? CreateCommandRegistry(services.WorkspaceCraftPath);

    private readonly IReadOnlyList<ConfigSchemaSection> _configSchema = services.ConfigSchema ?? [];

    private static CommandRegistry CreateCommandRegistry(string? workspaceDataPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceDataPath))
        {
            throw new ArgumentException(
                "AppServer requires either CommandRegistry or a resolved workspace DataPath.",
                nameof(workspaceDataPath));
        }

        return CommandRegistry.CreateDefault(
            Path.GetFileName(workspaceDataPath),
            new CustomCommandLoader(workspaceDataPath));
    }

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

    private WorkspaceConfigEditor? _workspaceConfig;
    private AppServerMcpConfigService? _mcpConfig;
    private ExternalChannelConfigService? _externalChannelConfig;
    private AppServerRuntimeConfigRefresher? _runtimeConfig;
    private AppServerThreadBinder? _threadBinder;
    private AppServerResponseWriter? _responseWriter;
    private AppServerThreadWireProjector? _threadProjector;
    private AgentProfileThreadConfigResolver? _profileResolver;

    private WorkspaceConfigEditor WorkspaceConfig => _workspaceConfig ??=
        new WorkspaceConfigEditor(services.AppConfigMonitor, services.WorkspaceCraftPath);

    private AppServerMcpConfigService McpConfig => _mcpConfig ??=
        new AppServerMcpConfigService(
            services.AppConfigMonitor,
            services.McpClientManager,
            services.HostWorkspacePath,
            services.WorkspaceCraftPath,
            services.LoggerFactory?.CreateLogger("DotCraft.Plugins"));

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
            services.VisualizationRuntimeRegistry,
            services.GenerativeUiRuntimeRegistry);

    private AppServerResponseWriter ResponseWriter => _responseWriter ??= new AppServerResponseWriter(transport);

    private AgentProfileThreadConfigResolver ProfileResolver => _profileResolver ??=
        new AgentProfileThreadConfigResolver(
            WorkspaceConfig,
            services.AppConfigMonitor,
            services.WorkspaceCraftPath,
            services.LoggerFactory?.CreateLogger<AgentProfileThreadConfigResolver>());

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
            services.WorkspaceCraftPath);

    private AppServerMethodTable? _domainMethods;
    private AppServerNotificationTable? _clientNotifications;

    /// <summary>
    /// Built lazily so domain handlers can capture the connection-scoped services initialized above.
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
        new InitializeRequestHandler(connection, services, _capabilityContributors, ThreadProjector),
        new TerminalRequestHandler(services.BackgroundTerminalService, sessionService),
        new RemoteToolHostRequestHandler(services.RemoteToolHostClient, sessionService),
        new DreamsRequestHandler(services.DreamsService, services.AppConfigMonitor, services.WorkspaceCraftPath),
        new SkillsRequestHandler(services.SkillsLoader, services.ContextPageManager, services.AppConfigMonitor, services.WorkspaceCraftPath),
        new ToolRequestHandler(),
        new FileSystemRequestHandler(services.AppConfigMonitor, connection, transport),
        new CommandExecRequestHandler(services.AppConfigMonitor, services.HostWorkspacePath, connection),
        new McpRequestHandler(services.McpClientManager, McpConfig, transport, services.AppConfigMonitor, services.BroadcastMcpStatusChanged, sessionService as IThreadToolDispatchService, sessionService as IThreadToolSnapshotService, sessionService as IThreadMcpRuntimeService, sessionService as IThreadAgentRefreshService),
        new McpAppRequestHandler(
            sessionService,
            connection,
            transport,
            sessionService as IThreadToolDispatchService,
            sessionService as IThreadToolSnapshotService,
            sessionService as IThreadMcpRuntimeService,
            services.McpAppTransientContextStore),
        new VisualizationRequestHandler(
            sessionService,
            connection,
            services.VisualizationAssetStore,
            services.VisualizationRuntimeRegistry,
            services.ThreadSubscribers),
        new GenerativeUiRequestHandler(sessionService, connection, services.GenerativeUiRuntimeRegistry, services.ThreadSubscribers),
        new ChannelRequestHandler(channelListContributor, services.ChannelStatusProvider, WorkspaceConfig, ExternalChannelConfig, services.WorkspaceCraftPath, services.AppConfigMonitor, services.OnExternalChannelUpserted, services.OnExternalChannelRemoved, services.ExternalChannelRuntimeProvider),
        new ProviderRequestHandler(
            WorkspaceConfig,
            services.WorkspaceCraftPath,
            services.AppConfigMonitor,
            services.ModelProviderRegistry,
            services.SupportsUltraReasoning,
            services.ChatClientRegistry?.RuntimeResolver,
            services.ProviderAuthentication),
        new ModelBackendRequestHandler(transport, connection, services, WorkspaceConfig),
        new WorkspaceRequestHandler(services.CommitMessageSuggest, services.WelcomeSuggestionService, _configSchema, services.MemoryStore, services.DreamsService, services.AppConfigMonitor, services.HostWorkspacePath),
        new ConfigRequestHandler(services.Configuration),
        new SourceControlRequestHandler(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath),
        new HookRequestHandler(services.HookRunner, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, WorkspaceConfig, RuntimeConfig, services.BuiltInPluginSourceRoots),
        new PluginRequestHandler(transport, connection, services.SkillsLoader, services.AppConfigMonitor, services.WorkspaceCraftPath, services.WorkspaceTempPath, services.HostWorkspacePath, services.BuiltInPluginSourceRoots, McpConfig, services.LspServerManager, services.ContextPageManager, services.AppBindingService, WorkspaceConfig, RuntimeConfig, services.HookRunner, services.PluginWorkflowSummaryProvider, services.PluginDotnetRuntimeCoordinator, services.PluginConfigStore, services.BroadcastPluginSnapshotUpdated, services.PluginManagementState, services.LoggerFactory?.CreateLogger("DotCraft.Plugins")),
        new AgentProfileRequestHandler(sessionService, services.WorkspaceCraftPath, WorkspaceConfig.UserDataPath, services.HostWorkspacePath, services.AppConfigMonitor),
        new CommandRequestHandler(
            _commandRegistry,
            sessionService,
            connection,
            services.WorkspaceCraftPath,
            () => services.AppConfigMonitor?.Current ?? WorkspaceConfig.LoadCurrentMergedConfig(),
            services.ModelProviderRegistry,
            services.AppBindingService,
            (thread, token) => ThreadProjector.EnrichAsync(thread.ToWire(), thread, token)),
        new ThreadRecoveryRequestHandler(sessionService),
        new ThreadRequestHandler(sessionService, connection, transport, ResponseWriter, ThreadBinder, ThreadProjector, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, ProfileResolver, services.StreamDebugLogger, services.LoggerFactory?.CreateLogger<ThreadRequestHandler>()),
        new TurnRequestHandler(sessionService, connection, transport, ResponseWriter, _commandRegistry, services.SkillsLoader, services.TraceStore, services.StreamDebugLogger, _defaultApprovalDecision, ThreadProjector, services.LoggerFactory?.CreateLogger<TurnRequestHandler>()),
        new WorktreeRequestHandler(sessionService, ResponseWriter, ThreadBinder, WorkspaceConfig, services.AppConfigMonitor, services.HostWorkspacePath, ProfileResolver, ThreadProjector.ProjectAsync),
        new SubAgentRequestHandler(sessionService, services.AppConfigMonitor, services.WorkspaceCraftPath, services.HostWorkspacePath, RuntimeConfig, ThreadProjector.ProjectHeaderAsync, services.SubAgentCoordinatorFactory, services.Contributions),
        new SubAgentBoardRequestHandler(sessionService),
        new UsageRequestHandler(services.UsageAnalytics, services.TraceStore, sessionService),
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

    public async Task<object?> HandleRequestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var method = msg.Method ?? string.Empty;

        if (method != Contract.AppServerRpc.Initialize.Name && !connection.IsInitialized)
            throw AppServerErrors.NotInitialized();

        if (method != Contract.AppServerRpc.Initialize.Name && connection.IsInitialized && !connection.IsClientReady)
            throw AppServerErrors.InvalidRequest("Server is awaiting the 'initialized' notification before handling requests.");

        if (connection.IsAppPrincipalAuthenticated && !IsAppPrincipalMethod(method))
            throw AppServerErrors.AppPrincipalUnauthorized("App-principal connections may call only App Binding app-role methods.");

        try
        {
            foreach (var refresher in services.RuntimeRefreshers)
                await refresher.RefreshAsync(ct);
            if (DomainMethods.TryGet(method, out var domainHandler))
                return await domainHandler(msg, ct);

            return await TryHandleExtensionAsync(method, msg, ct);
        }
        catch (ThreadNotFoundException ex)
        {
            throw AppServerErrors.ThreadNotFound(ex.ThreadId);
        }
        catch (KeyNotFoundException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
        catch (WorktreeHandoffConflictException ex)
        {
            throw AppServerErrors.WorktreeHandoffConflict(ex.ConflictPaths);
        }
        catch (WorktreeInUseException ex)
        {
            throw AppServerErrors.WorktreeInUse(ex.Path);
        }
        catch (WorktreeEnvironmentException ex)
        {
            throw AppServerErrors.WorktreeEnvironment(ex);
        }
        catch (ThreadHistoryUnavailableException ex)
        {
            throw AppServerErrors.ThreadHistoryUnavailable(ex.Message);
        }
        catch (AppBindingException ex)
        {
            throw ex.Error switch
            {
                AppBindingError.InvalidInput => AppServerErrors.InvalidParams(ex.Message),
                AppBindingError.Unauthorized => AppServerErrors.AppPrincipalUnauthorized(ex.Message),
                AppBindingError.Conflict => AppServerErrors.AppBindingConflict(ex.Message),
                AppBindingError.PolicyDenied => AppServerErrors.AppBindingPolicyDenied(ex.Message),
                AppBindingError.SurfaceUnavailable => AppServerErrors.AppSurfaceUnavailable(
                    ex.AppId ?? string.Empty,
                    ex.SurfaceId ?? string.Empty),
                _ => AppServerErrors.InternalError(ex.Message)
            };
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

    public void HandleInitializedNotification()
    {
        connection.MarkClientReady();
    }

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

    public static object BuildResponse(JsonElement? id, object? result) => new
    {
        jsonrpc = "2.0",
        id,
        result
    };

    public static object BuildErrorResponse(JsonElement? id, AppServerError error) => new
    {
        jsonrpc = "2.0",
        id,
        error
    };
}
