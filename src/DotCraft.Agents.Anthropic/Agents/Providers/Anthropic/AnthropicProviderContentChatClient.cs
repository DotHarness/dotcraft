using System.Runtime.CompilerServices;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicProviderContentChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Rewrite(messages), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(Rewrite(messages), options, cancellationToken))
            yield return update;
    }

    private static IReadOnlyList<ChatMessage> Rewrite(IEnumerable<ChatMessage> source)
    {
        var messages = source as IReadOnlyList<ChatMessage> ?? source.ToArray();
        List<ChatMessage>? rewritten = null;
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var next = Rewrite(message);
            if (!ReferenceEquals(next, message))
            {
                rewritten ??= [.. messages.Take(index)];
                rewritten.Add(next);
            }
            else
                rewritten?.Add(message);
        }
        return rewritten ?? messages;
    }

    private static ChatMessage Rewrite(ChatMessage message)
    {
        List<AIContent>? contents = null;
        for (var index = 0; index < message.Contents.Count; index++)
        {
            var content = message.Contents[index];
            var next = Rewrite(content);
            if (!ReferenceEquals(next, content))
            {
                contents ??= [.. message.Contents.Take(index)];
                contents.Add(next);
            }
            else
                contents?.Add(content);
        }
        return contents == null ? message : new ChatMessage(message.Role, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };
    }

    private static AIContent Rewrite(AIContent content)
    {
        if (content is DeferredToolReferenceContent reference)
        {
            return new TextContent(string.Empty)
            {
                RawRepresentation = new Block(new BetaToolReferenceBlockParam(reference.ToolName))
            };
        }

        if (content is FunctionResultContent result)
        {
            var rewrittenResult = RewriteResult(result.Result);
            var rewritten = ReferenceEquals(rewrittenResult, result.Result)
                ? result
                : new FunctionResultContent(result.CallId, rewrittenResult)
                {
                    AdditionalProperties = result.AdditionalProperties,
                    Exception = result.Exception,
                    RawRepresentation = result.RawRepresentation
                };
            return TryGetCacheTtl(result.AdditionalProperties, out var ttl)
                ? AnthropicPromptCacheDialect.Instance.MarkFunctionResult(
                    rewritten,
                    rewrittenResult?.ToString() ?? string.Empty,
                    AnthropicPromptCacheDialect.Instance.CreateMarker(ttl))
                : rewritten;
        }

        if (content is TextContent text && TryGetCacheTtl(text.AdditionalProperties, out var textTtl))
        {
            return AnthropicPromptCacheDialect.Instance.MarkText(
                text,
                AnthropicPromptCacheDialect.Instance.CreateMarker(textTtl));
        }

        return content;
    }

    private static object? RewriteResult(object? result)
    {
        if (result is not IEnumerable<AIContent> contents)
            return result;
        var original = contents as IReadOnlyList<AIContent> ?? contents.ToArray();
        var rewritten = original.Select(Rewrite).ToArray();
        return rewritten.Where((item, index) => !ReferenceEquals(item, original[index])).Any()
            ? rewritten
            : result;
    }

    private static bool TryGetCacheTtl(AdditionalPropertiesDictionary? properties, out string? ttl)
    {
        ttl = null;
        if (properties == null || !properties.TryGetValue("cache_control", out var value))
            return false;
        if (value is IReadOnlyDictionary<string, object> marker
            && marker.TryGetValue("ttl", out var ttlValue))
            ttl = ttlValue?.ToString();
        return true;
    }
}
