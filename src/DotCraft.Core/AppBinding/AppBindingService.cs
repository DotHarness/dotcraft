using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

/// <summary>
/// Coordinates App Binding discovery, durable lifecycle state, and runtime tool dispatch.
/// </summary>
public sealed class AppBindingService
{
    private const int MaxContextBlocksPerBinding = 32;
    private const int MaxContextBlockMetadataLength = 128;
    private const int MaxContextBlockContentBytes = 16 * 1024;

    private readonly AppBindingStoreAccessor _storeAccessor = new();
    private readonly AppBindingAttachmentRegistry _attachments = new();
    private readonly IReadOnlyDictionary<string, IManagedAppBindingRuntime> _managedRuntimesByAppId;
    private readonly AppConnectionService _connections;
    private readonly AppContextBlockService _contextBlocks;
    private readonly AppToolAttachmentService _tools;
    private readonly AppBindingLifecycleService _lifecycle;

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
        _connections = new AppConnectionService(_storeAccessor, _attachments);
        _contextBlocks = new AppContextBlockService(_storeAccessor, _managedRuntimesByAppId, NotifyAppContextBlocksChanged);
        _tools = new AppToolAttachmentService(this, _storeAccessor, _attachments, _managedRuntimesByAppId);
        _lifecycle = new AppBindingLifecycleService(this, _storeAccessor, _attachments, _tools, _managedRuntimesByAppId);
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
            if (string.IsNullOrWhiteSpace(owningPluginId))
                continue;

            var owningPlugin = catalog.Plugins
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

