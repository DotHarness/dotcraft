using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AdditionalPropertiesPromptCacheDialect : IPromptCacheDialect
{
    public static AdditionalPropertiesPromptCacheDialect Instance { get; } = new();
    public static AdditionalPropertiesPromptCacheDialect Anthropic { get; } = new(groupToolResults: true);
    private readonly bool _groupToolResults;

    private AdditionalPropertiesPromptCacheDialect(bool groupToolResults = false) =>
        _groupToolResults = groupToolResults;

    public string Name => _groupToolResults ? "AnthropicNative" : "OpenAICompatible";
    public bool GroupToolResults => _groupToolResults;

    public object CreateMarker(string? ttl)
    {
        var marker = new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "ephemeral" };
        if (!string.IsNullOrWhiteSpace(ttl))
            marker["ttl"] = ttl.Trim();
        return marker;
    }

    public TextContent MarkText(TextContent content, object marker) =>
        new(content.Text)
        {
            AdditionalProperties = WithMarker(content.AdditionalProperties, marker),
            RawRepresentation = content.RawRepresentation
        };

    public FunctionResultContent MarkFunctionResult(FunctionResultContent content, string wireText, object marker) =>
        new(content.CallId, content.Result)
        {
            AdditionalProperties = WithMarker(content.AdditionalProperties, marker),
            Exception = content.Exception,
            RawRepresentation = content.RawRepresentation
        };

    public object? CreateMessageRawRepresentation(
        ChatMessage original,
        ChatMessage rewritten,
        IReadOnlySet<int> markedContentIndexes,
        object marker) => original.RawRepresentation;

    private static AdditionalPropertiesDictionary WithMarker(
        AdditionalPropertiesDictionary? source,
        object marker)
    {
        var properties = source is null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(source);
        properties[PromptCachingChatClient.CacheControlKey] = marker;
        return properties;
    }
}

internal sealed record PromptCacheMaintenanceScope(
    int SnapshotMessageCount,
    PromptCacheMaintenanceWriteMode CacheWriteMode = PromptCacheMaintenanceWriteMode.WriteThrough);

internal enum PromptCacheMaintenanceWriteMode
{
    WriteThrough,
    ReadOnlyPrefix
}

internal enum PromptCacheMarkerStrategy
{
    OpenAICompatible,
    AnthropicNative
}
