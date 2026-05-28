using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Removes server-side tool call/result contents from outward chat updates so
/// OpenAI protocol clients that don't provide tools won't receive tool calls.
/// </summary>
public sealed class ToolCallFilteringChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            var sanitized = Sanitize(update);
            if (sanitized != null)
            {
                yield return sanitized;
            }
        }
    }

    private static ChatResponseUpdate? Sanitize(ChatResponseUpdate update)
    {
        if (update.Contents.Count == 0)
        {
            return update;
        }

        var filteredContents = new List<AIContent>(update.Contents.Count);
        for (var i = 0; i < update.Contents.Count; i++)
        {
            var content = update.Contents[i];
            if (content is FunctionCallContent or FunctionResultContent)
            {
                continue;
            }

            filteredContents.Add(content);
        }

        if (filteredContents.Count == update.Contents.Count)
        {
            return update;
        }

        if (filteredContents.Count == 0 && update.FinishReason == ChatFinishReason.ToolCalls)
        {
            // Drop pure tool-call updates to avoid exposing internal tool traffic.
            return null;
        }

        return new ChatResponseUpdate(update.Role, filteredContents)
        {
            ConversationId = update.ConversationId,
            ResponseId = update.ResponseId,
            FinishReason = update.FinishReason,
            AdditionalProperties = update.AdditionalProperties,
            AuthorName = update.AuthorName,
            CreatedAt = update.CreatedAt,
            MessageId = update.MessageId,
            ModelId = update.ModelId
        };
    }
}
