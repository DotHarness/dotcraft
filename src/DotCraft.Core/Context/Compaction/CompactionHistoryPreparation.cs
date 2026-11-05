using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Context.WorldState;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionHistoryPreparation
{
    internal static IReadOnlyList<ChatMessage> Conversation(IReadOnlyList<ChatMessage> history) =>
        history.Where(static message => !IsContext(message)).ToArray();

    internal static bool IsContext(ChatMessage message) =>
        AgentInstructionsHistory.IsInstructions(message)
        || WorldStateComposer.SectionIdFromItemKind(ThreadContextItems.GetKind(message)) != null
        || ThreadContextItems.IsKind(message, ThreadContextItems.SubAgentRoleKind);

    internal static IReadOnlyList<ChatMessage> Replacement(
        IReadOnlyList<ChatMessage> tail, IProviderHistoryRewritePolicy? policy)
    {
        var messages = ImageContentSanitizingChatClient.ReplaceToolImagesWithDescriptions(Conversation(tail));
        return policy?.PrepareForNewPrefix(messages) ?? messages;
    }

    internal static IReadOnlyList<ChatMessage> Fallback(IReadOnlyList<ChatMessage> history, IChatClient client)
    {
        var messages = ImageContentSanitizingChatClient.ReplaceToolImagesWithDescriptions(Conversation(history));
        var sanitized = messages.Select(message =>
        {
            if (!message.Contents.Any(IsBinary))
                return message;
            var rewritten = message.Clone();
            rewritten.Contents = message.Contents.Select(content => IsBinary(content)
                ? new TextContent("[Large binary content omitted during context compaction.]") : content).ToList();
            rewritten.RawRepresentation = null;
            return rewritten;
        }).ToArray();
        return client.GetService<IProviderHistoryRewritePolicy>()?.PrepareForNewPrefix(sanitized) ?? sanitized;
    }

    private static bool IsBinary(AIContent content)
    {
        var mediaType = content switch { DataContent data => data.MediaType, UriContent uri => uri.MediaType, _ => null };
        return mediaType is not null && (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase));
    }

    internal static int OriginalBoundary(IReadOnlyList<ChatMessage> history, int conversationBoundary)
    {
        var count = 0;
        for (var i = 0; i < history.Count; i++)
        {
            if (IsContext(history[i]))
                continue;
            if (count++ == conversationBoundary)
                return i;
        }
        return history.Count;
    }

    internal static string? SnapshotFailure(
        PromptRequestSnapshot? snapshot, IReadOnlyList<ChatMessage> history, bool runnerAvailable)
    {
        if (snapshot is null)
            return "snapshot_absent";
        if (!runnerAvailable)
            return "snapshot_runner_unavailable";
        if (snapshot.Messages.Count > history.Count)
            return "snapshot_longer_than_history";
        if (snapshot.MessageFingerprint == MessageTokenEstimator.ComputePrefixFingerprint(history, snapshot.Messages.Count))
            return null;

        for (var i = 0; i < snapshot.Messages.Count; i++)
        {
            if (MessageTokenEstimator.ComputePrefixFingerprint([snapshot.Messages[i]], 1)
                != MessageTokenEstimator.ComputePrefixFingerprint([history[i]], 1))
                return $"history_prefix_mismatch:{i}";
        }
        return "snapshot_fingerprint_mismatch";
    }
}
