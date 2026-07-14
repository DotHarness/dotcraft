using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppUiInteractionService(
    AppBindingStoreAccessor stores,
    AppToolAttachmentService tools,
    Action<string> contextBlocksChanged)
{
    private const int MaxContextBlocksPerBinding = 32;
    private const int MaxContextBlockMetadataLength = 128;
    private const int MaxContextBlockContentBytes = 16 * 1024;

    public async ValueTask<AppBoundToolCallResult> InvokeUiToolAsync(
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

        bool Matches(AppBoundToolSpec candidate) =>
            string.Equals(candidate.Name, tool, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(@namespace)
                || string.Equals(candidate.Namespace, @namespace, StringComparison.Ordinal));

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(Matches));

        if (binding == null)
            return AppToolAttachmentService.Failed(AppBindingErrorCodes.ToolUnavailable, $"Tool '{tool}' is not app-bound to thread '{threadId}'.");

        var spec = binding.AttachedTools.First(Matches);

        // The app author decides which tools its UI may call via _meta.ui.visibility containing "app".
        if (!LegacyAppBindingUiToolVisibility.IsAppVisible(spec.Meta?.Ui))
            return AppToolAttachmentService.Failed(
                AppBindingErrorCodes.ToolUnavailable,
                $"Tool '{tool}' is not exposed to its UI (requires _meta.ui.visibility to include \"app\").");

        var inputSchema = spec.InputSchema ?? new JsonObject { ["type"] = "object" };
        if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, arguments, out var validationError))
            return AppToolAttachmentService.Failed("InvalidArguments", validationError);

        var runtimeState = tools.GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            return AppToolAttachmentService.Failed(AppBindingErrorCodes.Offline, $"The app binding for tool '{tool}' is {runtimeState}.");

        if (spec.Approval is { } approval)
        {
            var targetState = ApprovalArgumentResolver.ResolveTargetArgument(
                arguments,
                spec.InputSchema,
                approval.TargetArgument,
                out _);
            if (targetState == ApprovalTargetArgumentState.MissingRequired)
            {
                return AppToolAttachmentService.Failed(
                    "InvalidArguments",
                    $"Tool '{tool}' requires string argument '{approval.TargetArgument}' for approval routing.");
            }

            if (targetState == ApprovalTargetArgumentState.Present)
            {
                if (approvalGate == null)
                    return AppToolAttachmentService.Failed(
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
                    return AppToolAttachmentService.Failed(AppBindingErrorCodes.ApprovalDeclined, $"The user declined to run '{tool}'.");
            }
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

        return await tools.InvokeAttachedToolAsync(
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

    public UiOpenLinkResult OpenLink(
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

    public UiUpdateModelContextResult UpdateModelContext(
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
        var result = stores.GetStore(workspaceCraftPath).Update(state =>
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
                    AppBindingService.AddAudit(state, "binding.uiModelContext.clear", live.ThreadId, live.BindingId, live.AppId, userId, blockId);
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
            AppBindingService.AddAudit(state, "binding.uiModelContext.upsert", live.ThreadId, live.BindingId, live.AppId, userId, blockId);
            return new UiUpdateModelContextResult { BlockId = blockId, Cleared = false };
        });

        NotifyAppContextBlocksChanged(binding.ThreadId);
        return result;
    }

    public async ValueTask<UiResourceReadResult> ReadUiResourceAsync(
        string workspaceCraftPath,
        string threadId,
        string? @namespace,
        string uri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw AppServerErrors.InvalidParams("'uri' is required.");

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(tool =>
                string.Equals(tool.Meta?.Ui?.ResourceUri, uri, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal))));

        if (binding == null)
            throw AppServerErrors.InvalidParams(
                $"No app-bound tool on thread '{threadId}' declares UI resource '{uri}'.");

        var runtimeState = tools.GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"The app binding for UI resource '{uri}' is {runtimeState}.");

        if (!tools.TryGetLiveAttachment(binding.BindingId, out var attachment))
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

        result.Csp = binding.AttachedTools
            .FirstOrDefault(tool =>
                string.Equals(tool.Meta?.Ui?.ResourceUri, uri, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal)))
            ?.Meta?.Ui?.Csp;
        return result;
    }

    private void NotifyAppContextBlocksChanged(string threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
            contextBlocksChanged(threadId);
    }

    private static UiToolApprovalInfo BuildUiToolApprovalInfo(AppBoundToolSpec spec, JsonObject arguments)
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

    private static bool IsAllowedExternalLink(string url, IReadOnlyList<string> appProtocols, out string normalized)
    {
        normalized = url.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return false;
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
               || appProtocols.Any(protocol => string.Equals(uri.Scheme, protocol, StringComparison.OrdinalIgnoreCase));
    }

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

    private AppBindingRecord ResolveActiveUiBinding(string workspaceCraftPath, string threadId, string? @namespace)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
            && candidate.AttachedTools.Any(tool =>
                tool.Meta?.Ui != null
                && (string.IsNullOrEmpty(@namespace)
                    || string.Equals(tool.Namespace, @namespace, StringComparison.Ordinal))));
        if (binding == null)
            throw AppServerErrors.InvalidParams($"No UI-bearing app binding on thread '{threadId}'.");

        var runtimeState = tools.GetRuntimeBindingState(binding);
        if (runtimeState != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"The app binding for thread '{threadId}' is {runtimeState}.");
        return binding;
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
        stores.GetStore(workspaceCraftPath).Update(state =>
        {
            AppBindingService.AddAudit(state, @event, threadId, bindingId, appId, userId, detail);
            return true;
        });
    }
}
