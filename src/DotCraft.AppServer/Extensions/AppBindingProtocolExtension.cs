using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Skills;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppBinding;

/// <summary>AppServer extension for the App Binding control plane.</summary>
public sealed class AppBindingProtocolExtension : IAppServerContractExtension
{
    private const string AppList = "app/list";
    private const string AppView = "app/view";
    private const string ConnectionStart = "app/connection/start";
    private const string ConnectionRequestGet = "app/connection/request/get";
    private const string ConnectionConnect = "app/connection/connect";
    private const string ConnectionAuthenticate = "app/connection/authenticate";
    private const string ConnectionRefresh = "app/connection/refresh";
    private const string ConnectionStatus = "app/connection/status";
    private const string ConnectionRevoke = "app/connection/revoke";
    private const string SurfacePublish = "app/surface/publish";
    private const string SurfaceResolve = "app/surface/resolve";
    private const string BindingEnable = "thread/appBindings/enable";
    private const string BindingRequestGet = "app/binding/request/get";
    private const string BindingActivate = "app/binding/activate";
    private const string BindingRebind = "app/binding/rebind";
    private const string PrincipalBindingsList = "app/bindings/list";
    private const string ThreadBindingsList = "thread/appBindings/list";
    private const string BindingConfirm = "thread/appBindings/confirmCapabilities";
    private const string BindingRevoke = "thread/appBindings/revoke";
    private const string SocialRequestCreate = "thread/socialBindings/request/create";
    private const string SocialRequestGet = "app/socialBinding/request/get";
    private const string SocialAccept = "app/socialBinding/accept";
    private const string SocialRebind = "app/socialBinding/rebind";
    private const string SocialResolve = "app/socialBinding/resolve";
    private const string ThreadInputEnqueue = "app/threadInput/enqueue";

    private readonly AppBindingService _controlPlane;
    private readonly AppBindingCoordinator _coordinator;
    private readonly IAppConfigMonitor _appConfigMonitor;
    private readonly SkillsLoader? _skillsLoader;
    private readonly IReadOnlyList<string>? _builtInPluginSourceRoots;
    private readonly SocialChannelDeliveryCoordinator? _socialDeliveryCoordinator;

    public AppBindingProtocolExtension(
        AppBindingService controlPlane,
        AppBindingCoordinator coordinator,
        IAppConfigMonitor appConfigMonitor,
        SkillsLoader? skillsLoader = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        Channels.IChannelRuntimeRegistry? channelRuntimeRegistry = null)
    {
        _controlPlane = controlPlane;
        _coordinator = coordinator;
        _appConfigMonitor = appConfigMonitor;
        _skillsLoader = skillsLoader;
        _builtInPluginSourceRoots = builtInPluginSourceRoots;
        _socialDeliveryCoordinator = channelRuntimeRegistry == null
            ? null
            : new SocialChannelDeliveryCoordinator(controlPlane, channelRuntimeRegistry);
    }

    public IReadOnlyCollection<string> Methods { get; } =
    [
        AppList, AppView,
        ConnectionStart, ConnectionRequestGet, ConnectionConnect, ConnectionAuthenticate,
        ConnectionRefresh, ConnectionStatus, ConnectionRevoke, SurfacePublish, SurfaceResolve,
        BindingEnable, BindingRequestGet, BindingActivate, BindingRebind,
        PrincipalBindingsList, ThreadBindingsList, BindingConfirm, BindingRevoke,
        SocialRequestCreate, SocialRequestGet, SocialAccept, SocialRebind, SocialResolve,
        ThreadInputEnqueue
    ];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
    [
        Contract.AppServerRpc.AppList, Contract.AppServerRpc.AppView,
        Contract.AppServerRpc.AppConnectionStart, Contract.AppServerRpc.AppConnectionRequestGet,
        Contract.AppServerRpc.AppConnectionConnect, Contract.AppServerRpc.AppConnectionAuthenticate,
        Contract.AppServerRpc.AppConnectionRefresh, Contract.AppServerRpc.AppConnectionStatus,
        Contract.AppServerRpc.AppConnectionRevoke, Contract.AppServerRpc.AppSurfacePublish,
        Contract.AppServerRpc.AppSurfaceResolve, Contract.AppServerRpc.ThreadAppBindingEnable,
        Contract.AppServerRpc.AppBindingRequestGet, Contract.AppServerRpc.AppBindingActivate,
        Contract.AppServerRpc.AppBindingRebind, Contract.AppServerRpc.AppBindingsList,
        Contract.AppServerRpc.ThreadAppBindingsList, Contract.AppServerRpc.ThreadAppBindingConfirmCapabilities,
        Contract.AppServerRpc.ThreadAppBindingRevoke, Contract.AppServerRpc.ThreadSocialBindingRequestCreate,
        Contract.AppServerRpc.SocialBindingRequestGet, Contract.AppServerRpc.SocialBindingAccept,
        Contract.AppServerRpc.SocialBindingRebind, Contract.AppServerRpc.AppSocialBindingResolve,
        Contract.AppServerRpc.AppThreadInputEnqueue
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
        {
            builder.Capabilities.AppBindingVersion = AppBindingContract.Version;
            builder.Capabilities.AppThreadInputEnqueue = true;
        }
    }

