using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// A contiguous span of messages that forms a single API response round.
/// </summary>
public sealed record MessageGroup(IReadOnlyList<ChatMessage> Messages, int EstimatedTokens);

/// <summary>
/// Groups <see cref="ChatMessage"/> sequences into API-round groups so that
/// compaction never splits a <see cref="FunctionCallContent"/> from its
/// matching <see cref="FunctionResultContent"/>.
/// </summary>
public static class MessageGrouper
{
    /// <summary>
    /// Splits <paramref name="messages"/> into API-round groups.
    /// Boundary rule: a new group begins when a new assistant response starts.
    /// Streaming chunks from the same API response normally share
    /// <see cref="ChatMessage.MessageId"/> and stay in one group; messages
    /// without ids conservatively start a new assistant group.
    /// </summary>
    public static IReadOnlyList<MessageGroup> GroupByApiRound(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return Array.Empty<MessageGroup>();

        var groups = new List<MessageGroup>();
        var current = new List<ChatMessage>();
        string? lastAssistantMessageId = null;

        foreach (var msg in messages)
        {
            var startsNewGroup = IsAssistantResponseBoundary(msg, lastAssistantMessageId)
                && current.Count > 0;
            if (startsNewGroup)
            {
                groups.Add(CreateGroup(current));
                current = new List<ChatMessage>();
            }

            current.Add(msg);
            if (msg.Role == ChatRole.Assistant)
                lastAssistantMessageId = string.IsNullOrEmpty(msg.MessageId) ? null : msg.MessageId;
        }

        if (current.Count > 0)
            groups.Add(CreateGroup(current));

        return groups;
    }

    /// <summary>
    /// Filters <paramref name="messages"/> so that any dangling
    /// <see cref="FunctionCallContent"/> — a tool call whose matching
    /// <see cref="FunctionResultContent"/> was dropped — is removed before the
    /// slice is handed to the summarizer so the summary call never sees half a
    /// tool-use pair.
    /// </summary>
    public static List<ChatMessage> EnsurePairing(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return new List<ChatMessage>();

        var resultCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent fr && !string.IsNullOrEmpty(fr.CallId))
                    resultCallIds.Add(fr.CallId);
            }
        }

        var result = new List<ChatMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var keepAll = true;
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !string.IsNullOrEmpty(fc.CallId)
                    && !resultCallIds.Contains(fc.CallId))
                {
                    keepAll = false;
                    break;
                }
            }

            if (keepAll)
            {
                result.Add(msg);
                continue;
            }

            var filtered = new List<AIContent>(msg.Contents.Count);
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !string.IsNullOrEmpty(fc.CallId)
                    && !resultCallIds.Contains(fc.CallId))
                    continue;
                filtered.Add(content);
            }

            if (filtered.Count > 0)
            {
                var rebuilt = new ChatMessage(msg.Role, filtered)
                {
                    AuthorName = msg.AuthorName,
                    MessageId = msg.MessageId,
                };
                result.Add(rebuilt);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns messages whose tool-use argument payloads are always object-shaped.
    /// Some providers reject <see langword="null"/> tool arguments even for
    /// zero-argument tools.
    /// </summary>
    public static List<ChatMessage> NormalizeFunctionCallArguments(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
            result.Add(NormalizeFunctionCallArguments(message));
        return result;
    }

    /// <summary>
    /// Returns a message whose tool-use argument payloads are object-shaped.
    /// </summary>
    public static ChatMessage NormalizeFunctionCallArguments(ChatMessage message)
    {
        if (!message.Contents.OfType<FunctionCallContent>().Any(static call => call.Arguments is null))
            return message;

        var clone = message.Clone();
        foreach (var call in clone.Contents.OfType<FunctionCallContent>())
            call.Arguments ??= new Dictionary<string, object?>();
        return clone;
    }

    private static MessageGroup CreateGroup(List<ChatMessage> messages) =>
        new(messages.ToArray(), MessageTokenEstimator.Estimate(messages));

    private static bool IsAssistantResponseBoundary(ChatMessage msg, string? lastAssistantMessageId)
    {
        if (msg.Role != ChatRole.Assistant)
            return false;

        if (string.IsNullOrEmpty(msg.MessageId))
            return true;

        return !string.Equals(msg.MessageId, lastAssistantMessageId, StringComparison.Ordinal);
    }
}
