using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicHistoryRewritePolicy : IProviderHistoryRewritePolicy
{
    internal static readonly AnthropicHistoryRewritePolicy Instance = new();

    public IReadOnlyList<ChatMessage> PrepareForNewPrefix(IReadOnlyList<ChatMessage> messages)
    {
        List<ChatMessage>? result = null;
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var retained = message.Contents.Where(content => !IsThinking(content)).ToList();
            if (retained.Count == message.Contents.Count)
            {
                result?.Add(message);
                continue;
            }

            result ??= messages.Take(index).ToList();
            if (retained.Count == 0)
                continue;

            var rewritten = message.Clone();
            rewritten.Contents = retained;
            rewritten.RawRepresentation = null;
            result.Add(rewritten);
        }
        return result ?? messages;
    }

    private static bool IsThinking(AIContent content) =>
        content is TextReasoningContent
        || content.RawRepresentation is BetaContentBlockParam block
            && block.Json.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() is "thinking" or "redacted_thinking";
}