    public async Task<object?> HandleContractAsync(
        IRpcMethodDescriptor _,
        object requestParams,
        AppServerIncomingMessage msg,
        AppServerExtensionContext context)
    {
        var method = msg.Method ?? string.Empty;
        var craftPath = RequireWorkspace(context, method);
        var userId = CurrentUser(context);

        if (method is AppList or AppView)
        {
            EnsureTrustedClient(context.Connection);
            if (method == AppList)
            {
                var parameters = (Contract.AppListParams)requestParams;
                var threadId = AppBindingContractMapper.Read(parameters.ThreadId);
                if (!string.IsNullOrWhiteSpace(threadId))
                    await context.SessionService.GetThreadAsync(threadId, context.CancellationToken);
                var apps = DiscoverCatalog(context)
                        .Entries
                        .Where(entry => AppBindingContractMapper.Read(parameters.IncludeCatalog) != false || entry.Plugin.Installed)
                        .Where(entry => AppBindingContractMapper.Read(parameters.IncludeDisabled) != false || entry.Plugin.Enabled)
                        .Select(MapCatalogApp)
                        .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                ApplyBindingState(craftPath, threadId, apps);
                return new Contract.AppListResult
                {
                    Apps = apps.Select(AppBindingContractMapper.ToContract).ToArray()
                };
            }

            var viewParameters = (Contract.AppViewParams)requestParams;
            var viewThreadId = AppBindingContractMapper.Read(viewParameters.ThreadId);
            var appId = AppBindingContractMapper.Read(viewParameters.AppId) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(viewThreadId))
                await context.SessionService.GetThreadAsync(viewThreadId, context.CancellationToken);
            var projection = MapCatalogApp(EnsureCatalogApp(context, appId));
            ApplyBindingState(craftPath, viewThreadId, [projection]);
            return new Contract.AppViewResult { App = AppBindingContractMapper.ToContract(projection) };
        }

