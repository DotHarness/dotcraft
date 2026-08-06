using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicPromptCacheDialect : IPromptCacheDialect
{
    public static AnthropicPromptCacheDialect Instance { get; } = new();
    public string Name => "AnthropicNative";
    public bool GroupToolResults => true;

    public object CreateMarker(string? ttl) => string.IsNullOrWhiteSpace(ttl)
        ? new CacheControlEphemeral()
        : new CacheControlEphemeral { Ttl = ttl.Trim() };

    public TextContent MarkText(TextContent content, object marker)
    {
        var cacheControl = RequireMarker(marker);
        var rewritten = new TextContent(content.Text)
        {
            AdditionalProperties = content.AdditionalProperties == null
                ? null
                : new AdditionalPropertiesDictionary(content.AdditionalProperties),
            RawRepresentation = content.RawRepresentation is TextBlockParam block
                ? block with { CacheControl = cacheControl }
                : null
        };
        rewritten.WithCacheControl(cacheControl);
        return rewritten;
    }

    public FunctionResultContent MarkFunctionResult(
        FunctionResultContent content,
        string wireText,
        object marker)
    {
        var rewritten = new FunctionResultContent(content.CallId, content.Result)
        {
            AdditionalProperties = content.AdditionalProperties == null
                ? null
                : new AdditionalPropertiesDictionary(content.AdditionalProperties),
            Exception = content.Exception
        };
        rewritten.WithCacheControl(RequireMarker(marker));
        return rewritten;
    }

    public object? CreateMessageRawRepresentation(
        ChatMessage original,
        ChatMessage rewritten,
        IReadOnlySet<int> markedContentIndexes,
        object marker) => original.RawRepresentation;

    private static CacheControlEphemeral RequireMarker(object marker) =>
        marker as CacheControlEphemeral
        ?? throw new ArgumentException("Invalid Anthropic prompt-cache marker.", nameof(marker));
}
