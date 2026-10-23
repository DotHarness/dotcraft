using DotCraft.Context;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Action<ThreadTokenUsageUpdate>? ThreadTokenUsageUpdatedForBroadcast { get; set; }

    private static TokenUsageInfo CombineTurnUsage(
        long inputTokens,
        long outputTokens,
        long cachedInputTokens,
        long cacheWriteInputTokens,
        long reasoningOutputTokens,
        int llmCallCount,
        TokenTracker tokenTracker)
    {
        var input = inputTokens + tokenTracker.SubAgentInputTokens;
        var output = outputTokens + tokenTracker.SubAgentOutputTokens;
        return new TokenUsageInfo
        {
            InputTokens = input,
            OutputTokens = output,
            CachedInputTokens = Math.Clamp(cachedInputTokens + tokenTracker.SubAgentCachedInputTokens, 0, input),
            CacheWriteInputTokens = Math.Clamp(cacheWriteInputTokens + tokenTracker.SubAgentCacheWriteInputTokens, 0, input),
            ReasoningOutputTokens = reasoningOutputTokens + tokenTracker.SubAgentReasoningOutputTokens,
            LlmCallCount = llmCallCount + tokenTracker.SubAgentLlmCallCount,
            TotalTokens = input + output
        };
    }

    private void PublishThreadTokenUsage(
        SessionThread thread,
        SessionTurn turn,
        TokenUsageInfo turnUsage,
        TokenUsageInfo last,
        int? modelContextWindow)
    {
        var handler = ThreadTokenUsageUpdatedForBroadcast;
        if (handler is null)
            return;

        var total = turnUsage + thread.OmittedTokenUsage;
        foreach (var other in thread.Turns)
        {
            if (!string.Equals(other.Id, turn.Id, StringComparison.Ordinal) && other.TokenUsage is { } usage)
                total += usage;
        }

        var parentThreadId = thread.Source.SubAgent?.ParentThreadId;
        handler(new ThreadTokenUsageUpdate(
            thread.Id,
            string.IsNullOrEmpty(parentThreadId) ? null : parentThreadId,
            turn.Id,
            total,
            last,
            modelContextWindow));
    }
}
