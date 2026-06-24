using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionMessageTruncator
{
    private const string TruncatedToolResultMarker =
        "Output exceeded the available model context and was truncated.";

    public static bool TryTruncateOldestToolResult(
        IReadOnlyList<ChatMessage> messages,
        out List<ChatMessage> truncated)
    {
        truncated = [];
        var markerTokens = MessageTokenEstimator.RoughTokenCount(TruncatedToolResultMarker);
        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            for (var contentIndex = 0; contentIndex < message.Contents.Count; contentIndex++)
            {
                if (message.Contents[contentIndex] is not FunctionResultContent result
                    || IsAlreadyTruncated(result)
                    || MessageTokenEstimator.EstimateContent(result) <= markerTokens + 16)
                {
                    continue;
                }

                truncated = messages.Select(message => message.Clone()).ToList();
                var rewrittenMessage = truncated[messageIndex];
                var contents = new List<AIContent>(rewrittenMessage.Contents);
                contents[contentIndex] = new FunctionResultContent(result.CallId, TruncatedToolResultMarker);
                truncated[messageIndex] = new ChatMessage(rewrittenMessage.Role, contents)
                {
                    AuthorName = rewrittenMessage.AuthorName,
                    MessageId = rewrittenMessage.MessageId
                };
                return true;
            }
        }

        return false;
    }

    public static List<ChatMessage> TruncateOldestGroups(IReadOnlyList<ChatMessage> messages)
    {
        var groups = MessageGrouper.GroupByApiRound(messages);
        if (groups.Count <= 1)
            return [];

        var dropCount = Math.Clamp((int)Math.Ceiling(groups.Count * 0.2), 1, groups.Count - 1);
        var truncated = groups
            .Skip(dropCount)
            .SelectMany(group => group.Messages)
            .ToList();
        return MessageGrouper.EnsurePairing(truncated);
    }

    private static bool IsAlreadyTruncated(FunctionResultContent result) =>
        result.Result is string text
        && (string.Equals(text, TruncatedToolResultMarker, StringComparison.Ordinal)
            || string.Equals(text, CompactableToolNames.ClearedResultMarker, StringComparison.Ordinal));
}
