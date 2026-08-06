using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Applies provider-owned cache markers to otherwise neutral MEAI content.</summary>
public interface IPromptCacheDialect
{
    string Name { get; }

    bool GroupToolResults { get; }

    object CreateMarker(string? ttl);

    TextContent MarkText(TextContent content, object marker);

    FunctionResultContent MarkFunctionResult(
        FunctionResultContent content,
        string wireText,
        object marker);

    object? CreateMessageRawRepresentation(
        ChatMessage original,
        ChatMessage rewritten,
        IReadOnlySet<int> markedContentIndexes,
        object marker);
}
