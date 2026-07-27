using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.AppBinding;

/// <summary>AppServer extension for the App Binding control plane.</summary>
public sealed class AppBindingProtocolExtension : IAppServerProtocolExtension
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

    private static readonly HashSet<string> LegacyMethods = new(StringComparer.Ordinal)
    {
        "app/connection/refreshMetadata",
        "app/binding/request/create",
        "app/binding/request/cancel",
        "app/binding/accept",
        "app/binding/attachTools",
        "app/binding/context/upsert",
        "app/binding/context/remove",
        "thread/appBindings/refresh",
        "thread/appContextBlocks/list",
        "ui/resource/read",
        "ui/tool/call",
        "ui/open-link",
        "ui/update-model-context"
    };

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
        DotCraft.Channels.IChannelRuntimeRegistry? channelRuntimeRegistry = null)
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
        ThreadInputEnqueue,
        ..LegacyMethods
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
        {
            builder.Capabilities.AppBindingVersion = AppBindingContract.Version;
            builder.Capabilities.AppThreadInputEnqueue = true;
        }
    }

    public async Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
    {
        var method = msg.Method ?? string.Empty;
        if (LegacyMethods.Contains(method))
            throw AppServerErrors.AppBindingUpgradeRequired();

        var craftPath = RequireWorkspace(context, method);
        var userId = CurrentUser(context);

        if (method is AppList or AppView)
        {
            EnsureTrustedClient(context.Connection);
            if (method == AppList)
            {
                var parameters = GetParams<AppListParams>(msg);
                if (!string.IsNullOrWhiteSpace(parameters.ThreadId))
                    await context.SessionService.GetThreadAsync(parameters.ThreadId, context.CancellationToken);
                var list = new AppListResult
                {
                    Apps = DiscoverCatalog(context)
                        .Entries
                        .Where(entry => parameters.IncludeCatalog != false || entry.Plugin.Installed)
                        .Where(entry => parameters.IncludeDisabled != false || entry.Plugin.Enabled)
                        .Select(MapCatalogApp)
                        .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                ApplyBindingState(craftPath, parameters.ThreadId, list.Apps);
                return list;
            }

            var viewParameters = GetParams<AppViewParams>(msg);
            if (!string.IsNullOrWhiteSpace(viewParameters.ThreadId))
                await context.SessionService.GetThreadAsync(viewParameters.ThreadId, context.CancellationToken);
            var view = new AppViewResult { App = MapCatalogApp(EnsureCatalogApp(context, viewParameters.AppId)) };
            ApplyBindingState(craftPath, viewParameters.ThreadId, [view.App]);
            return view;
        }

        switch (method)
        {
            case ConnectionStart:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<AppConnectionStartParams>(msg);
                EnsureCatalogApp(context, parameters.AppId);
                var result = _controlPlane.StartConnection(craftPath, parameters.AppId, userId);
                result.Handoff = BuildHandoff(context, parameters.AppId, result.ConnectionRequestId, result.RequestToken, "connect");
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("app/connection/changed", new { appId = parameters.AppId, state = "connecting" }));
            }
            case ConnectionRequestGet:
            {
                var parameters = GetParams<AppConnectionRequestGetParams>(msg);
                var request = _controlPlane.GetConnectionRequest(craftPath, parameters);
                var app = EnsureCatalogApp(context, request.AppId);
                return new
                {
                    request.ConnectionRequestId,
                    request.AppId,
                    app.Descriptor.DisplayName,
                    app.Descriptor.DeveloperName,
                    request.UserId,
                    request.ExpiresAt
                };
            }
            case ConnectionConnect:
            {
                var result = _controlPlane.Connect(craftPath, GetParams<AppConnectionConnectParams>(msg));
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("app/connection/changed", new { result.Principal.AppId, state = "connected" }));
            }
            case ConnectionAuthenticate:
            {
                var parameters = GetParams<AppConnectionAuthenticateParams>(msg);
                var principal = _controlPlane.Authenticate(craftPath, parameters.AppId, parameters.Credential);
                context.Connection.BindAppPrincipal(principal.PrincipalId, principal.AppId);
                return new { principal };
            }
            case ConnectionRefresh:
                return _controlPlane.Refresh(craftPath, RequirePrincipal(context.Connection));
            case ConnectionStatus:
            {
                var parameters = GetParams<AppConnectionStatusParams>(msg);
                var principal = context.Connection.IsAppPrincipalAuthenticated
                    ? _controlPlane.GetActivePrincipal(craftPath, context.Connection.AppPrincipalAppId!)
                    : _controlPlane.GetActivePrincipal(craftPath, parameters.AppId);
                return new
                {
                    appId = context.Connection.AppPrincipalAppId ?? parameters.AppId,
                    state = principal == null ? "notConnected" : "connected",
                    principal
                };
            }
            case ConnectionRevoke:
            {
                var parameters = GetParams<AppConnectionRevokeParams>(msg);
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
                    var bindings = _controlPlane.ListAppBindings(craftPath, parameters.AppId);
                    _controlPlane.RevokeApp(craftPath, parameters.AppId, userId);
                    if (context.SessionService is IThreadMcpRuntimeService runtime)
                        foreach (var binding in bindings)
                            await _coordinator.RemoveAsync(binding.ThreadId, binding.BindingId, runtime, context.CancellationToken);
                }
                var result = new { state = AppBindingStates.Revoked };
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("app/connection/changed", new { appId = parameters.AppId ?? context.Connection.AppPrincipalAppId, state = "revoked" }));
            }
            case SurfacePublish:
                return _controlPlane.PublishSurface(
                    craftPath,
                    RequirePrincipal(context.Connection),
                    GetParams<AppSurfacePublishParams>(msg));
            case SurfaceResolve:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<AppSurfaceResolveParams>(msg);
                EnsureCatalogApp(context, parameters.AppId);
                return _controlPlane.ResolveSurface(craftPath, parameters.AppId, parameters.SurfaceId);
            }
            case BindingEnable:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<ThreadAppBindingEnableParams>(msg);
                await context.SessionService.GetThreadAsync(parameters.ThreadId, context.CancellationToken);
                EnsureCatalogApp(context, parameters.AppId);
                var result = _controlPlane.Enable(craftPath, parameters.ThreadId, parameters.AppId, userId);
                result.Handoff = BuildHandoff(context, parameters.AppId, result.BindingRequestId, result.RequestToken, "bind");
                context.NotifyAppPrincipal?.Invoke(parameters.AppId, "app/binding/requested",
                    new { result.BindingRequestId, result.BindingId, parameters.ThreadId, parameters.AppId });
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { parameters.ThreadId, state = AppBindingStates.Connecting }));
            }
            case BindingRequestGet:
                return _controlPlane.GetBindingRequest(
                    craftPath,
                    GetParams<AppBindingRequestGetParams>(msg),
                    context.Connection.AppPrincipalId);
            case PrincipalBindingsList:
                return new { bindings = _controlPlane.ListPrincipalBindings(craftPath, RequirePrincipal(context.Connection)) };
            case ThreadBindingsList:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<ThreadAppBindingsListParams>(msg);
                return new { bindings = _controlPlane.ListThreadBindings(craftPath, parameters.ThreadId) };
            }
            case BindingRevoke:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<ThreadAppBindingRevokeParams>(msg);
                var binding = _controlPlane.RevokeBinding(craftPath, parameters.ThreadId, parameters.BindingId, userId);
                if (context.SessionService is IThreadMcpRuntimeService mcpRuntime)
                    await _coordinator.RemoveAsync(parameters.ThreadId, parameters.BindingId, mcpRuntime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, binding,
                    ("thread/appBindings/changed", new { parameters.ThreadId, parameters.BindingId, state = AppBindingStates.Revoked }));
            }
            case BindingActivate:
            {
                var runtime = RequireMcpRuntime(context.SessionService);
                var parameters = GetParams<AppBindingActivateParams>(msg);
                var result = await _coordinator.ActivateAsync(craftPath, RequirePrincipal(context.Connection),
                    parameters, runtime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { result.ThreadId, result.BindingId, result.State }));
            }
            case BindingRebind:
            {
                var runtime = RequireMcpRuntime(context.SessionService);
                var parameters = GetParams<AppBindingRebindParams>(msg);
                var result = await _coordinator.RebindAsync(craftPath, RequirePrincipal(context.Connection),
                    parameters, runtime, context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { result.ThreadId, result.BindingId, result.State }));
            }
            case BindingConfirm:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<ThreadAppBindingConfirmCapabilitiesParams>(msg);
                var result = await _coordinator.ConfirmAsync(craftPath,
                    parameters, userId,
                    RequireMcpRuntime(context.SessionService), context.CancellationToken);
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { parameters.ThreadId, parameters.BindingId, result.State }));
            }
            case SocialRequestCreate:
            {
                EnsureTrustedClient(context.Connection);
                var parameters = GetParams<ThreadSocialBindingRequestCreateParams>(msg);
                await context.SessionService.GetThreadAsync(parameters.ThreadId, context.CancellationToken);
                var result = _controlPlane.CreateSocialRequest(craftPath, parameters.ThreadId, parameters.ChannelName, userId);
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { parameters.ThreadId, state = AppBindingStates.Connecting }),
                    ("app/binding/requested", result));
            }
            case SocialRequestGet:
            {
                var channel = RequireChannel(context.Connection);
                return _controlPlane.GetSocialRequest(craftPath,
                    GetParams<SocialBindingRequestGetParams>(msg).Code, channel);
            }
            case SocialAccept:
            {
                var result = _controlPlane.AcceptSocial(craftPath, RequireChannel(context.Connection),
                    GetParams<SocialBindingAcceptParams>(msg));
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { result.ThreadId, result.BindingId, result.State }));
            }
            case SocialRebind:
            {
                var result = _controlPlane.RebindSocial(craftPath, RequireChannel(context.Connection),
                    GetParams<SocialBindingRebindParams>(msg));
                return await SendNotificationsAfterResponseAsync(msg, context, result,
                    ("thread/appBindings/changed", new { result.ThreadId, result.BindingId, result.State }));
            }
            case SocialResolve:
            {
                var channel = RequireChannel(context.Connection);
                var parameters = GetParams<AppSocialBindingResolveParams>(msg);
                if (!string.Equals(parameters.ChannelName, channel, StringComparison.OrdinalIgnoreCase))
                    throw AppServerErrors.AppPrincipalUnauthorized("A channel adapter may resolve only its own bindings.");
                return new { binding = _controlPlane.ResolveSocial(craftPath, channel, parameters.AccountId,
                    parameters.ConversationKind, parameters.ConversationId) };
            }
            case ThreadInputEnqueue:
            {
                var parameters = GetParams<AppThreadInputEnqueueParams>(msg);
                var principal = context.Connection.IsChannelAdapter
                    ? $"channel:{context.Connection.ChannelAdapterName!.ToLowerInvariant()}"
                    : RequirePrincipal(context.Connection);
                var binding = _controlPlane.AuthorizeThreadInput(craftPath, parameters.BindingId, principal);
                if (parameters.Input.Count == 0)
                    throw AppServerErrors.InvalidParams("'input' must not be empty.");
                await context.SessionService.EnsureThreadLoadedAsync(binding.ThreadId, context.CancellationToken);
                var contents = parameters.Input.Select(part => part.ToAIContent()).ToList();
                using (TurnTriggerScope.Set(new TurnTriggerInfo
                       {
                           Kind = "app",
                           Label = string.IsNullOrWhiteSpace(parameters.TriggerLabel) ? null : parameters.TriggerLabel.Trim(),
                           RefId = string.IsNullOrWhiteSpace(parameters.TriggerRefId) ? null : parameters.TriggerRefId.Trim()
                       }))
                {
                    var queued = await context.SessionService.EnqueueTurnInputAsync(
                        binding.ThreadId, contents, parameters.Sender, context.CancellationToken,
                        new SessionInputSnapshot
                        {
                            NativeInputParts = parameters.Input,
                            MaterializedInputParts = parameters.Input,
                            DisplayText = parameters.DisplayText ?? SessionWireMapper.BuildDisplayText(parameters.Input),
                            DeliveryBindingId = binding.Kind == "social" ? binding.BindingId : null
                        });
                    if (binding.Kind == "social")
                        _socialDeliveryCoordinator?.StartQueuedTurnDelivery(context.SessionService, craftPath,
                            binding.ThreadId, binding.BindingId, queued.Id, binding.AuthorityRevision,
                            context.CancellationToken);
                    if (string.Equals(parameters.StartPolicy, AppThreadInputStartPolicies.RunWhenIdle, StringComparison.Ordinal))
                        await context.SessionService.TryStartNextQueuedTurnAsync(binding.ThreadId, context.CancellationToken);
                    var thread = await context.SessionService.GetThreadAsync(binding.ThreadId, context.CancellationToken);
                    return new AppThreadInputEnqueueResult { QueuedInput = queued, QueuedInputs = thread.QueuedInputs.ToList() };
                }
            }
            default:
                throw AppServerErrors.MethodNotFound(method);
        }
    }

    private static IThreadMcpRuntimeService RequireMcpRuntime(ISessionService sessionService) =>
        sessionService as IThreadMcpRuntimeService
        ?? throw AppServerErrors.InvalidRequest("This host does not provide binding-scoped MCP sessions.");

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

    private static AppInfoWire MapCatalogApp(AppCatalogEntry entry) => new()
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
        NativeApp = new AppNativeApplicationWire
        {
            DisplayName = entry.Descriptor.NativeApplication.DisplayName,
            Protocol = entry.Descriptor.NativeApplication.Protocol,
            InstallUrl = entry.Descriptor.NativeApplication.InstallUrl
        },
        HandoffModes = entry.Descriptor.Connection.HandoffModes
    };

    private AppHandoffWire BuildHandoff(
        AppServerExtensionContext context,
        string appId,
        string requestId,
        string token,
        string operation)
    {
        var app = EnsureCatalogApp(context, appId);
        var mode = app.Descriptor.Connection.HandoffModes.First();
        return new AppHandoffWire
        {
            Mode = mode.Mode,
            Uri = string.IsNullOrWhiteSpace(mode.UriTemplate)
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
            .Replace("{request}", Uri.EscapeDataString(requestId), StringComparison.Ordinal)
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

    private void ApplyBindingState(string craftPath, string? threadId, IReadOnlyList<AppInfoWire> apps)
    {
        var bindings = string.IsNullOrWhiteSpace(threadId)
            ? Array.Empty<AppBindingWire>()
            : _controlPlane.ListThreadBindings(craftPath, threadId).ToArray();
        foreach (var app in apps)
        {
            if (_controlPlane.GetActivePrincipal(craftPath, app.AppId) != null)
                app.ConnectionState = AppConnectionStates.Connected;
            var binding = bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.AppId, app.AppId, StringComparison.Ordinal));
            app.BindingSummary = binding == null ? null : new ThreadAppBindingSummaryWire
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

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new();
        try
        {
            return JsonSerializer.Deserialize<T>(
                       msg.Params.Value.GetRawText(),
                       DotCraft.Protocol.SessionWireJsonOptions.Default) ?? new();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    private static async Task<object?> SendNotificationsAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object result,
        params (string Method, object Params)[] notifications)
    {
        await context.Transport.WriteMessageAsync(
            AppServerRequestHandler.BuildResponse(msg.Id, result),
            context.CancellationToken);
        foreach (var notification in notifications)
        {
            if (context.BroadcastTrustedNotification != null)
                context.BroadcastTrustedNotification(notification.Method, notification.Params);
            else
                await context.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = notification.Method,
                    @params = notification.Params
                }, context.CancellationToken);
        }
        return null;
    }
}