        return new AppCatalogSnapshot(entries, diagnostics) { Plugins = catalog.Plugins };
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
                            && IsVisibleOnAppListSurface(entry, surface))
            .Select(entry => MapAppInfo(entry, state, userId, p.ThreadId, surface))
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
        return new AppViewResult { App = MapAppInfo(entry, state, userId, threadId, AppBindingCatalogSurfaces.SdkDefault) };
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tool))
            throw AppServerErrors.InvalidParams("'tool' is required.");

        bool Matches(DynamicToolSpec candidate) =>
            string.Equals(candidate.Name, tool, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(@namespace)
                || string.Equals(candidate.Namespace, @namespace, StringComparison.Ordinal));

        var state = GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(Matches));

        if (binding == null)
            return Failed(AppBindingErrorCodes.ToolUnavailable, $"Tool '{tool}' is not app-bound to thread '{threadId}'.");

        var spec = binding.AttachedTools.First(Matches);

        // The app author decides which tools its UI may call via _meta.ui.visibility containing "app".
        if (!UiToolVisibility.IsAppVisible(spec.Meta?.Ui))
            return Failed(
                AppBindingErrorCodes.ToolUnavailable,
                $"Tool '{tool}' is not exposed to its UI (requires _meta.ui.visibility to include \"app\").");

        var inputSchema = spec.InputSchema ?? new JsonObject { ["type"] = "object" };
        if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, arguments, out var validationError))
            return Failed("InvalidArguments", validationError);

        var runtimeState = GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            return Failed(AppBindingErrorCodes.Offline, $"The app binding for tool '{tool}' is {runtimeState}.");

        // M‑v: a mutating UI tool call (one that declares an approval descriptor) requires user
        // approval. A decoupled ui/tool/call has no turn/item, so the host raises the approval via
        // the gate and awaits it. No gate (e.g. a non‑Desktop client that cannot prompt) → reject.
        // See specs/protocols/tool-result-presentation.md §10.
        if (spec.Approval != null)
        {
            if (approvalGate == null)
                return Failed(
                    AppBindingErrorCodes.ApprovalRequired,
                    $"Tool '{tool}' requires approval, which this client cannot prompt for.");

            var approved = await approvalGate(BuildUiToolApprovalInfo(spec, arguments), cancellationToken);
            AddAuditWithSave(
                workspaceCraftPath,
                approved ? "binding.uiToolApproval.accepted" : "binding.uiToolApproval.declined",
                threadId,
                binding.BindingId,
                binding.AppId,
                userId,
                $"tool={tool}");
            if (!approved)
                return Failed(AppBindingErrorCodes.ApprovalDeclined, $"The user declined to run '{tool}'.");
        }

        var callId = $"uitool_{Guid.NewGuid():N}";
        AddAuditWithSave(
            workspaceCraftPath,
            "binding.uiToolCall",
            threadId,
            binding.BindingId,
            binding.AppId,
            userId,
            string.IsNullOrWhiteSpace(sourceCallId) ? $"tool={tool}" : $"tool={tool};sourceCallId={sourceCallId}");

        // No turn/item: dispatch directly to the app and return the result to the UI.
        return await InvokeAttachedToolAsync(
            workspaceCraftPath,
            binding.BindingId,
            spec,
            executionThreadId: threadId,
            executionTurnId: string.Empty,
            sessionService,
            callId,
            arguments,
            cancellationToken);
    }

    /// <summary>
    /// Derives the approval prompt's <c>operation</c> / <c>target</c> from a mutating tool's approval
    /// descriptor and the call arguments (M‑v decoupled approval).
    /// </summary>
    private static UiToolApprovalInfo BuildUiToolApprovalInfo(DynamicToolSpec spec, JsonObject arguments)
    {
        var approval = spec.Approval!;
        string operation;
        if (!string.IsNullOrWhiteSpace(approval.Operation))
            operation = approval.Operation!;
        else if (!string.IsNullOrWhiteSpace(approval.OperationArgument)
                 && arguments.TryGetPropertyValue(approval.OperationArgument!, out var op) && op != null)
            operation = op.ToString();
        else
            operation = spec.Name;

        var target = !string.IsNullOrWhiteSpace(approval.TargetArgument)
                     && arguments.TryGetPropertyValue(approval.TargetArgument, out var tgt) && tgt != null
            ? tgt.ToString()
            : string.Empty;

        return new UiToolApprovalInfo(approval.Kind, operation, target);
    }

    /// <summary>
    /// Validates and authorizes a UI‑initiated <c>ui/open-link</c>. Enforces the host scheme policy
    /// (<c>https:</c>/<c>mailto:</c>, plus the bound app's own declared
    /// <c>nativeApplication.protocol</c> deep‑link scheme — M‑v), confirms an active UI‑bearing
    /// binding owns the surface, records the open on the audit trail, and returns the cleared URL —
    /// the Desktop host performs the navigation. Decoupled from the conversation (no turn/item).
    /// </summary>
    internal UiOpenLinkResult OpenLink(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string url,
        string? sourceCallId,
        string userId)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw AppServerErrors.InvalidParams("'url' is required.");

        var binding = ResolveActiveUiBinding(workspaceCraftPath, threadId, @namespace);
        var provenance = string.IsNullOrWhiteSpace(sourceCallId) ? null : $"sourceCallId={sourceCallId}";

        if (!IsAllowedExternalLink(url, DeclaredAppProtocols(catalog, binding.AppId), out var normalized))
        {
            AddAuditWithSave(
                workspaceCraftPath,
                "binding.uiOpenLink.blocked",
                threadId,
                binding.BindingId,
                binding.AppId,
                userId,
                provenance);
            throw AppServerErrors.InvalidParams(
                "Link scheme is not allowed. ui/open-link permits https:, mailto:, and the bound app's declared protocol.");
        }

        AddAuditWithSave(
            workspaceCraftPath,
            "binding.uiOpenLink",
            threadId,
            binding.BindingId,
            binding.AppId,
            userId,
            provenance);
        return new UiOpenLinkResult { Url = normalized };
    }

    /// <summary>
    /// Records UI‑derived model‑visible state pushed via <c>ui/update-model-context</c> (M‑iii) as an
    /// App Binding context block keyed to the originating <c>dynamicToolCall</c> item (<c>ui:&lt;callId&gt;</c>),
    /// <c>visibility: "model"</c>, last‑write‑wins. Empty/absent content removes the block (e.g. on
    /// teardown). Decoupled from the conversation (no turn/item).
    /// </summary>
    internal UiUpdateModelContextResult UpdateModelContext(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string sourceCallId,
        string? title,
        string? content,
        string userId)
    {
        if (string.IsNullOrWhiteSpace(sourceCallId))
            throw AppServerErrors.InvalidParams("'sourceCallId' is required.");

        var binding = ResolveActiveUiBinding(workspaceCraftPath, threadId, @namespace);
        var blockId = $"ui:{sourceCallId.Trim()}";
        var trimmedContent = content?.Trim() ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(trimmedContent) > MaxContextBlockContentBytes)
            throw AppServerErrors.InvalidParams(
                $"ui/update-model-context content exceeds the {MaxContextBlockContentBytes}-byte limit.");

        var safeTitle = string.IsNullOrWhiteSpace(title) ? "UI state" : title.Trim();
        if (safeTitle.Length > MaxContextBlockMetadataLength)
            safeTitle = safeTitle[..MaxContextBlockMetadataLength];

        var cleared = trimmedContent.Length == 0;
        var result = GetStore(workspaceCraftPath).Update(state =>
        {
            var live = FindBinding(state, binding.BindingId)
                       ?? throw AppServerErrors.InvalidParams("The app binding no longer exists.");
            var now = DateTimeOffset.UtcNow;

            if (cleared)
            {
                var removed = live.ContextBlocks.RemoveAll(block =>
                    string.Equals(block.BlockId, blockId, StringComparison.Ordinal)) > 0;
                if (removed)
                {
                    live.LastChangedAt = now;
                    AddAudit(state, "binding.uiModelContext.clear", live.ThreadId, live.BindingId, live.AppId, userId, blockId);
                }

                return new UiUpdateModelContextResult { BlockId = blockId, Cleared = true };
            }

            var existing = live.ContextBlocks.FirstOrDefault(block =>
                string.Equals(block.BlockId, blockId, StringComparison.Ordinal));
            if (existing == null && live.ContextBlocks.Count >= MaxContextBlocksPerBinding)
                throw AppServerErrors.InvalidParams(
                    $"Binding '{live.BindingId}' already has the maximum {MaxContextBlocksPerBinding} context blocks.");

            var block = existing ?? new AppContextBlockRecord();
            block.BlockId = blockId;
            block.Kind = AppContextBlockKinds.UiModelContext;
            block.Title = safeTitle;
            block.Content = trimmedContent;
            block.Visibility = AppContextBlockVisibilities.Model;
            block.ExpiresAt = null;
            block.UpdatedAt = now;
            block.Version = now.ToString("O");
            if (existing == null)
                live.ContextBlocks.Add(block);

            live.LastChangedAt = now;
            AddAudit(state, "binding.uiModelContext.upsert", live.ThreadId, live.BindingId, live.AppId, userId, blockId);
            return new UiUpdateModelContextResult { BlockId = blockId, Cleared = false };
        });

        NotifyAppContextBlocksChanged(binding.ThreadId);
        return result;
    }

    /// <summary>
    /// Host scheme policy for <c>ui/open-link</c>: <c>https:</c>, <c>mailto:</c>, and the bound
    /// app's own declared deep‑link protocol(s) (per‑binding, from its catalog descriptor).
    /// </summary>
    private static bool IsAllowedExternalLink(string url, IReadOnlyList<string> appProtocols, out string normalized)
    {
        normalized = url.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return false;
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
               || appProtocols.Any(protocol => string.Equals(uri.Scheme, protocol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Collects the deep‑link protocol(s) an app declares in its catalog descriptor
    /// (<c>nativeApplication.protocol</c> plus per‑platform overrides). Empty when the app is not
    /// in the catalog or declares no native application.
    /// </summary>
    private static IReadOnlyList<string> DeclaredAppProtocols(AppCatalogSnapshot catalog, string appId)
    {
        var native = catalog.Entries
            .FirstOrDefault(entry => string.Equals(entry.Descriptor.AppId, appId, StringComparison.Ordinal))
            ?.Descriptor.NativeApplication;
        if (native == null)
            return [];

        var protocols = new List<string>();
        if (!string.IsNullOrWhiteSpace(native.Protocol))
            protocols.Add(native.Protocol.Trim().TrimEnd(':'));
        if (native.Platforms != null)
        {
            foreach (var platform in native.Platforms.Values)
            {
                if (!string.IsNullOrWhiteSpace(platform.Protocol))
                    protocols.Add(platform.Protocol.Trim().TrimEnd(':'));
            }
        }

        return protocols;
    }

    /// <summary>
    /// Resolves the active, UI‑bearing App Binding for a thread's interactive surface. Disambiguates
    /// by <paramref name="namespace"/> when supplied; rejects when no active UI binding is found.
    /// </summary>
    private AppBindingRecord ResolveActiveUiBinding(string workspaceCraftPath, string threadId, string? @namespace)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var state = GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(tool =>
                tool.Meta?.Ui != null
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal))));
        if (binding == null)
            throw AppServerErrors.InvalidParams($"No UI‑bearing app binding on thread '{threadId}'.");

        var runtimeState = GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"The app binding for thread '{threadId}' is {runtimeState}.");
        return binding;
    }

    /// <summary>
    /// Brokers a <c>ui://</c> Interactive Tool UI resource read to the app that owns the binding
    /// for <paramref name="threadId"/>. The resource URI must be declared by an attached tool's
    /// <c>_meta.ui.resourceUri</c>; reads outside the binding's tools are rejected.
    /// </summary>
    internal async ValueTask<UiResourceReadResult> ReadUiResourceAsync(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string uri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw AppServerErrors.InvalidParams("'uri' is required.");

        var state = GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(tool =>
                string.Equals(tool.Meta?.Ui?.ResourceUri, uri, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal))));

        if (binding == null)
            throw AppServerErrors.InvalidParams(
                $"No app-bound tool on thread '{threadId}' declares UI resource '{uri}'.");

        var runtimeState = GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"The app binding for UI resource '{uri}' is {runtimeState}.");

        if (!TryGetLiveAttachment(binding.BindingId, out var attachment))
            throw AppServerErrors.InvalidParams("The app binding is offline. Reconnect the app or refresh the binding.");

        var response = await attachment.Transport.SendClientRequestAsync(
            AppServerMethods.ItemResourceRead,
            new UiResourceReadParams { ThreadId = threadId, Namespace = @namespace, Uri = uri },
            cancellationToken,
            TimeSpan.FromSeconds(30));

        if (response.Error.HasValue)
            throw AppServerErrors.InvalidParams($"App failed to read UI resource '{uri}': {response.Error.Value}");
        if (!response.Result.HasValue)
            throw AppServerErrors.InvalidParams($"App returned no contents for UI resource '{uri}'.");

        var result = response.Result.Value.Deserialize<UiResourceReadResult>(SessionWireJsonOptions.Default)
            ?? throw AppServerErrors.InvalidParams($"App returned an invalid response for UI resource '{uri}'.");

        // Host‑populate the per‑resource CSP (M‑iii data path B) from the *server‑validated*
        // descriptor — the owning tool's _meta.ui.csp — never from the app's resource response or
        // the iframe. The host (dotcraft-app:// handler) widens connect/resource/frame from this.
        result.Csp = binding.AttachedTools
            .FirstOrDefault(tool =>
                string.Equals(tool.Meta?.Ui?.ResourceUri, uri, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal)))
            ?.Meta?.Ui?.Csp;
        return result;
    }

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

    private static bool IsVisibleOnAppListSurface(AppCatalogEntry entry, string surface)
    {
        if (entry.ManagedRuntime == null)
            return true;

        return entry.Plugin.Installed
               && entry.Plugin.Enabled
               && entry.ManagedRuntime.Surfaces.Contains(surface);
    }

    private AppInfoWire MapAppInfo(
        AppCatalogEntry entry,
        AppBindingStateDocument state,
        string userId,
        string? threadId,
        string surface)
    {
        var managedRuntime = entry.ManagedRuntime == null
            ? null
            : _managedRuntimesByAppId.GetValueOrDefault(entry.Descriptor.AppId);
        var descriptor = managedRuntime?.GetCatalogDescriptor(surface) ?? entry.Descriptor;
        var managed = entry.ManagedRuntime != null;
        var requiresExternalConnection = entry.ManagedRuntime?.RequiresExternalConnection ?? true;
        var connectionStatus = managed && !requiresExternalConnection
            ? new AppConnectionStatusWire { AppId = descriptor.AppId, State = AppConnectionStates.Connected }
            : MapConnectionStatus(state, userId, descriptor.AppId);
        var connection = managed && !requiresExternalConnection ? null : FindConnection(state, userId, descriptor.AppId);
        var binding = string.IsNullOrWhiteSpace(threadId)
            ? null
            : state.Bindings
                .Where(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
                                    && string.Equals(candidate.AppId, descriptor.AppId, StringComparison.Ordinal)
                                    && candidate.State != AppBindingStates.Revoked)
                .OrderByDescending(candidate => candidate.LastChangedAt)
                .FirstOrDefault();

        var icon = ResolveIconForWire(descriptor.Icon) ?? ResolvePluginInterfaceIconForWire(entry.Plugin.Manifest);
        return new AppInfoWire
        {
            AppId = descriptor.AppId,
            ToolNamespace = descriptor.ToolNamespace,
            DisplayName = descriptor.DisplayName,
            DeveloperName = descriptor.DeveloperName,
            Description = descriptor.Description,
            Category = descriptor.Category,
            Icon = icon,
            PluginId = entry.Plugin.Manifest.Id,
            Installed = entry.Plugin.Installed,
            Enabled = entry.Plugin.Enabled,
            CatalogVisible = true,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            ReleasePage = descriptor.ReleasePage,
            DownloadUrl = descriptor.DownloadUrl,
            NativeApp = new AppNativeApplicationWire
            {
                DisplayName = string.IsNullOrWhiteSpace(descriptor.NativeApplication.DisplayName)
                    ? descriptor.DisplayName
                    : descriptor.NativeApplication.DisplayName,
                Protocol = descriptor.NativeApplication.Protocol,
                InstallUrl = descriptor.NativeApplication.InstallUrl ?? descriptor.ReleasePage ?? descriptor.DownloadUrl,
                Status = managed && !requiresExternalConnection
                    ? AppNativeApplicationStates.Installed
                    : AppNativeApplicationStates.Unknown
            },
            ConnectionState = connectionStatus.State,
            AccountLabel = connection?.AccountLabel,
            HandoffModes = descriptor.Connection.HandoffModes,
            Scopes = descriptor.Scopes,
            ToolCatalog = descriptor.ToolCatalog,
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor
            {
                Enabled = descriptor.DynamicToolCatalog.Enabled,
                Description = descriptor.DynamicToolCatalog.Description
            },
            BindingSummary = binding == null
                ? null
                : new ThreadAppBindingSummaryWire
                {
                    ThreadId = binding.ThreadId,
                    BindingId = binding.BindingId,
                    AppId = binding.AppId,
                    DisplayName = descriptor.DisplayName,
                    Icon = icon,
                    ToolNamespace = descriptor.ToolNamespace,
                    State = binding.State,
                    ConnectionState = connectionStatus.State,
                    Managed = managed,
                    RequiresExternalConnection = requiresExternalConnection,
                    GrantedScopes = binding.GrantedScopes.ToList(),
                    ExpiresAt = binding.ExpiresAt
                },
            Diagnostics = entry.Diagnostics.Select(MapDiagnostic).ToList()
        };
    }

    /// <summary>
    /// Resolves a thread's <paramref name="originChannel"/> to the app that declared it as its
    /// <c>originChannel</c>, returning branding (icon + display name) for the thread origin badge.
    /// When the app also declares <c>originMembers</c> and <paramref name="channelContext"/> matches one,
    /// the matched member's branding is returned instead of the app-level visual. Opt-in (declared
    /// origin channels only); returns null when nothing matches or the channel is blank — so callers
    /// fall back to the generic badge.
    /// <para>
    /// Matches both workspace-installed and bundled built-in (installable) apps. Origin branding is
    /// purely cosmetic and does not grant tools, so it must not depend on whether the declaring app has
    /// been deployed into the thread's specific workspace. This matters for threads that run in
    /// workspaces without the app deployed — e.g. an app's own git worktrees — which would otherwise
    /// see only the generic channel icon.
    /// </para>
    /// </summary>
    public ThreadOriginAppWire? ResolveOriginApp(AppCatalogSnapshot catalog, string? originChannel, string? channelContext = null)
    {
        if (string.IsNullOrWhiteSpace(originChannel))
            return null;

        var entry = catalog.Entries
            .Where(candidate => (candidate.Plugin.Installed || candidate.Plugin.Installable)
                                && !string.IsNullOrWhiteSpace(candidate.Descriptor.OriginChannel)
                                && string.Equals(candidate.Descriptor.OriginChannel, originChannel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Descriptor.AppId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (entry is null)
            return null;

        var member = ResolveOriginMember(entry.Descriptor, channelContext);
        if (member is not null)
        {
            return new ThreadOriginAppWire
            {
                AppId = entry.Descriptor.AppId,
                DisplayName = member.DisplayName,
                Icon = ResolvePluginRelativeIconForWire(entry, member.Icon),
                MemberId = member.Match
            };
        }

        return new ThreadOriginAppWire
        {
            AppId = entry.Descriptor.AppId,
            DisplayName = entry.Descriptor.DisplayName,
            Icon = ResolveIconForWire(entry.Descriptor.Icon)
                   ?? ResolvePluginInterfaceIconForWire(entry.Plugin.Manifest)
        };
    }

    private static AppOriginMemberDescriptor? ResolveOriginMember(AppDescriptor descriptor, string? channelContext)
    {
        if (descriptor.OriginMembers is not { Count: > 0 } members || string.IsNullOrWhiteSpace(channelContext))
            return null;

        return members.FirstOrDefault(member =>
            !string.IsNullOrWhiteSpace(member.Match)
            && channelContext.Contains(member.Match, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves an icon path declared in a descriptor that has not been path-absolutized by catalog
    /// discovery (managed-runtime descriptors, and origin-member icons), relative to the owning
    /// plugin root, then delegates to <see cref="ResolveIconForWire"/>. Refuses paths that escape the
    /// plugin root.
    /// </summary>
    private static string? ResolvePluginRelativeIconForWire(AppCatalogEntry entry, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathFullyQualified(icon))
        {
            return ResolveIconForWire(icon);
        }

        try
        {
            var root = Path.GetFullPath(entry.Plugin.Manifest.RootPath);
            var full = Path.GetFullPath(Path.Combine(root, icon));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ResolveIconForWire(full);
        }
        catch
        {
            return null;
        }
    }

    internal ThreadAppBindingWire MapBinding(
        AppBindingRecord binding,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection)
    {
        var effectiveState = binding.State;
        if (binding.State == AppBindingStates.Active
            && binding.ExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            effectiveState = AppBindingStates.Expired;
        }

        var managedRuntime = _managedRuntimesByAppId.GetValueOrDefault(binding.AppId);
        var managed = managedRuntime != null;
        var requiresExternalConnection = managedRuntime?.RequiresExternalConnection ?? true;
        var connectionStatus = managed && !requiresExternalConnection
            ? new AppConnectionStatusWire { AppId = binding.AppId, State = AppConnectionStates.Connected }
            : connection;
        return new ThreadAppBindingWire
        {
            BindingId = binding.BindingId,
            ThreadId = binding.ThreadId,
            AppId = binding.AppId,
            DisplayName = descriptor?.DisplayName,
            Icon = ResolveIconForWire(descriptor?.Icon),
            ToolNamespace = descriptor?.ToolNamespace,
            State = effectiveState,
            ConnectionState = connectionStatus.State,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            GrantedScopes = binding.GrantedScopes.ToList(),
            AttachedToolCount = binding.AttachedTools.Count,
            ExpiresAt = binding.ExpiresAt,
            LastChangedAt = binding.LastChangedAt,
            ApprovalMode = binding.ApprovalMode,
            AuditRef = binding.AuditRef,
            Diagnostic = binding.Diagnostic
        };
    }

    internal ThreadAppBindingWire MapPendingBindingRequest(
        AppBindingRequestRecord request,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection)
    {
        var managedRuntime = _managedRuntimesByAppId.GetValueOrDefault(request.AppId);
        var managed = managedRuntime != null;
        var requiresExternalConnection = managedRuntime?.RequiresExternalConnection ?? true;
        var connectionStatus = managed && !requiresExternalConnection
            ? new AppConnectionStatusWire { AppId = request.AppId, State = AppConnectionStates.Connected }
            : connection;
        return new ThreadAppBindingWire
        {
            BindingRequestId = request.BindingRequestId,
            BindingId = request.BindingRequestId,
            ThreadId = request.ThreadId,
            AppId = request.AppId,
            DisplayName = descriptor?.DisplayName,
            Icon = ResolveIconForWire(descriptor?.Icon),
            ToolNamespace = descriptor?.ToolNamespace,
            State = AppBindingStates.Pending,
            ConnectionState = connectionStatus.State,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            GrantedScopes = [],
            AttachedToolCount = 0,
            ExpiresAt = request.ExpiresAt,
            LastChangedAt = request.CreatedAt,
            Diagnostic = request.Reason
        };
    }

    internal static ThreadAppBindingRefreshWire MapRefresh(AppBindingRecord binding) =>
        new()
        {
            BindingId = binding.BindingId,
            State = binding.State,
            AttachedToolCount = binding.AttachedTools.Count
        };

    internal static ThreadAppBindingSummaryWire MapSummary(ThreadAppBindingWire binding) =>
        new()
        {
            ThreadId = binding.ThreadId,
            BindingRequestId = binding.BindingRequestId,
            BindingId = binding.BindingId,
            AppId = binding.AppId,
            DisplayName = binding.DisplayName,
            Icon = binding.Icon,
            ToolNamespace = binding.ToolNamespace,
            State = binding.State,
            ConnectionState = binding.ConnectionState,
            Managed = binding.Managed,
            RequiresExternalConnection = binding.RequiresExternalConnection,
            GrantedScopes = binding.GrantedScopes.ToList(),
            ExpiresAt = binding.ExpiresAt
        };

    internal static AppConnectionStatusWire MapConnectionStatus(AppConnectionRecord? connection, string? appId = null)
    {
        if (connection == null)
        {
            return new AppConnectionStatusWire
            {
                AppId = appId ?? string.Empty,
                State = AppConnectionStates.NotConnected
            };
        }

        var state = connection.State;
        if (state == AppConnectionStates.Connected
            && connection.ExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            state = AppConnectionStates.NeedsAuth;
        }

        return new AppConnectionStatusWire
        {
            AppId = connection.AppId,
            State = state,
            ConnectedAt = connection.ConnectedAt,
            ExpiresAt = connection.ExpiresAt,
            AccountLabel = connection.AccountLabel,
            Diagnostic = connection.Diagnostic,
            PublicMetadata = state == AppConnectionStates.Connected
                ? connection.PublicMetadata?.DeepClone() as JsonObject
                : null
        };
    }

    internal static AppConnectionStatusWire MapConnectionStatus(
        AppBindingStateDocument state,
        string userId,
        string appId)
    {
        var connection = FindConnection(state, userId, appId);
        var status = MapConnectionStatus(connection, appId);
        if (status.State != AppConnectionStates.NotConnected)
            return status;

        var pending = state.ConnectionRequests
            .Where(request => string.Equals(request.UserId, userId, StringComparison.Ordinal)
                              && string.Equals(request.AppId, appId, StringComparison.Ordinal)
                              && request.State == AppConnectionStates.Connecting
                              && !request.Consumed
                              && request.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(request => request.CreatedAt)
            .FirstOrDefault();
        if (pending == null)
            return status;

        return new AppConnectionStatusWire
        {
            AppId = appId,
            State = AppConnectionStates.Connecting,
            ExpiresAt = pending.ExpiresAt
        };
    }

    private string GetRuntimeBindingState(AppBindingRecord binding)
        => _tools.GetRuntimeBindingState(binding);

    private bool TryGetLiveAttachment(
        string bindingId,
        [NotNullWhen(true)] out ActiveAppBindingAttachment? attachment)
        => _tools.TryGetLiveAttachment(bindingId, out attachment);

    private static PluginDiagnosticWire MapDiagnostic(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = diagnostic.PluginId,
            Path = diagnostic.Path
        };

    private static string? ResolveIconForWire(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return icon;
        }

        try
        {
            if (!Path.IsPathFullyQualified(icon) || !File.Exists(icon))
                return icon;

            var mimeType = Path.GetExtension(icon).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };
            return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(icon))}";
        }
        catch
        {
            return icon;
        }
    }

    private static string? ResolvePluginInterfaceIconForWire(PluginManifest manifest)
    {
        var interfaceMetadata = manifest.Interface;
        return ResolveIconForWire(interfaceMetadata?.ComposerIcon)
               ?? ResolveIconForWire(interfaceMetadata?.Logo);
    }

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

    internal static DynamicToolCallResult Failed(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

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

    private void AddAuditWithSave(
        string workspaceCraftPath,
        string @event,
        string? threadId,
        string? bindingId,
        string? appId,
        string? userId,
        string? detail)
    {
        GetStore(workspaceCraftPath).Update(state =>
        {
            AddAudit(state, @event, threadId, bindingId, appId, userId, detail);
            return true;
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
