namespace DotCraft.Tracing;

internal sealed class TokenUsageRequestAccumulator
{
    private bool _hasCurrentRequest;
    private int? _currentRequestIndex;
    private long _lastInputTokens;
    private long _lastOutputTokens;
    private long _lastCachedInputTokens;
    private long _lastCacheWriteInputTokens;
    private long _lastReasoningOutputTokens;

    public TokenUsageRequestDelta ApplySnapshot(TokenUsageSnapshot snapshot, int? requestIndex = null)
    {
        var hasUsage = snapshot.InputTokens > 0
            || snapshot.OutputTokens > 0
            || snapshot.CachedInputTokens > 0
            || snapshot.CacheWriteInputTokens > 0
            || snapshot.ReasoningOutputTokens > 0;
        if (!hasUsage)
            return new TokenUsageRequestDelta(new TokenUsageSnapshot(), false, requestIndex);

        var isNewRequest = ShouldStartNewRequest(snapshot, requestIndex);
        if (isNewRequest)
            ResetCurrentRequest(requestIndex);

        var delta = new TokenUsageSnapshot(
            InputTokens: PositiveDelta(snapshot.InputTokens, _lastInputTokens),
            OutputTokens: PositiveDelta(snapshot.OutputTokens, _lastOutputTokens),
            CachedInputTokens: PositiveDelta(snapshot.CachedInputTokens, _lastCachedInputTokens),
            ReasoningOutputTokens: PositiveDelta(snapshot.ReasoningOutputTokens, _lastReasoningOutputTokens),
            CacheWriteInputTokens: PositiveDelta(snapshot.CacheWriteInputTokens, _lastCacheWriteInputTokens));

        _lastInputTokens = snapshot.InputTokens;
        _lastOutputTokens = snapshot.OutputTokens;
        _lastCachedInputTokens = snapshot.CachedInputTokens;
        _lastCacheWriteInputTokens = snapshot.CacheWriteInputTokens;
        _lastReasoningOutputTokens = snapshot.ReasoningOutputTokens;

        return new TokenUsageRequestDelta(delta, isNewRequest, _currentRequestIndex);
    }

    private bool ShouldStartNewRequest(TokenUsageSnapshot snapshot, int? requestIndex)
    {
        if (!_hasCurrentRequest)
            return true;

        if (requestIndex.HasValue)
            return _currentRequestIndex != requestIndex;

        if (_currentRequestIndex.HasValue)
            return false;

        return snapshot.InputTokens < _lastInputTokens
            || snapshot.OutputTokens < _lastOutputTokens
            || snapshot.CachedInputTokens < _lastCachedInputTokens
            || snapshot.CacheWriteInputTokens < _lastCacheWriteInputTokens
            || snapshot.ReasoningOutputTokens < _lastReasoningOutputTokens;
    }

    private void ResetCurrentRequest(int? requestIndex)
    {
        _hasCurrentRequest = true;
        _currentRequestIndex = requestIndex;
        _lastInputTokens = 0;
        _lastOutputTokens = 0;
        _lastCachedInputTokens = 0;
        _lastCacheWriteInputTokens = 0;
        _lastReasoningOutputTokens = 0;
    }

    private static long PositiveDelta(long current, long previous)
        => current > previous ? current - previous : 0;
}

internal readonly record struct TokenUsageRequestDelta(
    TokenUsageSnapshot Usage,
    bool IsNewRequest,
    int? RequestIndex)
{
    public int LlmCallDelta => IsNewRequest ? 1 : 0;
}
