using DotCraft.Mcp;
using DotCraft.Tools;

namespace DotCraft.Protocol.AppServer;

internal sealed record McpAppEligibility(
    McpToolCallPayload Payload,
    ToolRegistration Registration,
    McpAppToolMetadata AppMetadata,
    McpClientManager Manager,
    long Generation,
    EffectiveToolSnapshot Snapshot);

internal sealed record McpAppEligibilityContext(
    EffectiveToolSnapshot Snapshot,
    McpClientManager Manager,
    IReadOnlyDictionary<string, McpServerStatusSnapshot> Statuses);

internal static class McpAppEligibilityResolver
{
    public static async ValueTask<McpAppEligibilityContext?> CreateContextAsync(
        string threadId,
        IThreadToolSnapshotService? snapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        CancellationToken cancellationToken)
    {
        if (snapshots is null || mcpRuntime is null)
            return null;

        try
        {
            var snapshot = await snapshots
                .GetEffectiveToolSnapshotAsync(threadId, cancellationToken)
                .ConfigureAwait(false);
            var manager = await mcpRuntime
                .GetEffectiveMcpRuntimeAsync(threadId, cancellationToken)
                .ConfigureAwait(false);
            if (manager is null)
                return null;

            var statuses = (await manager.ListStatusesAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(status => status.Name, StringComparer.OrdinalIgnoreCase);
            return new McpAppEligibilityContext(snapshot, manager, statuses);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Availability is an optional projection. Runtime inspection failures must not make
            // the underlying tool result or thread unreadable.
            return null;
        }
    }

    public static async ValueTask<McpAppEligibility?> ResolveAsync(
        string threadId,
        string turnId,
        SessionItem? item,
        IThreadToolSnapshotService? snapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(
            threadId,
            snapshots,
            mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        return context is null ? null : Resolve(turnId, item, context);
    }

    public static McpAppEligibility? Resolve(
        string turnId,
        SessionItem? item,
        McpAppEligibilityContext context)
    {
        if (item is null
            || item.Type != ItemType.McpToolCall
            || item.Status != ItemStatus.Completed
            || !string.Equals(item.TurnId, turnId, StringComparison.Ordinal)
            || item.AsMcpToolCall is not { } payload
            || payload.Status is not ("completed" or "failed")
            || string.IsNullOrWhiteSpace(payload.McpAppResourceUri)
            || !Uri.TryCreate(payload.McpAppResourceUri, UriKind.Absolute, out var persistedResourceUri)
            || !string.Equals(persistedResourceUri.Scheme, "ui", StringComparison.OrdinalIgnoreCase))
            return null;

        ToolName canonicalName;
        try
        {
            canonicalName = new ToolName(payload.Namespace, payload.ToolName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!context.Snapshot.Registrations.TryGetValue(canonicalName, out var registration)
            || registration.Definition.Id.ToString() != payload.ToolDefinitionId
            || registration.Definition.Id.Kind != ToolSourceKind.Mcp
            || !string.Equals(registration.Definition.Id.SourceId, payload.Server, StringComparison.Ordinal)
            || !string.Equals(registration.Definition.Id.SourceToolId.Value, payload.SourceToolId, StringComparison.Ordinal)
            || !registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
            || !McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var appMetadata)
            || !appMetadata.Visibility.HasFlag(McpAppVisibility.App)
            || appMetadata.ResourceUri is null
            || Uri.Compare(
                    appMetadata.ResourceUri,
                    persistedResourceUri,
                    UriComponents.AbsoluteUri,
                    UriFormat.SafeUnescaped,
                    StringComparison.Ordinal) != 0
            || !context.Statuses.TryGetValue(payload.Server, out var status)
            || !status.Enabled
            || !string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase))
            return null;

        // Binding MCP sessions carry thread-scoped authority. A rebind must not make a result
        // from an older authority revision interactive again.
        if (string.Equals(payload.Origin, "binding", StringComparison.OrdinalIgnoreCase)
            && registration.Binding.Revision != payload.BindingRevision)
            return null;

        var generation = registration.Binding.Revision;
        return new McpAppEligibility(
            payload,
            registration,
            appMetadata,
            context.Manager,
            generation,
            context.Snapshot);
    }
}
