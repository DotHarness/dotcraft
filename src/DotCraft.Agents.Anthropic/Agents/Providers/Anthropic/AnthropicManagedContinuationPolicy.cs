using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicManagedContinuationPolicy : IProviderManagedContinuationPolicy
{
    private const int MaximumPauseTurnContinuations = 5;
    private static readonly ChatFinishReason PauseTurn = new("pause_turn");

    public static AnthropicManagedContinuationPolicy Instance { get; } = new();

    public int MaximumContinuations => MaximumPauseTurnContinuations;

    public bool ShouldContinue(ChatResponse response) => response.FinishReason == PauseTurn;
}