        switch (method)
        {
            case ConnectionStart:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.AppConnectionStartParams)requestParams;
                var appId = AppBindingContractMapper.Read(parameters.AppId) ?? string.Empty;
                EnsureCatalogApp(context, appId);
                var result = _controlPlane.StartConnection(craftPath, appId, userId);
                result.Handoff = BuildHandoff(context, appId, result.ConnectionRequestId, result.RequestToken, "connect");
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.AppConnectionChanged, new Contract.AppConnectionChangedNotification
                    {
                        AppId = appId,
                        State = "connecting"
                    }));
            }
            case ConnectionRequestGet:
            {
                var parameters = (Contract.AppConnectionRequestGetParams)requestParams;
                var request = _controlPlane.GetConnectionRequest(craftPath, AppBindingContractMapper.FromContract(parameters));
                var app = EnsureCatalogApp(context, request.AppId);
                return new Contract.AppConnectionRequestGetResult
                {
                    ConnectionRequestId = request.ConnectionRequestId,
                    AppId = request.AppId,
                    DisplayName = app.Descriptor.DisplayName,
                    DeveloperName = app.Descriptor.DeveloperName,
                    UserId = request.UserId,
                    ExpiresAt = request.ExpiresAt
                };
            }
            case ConnectionConnect:
            {
                var result = _controlPlane.Connect(
                    craftPath,
                    AppBindingContractMapper.FromContract((Contract.AppConnectionConnectParams)requestParams));
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.AppConnectionChanged, new Contract.AppConnectionChangedNotification
                    {
                        AppId = result.Principal.AppId,
                        State = "connected"
                    }));
            }
            case ConnectionAuthenticate:
            {
                var parameters = (Contract.AppConnectionAuthenticateParams)requestParams;
                var appId = AppBindingContractMapper.Read(parameters.AppId) ?? string.Empty;
                var credential = AppBindingContractMapper.Read(parameters.Credential) ?? string.Empty;
                var principal = _controlPlane.Authenticate(craftPath, appId, credential);
                context.Connection.BindAppPrincipal(principal.PrincipalId, principal.AppId);
                return new Contract.AppConnectionAuthenticateResult { Principal = AppBindingContractMapper.ToContract(principal) };
            }
            case ConnectionRefresh:
                return AppBindingContractMapper.ToContract(
                    _controlPlane.Refresh(craftPath, RequirePrincipal(context.Connection)));
            case ConnectionStatus:
            {
                var parameters = (Contract.AppConnectionStatusParams)requestParams;
                var requestedAppId = AppBindingContractMapper.Read(parameters.AppId);
                var principal = context.Connection.IsAppPrincipalAuthenticated
                    ? _controlPlane.GetActivePrincipal(craftPath, context.Connection.AppPrincipalAppId!)
                    : _controlPlane.GetActivePrincipal(craftPath, requestedAppId ?? string.Empty);
                return new Contract.AppConnectionStatusResult
                {
                    AppId = context.Connection.AppPrincipalAppId ?? requestedAppId ?? string.Empty,
                    State = principal == null ? "notConnected" : "connected",
                    Principal = principal is null
                        ? default
                        : Optional<Contract.AppPrincipal?>.FromValue(
                            AppBindingContractMapper.ToContract(principal))
                };
            }
            case ConnectionRevoke:
            {
                var parameters = (Contract.AppConnectionRevokeParams)requestParams;
                var requestedAppId = AppBindingContractMapper.Read(parameters.AppId);
                if (context.Connection.IsAppPrincipalAuthenticated)
                {
                    var bindings = _controlPlane.ListPrincipalBindings(craftPath, context.Connection.AppPrincipalId!);
                    _controlPlane.RevokePrincipal(craftPath, context.Connection.AppPrincipalId!, context.Connection.AppPrincipalId!);
                    if (context.SessionService is IThreadMcpRuntimeService runtime)
                        foreach (var binding in bindings)
                            await _coordinator.RemoveAsync(binding.ThreadId, binding.BindingId, runtime, context.CancellationToken);
                }
                else
                {
                    EnsureTrustedClient(context.Connection);
                    var appId = requestedAppId ?? string.Empty;
                    var bindings = _controlPlane.ListAppBindings(craftPath, appId);
                    _controlPlane.RevokeApp(craftPath, appId, userId);
                    if (context.SessionService is IThreadMcpRuntimeService runtime)
                        foreach (var binding in bindings)
                            await _coordinator.RemoveAsync(binding.ThreadId, binding.BindingId, runtime, context.CancellationToken);
                }
                var result = new Contract.AppConnectionRevokeResult { State = AppBindingStates.Revoked };
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    (Contract.AppServerRpc.AppConnectionChanged, new Contract.AppConnectionChangedNotification
                    {
                        AppId = OmitIfNull(requestedAppId ?? context.Connection.AppPrincipalAppId),
                        State = "revoked"
                    }));
            }
            case SurfacePublish:
                return AppBindingContractMapper.ToContract(
                    _controlPlane.PublishSurface(
                        craftPath,
                        RequirePrincipal(context.Connection),
                        AppBindingContractMapper.FromContract((Contract.AppSurfacePublishParams)requestParams)));
            case SurfaceResolve:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.AppSurfaceResolveParams)requestParams;
                var appId = AppBindingContractMapper.Read(parameters.AppId) ?? string.Empty;
                var surfaceId = AppBindingContractMapper.Read(parameters.SurfaceId) ?? string.Empty;
                EnsureCatalogApp(context, appId);
                return AppBindingContractMapper.ToContract(_controlPlane.ResolveSurface(craftPath, appId, surfaceId));
            }
            case BindingEnable:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.ThreadAppBindingEnableParams)requestParams;
                var threadId = AppBindingContractMapper.Read(parameters.ThreadId) ?? string.Empty;
                var appId = AppBindingContractMapper.Read(parameters.AppId) ?? string.Empty;
                await context.SessionService.GetThreadAsync(threadId, context.CancellationToken);
                EnsureCatalogApp(context, appId);
                var result = _controlPlane.Enable(craftPath, threadId, appId, userId);
                result.Handoff = BuildHandoff(context, appId, result.BindingRequestId, result.RequestToken, "bind");
                context.NotifyAppPrincipal?.Invoke(appId, Contract.AppServerRpc.AppBindingRequested.Name,
                    new Contract.AppBindingRequestedNotification
                    {
                        BindingRequestId = result.BindingRequestId,
                        BindingId = result.BindingId,
                        ThreadId = threadId,
                        AppId = appId
                    });
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, new Contract.ThreadAppBindingsChangedNotification
                    {
                        ThreadId = threadId,
                        State = AppBindingStates.Connecting
                    }));
            }
            case BindingRequestGet:
            {
                var request = _controlPlane.GetBindingRequest(
                    craftPath,
                    AppBindingContractMapper.FromContract((Contract.AppBindingRequestGetParams)requestParams),
                    context.Connection.AppPrincipalId);
                var app = EnsureCatalogApp(context, request.AppId);
                var thread = await context.SessionService.GetThreadAsync(request.ThreadId, context.CancellationToken);
                return new Contract.AppBindingRequestGetResult
                {
                    AppId = request.AppId,
                    BindingId = request.BindingId,
                    BindingKind = Optional<string?>.FromValue("app"),
                    BindingRequestId = request.BindingRequestId,
                    DeveloperName = app.Descriptor.DeveloperName,
                    DisplayName = app.Descriptor.DisplayName,
                    ExpiresAt = request.ExpiresAt,
                    RequestedScopes = Array.Empty<string>(),
                    RequestedTools = Array.Empty<string>(),
                    ScopeCatalog = Array.Empty<Contract.AppScopeDescriptor>(),
                    Source = "thread",
                    State = request.State,
                    ThreadId = request.ThreadId,
                    ThreadTitle = OmitIfNull(thread.DisplayName),
                    ToolCatalog = Array.Empty<Contract.AppToolCatalogEntry>()
                };
            }
            case PrincipalBindingsList:
                return new Contract.AppBindingsListResult
                {
                    Bindings = _controlPlane.ListPrincipalBindings(craftPath, RequirePrincipal(context.Connection))
                        .Select(AppBindingContractMapper.ToContract)
                        .ToArray()
                };
            case ThreadBindingsList:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.ThreadAppBindingsListParams)requestParams;
                var threadId = AppBindingContractMapper.Read(parameters.ThreadId) ?? string.Empty;
                return new Contract.ThreadAppBindingsListResult
                {
                    Bindings = _controlPlane.ListThreadBindings(craftPath, threadId)
                        .Select(AppBindingContractMapper.ToContract)
                        .ToArray()
                };
            }
            case BindingRevoke:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.ThreadAppBindingRevokeParams)requestParams;
                var threadId = AppBindingContractMapper.Read(parameters.ThreadId) ?? string.Empty;
                var bindingId = AppBindingContractMapper.Read(parameters.BindingId) ?? string.Empty;
                var binding = _controlPlane.RevokeBinding(craftPath, threadId, bindingId, userId);
                if (context.SessionService is IThreadMcpRuntimeService mcpRuntime)
                    await _coordinator.RemoveAsync(threadId, bindingId, mcpRuntime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(binding),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, new Contract.ThreadAppBindingsChangedNotification
                    {
                        ThreadId = threadId,
                        BindingId = bindingId,
                        State = AppBindingStates.Revoked
                    }));
            }
            case BindingActivate:
            {
                var runtime = RequireMcpRuntime(context.SessionService);
                var parameters = AppBindingContractMapper.FromContract(
                    (Contract.AppBindingActivateParams)requestParams);
                var result = await _coordinator.ActivateAsync(craftPath, RequirePrincipal(context.Connection),
                    parameters, runtime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, Changed(result.ThreadId, result.BindingId, result.State)));
            }
            case BindingRebind:
            {
                var runtime = RequireMcpRuntime(context.SessionService);
                var parameters = AppBindingContractMapper.FromContract(
                    (Contract.AppBindingRebindParams)requestParams);
                var result = await _coordinator.RebindAsync(craftPath, RequirePrincipal(context.Connection),
                    parameters, runtime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, Changed(result.ThreadId, result.BindingId, result.State)));
            }
            case BindingConfirm:
            {
                EnsureTrustedClient(context.Connection);
                var contractParameters = (Contract.ThreadAppBindingConfirmCapabilitiesParams)requestParams;
                var parameters = AppBindingContractMapper.FromContract(contractParameters);
                var result = await _coordinator.ConfirmAsync(craftPath,
                    parameters, userId,
                    RequireMcpRuntime(context.SessionService), context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, Changed(parameters.ThreadId, parameters.BindingId, result.State)));
            }
            case SocialRequestCreate:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = (Contract.ThreadSocialBindingRequestCreateParams)requestParams;
                var threadId = AppBindingContractMapper.Read(parameters.ThreadId) ?? string.Empty;
                var channelName = AppBindingContractMapper.Read(parameters.ChannelName) ?? string.Empty;
                await context.SessionService.GetThreadAsync(threadId, context.CancellationToken);
                var result = _controlPlane.CreateSocialRequest(craftPath, threadId, channelName, userId);
                var contractResult = new Contract.ThreadSocialBindingRequestCreateResult
                {
                    BindingRequestId = result.BindingRequestId,
                    BindingId = result.BindingId,
                    Code = result.Code,
                    ChannelName = result.ChannelName,
                    ExpiresAt = result.ExpiresAt
                };
                return await SendNotificationsAfterResponseAsync(msg, context, contractResult,
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, new Contract.ThreadAppBindingsChangedNotification
                    {
                        ThreadId = threadId,
                        State = AppBindingStates.Connecting
                    }),
                    (Contract.AppServerRpc.AppBindingRequested, new Contract.AppBindingRequestedNotification
                    {
                        BindingRequestId = result.BindingRequestId,
                        BindingId = result.BindingId,
                        Code = result.Code,
                        ChannelName = result.ChannelName,
                        ExpiresAt = result.ExpiresAt
                    }));
            }
            case SocialRequestGet:
            {
                var channel = RequireChannel(context.Connection);
                var parameters = (Contract.SocialBindingRequestGetParams)requestParams;
                return AppBindingContractMapper.ToContract(
                    _controlPlane.GetSocialRequest(
                        craftPath,
                        AppBindingContractMapper.Read(parameters.Code) ?? string.Empty,
                        channel));
            }
            case SocialAccept:
            {
                var result = _controlPlane.AcceptSocial(craftPath, RequireChannel(context.Connection),
                    AppBindingContractMapper.FromContract((Contract.SocialBindingAcceptParams)requestParams));
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, Changed(result.ThreadId, result.BindingId, result.State)));
            }
            case SocialRebind:
            {
                var result = _controlPlane.RebindSocial(craftPath, RequireChannel(context.Connection),
                    AppBindingContractMapper.FromContract((Contract.SocialBindingRebindParams)requestParams));
                return await SendNotificationsAfterResponseAsync(msg, context, AppBindingContractMapper.ToContract(result),
                    (Contract.AppServerRpc.ThreadAppBindingsChanged, Changed(result.ThreadId, result.BindingId, result.State)));
            }
            case SocialResolve:
            {
                var channel = RequireChannel(context.Connection);
                var parameters = (Contract.AppSocialBindingResolveParams)requestParams;
                var channelName = AppBindingContractMapper.Read(parameters.ChannelName) ?? string.Empty;
                if (!string.Equals(channelName, channel, StringComparison.OrdinalIgnoreCase))
                    throw AppServerErrors.AppPrincipalUnauthorized("A channel adapter may resolve only its own bindings.");
                var binding = _controlPlane.ResolveSocial(
                    craftPath,
                    channel,
                    AppBindingContractMapper.Read(parameters.AccountId),
                    AppBindingContractMapper.Read(parameters.ConversationKind) ?? string.Empty,
                    AppBindingContractMapper.Read(parameters.ConversationId) ?? string.Empty);
                return new Contract.AppSocialBindingResolveResult
                {
                    Binding = Optional<Contract.AppBinding?>.FromValue(
                        binding is null ? null : AppBindingContractMapper.ToContract(binding))
                };
            }
            case ThreadInputEnqueue:
            {
                var parameters = (Contract.AppThreadInputEnqueueParams)requestParams;
                var bindingId = AppBindingContractMapper.Read(parameters.BindingId) ?? string.Empty;
                var input = AppBindingContractMapper.Read(parameters.Input) ?? [];
                var nativeInput = TurnContractMapper.ToDomain(input);
                var principal = context.Connection.IsChannelAdapter
                    ? $"channel:{context.Connection.ChannelAdapterName!.ToLowerInvariant()}"
                    : RequirePrincipal(context.Connection);
                var binding = _controlPlane.AuthorizeThreadInput(craftPath, bindingId, principal);
                if (nativeInput.Count == 0)
                    throw AppServerErrors.InvalidParams("'input' must not be empty.");
                await context.SessionService.EnsureThreadLoadedAsync(binding.ThreadId, context.CancellationToken);
                List<AIContent> contents;
                try
                {
                    contents = await SessionInputPartResolver.ResolveStrictAsync(
                        nativeInput,
                        context.CancellationToken);
                }
                catch (SessionInputPartValidationException ex)
                {
                    if (string.Equals(
                        ex.Code,
                        SessionInputPartResolver.RemoteImageUrlErrorCode,
                        StringComparison.Ordinal))
                    {
                        throw AppServerErrors.RemoteImageUrlNotSupported();
                    }

                    throw AppServerErrors.InvalidParams(ex.Message);
                }
                using (TurnTriggerScope.Set(new TurnTriggerInfo
                       {
                            Kind = "app",
                            Label = Normalize(AppBindingContractMapper.Read(parameters.TriggerLabel)),
                            RefId = Normalize(AppBindingContractMapper.Read(parameters.TriggerRefId))
                       }))
                {
                    var queued = await context.SessionService.EnqueueTurnInputAsync(
                        binding.ThreadId,
                        contents,
                        TurnContractMapper.ToDomain(AppBindingContractMapper.Read(parameters.Sender)),
                        context.CancellationToken,
                        new SessionInputSnapshot
                        {
                            NativeInputParts = nativeInput,
                            MaterializedInputParts = nativeInput,
                            DisplayText = AppBindingContractMapper.Read(parameters.DisplayText)
                                ?? SessionWireMapper.BuildDisplayText(nativeInput),
                            DeliveryBindingId = binding.Kind == "social" ? binding.BindingId : null
                        });
                    if (binding.Kind == "social")
                        _socialDeliveryCoordinator?.StartQueuedTurnDelivery(context.SessionService, craftPath,
                            binding.ThreadId, binding.BindingId, queued.Id, binding.AuthorityRevision,
                            context.CancellationToken);
                    if (string.Equals(
                        AppBindingContractMapper.Read(parameters.StartPolicy),
                        AppThreadInputStartPolicies.RunWhenIdle,
                        StringComparison.Ordinal))
                        await context.SessionService.TryStartNextQueuedTurnAsync(binding.ThreadId, context.CancellationToken);
                    var thread = await context.SessionService.GetThreadAsync(binding.ThreadId, context.CancellationToken);
                    return new Contract.AppThreadInputEnqueueResult
                    {
                        QueuedInput = TurnContractMapper.ToContract(queued),
                        QueuedInputs = Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                            TurnContractMapper.ToContract(thread.QueuedInputs))
                    };
                }
            }
            default:
                throw AppServerErrors.MethodNotFound(method);
        }
    }

    private static IThreadMcpRuntimeService RequireMcpRuntime(ISessionService sessionService) =>
        sessionService as IThreadMcpRuntimeService
        ?? throw AppServerErrors.InvalidRequest("This host does not provide binding-scoped MCP sessions.");

    private static Contract.ThreadAppBindingsChangedNotification Changed(
        string threadId,
        string bindingId,
        string state) => new()
    {
        ThreadId = threadId,
        BindingId = bindingId,
        State = state
    };

    private static string RequireChannel(AppServerConnection connection) =>
        connection.ChannelAdapterName
        ?? throw AppServerErrors.AppPrincipalUnauthorized("This method requires an authenticated channel adapter.");

    private AppCatalogEntry EnsureCatalogApp(AppServerExtensionContext context, string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        var catalog = DiscoverCatalog(context);
        return catalog.Entries.FirstOrDefault(entry =>
                   string.Equals(entry.Descriptor.AppId, appId, StringComparison.Ordinal))
               ?? throw AppServerErrors.InvalidParams($"App '{appId}' is not installed or enabled.");
    }

    private AppCatalogSnapshot DiscoverCatalog(AppServerExtensionContext context) =>
        AppBindingCatalog.Discover(
            _appConfigMonitor.Current,
            context.HostWorkspacePath!,
            context.WorkspaceCraftPath!,
            _skillsLoader,
            _builtInPluginSourceRoots);

    private static AppCatalogProjection MapCatalogApp(AppCatalogEntry entry) => new()
    {
        AppId = entry.Descriptor.AppId,
        DisplayName = entry.Descriptor.DisplayName,
        DeveloperName = entry.Descriptor.DeveloperName,
        Description = entry.Descriptor.Description,
        Category = entry.Descriptor.Category,
        Icon = entry.Descriptor.Icon,
        PluginId = entry.Plugin.Manifest.Id,
        Installed = entry.Plugin.Installed,
        Enabled = entry.Plugin.Enabled,
        ReleasePage = entry.Descriptor.ReleasePage,
        DownloadUrl = entry.Descriptor.DownloadUrl,
        NativeApp = new AppNativeApplicationProjection
        {
            DisplayName = entry.Descriptor.NativeApplication.DisplayName,
            Protocol = entry.Descriptor.NativeApplication.Protocol,
            InstallUrl = entry.Descriptor.NativeApplication.InstallUrl
        },
        HandoffModes = entry.Descriptor.Connection.HandoffModes
    };

    private AppHandoffDescriptor BuildHandoff(
        AppServerExtensionContext context,
        string appId,
        string requestId,
        string token,
        string operation)
    {
        var app = EnsureCatalogApp(context, appId);
        var mode = app.Descriptor.Connection.HandoffModes.First();
        return new AppHandoffDescriptor
        {
            Mode = mode.Mode,
            Uri = mode.Mode == "desktopService"
                ? BuildDesktopServiceHandoff(
                    mode.ServiceId!,
                    appId,
                    requestId,
                    token,
                    operation,
                    context.HostWorkspacePath!)
                : string.IsNullOrWhiteSpace(mode.UriTemplate)
                    ? null
                    : FillHandoffTemplate(
                    mode.UriTemplate,
                    appId,
                    requestId,
                    token,
                    operation,
                    ReadAppServerEndpoint(context.WorkspaceCraftPath!))
        };
    }

    private static string BuildDesktopServiceHandoff(
        string serviceId,
        string appId,
        string requestId,
        string token,
        string operation,
        string workspacePath)
    {
        var canonicalWorkspacePath = Path.GetFullPath(workspacePath);
        var runtimeIdentity = $"local:{canonicalWorkspacePath}";
        return $"dotcraft-service://{Uri.EscapeDataString(serviceId)}/{Uri.EscapeDataString(operation)}" +
               $"?app={Uri.EscapeDataString(appId)}" +
               $"&request={Uri.EscapeDataString(requestId)}" +
               $"&token={Uri.EscapeDataString(token)}" +
               $"&workspace={Uri.EscapeDataString(canonicalWorkspacePath)}" +
               $"&identity={Uri.EscapeDataString(runtimeIdentity)}";
    }

    private static string FillHandoffTemplate(
        string template,
        string appId,
        string requestId,
        string token,
        string operation,
        string endpoint) =>
        template
            .Replace("{appId}", Uri.EscapeDataString(appId), StringComparison.Ordinal)
            .Replace("{requestId}", Uri.EscapeDataString(requestId), StringComparison.Ordinal)
            .Replace("{requestToken}", Uri.EscapeDataString(token), StringComparison.Ordinal)
            .Replace("{operation}", Uri.EscapeDataString(operation), StringComparison.Ordinal)
            .Replace("{endpoint}", Uri.EscapeDataString(endpoint), StringComparison.Ordinal);

    private static string ReadAppServerEndpoint(string workspaceCraftPath)
    {
        try
        {
            var lockPath = Path.Combine(workspaceCraftPath, "appserver.lock");
            if (!File.Exists(lockPath))
                return string.Empty;

            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (!document.RootElement.TryGetProperty("endpoints", out var endpoints)
                || !endpoints.TryGetProperty("appServerWebSocket", out var endpoint))
            {
                return string.Empty;
            }

            return endpoint.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void EnsureTrustedClient(AppServerConnection connection)
    {
        if (connection.IsAppPrincipalAuthenticated || connection.IsChannelAdapter)
            throw AppServerErrors.AppPrincipalUnauthorized("This method requires a trusted DotCraft client connection.");
    }

    private static string RequirePrincipal(AppServerConnection connection) =>
        connection.AppPrincipalId
        ?? throw AppServerErrors.AppPrincipalUnauthorized("This method requires an authenticated app-principal connection.");

    private static string RequireWorkspace(AppServerExtensionContext context, string method) =>
        string.IsNullOrWhiteSpace(context.WorkspaceCraftPath)
            ? throw AppServerErrors.MethodNotFound(method)
            : context.WorkspaceCraftPath;

    private static string CurrentUser(AppServerExtensionContext context) =>
        string.IsNullOrWhiteSpace(context.Connection.ClientInfo?.Name)
            ? "appserver"
            : context.Connection.ClientInfo.Name;

    private void ApplyBindingState(string craftPath, string? threadId, IReadOnlyList<AppCatalogProjection> apps)
    {
        var bindings = string.IsNullOrWhiteSpace(threadId)
            ? Array.Empty<AppBindingSnapshot>()
            : _controlPlane.ListThreadBindings(craftPath, threadId).ToArray();
        foreach (var app in apps)
        {
            if (_controlPlane.GetActivePrincipal(craftPath, app.AppId) != null)
                app.ConnectionState = AppConnectionStates.Connected;
            var binding = bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.AppId, app.AppId, StringComparison.Ordinal));
            app.BindingSummary = binding == null ? null : new ThreadAppBindingSummarySnapshot
            {
                ThreadId = binding.ThreadId,
                BindingId = binding.BindingId,
                AppId = binding.AppId,
                DisplayName = app.DisplayName,
                Icon = app.Icon,
                State = binding.State,
                ConnectionState = app.ConnectionState,
                BindingKind = binding.SocialTarget == null ? "app" : "socialChannel",
                SocialTarget = binding.SocialTarget,
                AuthorityRevision = binding.AuthorityRevision,
                ApprovedCapabilityRevision = binding.ApprovedCapabilityRevision,
                CandidateCapabilityRevision = binding.CandidateCapabilityRevision,
                ApprovedTools = binding.ApprovedTools,
                PendingChanges = binding.PendingChanges,
                FailureReason = binding.FailureReason
            };
        }
    }


    private static async Task<object?> SendNotificationsAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object result,
        params (IRpcMethodDescriptor Method, object Params)[] notifications)
    {
        var descriptor = Contract.AppServerRpcCatalog.All.Single(candidate =>
            candidate.Name == msg.Method &&
            candidate.Kind == "request" &&
            candidate.Direction == RpcDirection.ClientToServer);
        if (result.GetType() != descriptor.ResultType)
        {
            throw new InvalidOperationException(
                $"App Binding handler '{descriptor.Name}' returned '{result.GetType().FullName}' instead of contract result '{descriptor.ResultType.FullName}'.");
        }
        await context.Transport.WriteMessageAsync(
            AppServerRequestHandler.BuildResponse(
                msg.Id,
                result),
            context.CancellationToken);
        foreach (var notification in notifications)
        {
            if (context.BroadcastTrustedNotification != null)
                context.BroadcastTrustedNotification(notification.Method.Name, notification.Params);
            else
                await context.Transport.NotifyContractAsync(
                    notification.Method.Name,
                    notification.Params,
                    context.CancellationToken);
        }
        return null;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Optional<T?>.FromValue(value);
}
