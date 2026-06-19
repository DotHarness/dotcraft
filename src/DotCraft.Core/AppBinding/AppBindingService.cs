using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

/// <summary>
/// Coordinates App Binding discovery, durable lifecycle state, and runtime tool dispatch.
/// </summary>
public sealed class AppBindingService
{
    private readonly AppBindingStoreAccessor _storeAccessor = new();
    private readonly AppBindingAttachmentRegistry _attachments = new();
    private readonly IReadOnlyDictionary<string, IManagedAppBindingRuntime> _managedRuntimesByAppId;
    private readonly AppBindingWireMapper _wireMapper;
    private readonly AppConnectionService _connections;
    private readonly AppContextBlockService _contextBlocks;
    private readonly AppToolAttachmentService _tools;
    private readonly AppBindingLifecycleService _lifecycle;
    private readonly AppUiInteractionService _ui;

    /// <summary>
    /// Raised after app-supplied context blocks for a thread change.
    /// </summary>
    public event Action<string>? AppContextBlocksChanged;

    /// <summary>
    /// Creates the service with optional first-party managed App Binding runtimes.
    /// </summary>
    public AppBindingService(IEnumerable<IManagedAppBindingRuntime>? managedRuntimes = null)
    {
        _managedRuntimesByAppId = (managedRuntimes ?? [])
            .Where(runtime => !string.IsNullOrWhiteSpace(runtime.Descriptor.AppId))
            .GroupBy(runtime => runtime.Descriptor.AppId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _wireMapper = new AppBindingWireMapper(_managedRuntimesByAppId);
        _connections = new AppConnectionService(_storeAccessor, _attachments);
        _contextBlocks = new AppContextBlockService(_storeAccessor, _managedRuntimesByAppId, NotifyAppContextBlocksChanged);
        _tools = new AppToolAttachmentService(this, _storeAccessor, _attachments, _managedRuntimesByAppId);
        _lifecycle = new AppBindingLifecycleService(this, _storeAccessor, _attachments, _tools, _managedRuntimesByAppId);
        _ui = new AppUiInteractionService(_storeAccessor, _tools, NotifyAppContextBlocksChanged);
    }

    public AppCatalogSnapshot DiscoverCatalog(
        AppConfig config,
        string workspacePath,
        string workspaceCraftPath,
        SkillsLoader? skillsLoader = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        var catalog = AppBindingCatalog.Discover(config, workspacePath, workspaceCraftPath, skillsLoader, builtInPluginSourceRoots);
        if (_managedRuntimesByAppId.Count == 0)
            return catalog;

        var entries = catalog.Entries.ToList();
        var diagnostics = catalog.Diagnostics.ToList();
        foreach (var runtime in _managedRuntimesByAppId.Values.OrderBy(runtime => runtime.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var owningPluginId = runtime.OwningPluginId;
            var owningPlugin = string.IsNullOrWhiteSpace(owningPluginId)
                ? CreateSyntheticManagedPlugin(runtime.Descriptor, workspaceCraftPath)
                : catalog.Plugins
                    .FirstOrDefault(plugin => plugin.Installed
                                              && PluginIds.EqualsCanonical(plugin.Manifest.Id, owningPluginId));
            if (owningPlugin == null)
                continue;

            if (entries.Any(entry =>
                    string.Equals(entry.Descriptor.AppId, runtime.Descriptor.AppId, StringComparison.Ordinal)
                    || string.Equals(entry.Descriptor.ToolNamespace, runtime.Descriptor.ToolNamespace, StringComparison.Ordinal)))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "ManagedAppBindingCollision",
                    $"Managed app '{runtime.Descriptor.AppId}' was skipped because a catalog app already uses its appId or toolNamespace.",
                    runtime.Descriptor.AppId));
                continue;
            }

            entries.Add(new AppCatalogEntry(
                CloneDescriptor(runtime.Descriptor),
                owningPlugin,
                [])
            {
                ManagedRuntime = new ManagedAppBindingCatalogMetadata(
                    owningPlugin.Manifest.Id,
                    runtime.CatalogSurfaces,
                    runtime.RequiresExternalConnection)
            });
        }

