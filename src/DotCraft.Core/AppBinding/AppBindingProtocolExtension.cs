using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.AppBinding;

/// <summary>
/// AppServer JSON-RPC extension for App Binding lifecycle and thread grant management.
/// </summary>
public sealed class AppBindingProtocolExtension(
    AppBindingService service,
    IAppConfigMonitor appConfigMonitor,
    SkillsLoader? skillsLoader = null,
    IReadOnlyList<string>? builtInPluginSourceRoots = null,
    IChannelRuntimeRegistry? channelRuntimeRegistry = null) : IAppServerProtocolExtension
{
    private readonly SocialChannelDeliveryCoordinator? _socialDeliveryCoordinator = channelRuntimeRegistry == null
        ? null
        : new SocialChannelDeliveryCoordinator(service, channelRuntimeRegistry);

    private const string AppList = "app/list";
    private const string AppView = "app/view";
    private const string AppConnectionStart = "app/connection/start";
    private const string AppConnectionRequestGet = "app/connection/request/get";
    private const string AppConnectionConnect = "app/connection/connect";
    private const string AppConnectionStatus = "app/connection/status";
    private const string AppConnectionRefreshMetadata = "app/connection/refreshMetadata";
    private const string AppConnectionRevoke = "app/connection/revoke";
    private const string AppBindingRequestCreate = "app/binding/request/create";
    private const string AppBindingRequestGet = "app/binding/request/get";
    private const string AppBindingRequestCancel = "app/binding/request/cancel";
    private const string AppBindingAccept = "app/binding/accept";
    private const string AppBindingAttachTools = "app/binding/attachTools";
    private const string AppBindingContextUpsert = "app/binding/context/upsert";
    private const string AppBindingContextRemove = "app/binding/context/remove";
    private const string AppThreadInputEnqueue = "app/threadInput/enqueue";
    private const string AppSocialBindingResolve = "app/socialBinding/resolve";
    private const string ThreadAppBindingsList = "thread/appBindings/list";
    private const string ThreadAppBindingsRevoke = "thread/appBindings/revoke";
    private const string ThreadAppBindingsRefresh = "thread/appBindings/refresh";
    private const string ThreadAppContextBlocksList = "thread/appContextBlocks/list";
    private const string UiResourceRead = "ui/resource/read";
    private const string UiToolCall = "ui/tool/call";
    private const string UiOpenLink = "ui/open-link";
    private const string UiUpdateModelContext = "ui/update-model-context";
    private const string UiToolApprovalRequest = "ui/tool/approval/request";

    private const string AppConnectionChanged = "app/connection/changed";
    private const string ThreadAppBindingsChanged = "thread/appBindings/changed";
    private const string DotCraftTeamsAppId = "com.dotharness.dotcraft-teams";

    public IReadOnlyCollection<string> Methods { get; } =
    [
        AppList,
        AppView,
        AppConnectionStart,
        AppConnectionRequestGet,
        AppConnectionConnect,
        AppConnectionStatus,
        AppConnectionRefreshMetadata,
        AppConnectionRevoke,
        AppBindingRequestCreate,
        AppBindingRequestGet,
        AppBindingRequestCancel,
        AppBindingAccept,
        AppBindingAttachTools,
        AppBindingContextUpsert,
        AppBindingContextRemove,
        AppThreadInputEnqueue,
        AppSocialBindingResolve,
        ThreadAppBindingsList,
        ThreadAppBindingsRevoke,
        ThreadAppBindingsRefresh,
        ThreadAppContextBlocksList,
        UiResourceRead,
        UiToolCall,
        UiOpenLink,
        UiUpdateModelContext
    ];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.WorkspaceCraftPath))
        {
            builder.Capabilities.AppBinding = true;
            builder.Capabilities.AppContextBlocks = true;
            builder.Capabilities.AppThreadInputEnqueue = true;
        }
    }

    public async Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
    {
        var method = msg.Method ?? string.Empty;
        var workspaceCraftPath = RequireWorkspaceCraftPath(method, context);
        var workspacePath = RequireHostWorkspacePath(method, context);
        var catalog = service.DiscoverCatalog(
            appConfigMonitor.Current,
            workspacePath,
            workspaceCraftPath,
            skillsLoader,
            builtInPluginSourceRoots);
        var userId = CurrentUserId(context);
        var ct = context.CancellationToken;

        // Interactive Tool UI host methods are negotiated (tool-result-presentation.md §3): a client
        // that did not declare interactiveToolUi support cannot serve or drive an app's ui:// surface,
        // so these methods are not honored for it.
        if (method is UiResourceRead or UiToolCall or UiOpenLink or UiUpdateModelContext
            && !context.Connection.SupportsInteractiveToolUi)
            throw AppServerErrors.MethodNotFound(method);

        switch (method)
        {
            case AppList:
            {
                var p = GetParams<AppListParams>(msg);
                if (!string.IsNullOrWhiteSpace(p.ThreadId))
                    await EnsureThreadAsync(context, p.ThreadId!, ct);
                return service.ListApps(catalog, workspaceCraftPath, userId, p);
            }

            case AppView:
            {
                var p = GetParams<AppViewParams>(msg);
                if (string.IsNullOrWhiteSpace(p.AppId))
                    throw AppServerErrors.InvalidParams("'appId' is required.");
                if (!string.IsNullOrWhiteSpace(p.ThreadId))
                    await EnsureThreadAsync(context, p.ThreadId!, ct);
                return service.ViewApp(catalog, workspaceCraftPath, userId, p.AppId, p.ThreadId);
            }

            case AppConnectionStart:
            {
                var p = GetParams<AppConnectionStartParams>(msg);
                var result = service.StartConnection(catalog, workspaceCraftPath, userId, p);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    AppConnectionChanged,
                    new
                    {
                        appId = result.AppId,
                        state = result.State,
                        previousState = AppConnectionStates.NotConnected,
                        diagnostic = (string?)null
                    });
            }

            case AppConnectionRequestGet:
            {
                var p = GetParams<AppConnectionRequestGetParams>(msg);
                return service.GetConnectionRequest(catalog, workspaceCraftPath, p);
            }

            case AppConnectionConnect:
            {
                var p = GetParams<AppConnectionConnectParams>(msg);
                var result = service.CompleteConnection(catalog, workspaceCraftPath, p);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    AppConnectionChanged,
                    new
                    {
                        appId = result.AppId,
                        state = result.State,
                        previousState = AppConnectionStates.Connecting,
                        diagnostic = result.Diagnostic
                    });
            }

            case AppConnectionStatus:
            {
                var p = GetParams<AppConnectionStatusParams>(msg);
                if (string.IsNullOrWhiteSpace(p.AppId))
                    throw AppServerErrors.InvalidParams("'appId' is required.");
                return service.GetConnectionStatus(catalog, workspaceCraftPath, userId, p.AppId);
            }

            case AppConnectionRefreshMetadata:
            {
                var p = GetParams<AppConnectionMetadataRefreshParams>(msg);
                if (string.IsNullOrWhiteSpace(p.AppId))
                    throw AppServerErrors.InvalidParams("'appId' is required.");
                // Authorized by the app-owned connection proof (see RefreshConnectionMetadata),
                // not the caller's user. Desktop surfaces observe the new endpoint on their next
                // app/connection/status read, so no cross-client notification is emitted here.
                return service.RefreshConnectionMetadata(catalog, workspaceCraftPath, p);
            }

            case AppConnectionRevoke:
            {
                var p = GetParams<AppConnectionRevokeParams>(msg);
                if (string.IsNullOrWhiteSpace(p.AppId))
                    throw AppServerErrors.InvalidParams("'appId' is required.");

                var activeBindings = service
                    .ListBindingsForAppUser(catalog, workspaceCraftPath, userId, p.AppId, includeRevoked: false)
                    .Where(binding => binding.State == AppBindingStates.Active)
                    .ToList();
                var previous = service.GetConnectionStatus(catalog, workspaceCraftPath, userId, p.AppId).State;
                var result = service.RevokeConnection(catalog, workspaceCraftPath, userId, p);
                await context.Transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildResponse(msg.Id, result),
                    context.CancellationToken);
                await SendNotificationAsync(
                    context,
                    AppConnectionChanged,
                    new
                    {
                        appId = result.AppId,
                        state = result.State,
                        previousState = previous,
                        diagnostic = result.Diagnostic
                    });
                foreach (var binding in activeBindings)
                {
                    ReleaseAppContextPage(context, binding.ThreadId);
                    await SendNotificationAsync(
                        context,
                        ThreadAppBindingsChanged,
                        new
                        {
                            threadId = binding.ThreadId,
                            bindingId = binding.BindingId,
                            appId = binding.AppId,
                            state = AppBindingStates.Offline,
                            previousState = binding.State,
                            changeKind = "offline"
                        });
                }

                return null;
            }

            case AppBindingRequestCreate:
            {
                var p = GetParams<AppBindingRequestCreateParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                var result = service.CreateBindingRequest(catalog, workspaceCraftPath, userId, p);
                if (string.Equals(result.State, AppBindingStates.Active, StringComparison.Ordinal))
                {
                    var binding = service.ListThreadBindings(catalog, workspaceCraftPath, p.ThreadId, includeRevoked: false)
                        .Bindings
                        .FirstOrDefault(candidate => string.Equals(candidate.BindingId, result.BindingRequestId, StringComparison.Ordinal));
                    if (binding != null)
                    {
                        return await SendThreadBindingChangedAfterResponseAsync(
                            msg,
                            context,
                            result,
                            binding,
                            previousState: null,
                            changeKind: "managedCreated",
                            refreshAgent: true);
                    }
                }

                return result;
            }

            case AppBindingRequestGet:
            {
                var p = GetParams<AppBindingRequestGetParams>(msg);
                var result = service.GetBindingRequest(
                    catalog,
                    workspaceCraftPath,
                    p,
                    channelAdapterName: context.Connection.IsChannelAdapter
                        ? context.Connection.ChannelAdapterName
                        : null,
                    requireSocialAuthorization: true);
                var thread = await EnsureThreadAsync(context, result.ThreadId, ct);
                result.ThreadTitle = thread.DisplayName;
                return result;
            }

            case AppBindingRequestCancel:
            {
                var p = GetParams<AppBindingRequestCancelParams>(msg);
                var result = service.CancelBindingRequest(workspaceCraftPath, p);
                return await SendNotificationAfterResponseAsync(
                    msg,
                    context,
                    result,
                    ThreadAppBindingsChanged,
                    new
                    {
                        threadId = result.ThreadId,
                        bindingRequestId = result.BindingRequestId,
                        appId = result.AppId,
                        state = result.State,
                        previousState = AppBindingStates.Pending,
                        changeKind = "cancelled"
                    });
            }

            case AppBindingAccept:
            {
                var p = GetParams<AppBindingAcceptParams>(msg);
                AuthorizeSocialBindingAccept(context.Connection, p);
                var result = service.AcceptBinding(catalog, workspaceCraftPath, p);
                return await SendThreadBindingChangedAfterResponseAsync(
                    msg,
                    context,
                    result,
                    result.Binding,
                    previousState: AppBindingStates.Pending,
                    changeKind: "accepted",
                    refreshAgent: false);
            }

            case AppBindingAttachTools:
            {
                var p = GetParams<AppBindingAttachToolsParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                var before = service.ListThreadBindings(catalog, workspaceCraftPath, p.ThreadId, includeRevoked: true)
                    .Bindings
                    .FirstOrDefault(binding => string.Equals(binding.BindingId, p.BindingId, StringComparison.Ordinal));
                var result = service.AttachTools(
                    catalog,
                    workspaceCraftPath,
                    context.Transport,
                    context.Connection,
                    p);
                return await SendThreadBindingChangedAfterResponseAsync(
                    msg,
                    context,
                    result,
                    result.Binding,
                    previousState: before?.State ?? AppBindingStates.Active,
                    changeKind: "toolsAttached",
                    refreshAgent: true);
            }

            case AppBindingContextUpsert:
            {
                var p = GetParams<AppBindingContextUpsertParams>(msg);
                var result = service.UpsertContextBlock(catalog, workspaceCraftPath, p);
                ReleaseAppContextPage(context, result.Block.ThreadId);
                return result;
            }

            case AppBindingContextRemove:
            {
                var p = GetParams<AppBindingContextRemoveParams>(msg);
                var result = service.RemoveContextBlock(catalog, workspaceCraftPath, p);
                ReleaseAppContextPage(context, result.ThreadId);
                return result;
            }

            case AppThreadInputEnqueue:
            {
                var p = GetParams<AppThreadInputEnqueueParams>(msg);
                var threadId = service.AuthorizeThreadInputEnqueue(catalog, workspaceCraftPath, p);
                var prepared = PrepareAppThreadInput(p);
                await context.SessionService.EnsureThreadLoadedAsync(threadId, ct);
                var deliveryBindingId = service.GetActiveSocialTarget(workspaceCraftPath, p.BindingId) == null
                    ? null
                    : p.BindingId.Trim();

                var triggerKind = string.Equals(p.AppId, DotCraftTeamsAppId, StringComparison.Ordinal)
                    ? "team"
                    : "app";
                using (TurnTriggerScope.Set(new TurnTriggerInfo
                       {
                           Kind = triggerKind,
                           Label = string.IsNullOrWhiteSpace(p.TriggerLabel) ? null : p.TriggerLabel.Trim(),
                           RefId = string.IsNullOrWhiteSpace(p.TriggerRefId) ? null : p.TriggerRefId.Trim()
                       }))
                {
                    var queued = await context.SessionService.EnqueueTurnInputAsync(
                        threadId,
                        prepared.Content,
                        sender: p.Sender,
                        ct,
                        new SessionInputSnapshot
                        {
                            NativeInputParts = prepared.NativeInputParts,
                            MaterializedInputParts = prepared.MaterializedInputParts,
                            DisplayText = prepared.DisplayText,
                            DeliveryBindingId = deliveryBindingId
                        });
                    service.RecordThreadInputEnqueued(
                        workspaceCraftPath,
                        p.BindingId,
                        queued.Id,
                        triggerKind,
                        p.TriggerLabel,
                        p.TriggerRefId);
                    if (!string.IsNullOrWhiteSpace(deliveryBindingId))
                    {
                        _socialDeliveryCoordinator?.StartQueuedTurnDelivery(
                            context.SessionService,
                            workspaceCraftPath,
                            threadId,
                            deliveryBindingId,
                            queued.Id,
                            ct);
                    }

                    var startPolicy = NormalizeStartPolicy(p.StartPolicy);
                    if (string.Equals(startPolicy, AppThreadInputStartPolicies.RunWhenIdle, StringComparison.Ordinal))
                        await context.SessionService.TryStartNextQueuedTurnAsync(threadId, ct);

                    var thread = await context.SessionService.GetThreadAsync(threadId, ct);
                    return new AppThreadInputEnqueueResult
                    {
                        QueuedInput = queued,
                        QueuedInputs = thread.QueuedInputs.ToList()
                    };
                }
            }

            case AppSocialBindingResolve:
            {
                var p = GetParams<AppSocialBindingResolveParams>(msg);
                AuthorizeSocialBindingResolve(context.Connection, p);
                return service.ResolveSocialBinding(catalog, workspaceCraftPath, p);
            }

            case ThreadAppBindingsList:
            {
                var p = GetParams<ThreadAppBindingsListParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                return service.ListThreadBindings(catalog, workspaceCraftPath, p.ThreadId, p.IncludeRevoked == true);
            }

            case ThreadAppContextBlocksList:
            {
                var p = GetParams<ThreadAppContextBlocksListParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                return service.ListThreadContextBlocks(workspaceCraftPath, p.ThreadId, p.IncludeInactive == true);
            }

            case UiResourceRead:
            {
                var p = GetParams<UiResourceReadParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                return await service.ReadUiResourceAsync(workspaceCraftPath, p.ThreadId, p.Namespace, p.Uri, ct);
            }

            case UiToolCall:
            {
                var p = GetParams<UiToolCallParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                return await service.InvokeUiToolAsync(
                    workspaceCraftPath,
                    p.ThreadId,
                    p.Namespace,
                    p.Tool,
                    p.Arguments,
                    p.SourceCallId,
                    userId,
                    context.SessionService,
                    BuildUiToolApprovalGate(context, p.ThreadId, p.Namespace, p.Tool),
                    ct);
            }

            case UiOpenLink:
            {
                var p = GetParams<UiOpenLinkParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                return service.OpenLink(catalog, workspaceCraftPath, p.ThreadId, p.Namespace, p.Url, p.SourceCallId, userId);
            }

            case UiUpdateModelContext:
            {
                var p = GetParams<UiUpdateModelContextParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                var result = service.UpdateModelContext(
                    workspaceCraftPath,
                    p.ThreadId,
                    p.Namespace,
                    p.SourceCallId,
                    p.Title,
                    p.Content,
                    userId);
                ReleaseAppContextPage(context, p.ThreadId);
                return result;
            }

            case ThreadAppBindingsRevoke:
            {
                var p = GetParams<ThreadAppBindingRevokeParams>(msg);
                var thread = await EnsureThreadAsync(context, p.ThreadId, ct);
                var before = service.ListThreadBindings(catalog, workspaceCraftPath, p.ThreadId, includeRevoked: true)
                    .Bindings
                    .FirstOrDefault(binding => string.Equals(binding.BindingId, p.BindingId, StringComparison.Ordinal));
                var result = service.RevokeBinding(workspaceCraftPath, p);
                ReleaseAppContextPage(context, p.ThreadId);
                await InterruptActiveTurnAsync(context, thread);
                var after = before == null
                    ? new ThreadAppBindingWire
                    {
                        BindingId = result.BindingId,
                        ThreadId = p.ThreadId,
                        State = result.State
                    }
                    : new ThreadAppBindingWire
                    {
                        BindingId = before.BindingId,
                        ThreadId = before.ThreadId,
                        AppId = before.AppId,
                        GrantId = before.GrantId,
                        DisplayName = before.DisplayName,
                        Icon = before.Icon,
                        ToolNamespace = before.ToolNamespace,
                        State = result.State,
                        ConnectionState = before.ConnectionState,
                        GrantedScopes = before.GrantedScopes.ToList(),
                        AttachedToolCount = before.AttachedToolCount,
                        ExpiresAt = before.ExpiresAt,
                        LastChangedAt = before.LastChangedAt,
                        ApprovalMode = before.ApprovalMode,
                        AuditRef = before.AuditRef,
                        Diagnostic = before.Diagnostic,
                        BindingKind = before.BindingKind,
                        SocialTarget = before.SocialTarget,
                        ExposureRevision = before.ExposureRevision
                    };
                return await SendThreadBindingChangedAfterResponseAsync(
                    msg,
                    context,
                    result,
                    after,
                    previousState: before?.State,
                    changeKind: "revoked",
                    refreshAgent: true);
            }

            case ThreadAppBindingsRefresh:
            {
                var p = GetParams<ThreadAppBindingRefreshParams>(msg);
                await EnsureThreadAsync(context, p.ThreadId, ct);
                var before = service.ListThreadBindings(catalog, workspaceCraftPath, p.ThreadId, includeRevoked: true)
                    .Bindings
                    .ToDictionary(binding => binding.BindingId, StringComparer.Ordinal);
                var result = service.RefreshBindings(catalog, workspaceCraftPath, p);
                ReleaseAppContextPage(context, p.ThreadId);
                await RefreshThreadAgentAsync(context, p.ThreadId);
                await context.Transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildResponse(msg.Id, result),
                    context.CancellationToken);
                foreach (var refreshed in result.Bindings)
                {
                    before.TryGetValue(refreshed.BindingId, out var existing);
                    await SendNotificationAsync(
                        context,
                        ThreadAppBindingsChanged,
                        new
                        {
                            threadId = p.ThreadId,
                            bindingId = refreshed.BindingId,
                            appId = existing?.AppId,
                            state = refreshed.State,
                            previousState = existing?.State,
                            changeKind = "refreshed"
                        });
                }

                return null;
            }

            default:
                throw AppServerErrors.MethodNotFound(method);
        }
    }

    private static async Task<SessionThread> EnsureThreadAsync(
        AppServerExtensionContext context,
        string threadId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        return await context.SessionService.GetThreadAsync(threadId, ct);
    }

    private static PreparedAppThreadInput PrepareAppThreadInput(AppThreadInputEnqueueParams p)
    {
        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var nativeParts = InputMaterializationService.NormalizeInputParts(p.Input);
        if (nativeParts.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one valid part.");
        foreach (var part in nativeParts)
        {
            if (part.Type is "commandRef" or "skillRef")
            {
                throw AppServerErrors.InvalidParams(
                    "'app/threadInput/enqueue' does not accept commandRef or skillRef input parts.");
            }
        }

        var displayText = string.IsNullOrWhiteSpace(p.DisplayText)
            ? SessionWireMapper.BuildDisplayText(nativeParts)
            : p.DisplayText.Trim();
        return new PreparedAppThreadInput
        {
            NativeInputParts = nativeParts,
            MaterializedInputParts = nativeParts,
            DisplayText = displayText,
            Content = nativeParts.Select(part => part.ToAIContent()).ToList()
        };
    }

    private static string NormalizeStartPolicy(string? startPolicy)
    {
        if (string.IsNullOrWhiteSpace(startPolicy))
            return AppThreadInputStartPolicies.QueueOnly;
        var normalized = startPolicy.Trim();
        if (!AppThreadInputStartPolicies.IsKnown(normalized))
            throw AppServerErrors.InvalidParams($"Unknown app thread input startPolicy '{startPolicy}'.");
        return normalized;
    }

    private static void AuthorizeSocialBindingResolve(
        AppServerConnection connection,
        AppSocialBindingResolveParams p)
    {
        if (!connection.IsChannelAdapter || string.IsNullOrWhiteSpace(connection.ChannelAdapterName))
            throw AppServerErrors.InvalidParams("'app/socialBinding/resolve' may only be called by channel adapters.");
        if (string.IsNullOrWhiteSpace(p.ChannelName))
            throw AppServerErrors.InvalidParams("'channelName' is required.");
        if (!string.Equals(connection.ChannelAdapterName, p.ChannelName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Channel adapter cannot resolve bindings for another channel.");
        if (!string.Equals(p.AppId, ChannelAppId(p.ChannelName), StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams("Social binding appId does not match channelName.");
    }

    private static void AuthorizeSocialBindingAccept(
        AppServerConnection connection,
        AppBindingAcceptParams p)
    {
        if (p.SocialTarget == null)
            return;
        if (!connection.IsChannelAdapter || string.IsNullOrWhiteSpace(connection.ChannelAdapterName))
            throw AppServerErrors.InvalidParams("Social channel bindings may only be accepted by channel adapters.");
        if (!string.Equals(connection.ChannelAdapterName, p.SocialTarget.ChannelName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Channel adapter cannot accept bindings for another channel.");
    }

    private static string ChannelAppId(string channelName) =>
        $"com.dotharness.channel.{channelName.Trim().ToLowerInvariant()}";

    private sealed class PreparedAppThreadInput
    {
        public IReadOnlyList<SessionWireInputPart> NativeInputParts { get; init; } = [];

        public IReadOnlyList<SessionWireInputPart> MaterializedInputParts { get; init; } = [];

        public string DisplayText { get; init; } = string.Empty;

        public List<AIContent> Content { get; init; } = [];
    }

    private static async Task<object?> SendThreadBindingChangedAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object result,
        ThreadAppBindingWire binding,
        string? previousState,
        string changeKind,
        bool refreshAgent)
    {
        ReleaseAppContextPage(context, binding.ThreadId);
        if (refreshAgent)
            await RefreshThreadAgentAsync(context, binding.ThreadId);
        return await SendNotificationAfterResponseAsync(
            msg,
            context,
            result,
            ThreadAppBindingsChanged,
            new
            {
                threadId = binding.ThreadId,
                bindingId = binding.BindingId,
                appId = binding.AppId,
                state = binding.State,
                previousState,
                changeKind
            });
    }

    private static async Task RefreshThreadAgentAsync(AppServerExtensionContext context, string threadId)
    {
        if (context.SessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(threadId, context.CancellationToken);
    }

    private static void ReleaseAppContextPage(AppServerExtensionContext context, string? threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
            context.ContextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.AppContextBlocks());
    }

    private static async Task InterruptActiveTurnAsync(AppServerExtensionContext context, SessionThread thread)
    {
        var turn = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (turn == null)
            return;

        try
        {
            await context.SessionService.CancelTurnAsync(thread.Id, turn.Id, context.CancellationToken);
        }
        catch (AppServerException ex) when (ex.Code is AppServerErrors.TurnNotFoundCode or AppServerErrors.TurnNotRunningCode)
        {
            // The turn may have completed between the revoke read and cancellation request.
        }
    }

    private static async Task<object?> SendNotificationAfterResponseAsync(
        AppServerIncomingMessage msg,
        AppServerExtensionContext context,
        object result,
        string notificationMethod,
        object notificationParams)
    {
        await context.Transport.WriteMessageAsync(
            AppServerRequestHandler.BuildResponse(msg.Id, result),
            context.CancellationToken);
        await SendNotificationAsync(context, notificationMethod, notificationParams);
        return null;
    }

    private static Task SendNotificationAsync(
        AppServerExtensionContext context,
        string method,
        object @params) =>
        context.Transport.WriteMessageAsync(
            new
            {
                jsonrpc = "2.0",
                method,
                @params
            },
            context.CancellationToken);

    private static string RequireWorkspaceCraftPath(string method, AppServerExtensionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkspaceCraftPath))
            throw AppServerErrors.MethodNotFound(method);
        return context.WorkspaceCraftPath!;
    }

    private static string RequireHostWorkspacePath(string method, AppServerExtensionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HostWorkspacePath))
            throw AppServerErrors.MethodNotFound(method);
        return context.HostWorkspacePath!;
    }

    private static string CurrentUserId(AppServerExtensionContext context) =>
        string.IsNullOrWhiteSpace(context.Connection.ClientInfo?.Name)
            ? "appserver"
            : context.Connection.ClientInfo!.Name;

    private static T GetParams<T>(AppServerIncomingMessage msg)
        where T : new()
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

    /// <summary>
    /// Builds the decoupled mutate‑approval gate for a UI‑initiated <c>ui/tool/call</c> (M‑v): it
    /// sends a <c>ui/tool/approval/request</c> to the Desktop host (which surfaces it in the shared
    /// approval composer) and awaits the decision. Returns null when the client cannot prompt
    /// (no approval capability), so the service rejects the mutating call instead.
    /// </summary>
    private static UiToolApprovalGate? BuildUiToolApprovalGate(
        AppServerExtensionContext context,
        string threadId,
        string? @namespace,
        string tool)
    {
        var connection = context.Connection;
        var transport = context.Transport;
        if (!connection.SupportsApproval)
            return null;

        return async (info, gateCt) =>
        {
            AppServerIncomingMessage response;
            try
            {
                response = await transport.SendClientRequestAsync(
                    UiToolApprovalRequest,
                    new UiToolApprovalRequestParams
                    {
                        ThreadId = threadId,
                        ApprovalId = $"uiapproval_{Guid.NewGuid():N}",
                        Namespace = @namespace,
                        Tool = tool,
                        ApprovalType = info.ApprovalType,
                        Operation = info.Operation,
                        Target = info.Target
                    },
                    gateCt,
                    TimeSpan.FromSeconds(120));
            }
            catch (OperationCanceledException) when (gateCt.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Transport failure / timeout / client dropped → treat as not approved.
                return false;
            }

            return IsApprovalAccepted(response);
        };
    }

    private static bool IsApprovalAccepted(AppServerIncomingMessage response)
    {
        if (!response.Result.HasValue)
            return false;
        try
        {
            var result = JsonSerializer.Deserialize<AppServerApprovalResponseResult>(
                response.Result.Value.GetRawText(),
                SessionWireJsonOptions.Default);
            return result?.Decision is "accept" or "acceptForSession" or "acceptAlways";
        }
        catch
        {
            return false;
        }
    }
}
