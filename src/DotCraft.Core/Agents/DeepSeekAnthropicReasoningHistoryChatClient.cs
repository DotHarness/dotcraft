using AnthropicBetaContentBlockParam = Anthropic.Models.Beta.Messages.BetaContentBlockParam;
using AnthropicBetaThinkingBlockParam = Anthropic.Models.Beta.Messages.BetaThinkingBlockParam;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Normalizes DotCraft reasoning history to the DeepSeek Anthropic-compatible thinking block shape.
/// </summary>
internal sealed class DeepSeekAnthropicReasoningHistoryChatClient : DelegatingChatClient
{
    internal const string ThinkingBlockType = "thinking";

    private readonly bool _usesThinkingHistoryBlocks;

    public DeepSeekAnthropicReasoningHistoryChatClient(
        IChatClient innerClient,
        ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData adapter)
        : base(innerClient)
    {
        _usesThinkingHistoryBlocks = string.Equals(
            adapter.ReasoningHistoryBlockType,
            ThinkingBlockType,
            StringComparison.OrdinalIgnoreCase);
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(PrepareMessages(chatMessages), options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(PrepareMessages(chatMessages), options, cancellationToken);
    }

    internal IReadOnlyList<ChatMessage> PrepareMessages(IEnumerable<ChatMessage> messages)
    {
        var original = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        if (!_usesThinkingHistoryBlocks || original.Count == 0)
            return original;

        List<ChatMessage>? rewritten = null;
        for (var i = 0; i < original.Count; i++)
        {
            var message = original[i];
            var prepared = PrepareMessage(message);
            if (!ReferenceEquals(prepared, message))
            {
                rewritten ??= [.. original.Take(i)];
                rewritten.Add(prepared);
            }
            else
            {
                rewritten?.Add(message);
            }
        }

        return rewritten ?? original;
    }

    private static ChatMessage PrepareMessage(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant || message.Contents.Count == 0)
            return message;

        List<AIContent>? contents = null;
        for (var i = 0; i < message.Contents.Count; i++)
        {
            var content = message.Contents[i];
            var prepared = PrepareContent(content);
            if (!ReferenceEquals(prepared, content))
            {
                contents ??= [.. message.Contents.Take(i)];
                contents.Add(prepared);
            }
            else
            {
                contents?.Add(content);
            }
        }

        if (contents == null)
            return message;

        return new ChatMessage(message.Role, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId
        };
    }

    private static AIContent PrepareContent(AIContent content)
    {
        if (content is not TextReasoningContent reasoning || string.IsNullOrEmpty(reasoning.Text))
            return content;

        return new TextReasoningContent(reasoning.Text)
        {
            AdditionalProperties = reasoning.AdditionalProperties,
            Annotations = reasoning.Annotations,
            ProtectedData = reasoning.ProtectedData,
            RawRepresentation = CreateThinkingBlock(reasoning.Text, reasoning.ProtectedData)
        };
    }

    private static AnthropicBetaContentBlockParam CreateThinkingBlock(string thinking, string? signature)
    {
        return new AnthropicBetaContentBlockParam(
            new AnthropicBetaThinkingBlockParam
            {
                Thinking = thinking,
                Signature = signature ?? string.Empty
            },
            null);
    }
}