        return new AppCatalogSnapshot(entries, diagnostics)
        {
            Plugins = entries
                .Select(entry => entry.Plugin)
                .Concat(catalog.Plugins)
                .GroupBy(plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray()
        };
    }

    public AppListResult ListApps(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppListParams p)
    {
        var state = GetStore(workspaceCraftPath).Snapshot();
        var includeDisabled = p.IncludeDisabled != false;
        var includeCatalog = p.IncludeCatalog != false;
        var surface = AppBindingCatalogSurfaces.Normalize(p.Surface);
        var apps = catalog.Entries
            .Where(entry => (includeCatalog || entry.Plugin.Installed)
                            && (includeDisabled || entry.Plugin.Enabled)
                            && AppBindingWireMapper.IsVisibleOnAppListSurface(entry, surface))
            .Select(entry => _wireMapper.MapAppInfo(entry, state, userId, p.ThreadId, surface))
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AppListResult { Apps = apps };
    }

    public AppViewResult ViewApp(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        string appId,
        string? threadId)
    {
        var entry = FindApp(catalog, appId);
        var state = GetStore(workspaceCraftPath).Snapshot();
        return new AppViewResult { App = _wireMapper.MapAppInfo(entry, state, userId, threadId, AppBindingCatalogSurfaces.SdkDefault) };
    }

