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
                    var toolMessages = new List<ChatMessage>();
                    var blockStart = i;
                    while (i < messages.Count && messages[i].Role == ChatRole.Tool)
                    {
                        toolMessages.Add(messages[i]);
                        i++;
                    }

                    i--;
                    var repairedTool = RepairToolMessages(toolMessages, pendingCalls, out var changedTool);
                    if (changedTool)
                        repaired ??= CopyPrefix(messages, blockStart);
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

    private static ChatMessage RepairToolMessages(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<FunctionCallContent> pendingCalls,
        out bool changed)
    {
        changed = messages.Count != 1;
        var existingResults = new Dictionary<string, FunctionResultContent>(StringComparer.Ordinal);
        var originalResults = new List<FunctionResultContent>();
        var pendingCallIds = pendingCalls
            .Select(static call => call.CallId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent { CallId: { Length: > 0 } } result)
                {
                    originalResults.Add(result);
                    if (pendingCallIds.Contains(result.CallId) && !existingResults.ContainsKey(result.CallId))
                    {
                        existingResults.Add(result.CallId, result);
                        continue;
                    }
                }

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

        if (!changed)
        {
            if (originalResults.Count == contents.Count)
            {
                for (var i = 0; i < contents.Count; i++)
                {
                    if (!ReferenceEquals(originalResults[i], contents[i]))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            else
            {
                changed = true;
            }
        }

        if (!changed)
            return messages[0];

        return CloneWithContents(messages[0], contents);
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
