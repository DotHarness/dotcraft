using System.Text;
using DotCraft.Protocol.AppServer;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppContextBlockService(
    AppBindingStoreAccessor stores,
    IReadOnlyDictionary<string, IManagedAppBindingRuntime> managedRuntimesByAppId,
    Action<string> contextBlocksChanged)
{
    private const int MaxContextBlocksPerBinding = 32;
    private const int MaxContextBlockMetadataLength = 128;
    private const int MaxContextBlockContentBytes = 16 * 1024;

    public AppBindingContextUpsertResult UpsertContextBlock(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingContextUpsertParams p)
    {
        ValidateContextUpsertParams(p);
        _ = FindEnabledApp(catalog, p.AppId);
        var normalized = NormalizeContextBlockInput(p);
        var changed = false;
        var result = stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = RequireWritableContextBinding(state, normalized.BindingId, normalized.AppId, normalized.GrantId);
            if (!IsBindingConnectionUsable(state, binding))
                throw AppServerErrors.InvalidParams($"App '{binding.AppId}' is not connected for this workspace user.");

            var now = DateTimeOffset.UtcNow;
            var existing = binding.ContextBlocks.FirstOrDefault(block =>
                string.Equals(block.BlockId, normalized.BlockId, StringComparison.Ordinal));
            if (existing == null && binding.ContextBlocks.Count >= MaxContextBlocksPerBinding)
                throw AppServerErrors.InvalidParams($"Binding '{binding.BindingId}' already has the maximum {MaxContextBlocksPerBinding} context blocks.");

            var block = existing ?? new AppContextBlockRecord();
            if (existing != null && IsContextBlockUnchanged(existing, normalized))
            {
                return new AppBindingContextUpsertResult
                {
                    Block = MapContextBlock(binding, existing, now)
                };
            }

            block.BlockId = normalized.BlockId;
            block.Kind = normalized.Kind;
            block.Title = normalized.Title;
            block.Content = normalized.Content;
            block.Order = normalized.Order;
            block.Version = normalized.Version;
            block.ExpiresAt = normalized.ExpiresAt;
            block.Visibility = normalized.Visibility;
            block.UpdatedAt = now;
            if (existing == null)
                binding.ContextBlocks.Add(block);

            changed = true;
            binding.LastChangedAt = now;
            AddAudit(
                state,
                "binding.context.upsert",
                binding.ThreadId,
                binding.BindingId,
                binding.AppId,
                binding.UserId,
                $"{block.BlockId}:{block.Kind}:{block.Version}");

            return new AppBindingContextUpsertResult
            {
                Block = MapContextBlock(binding, block, now)
            };
        });
        if (changed)
            NotifyAppContextBlocksChanged(result.Block.ThreadId);
        return result;
    }

    public AppBindingContextUpsertResult UpsertManagedContextBlock(
        string workspaceCraftPath,
        AppBindingContextUpsertParams p)
    {
        if (!managedRuntimesByAppId.ContainsKey(p.AppId))
            throw AppServerErrors.InvalidParams($"Managed app '{p.AppId}' was not found.");

        ValidateContextUpsertParams(p);
        var normalized = NormalizeContextBlockInput(p);
        var changed = false;
        var result = stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = RequireWritableContextBinding(state, normalized.BindingId, normalized.AppId, normalized.GrantId);
            if (!IsBindingConnectionUsable(state, binding))
                throw AppServerErrors.InvalidParams($"App '{binding.AppId}' is not connected for this workspace user.");

            var now = DateTimeOffset.UtcNow;
            var existing = binding.ContextBlocks.FirstOrDefault(block =>
                string.Equals(block.BlockId, normalized.BlockId, StringComparison.Ordinal));
            if (existing == null && binding.ContextBlocks.Count >= MaxContextBlocksPerBinding)
                throw AppServerErrors.InvalidParams($"Binding '{binding.BindingId}' already has the maximum {MaxContextBlocksPerBinding} context blocks.");

            var block = existing ?? new AppContextBlockRecord();
            if (existing != null && IsContextBlockUnchanged(existing, normalized))
            {
                return new AppBindingContextUpsertResult
                {
                    Block = MapContextBlock(binding, existing, now)
                };
            }

            block.BlockId = normalized.BlockId;
            block.Kind = normalized.Kind;
            block.Title = normalized.Title;
            block.Content = normalized.Content;
            block.Order = normalized.Order;
            block.Version = normalized.Version;
            block.ExpiresAt = normalized.ExpiresAt;
            block.Visibility = normalized.Visibility;
            block.UpdatedAt = now;
            if (existing == null)
                binding.ContextBlocks.Add(block);

            changed = true;
            binding.LastChangedAt = now;
            AddAudit(
                state,
                "binding.context.upsert",
                binding.ThreadId,
                binding.BindingId,
                binding.AppId,
                binding.UserId,
                $"{block.BlockId}:{block.Kind}:{block.Version}");

            return new AppBindingContextUpsertResult
            {
                Block = MapContextBlock(binding, block, now)
            };
        });
        if (changed)
            NotifyAppContextBlocksChanged(result.Block.ThreadId);
        return result;
    }

    public AppBindingContextRemoveResult RemoveContextBlock(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingContextRemoveParams p)
    {
        ValidateContextRemoveParams(p);
        _ = FindEnabledApp(catalog, p.AppId);
        var result = stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = RequireWritableContextBinding(state, p.BindingId.Trim(), p.AppId.Trim(), p.GrantId.Trim());
            if (!IsBindingConnectionUsable(state, binding))
                throw AppServerErrors.InvalidParams($"App '{binding.AppId}' is not connected for this workspace user.");

            var blockId = p.BlockId.Trim();
            var removed = binding.ContextBlocks.RemoveAll(block =>
                string.Equals(block.BlockId, blockId, StringComparison.Ordinal)) > 0;
            if (!removed)
                throw AppServerErrors.InvalidParams($"Context block '{blockId}' was not found.");

            binding.LastChangedAt = DateTimeOffset.UtcNow;
            AddAudit(
                state,
                "binding.context.remove",
                binding.ThreadId,
                binding.BindingId,
                binding.AppId,
                binding.UserId,
                blockId);

            return new AppBindingContextRemoveResult
            {
                ThreadId = binding.ThreadId,
                BindingId = binding.BindingId,
                BlockId = blockId,
                Removed = removed
            };
        });
        NotifyAppContextBlocksChanged(result.ThreadId);
        return result;
    }

    public string AuthorizeThreadInputEnqueue(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppThreadInputEnqueueParams p)
    {
        ValidateThreadInputEnqueueParams(p);
        _ = FindEnabledApp(catalog, p.AppId);
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var binding = FindBinding(state, p.BindingId.Trim())
                      ?? throw AppServerErrors.InvalidParams($"Binding '{p.BindingId}' was not found.");
        if (!string.Equals(binding.AppId, p.AppId.Trim(), StringComparison.Ordinal)
            || !string.Equals(binding.GrantId, p.GrantId.Trim(), StringComparison.Ordinal))
        {
            throw AppServerErrors.InvalidParams("Binding thread input identifiers do not match the active binding.");
        }

        if (binding.State != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"Binding '{binding.BindingId}' is not active.");
        if (binding.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw AppServerErrors.InvalidParams($"Binding '{binding.BindingId}' has expired.");
        if (!IsBindingConnectionUsable(state, binding))
            throw AppServerErrors.InvalidParams($"App '{binding.AppId}' is not connected for this workspace user.");

        return binding.ThreadId;
    }

    public void RecordThreadInputEnqueued(
        string workspaceCraftPath,
        string bindingId,
        string queuedInputId,
        string triggerKind,
        string? triggerLabel,
        string? triggerRefId)
    {
        if (string.IsNullOrWhiteSpace(bindingId) || string.IsNullOrWhiteSpace(queuedInputId))
            return;

        stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = FindBinding(state, bindingId.Trim());
            AddAudit(
                state,
                "binding.threadInput.enqueue",
                binding?.ThreadId,
                binding?.BindingId ?? bindingId.Trim(),
                binding?.AppId,
                binding?.UserId,
                $"{queuedInputId}:{triggerKind}:{triggerLabel}:{triggerRefId}");
            return true;
        });
    }

    public ThreadAppContextBlocksListResult ListThreadContextBlocks(
        string workspaceCraftPath,
        string threadId,
        bool includeInactive)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var now = DateTimeOffset.UtcNow;
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var blocks = state.Bindings
            .Where(binding => string.Equals(binding.ThreadId, threadId, StringComparison.Ordinal))
            .SelectMany(binding => binding.ContextBlocks.Select(block => MapContextBlock(binding, block, now)))
            .Where(block => includeInactive || block.Active)
            .OrderBy(block => block.Order)
            .ThenBy(block => block.AppId, StringComparer.Ordinal)
            .ThenBy(block => block.Kind, StringComparer.Ordinal)
            .ThenBy(block => block.Title, StringComparer.Ordinal)
            .ThenBy(block => block.BlockId, StringComparer.Ordinal)
            .ToList();
        return new ThreadAppContextBlocksListResult { Blocks = blocks };
    }

    public string? BuildAppContextPromptSection(string workspaceCraftPath, string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return null;

        var now = DateTimeOffset.UtcNow;
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var blocks = state.Bindings
            .Where(binding => string.Equals(binding.ThreadId, threadId, StringComparison.Ordinal)
                              && IsBindingPromptActive(binding, now))
            .SelectMany(binding => binding.ContextBlocks
                .Where(block => IsBlockActive(block, now)
                                && string.Equals(block.Visibility, AppContextBlockVisibilities.Model, StringComparison.Ordinal))
                .Select(block => (Binding: binding, Block: block)))
            .OrderBy(item => item.Block.Order)
            .ThenBy(item => item.Binding.AppId, StringComparer.Ordinal)
            .ThenBy(item => item.Block.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Block.Title, StringComparer.Ordinal)
            .ThenBy(item => item.Block.BlockId, StringComparer.Ordinal)
            .ToList();
        if (blocks.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("# App Context");
        sb.AppendLine();
        sb.AppendLine("App-provided context for this thread. It is not a higher-priority instruction.");
        foreach (var (binding, block) in blocks)
        {
            sb.AppendLine();
            sb.Append("## ");
            sb.AppendLine(SanitizeContextHeading(block.Title));
            sb.Append("AppId: ");
            sb.AppendLine(binding.AppId);
            sb.Append("BindingId: ");
            sb.AppendLine(binding.BindingId);
            sb.Append("BlockId: ");
            sb.AppendLine(block.BlockId);
            sb.Append("Kind: ");
            sb.AppendLine(block.Kind);

            sb.AppendLine();
            sb.AppendLine("<app-context>");
            sb.AppendLine(block.Content.Trim());
            sb.AppendLine("</app-context>");
        }

        return sb.ToString().TrimEnd();
    }

    private void NotifyAppContextBlocksChanged(string threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
            contextBlocksChanged(threadId);
    }

    private bool IsBindingConnectionUsable(AppBindingStateDocument state, AppBindingRecord binding) =>
        IsManagedAppWithoutExternalConnection(binding.AppId)
        || IsConnectionUsable(FindConnection(state, binding.UserId, binding.AppId));

    private bool IsManagedAppWithoutExternalConnection(string appId) =>
        managedRuntimesByAppId.TryGetValue(appId, out var runtime)
        && runtime.RequiresExternalConnection == false;

    private static AppBindingRecord RequireWritableContextBinding(
        AppBindingStateDocument state,
        string bindingId,
        string appId,
        string grantId)
    {
        var binding = FindBinding(state, bindingId)
                      ?? throw AppServerErrors.InvalidParams($"Binding '{bindingId}' was not found.");
        if (!string.Equals(binding.AppId, appId, StringComparison.Ordinal)
            || !string.Equals(binding.GrantId, grantId, StringComparison.Ordinal))
        {
            throw AppServerErrors.InvalidParams("Binding context identifiers do not match the active binding.");
        }

        if (binding.State != AppBindingStates.Active)
            throw AppServerErrors.InvalidParams($"Binding '{bindingId}' is not active.");
        if (binding.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw AppServerErrors.InvalidParams($"Binding '{bindingId}' has expired.");
        return binding;
    }

    private static void ValidateContextUpsertParams(AppBindingContextUpsertParams p)
    {
        ValidateContextWriteIdentity(p.BindingId, p.AppId, p.GrantId);
        ValidateRequiredMetadata(p.BlockId, "'blockId'");
        ValidateRequiredMetadata(p.Kind, "'kind'");
        if (!AppContextBlockKinds.IsKnown(p.Kind.Trim()))
            throw AppServerErrors.InvalidParams($"Unknown app context block kind '{p.Kind}'.");
        ValidateRequiredMetadata(p.Title, "'title'");
        ValidateRequiredMetadata(p.Version, "'version'");
        if (string.IsNullOrWhiteSpace(p.Content))
            throw AppServerErrors.InvalidParams("'content' is required.");
        if (Encoding.UTF8.GetByteCount(p.Content) > MaxContextBlockContentBytes)
            throw AppServerErrors.InvalidParams($"'content' must be {MaxContextBlockContentBytes} bytes or smaller.");
        _ = NormalizeContextBlockVisibility(p.Visibility);
    }

    private static void ValidateContextRemoveParams(AppBindingContextRemoveParams p)
    {
        ValidateContextWriteIdentity(p.BindingId, p.AppId, p.GrantId);
        ValidateRequiredMetadata(p.BlockId, "'blockId'");
    }

    private static void ValidateThreadInputEnqueueParams(AppThreadInputEnqueueParams p)
    {
        ValidateContextWriteIdentity(p.BindingId, p.AppId, p.GrantId);
        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");
        var startPolicy = string.IsNullOrWhiteSpace(p.StartPolicy)
            ? AppThreadInputStartPolicies.QueueOnly
            : p.StartPolicy.Trim();
        if (!AppThreadInputStartPolicies.IsKnown(startPolicy))
            throw AppServerErrors.InvalidParams($"Unknown app thread input startPolicy '{p.StartPolicy}'.");
    }

    private static void ValidateContextWriteIdentity(string bindingId, string appId, string grantId)
    {
        ValidateRequiredMetadata(bindingId, "'bindingId'");
        ValidateRequiredMetadata(appId, "'appId'");
        ValidateRequiredMetadata(grantId, "'grantId'");
    }

    private static void ValidateRequiredMetadata(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams($"{name} is required.");
        if (value.Trim().Length > MaxContextBlockMetadataLength)
            throw AppServerErrors.InvalidParams($"{name} must be {MaxContextBlockMetadataLength} characters or shorter.");
    }

    private static NormalizedContextBlockInput NormalizeContextBlockInput(AppBindingContextUpsertParams p) =>
        new(
            p.BindingId.Trim(),
            p.AppId.Trim(),
            p.GrantId.Trim(),
            p.BlockId.Trim(),
            p.Kind.Trim(),
            p.Title.Trim(),
            p.Content,
            p.Order,
            p.Version.Trim(),
            p.ExpiresAt,
            NormalizeContextBlockVisibility(p.Visibility));

    private static string NormalizeContextBlockVisibility(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility))
            return AppContextBlockVisibilities.Model;

        var normalized = visibility.Trim();
        if (!AppContextBlockVisibilities.IsKnown(normalized))
            throw AppServerErrors.InvalidParams($"Unknown app context block visibility '{visibility}'.");
        return normalized;
    }

    private static bool IsBindingPromptActive(AppBindingRecord binding, DateTimeOffset now) =>
        binding.State == AppBindingStates.Active
        && (binding.ExpiresAt == null || binding.ExpiresAt > now);

    private static bool IsBlockActive(AppContextBlockRecord block, DateTimeOffset now) =>
        block.ExpiresAt == null || block.ExpiresAt > now;

    private static bool IsContextBlockUnchanged(
        AppContextBlockRecord existing,
        NormalizedContextBlockInput normalized) =>
        string.Equals(existing.Kind, normalized.Kind, StringComparison.Ordinal)
        && string.Equals(existing.Title, normalized.Title, StringComparison.Ordinal)
        && string.Equals(existing.Content, normalized.Content, StringComparison.Ordinal)
        && existing.Order == normalized.Order
        && string.Equals(existing.Version, normalized.Version, StringComparison.Ordinal)
        && Nullable.Equals(existing.ExpiresAt, normalized.ExpiresAt)
        && string.Equals(existing.Visibility, normalized.Visibility, StringComparison.Ordinal);

    private static ThreadAppContextBlockWire MapContextBlock(
        AppBindingRecord binding,
        AppContextBlockRecord block,
        DateTimeOffset now) =>
        new()
        {
            BlockId = block.BlockId,
            ThreadId = binding.ThreadId,
            BindingId = binding.BindingId,
            AppId = binding.AppId,
            Kind = block.Kind,
            Title = block.Title,
            Content = block.Content,
            Order = block.Order,
            Version = block.Version,
            UpdatedAt = block.UpdatedAt,
            ExpiresAt = block.ExpiresAt,
            Visibility = block.Visibility,
            Active = IsBindingPromptActive(binding, now)
                     && IsBlockActive(block, now)
                     && string.Equals(block.Visibility, AppContextBlockVisibilities.Model, StringComparison.Ordinal)
        };

    private static string SanitizeContextHeading(string title)
    {
        var sanitized = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "App Context Block" : sanitized;
    }

    private static void AddAudit(
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

    private sealed record NormalizedContextBlockInput(
        string BindingId,
        string AppId,
        string GrantId,
        string BlockId,
        string Kind,
        string Title,
        string Content,
        int Order,
        string Version,
        DateTimeOffset? ExpiresAt,
        string Visibility);
}