    public AppConnectionStartResult StartConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppConnectionStartParams p) =>
        _connections.StartConnection(catalog, workspaceCraftPath, userId, p);

    public AppConnectionRequestGetResult GetConnectionRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionRequestGetParams p) =>
        _connections.GetConnectionRequest(catalog, workspaceCraftPath, p);

    public AppConnectionStatusWire CompleteConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionConnectParams p) =>
        _connections.CompleteConnection(catalog, workspaceCraftPath, p);

    public AppConnectionStatusWire GetConnectionStatus(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        string appId) =>
        _connections.GetConnectionStatus(catalog, workspaceCraftPath, userId, appId);

    /// <summary>
    /// Refreshes only the <c>publicMetadata</c> of an existing connected connection
    /// (for example a new dynamic loopback surface port). The refresh is initiated by
    /// the app over its own loopback app-server connection, which does not share the
    /// Desktop initiator's user id; authority is therefore the app-owned connection
    /// proof, not the caller's user. Never creates a connection or changes scope.
    /// </summary>
    public AppConnectionStatusWire RefreshConnectionMetadata(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionMetadataRefreshParams p) =>
        _connections.RefreshConnectionMetadata(catalog, workspaceCraftPath, p);

    public AppConnectionStatusWire RevokeConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppConnectionRevokeParams p) =>
        _connections.RevokeConnection(catalog, workspaceCraftPath, userId, p);

    public AppBindingRequestCreateResult CreateBindingRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppBindingRequestCreateParams p) =>
        _lifecycle.CreateBindingRequest(catalog, workspaceCraftPath, userId, p);

    public AppBindingRequestGetResult GetBindingRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingRequestGetParams p,
        string? threadTitle = null) =>
        _lifecycle.GetBindingRequest(catalog, workspaceCraftPath, p, threadTitle);

    public AppBindingRequestCancelResult CancelBindingRequest(
        string workspaceCraftPath,
        AppBindingRequestCancelParams p) =>
        _lifecycle.CancelBindingRequest(workspaceCraftPath, p);

    public AppBindingAcceptResult AcceptBinding(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingAcceptParams p) =>
        _lifecycle.AcceptBinding(catalog, workspaceCraftPath, p);

    public AppSocialBindingResolveResult ResolveSocialBinding(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppSocialBindingResolveParams p) =>
        _lifecycle.ResolveSocialBinding(catalog, workspaceCraftPath, p);

    public AppBindingAttachToolsResult AttachTools(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        IAppServerTransport transport,
        AppServerConnection connection,
        AppBindingAttachToolsParams p) =>
        _tools.AttachTools(catalog, workspaceCraftPath, transport, connection, p);

    /// <summary>
    /// Creates or repairs an active binding for a first-party managed App Binding runtime.
    /// </summary>
    public ThreadAppBindingWire EnsureManagedBinding(
        string workspaceCraftPath,
        string threadId,
        string appId,
        string userId,
        string grantId,
        IReadOnlyList<string> grantedScopes,
        IReadOnlyList<DynamicToolSpec>? tools = null,
        AppDescriptor? descriptorOverride = null) =>
        _lifecycle.EnsureManagedBinding(
            workspaceCraftPath,
            threadId,
            appId,
            userId,
            grantId,
            grantedScopes,
            tools,
            descriptorOverride);

    public AppBindingContextUpsertResult UpsertContextBlock(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingContextUpsertParams p) =>
        _contextBlocks.UpsertContextBlock(catalog, workspaceCraftPath, p);

    /// <summary>
    /// Creates or replaces one context block for a managed App Binding runtime.
    /// </summary>
    public AppBindingContextUpsertResult UpsertManagedContextBlock(
        string workspaceCraftPath,
        AppBindingContextUpsertParams p) =>
        _contextBlocks.UpsertManagedContextBlock(workspaceCraftPath, p);

    public AppBindingContextRemoveResult RemoveContextBlock(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingContextRemoveParams p) =>
        _contextBlocks.RemoveContextBlock(catalog, workspaceCraftPath, p);

    private void NotifyAppContextBlocksChanged(string threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
            AppContextBlocksChanged?.Invoke(threadId);
    }

    /// <summary>
    /// Validates an App Binding-safe queued input request and returns the binding target thread id.
    /// </summary>
    public string AuthorizeThreadInputEnqueue(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppThreadInputEnqueueParams p) =>
        _contextBlocks.AuthorizeThreadInputEnqueue(catalog, workspaceCraftPath, p);

    /// <summary>
    /// Records audit for an App Binding-safe queued input write.
    /// </summary>
    public void RecordThreadInputEnqueued(
        string workspaceCraftPath,
        string bindingId,
        string queuedInputId,
        string triggerKind,
        string? triggerLabel,
        string? triggerRefId) =>
        _contextBlocks.RecordThreadInputEnqueued(
            workspaceCraftPath,
            bindingId,
            queuedInputId,
            triggerKind,
            triggerLabel,
            triggerRefId);

    public ThreadAppContextBlocksListResult ListThreadContextBlocks(
        string workspaceCraftPath,
        string threadId,
        bool includeInactive) =>
        _contextBlocks.ListThreadContextBlocks(workspaceCraftPath, threadId, includeInactive);

    public string? BuildAppContextPromptSection(string workspaceCraftPath, string threadId) =>
        _contextBlocks.BuildAppContextPromptSection(workspaceCraftPath, threadId);

    public ThreadAppBindingsListResult ListThreadBindings(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId,
        bool includeRevoked) =>
        _lifecycle.ListThreadBindings(catalog, workspaceCraftPath, threadId, includeRevoked);

    public SocialChannelTargetWire? GetSocialTarget(string workspaceCraftPath, string bindingId)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
            return null;

        var binding = GetStore(workspaceCraftPath).Snapshot().Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal));
        return binding?.SocialTarget;
    }

    public SocialChannelTargetWire? GetActiveSocialTarget(string workspaceCraftPath, string bindingId)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
            return null;

        var binding = GetStore(workspaceCraftPath).Snapshot().Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal));
        if (binding == null
            || binding.State != AppBindingStates.Active
            || !string.Equals(binding.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
            || binding.SocialTarget == null)
        {
            return null;
        }

        if (binding.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            return null;

        return binding.SocialTarget;
    }

    public void RecordSocialDelivery(
        string workspaceCraftPath,
        string bindingId,
        string turnId,
        bool delivered,
        string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(bindingId) || string.IsNullOrWhiteSpace(turnId))
            return;

        GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = state.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.BindingId, bindingId.Trim(), StringComparison.Ordinal));
            AddAudit(
                state,
                delivered ? "binding.socialDelivery.delivered" : "binding.socialDelivery.failed",
                binding?.ThreadId,
                binding?.BindingId ?? bindingId.Trim(),
                binding?.AppId,
                binding?.UserId,
                string.IsNullOrWhiteSpace(diagnostic) ? turnId : $"{turnId}:{diagnostic}");
            return true;
        });
    }

    public IReadOnlyList<ThreadAppBindingWire> ListBindingsForAppUser(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        string appId,
        bool includeRevoked) =>
        _lifecycle.ListBindingsForAppUser(catalog, workspaceCraftPath, userId, appId, includeRevoked);

    public IReadOnlyList<ThreadAppBindingWire> MoveActiveBindingsOfflineForApps(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        IReadOnlyCollection<string> appIds,
        string diagnostic,
        string auditEvent) =>
        _lifecycle.MoveActiveBindingsOfflineForApps(catalog, workspaceCraftPath, appIds, diagnostic, auditEvent);

    public List<ThreadAppBindingSummaryWire> ListThreadBindingSummaries(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId) =>
        _lifecycle.ListThreadBindingSummaries(catalog, workspaceCraftPath, threadId);

    public IReadOnlyList<ThreadAppBindingWire> RevokeBindingsForDeletedThread(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId) =>
        _lifecycle.RevokeBindingsForDeletedThread(catalog, workspaceCraftPath, threadId);

    public ThreadAppBindingRevokeResult RevokeBinding(
        string workspaceCraftPath,
        ThreadAppBindingRevokeParams p) =>
        _lifecycle.RevokeBinding(workspaceCraftPath, p);

    public ThreadAppBindingRefreshResult RefreshBindings(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        ThreadAppBindingRefreshParams p) =>
        _lifecycle.RefreshBindings(catalog, workspaceCraftPath, p);

    public IReadOnlyList<AITool> CreateRuntimeToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames) =>
        _tools.CreateRuntimeToolsForThread(thread, reservedToolNames);

    internal async ValueTask<DynamicToolCallResult> InvokeAttachedToolAsync(
        string workspaceCraftPath,
        string bindingId,
        DynamicToolSpec spec,
        string executionThreadId,
        string executionTurnId,
        ISessionService? executionSessionService,
        string callId,
        JsonObject arguments,
        CancellationToken cancellationToken) =>
        await _tools.InvokeAttachedToolAsync(
            workspaceCraftPath,
            bindingId,
            spec,
            executionThreadId,
            executionTurnId,
            executionSessionService,
            callId,
            arguments,
            cancellationToken);

    /// <summary>
    /// Invokes an app-bound tool on behalf of its Interactive Tool UI (MCP Apps <c>callTool</c>).
    /// The call is decoupled from the agent conversation: it produces no turn or item, is gated by
    /// App Binding scope + UI visibility, is recorded on the audit trail, and returns its result to
    /// the host (which relays it to the UI). The model only learns of UI state via
    /// <c>ui/update-model-context</c> or <c>ui/message</c>. See appserver-protocol.md §11.3.2.
    /// </summary>
    internal async ValueTask<DynamicToolCallResult> InvokeUiToolAsync(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string tool,
        JsonObject arguments,
        string? sourceCallId,
        string userId,
        ISessionService? sessionService,
        UiToolApprovalGate? approvalGate,
        CancellationToken cancellationToken) =>
        await _ui.InvokeUiToolAsync(
            workspaceCraftPath,
            threadId,
            @namespace,
            tool,
            arguments,
            sourceCallId,
            userId,
            sessionService,
            approvalGate,
            cancellationToken);

    internal UiOpenLinkResult OpenLink(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string url,
        string? sourceCallId,
        string userId) =>
        _ui.OpenLink(catalog, workspaceCraftPath, threadId, @namespace, url, sourceCallId, userId);

    internal UiUpdateModelContextResult UpdateModelContext(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string sourceCallId,
        string? title,
        string? content,
        string userId) =>
        _ui.UpdateModelContext(workspaceCraftPath, threadId, @namespace, sourceCallId, title, content, userId);

    internal async ValueTask<UiResourceReadResult> ReadUiResourceAsync(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string uri,
        CancellationToken cancellationToken) =>
        await _ui.ReadUiResourceAsync(workspaceCraftPath, threadId, @namespace, uri, cancellationToken);
    internal AppBindingStore GetStore(string workspaceCraftPath) =>
        _storeAccessor.GetStore(workspaceCraftPath);

    internal bool IsBindingConnectionUsable(AppBindingStateDocument state, AppBindingRecord binding) =>
        IsManagedAppWithoutExternalConnection(binding.AppId)
        || IsConnectionUsable(FindConnection(state, binding.UserId, binding.AppId));

    internal bool IsManagedAppWithoutExternalConnection(string appId) =>
        _managedRuntimesByAppId.TryGetValue(appId, out var runtime)
        && runtime.RequiresExternalConnection == false;

    internal static void ValidateRequestedScopes(AppDescriptor descriptor, IReadOnlyList<string> requestedScopes)
    {
        var known = descriptor.Scopes.Select(scope => scope.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var scope in requestedScopes)
        {
            if (!known.Contains(scope))
                throw AppServerErrors.InvalidParams($"Requested scope '{scope}' is not declared by app '{descriptor.AppId}'.");
        }
    }

    internal static void ValidateRequestedTools(AppDescriptor descriptor, IReadOnlyList<string>? requestedTools)
    {
        if (requestedTools is not { Count: > 0 })
            return;

        if (descriptor.DynamicToolCatalog.Enabled)
            return;

        var known = descriptor.ToolCatalog.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in requestedTools)
        {
            if (!known.Contains(tool))
                throw AppServerErrors.InvalidParams($"Requested tool '{tool}' is not declared by app '{descriptor.AppId}'.");
        }
    }

    internal static void ValidateGrantedScopes(
        AppDescriptor descriptor,
        IReadOnlyList<string> requestedScopes,
        IReadOnlyList<string> grantedScopes)
    {
        ValidateRequestedScopes(descriptor, grantedScopes);
        var requested = requestedScopes.ToHashSet(StringComparer.Ordinal);
        foreach (var scope in grantedScopes)
        {
            if (!requested.Contains(scope))
                throw AppServerErrors.InvalidParams($"Granted scope '{scope}' was not requested.");
        }
    }

    internal static List<DynamicToolSpec> ValidateAttachedTools(
        AppDescriptor descriptor,
        AppBindingRecord binding,
        AppBindingAttachToolsParams p,
        List<string> warnings,
        bool allowDirectMutatingToolExposure = false)
    {
        var catalogByName = descriptor.ToolCatalog.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        AddDynamicAttachedToolCatalog(descriptor, p.ToolCatalog, catalogByName);
        var grantedScopes = binding.GrantedScopes.ToHashSet(StringComparer.Ordinal);
        var accepted = new List<DynamicToolSpec>();
        var direct = p.DirectToolNames?.ToHashSet(StringComparer.Ordinal) ?? [];
        var deferred = p.DeferredToolNames?.ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var tool in p.Tools)
        {
            if (!string.Equals(tool.Namespace, descriptor.ToolNamespace, StringComparison.Ordinal))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' must use namespace '{descriptor.ToolNamespace}'.");

            if (!catalogByName.TryGetValue(tool.Name, out var catalogEntry))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' is not declared in the app tool catalog.");

            if (!grantedScopes.Contains(catalogEntry.Scope))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' requires ungranted scope '{catalogEntry.Scope}'.");

            var clone = CloneSpec(tool);
            var requestedDirect = direct.Contains(tool.Name);
            var requestedDeferred = deferred.Contains(tool.Name);
            if (requestedDirect && requestedDeferred)
                warnings.Add($"Tool '{tool.Name}' was listed as both direct and deferred; deferred wins.");

            var enforceDeferredForRisk = AppBindingRisks.Rank(catalogEntry.Risk) > AppBindingRisks.Rank(AppBindingRisks.Read)
                && !allowDirectMutatingToolExposure;

            if (enforceDeferredForRisk
                && requestedDirect
                && !requestedDeferred)
            {
                warnings.Add($"Tool '{tool.Name}' is {catalogEntry.Risk}; deferred exposure was enforced.");
            }

            clone.DeferLoading = requestedDeferred
                || enforceDeferredForRisk
                || (!requestedDirect && string.Equals(catalogEntry.DefaultExposure, AppBindingExposures.Deferred, StringComparison.Ordinal));
            accepted.Add(clone);
        }

        return accepted;
    }

    private static void AddDynamicAttachedToolCatalog(
        AppDescriptor descriptor,
        IReadOnlyList<AppToolCatalogEntry>? dynamicCatalog,
        Dictionary<string, AppToolCatalogEntry> catalogByName)
    {
        if (dynamicCatalog is not { Count: > 0 })
            return;

        if (!descriptor.DynamicToolCatalog.Enabled)
            throw AppServerErrors.InvalidParams($"App '{descriptor.AppId}' does not allow dynamic app tool catalogs.");

        var scopeById = descriptor.Scopes.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        foreach (var tool in dynamicCatalog)
        {
            if (!PluginManifestParser.IsValidFunctionName(tool.Name)
                || string.IsNullOrWhiteSpace(tool.Scope)
                || !AppBindingRisks.IsKnown(tool.Risk)
                || !AppBindingExposures.IsKnown(tool.DefaultExposure))
            {
                throw AppServerErrors.InvalidParams("Dynamic app tool catalog entries require a valid name, scope, risk, and defaultExposure.");
            }

            if (!scopeById.TryGetValue(tool.Scope, out var scope))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' references unknown scope '{tool.Scope}'.");

            if (AppBindingRisks.Rank(tool.Risk) < AppBindingRisks.Rank(scope.Risk))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' risk must not be lower than scope '{tool.Scope}' risk.");

            if (catalogByName.ContainsKey(tool.Name))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' is declared more than once.");

            catalogByName.Add(tool.Name, tool);
        }
    }

    public ThreadOriginAppWire? ResolveOriginApp(AppCatalogSnapshot catalog, string? originChannel, string? channelContext = null) =>
        _wireMapper.ResolveOriginApp(catalog, originChannel, channelContext);

    internal ThreadAppBindingWire MapBinding(
        AppBindingRecord binding,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection) =>
        _wireMapper.MapBinding(binding, descriptor, connection);

    internal ThreadAppBindingWire MapPendingBindingRequest(
        AppBindingRequestRecord request,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection) =>
        _wireMapper.MapPendingBindingRequest(request, descriptor, connection);

    internal static ThreadAppBindingRefreshWire MapRefresh(AppBindingRecord binding) =>
        AppBindingWireMapper.MapRefresh(binding);

    internal static ThreadAppBindingSummaryWire MapSummary(ThreadAppBindingWire binding) =>
        AppBindingWireMapper.MapSummary(binding);

    internal static AppConnectionStatusWire MapConnectionStatus(AppConnectionRecord? connection, string? appId = null) =>
        AppBindingWireMapper.MapConnectionStatus(connection, appId);

    internal static AppConnectionStatusWire MapConnectionStatus(
        AppBindingStateDocument state,
        string userId,
        string appId) =>
        AppBindingWireMapper.MapConnectionStatus(state, userId, appId);
    internal static AppHandoffWire BuildHandoff(
        string workspaceCraftPath,
        AppDescriptor descriptor,
        string? preferredMode,
        string requestId,
        string requestToken,
        string operation,
        IReadOnlyList<string>? scopes = null)
    {
        var handoff = descriptor.Connection.HandoffModes.FirstOrDefault(mode =>
                          !string.IsNullOrWhiteSpace(preferredMode)
                          && string.Equals(mode.Mode, preferredMode, StringComparison.Ordinal))
                      ?? descriptor.Connection.HandoffModes.First();
        return new AppHandoffWire
        {
            Mode = handoff.Mode,
            Uri = string.IsNullOrWhiteSpace(handoff.UriTemplate)
                ? null
                : FillTemplate(
                    handoff.UriTemplate!,
                    descriptor.AppId,
                    requestId,
                    requestToken,
                    operation,
                    scopes,
                    ReadAppServerEndpoint(workspaceCraftPath),
                    escapeValues: true)
        };
    }

    private static string FillTemplate(
        string template,
        string appId,
        string requestId,
        string token,
        string operation,
        IReadOnlyList<string>? scopes,
        string endpoint,
        bool escapeValues)
    {
        var joinedScopes = string.Join(",", scopes ?? []);
        return template
            .Replace("{appId}", TemplateValue(appId, escapeValues), StringComparison.Ordinal)
            .Replace("{requestId}", TemplateValue(requestId, escapeValues), StringComparison.Ordinal)
            .Replace("{requestToken}", TemplateValue(token, escapeValues), StringComparison.Ordinal)
            .Replace("{request}", TemplateValue(requestId, escapeValues), StringComparison.Ordinal)
            .Replace("{operation}", TemplateValue(operation, escapeValues), StringComparison.Ordinal)
            .Replace("{endpoint}", TemplateValue(endpoint, escapeValues), StringComparison.Ordinal)
            .Replace("{scopes}", TemplateValue(joinedScopes, escapeValues), StringComparison.Ordinal);
    }

    private static string TemplateValue(string value, bool escapeValue) =>
        escapeValue ? Uri.EscapeDataString(value) : value;

    private static string ReadAppServerEndpoint(string workspaceCraftPath)
    {
        try
        {
            var lockPath = Path.Combine(workspaceCraftPath, "appserver.lock");
            if (!File.Exists(lockPath))
                return string.Empty;

            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (!document.RootElement.TryGetProperty("endpoints", out var endpoints))
                return string.Empty;
            if (!endpoints.TryGetProperty("appServerWebSocket", out var endpoint))
                return string.Empty;
            return endpoint.GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string HighestRisk(AppDescriptor descriptor, IReadOnlyList<string> requestedScopes)
    {
        var byId = descriptor.Scopes.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        return requestedScopes
            .Select(scope => byId[scope].Risk)
            .OrderByDescending(AppBindingRisks.Rank)
            .FirstOrDefault() ?? AppBindingRisks.Read;
    }

    private static DynamicToolSpec CloneSpec(DynamicToolSpec spec) =>
        new()
        {
            Namespace = spec.Namespace,
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = spec.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = spec.DeferLoading,
            Approval = spec.Approval == null
                ? null
                : new ChannelToolApprovalDescriptor
                {
                    Kind = spec.Approval.Kind,
                    TargetArgument = spec.Approval.TargetArgument,
                    Operation = spec.Approval.Operation,
                    OperationArgument = spec.Approval.OperationArgument
                },
            Meta = spec.Meta
        };

    private static AppDescriptor CloneDescriptor(AppDescriptor descriptor) =>
        JsonSerializer.Deserialize<AppDescriptor>(
            JsonSerializer.Serialize(descriptor, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? new AppDescriptor();

    private static DiscoveredPlugin CreateSyntheticManagedPlugin(AppDescriptor descriptor, string workspaceCraftPath)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceCraftPath, "managed-apps", descriptor.AppId));
        return new DiscoveredPlugin(
            new PluginManifest
            {
                SchemaVersion = PluginManifestParser.SupportedSchemaVersion,
                Id = $"managed:{descriptor.AppId}",
                DisplayName = descriptor.DisplayName,
                Description = descriptor.Description,
                Capabilities = ["app"],
                RootPath = root,
                ManifestPath = Path.Combine(root, ".craft-plugin", "plugin.json")
            },
            PluginDiscoverySourceKind.BuiltIn,
            root,
            Enabled: true,
            Installed: true,
            Installable: false,
            Removable: false);
    }

    internal static void AddAudit(
        AppBindingStateDocument state,
        string @event,
        string? threadId,
        string? bindingId,
        string? appId,
        string? userId,
        string? detail)
    {
        state.Audit.Add(new AppBindingAuditRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Event = @event,
            ThreadId = threadId,
            BindingId = bindingId,
            AppId = appId,
            UserId = userId,
            Detail = detail
        });
    }

}

/// <summary>
/// Projects active AppBinding context blocks into a thread-scoped system prompt page.
/// </summary>
public sealed class AppBindingThreadSystemPromptContextProvider(AppBindingService service) : IThreadSystemPromptContextProvider
{
    public ContextPageKey ContextPageKey => ContextPageKeys.AppContextBlocks();

    public string? GetSystemPromptSection(ThreadSystemPromptContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkspacePath))
            return null;

        var workspaceCraftPath = Path.Combine(context.WorkspacePath, ".craft");
        return Directory.Exists(workspaceCraftPath)
            ? service.BuildAppContextPromptSection(workspaceCraftPath, context.ThreadId)
            : null;
    }
}

/// <summary>
/// Exposes App Binding grants as thread-scoped runtime tools.
/// </summary>
public sealed class AppBindingRuntimeToolProvider(AppBindingService service) : IThreadRuntimeToolProvider
{
    public int Priority => 91;

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames) =>
        service.CreateRuntimeToolsForThread(thread, reservedToolNames);
}
