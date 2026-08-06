using Anthropic.Models.Beta.Messages;

namespace DotCraft.Agents;

internal sealed class AnthropicToolCallArgumentsDeltaExtractor : IToolCallArgumentsDeltaExtractor
{
    public static AnthropicToolCallArgumentsDeltaExtractor Instance { get; } = new();

    public IEnumerable<ProviderToolCallArgumentsDelta> Extract(object? rawRepresentation)
    {
        if (rawRepresentation is BetaRawMessageStreamEvent streamEvent)
            rawRepresentation = streamEvent.Value;
        if (rawRepresentation is BetaRawContentBlockStartEvent start
            && start.ContentBlock.TryPickBetaToolUse(out var tool)
            && TryIndex(start.Index, out var startIndex))
        {
            yield return new ProviderToolCallArgumentsDelta(
                startIndex,
                tool.Name,
                tool.ID,
                string.Empty);
            yield break;
        }
        if (rawRepresentation is BetaRawContentBlockDeltaEvent delta
            && delta.Delta.TryPickInputJson(out var input)
            && TryIndex(delta.Index, out var deltaIndex))
        {
            yield return new ProviderToolCallArgumentsDelta(
                deltaIndex,
                null,
                null,
                input.PartialJson);
        }
    }

    private static bool TryIndex(long value, out int index)
    {
        if (value is < 0 or > int.MaxValue)
        {
            index = 0;
            return false;
        }
        index = (int)value;
        return true;
    }
}
