using DotCraft.Mcp;
using DotCraft.Tools;

namespace DotCraft.Protocol.AppServer;

internal sealed record McpAppLiveEligibility(
    McpToolCallPayload Payload,
    ToolRegistration Registration,
    McpAppToolMetadata AppMetadata,
    McpClientManager Manager,
    long Generation);

internal static class McpAppLiveEligibilityResolver
{
    public static async ValueTask<McpAppLiveEligibility?> ResolveAsync(
        string threadId,
        string turnId,
        SessionItem? item,
        IThreadToolSnapshotService? snapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        CancellationToken cancellationToken)
    {
        if (snapshots is null
            || mcpRuntime is null
            || item is null
            || item.Type != ItemType.McpToolCall
            || item.Status != ItemStatus.Completed
            || !string.Equals(item.TurnId, turnId, StringComparison.Ordinal)
            || item.AsMcpToolCall is not { } payload
            || payload.Status is not ("completed" or "failed")
            || payload.McpGeneration is not { } generation)
            return null;

        var snapshot = await snapshots
            .GetEffectiveToolSnapshotAsync(threadId, cancellationToken)
            .ConfigureAwait(false);
        var canonicalName = new ToolName(payload.Namespace, payload.ToolName);
        if (!snapshot.Registrations.TryGetValue(canonicalName, out var registration)
            || snapshot.Revision != payload.SnapshotRevision
            || registration.Definition.Id.ToString() != payload.ToolDefinitionId
            || registration.Binding.Id.Value != payload.RuntimeBindingId
            || registration.Binding.Revision != payload.BindingRevision
            || registration.Definition.Id.Kind != ToolSourceKind.Mcp
            || !registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
            || !McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var appMetadata)
            || !appMetadata.Visibility.HasFlag(McpAppVisibility.App)
            || appMetadata.ResourceUri is null)
            return null;

        var manager = await mcpRuntime
            .GetEffectiveMcpRuntimeAsync(threadId, cancellationToken)
            .ConfigureAwait(false);
        if (manager is null
            || await manager.GetGenerationAsync(payload.Server, cancellationToken).ConfigureAwait(false) != generation)
            return null;

        return new McpAppLiveEligibility(payload, registration, appMetadata, manager, generation);
    }
}
