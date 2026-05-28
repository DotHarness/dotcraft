using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Repairs model-visible chat history so strict providers never receive half of a tool-use pair.
/// </summary>
internal static class ModelRequestHistorySanitizer
{
    internal const string SyntheticToolResultText =
        "Tool result unavailable: DotCraft repaired an incomplete historical tool call. " +
        "The tool was not executed or its result was not persisted. Continue from the latest user request and retry if needed.";

    public static IReadOnlyList<ChatMessage> Sanitize(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return messages;

        List<ChatMessage>? repaired = null;
        List<FunctionCallContent>? pendingCalls = null;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (pendingCalls is { Count: > 0 })
            {
                if (message.Role == ChatRole.Tool)
                {
                    var repairedTool = RepairToolMessage(message, pendingCalls, out var changedTool);
                    if (changedTool)
                        repaired ??= CopyPrefix(messages, i);
                    repaired?.Add(repairedTool);
                    pendingCalls = null;
                    continue;
                }

                repaired ??= CopyPrefix(messages, i);
                repaired.Add(CreateSyntheticToolMessage(pendingCalls));
                pendingCalls = null;
            }

            repaired?.Add(message);
            pendingCalls = CollectFunctionCalls(message);
        }

        if (pendingCalls is { Count: > 0 })
        {
            repaired ??= CopyPrefix(messages, messages.Count);
            repaired.Add(CreateSyntheticToolMessage(pendingCalls));
        }

        return repaired ?? messages;
    }

    private static List<ChatMessage> CopyPrefix(IReadOnlyList<ChatMessage> messages, int count)
    {
        var result = new List<ChatMessage>(messages.Count + 1);
        for (var i = 0; i < count; i++)
            result.Add(messages[i]);
        return result;
    }

    private static List<FunctionCallContent>? CollectFunctionCalls(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant)
            return null;

        List<FunctionCallContent>? calls = null;
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent { CallId: { Length: > 0 } } call)
                (calls ??= []).Add(call);
        }

        return calls;
    }

    private static ChatMessage RepairToolMessage(
        ChatMessage message,
        IReadOnlyList<FunctionCallContent> pendingCalls,
        out bool changed)
    {
        changed = false;
        var existingResults = new Dictionary<string, FunctionResultContent>(StringComparer.Ordinal);
        foreach (var content in message.Contents)
        {
            if (content is FunctionResultContent { CallId: { Length: > 0 } } result &&
                !existingResults.ContainsKey(result.CallId))
            {
                existingResults.Add(result.CallId, result);
            }
            else
            {
                changed = true;
            }
        }

        var contents = new List<AIContent>(pendingCalls.Count);
        foreach (var call in pendingCalls)
        {
            if (existingResults.TryGetValue(call.CallId, out var result))
            {
                contents.Add(result);
                continue;
            }

            contents.Add(CreateSyntheticResult(call.CallId));
            changed = true;
        }

        if (!changed && contents.Count == message.Contents.Count)
            return message;

        return CloneWithContents(message, contents);
    }

    private static ChatMessage CreateSyntheticToolMessage(IEnumerable<FunctionCallContent> calls) =>
        new(ChatRole.Tool, calls.Select(call => (AIContent)CreateSyntheticResult(call.CallId)).ToList());

    private static FunctionResultContent CreateSyntheticResult(string callId) =>
        new(callId, SyntheticToolResultText);

    private static ChatMessage CloneWithContents(ChatMessage message, IList<AIContent> contents) =>
        new(message.Role, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId
        };
}
