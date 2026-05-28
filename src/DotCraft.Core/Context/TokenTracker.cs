namespace DotCraft.Context;

public sealed class TokenTracker
{
    private static readonly AsyncLocal<TokenTracker?> _current = new();

    /// <summary>
    /// Ambient token tracker for the current async flow.
    /// Set by terminal runners / AgentRunner before the agent runs;
    /// read in SubAgentManager to aggregate SubAgent token costs.
    /// </summary>
    public static TokenTracker? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    private long _lastInputTokens;
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _totalCachedInputTokens;
    private long _totalCacheWriteInputTokens;
    private long _totalReasoningOutputTokens;
    private long _subAgentInputTokens;
    private long _subAgentOutputTokens;
    private long _subAgentCachedInputTokens;
    private long _subAgentCacheWriteInputTokens;
    private long _subAgentReasoningOutputTokens;
    private long _subAgentLlmCallCount;

    /// <summary>
    /// Input tokens from the most recent LLM call (for context compaction threshold).
    /// </summary>
    public long LastInputTokens => Interlocked.Read(ref _lastInputTokens);

    /// <summary>
    /// Cumulative input tokens across all LLM calls in this session turn.
    /// </summary>
    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);
    public long TotalCachedInputTokens => Interlocked.Read(ref _totalCachedInputTokens);
    public long TotalCacheWriteInputTokens => Interlocked.Read(ref _totalCacheWriteInputTokens);
    public long TotalReasoningOutputTokens => Interlocked.Read(ref _totalReasoningOutputTokens);
    public long SubAgentInputTokens => Interlocked.Read(ref _subAgentInputTokens);
    public long SubAgentOutputTokens => Interlocked.Read(ref _subAgentOutputTokens);
    public long SubAgentCachedInputTokens => Interlocked.Read(ref _subAgentCachedInputTokens);
    public long SubAgentCacheWriteInputTokens => Interlocked.Read(ref _subAgentCacheWriteInputTokens);
    public long SubAgentReasoningOutputTokens => Interlocked.Read(ref _subAgentReasoningOutputTokens);
    public int SubAgentLlmCallCount => (int)Interlocked.Read(ref _subAgentLlmCallCount);

    public void Update(long inputTokens, long outputTokens)
        => Update(inputTokens, outputTokens, 0, 0);

    public void Update(long inputTokens, long outputTokens, long cachedInputTokens, long reasoningOutputTokens)
    {
        Interlocked.Exchange(ref _lastInputTokens, inputTokens);
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Add(ref _totalCachedInputTokens, cachedInputTokens);
        Interlocked.Add(ref _totalReasoningOutputTokens, reasoningOutputTokens);
    }

    public void Update(long inputTokens, long outputTokens, long cachedInputTokens, long cacheWriteInputTokens, long reasoningOutputTokens)
    {
        Interlocked.Exchange(ref _lastInputTokens, inputTokens);
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Add(ref _totalCachedInputTokens, cachedInputTokens);
        Interlocked.Add(ref _totalCacheWriteInputTokens, cacheWriteInputTokens);
        Interlocked.Add(ref _totalReasoningOutputTokens, reasoningOutputTokens);
    }

    /// <summary>
    /// Accumulate per-notification usage deltas from streaming (where snapshots are cumulative)
    /// and record the latest cumulative input token count for <see cref="LastInputTokens"/> compaction checks.
    /// </summary>
    public void UpdateWithStreamingDeltas(long deltaInput, long deltaOutput, long cumulativeInputSnapshot)
        => UpdateWithStreamingDeltas(deltaInput, deltaOutput, 0, 0, cumulativeInputSnapshot);

    public void UpdateWithStreamingDeltas(
        long deltaInput,
        long deltaOutput,
        long deltaCachedInput,
        long deltaReasoningOutput,
        long cumulativeInputSnapshot)
    {
        Interlocked.Exchange(ref _lastInputTokens, cumulativeInputSnapshot);
        Interlocked.Add(ref _totalInputTokens, deltaInput);
        Interlocked.Add(ref _totalOutputTokens, deltaOutput);
        Interlocked.Add(ref _totalCachedInputTokens, deltaCachedInput);
        Interlocked.Add(ref _totalReasoningOutputTokens, deltaReasoningOutput);
    }

    public void UpdateWithStreamingDeltas(
        long deltaInput,
        long deltaOutput,
        long deltaCachedInput,
        long deltaCacheWriteInput,
        long deltaReasoningOutput,
        long cumulativeInputSnapshot)
    {
        Interlocked.Exchange(ref _lastInputTokens, cumulativeInputSnapshot);
        Interlocked.Add(ref _totalInputTokens, deltaInput);
        Interlocked.Add(ref _totalOutputTokens, deltaOutput);
        Interlocked.Add(ref _totalCachedInputTokens, deltaCachedInput);
        Interlocked.Add(ref _totalCacheWriteInputTokens, deltaCacheWriteInput);
        Interlocked.Add(ref _totalReasoningOutputTokens, deltaReasoningOutput);
    }

    public void AddSubAgentTokens(long input, long output)
        => AddSubAgentTokens(input, output, 0, 0);

    public void AddSubAgentTokens(long input, long output, long cachedInput, long reasoningOutput)
    {
        Interlocked.Add(ref _subAgentInputTokens, input);
        Interlocked.Add(ref _subAgentOutputTokens, output);
        Interlocked.Add(ref _subAgentCachedInputTokens, cachedInput);
        Interlocked.Add(ref _subAgentReasoningOutputTokens, reasoningOutput);
    }

    public void AddSubAgentTokens(long input, long output, long cachedInput, long cacheWriteInput, long reasoningOutput)
        => AddSubAgentTokens(input, output, cachedInput, cacheWriteInput, reasoningOutput, llmCallCount: 0);

    public void AddSubAgentTokens(
        long input,
        long output,
        long cachedInput,
        long cacheWriteInput,
        long reasoningOutput,
        int llmCallCount)
    {
        Interlocked.Add(ref _subAgentInputTokens, input);
        Interlocked.Add(ref _subAgentOutputTokens, output);
        Interlocked.Add(ref _subAgentCachedInputTokens, cachedInput);
        Interlocked.Add(ref _subAgentCacheWriteInputTokens, cacheWriteInput);
        Interlocked.Add(ref _subAgentReasoningOutputTokens, reasoningOutput);
        if (llmCallCount > 0)
            Interlocked.Add(ref _subAgentLlmCallCount, llmCallCount);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _lastInputTokens, 0);
        Interlocked.Exchange(ref _totalInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
        Interlocked.Exchange(ref _totalCachedInputTokens, 0);
        Interlocked.Exchange(ref _totalCacheWriteInputTokens, 0);
        Interlocked.Exchange(ref _totalReasoningOutputTokens, 0);
        Interlocked.Exchange(ref _subAgentInputTokens, 0);
        Interlocked.Exchange(ref _subAgentOutputTokens, 0);
        Interlocked.Exchange(ref _subAgentCachedInputTokens, 0);
        Interlocked.Exchange(ref _subAgentCacheWriteInputTokens, 0);
        Interlocked.Exchange(ref _subAgentReasoningOutputTokens, 0);
        Interlocked.Exchange(ref _subAgentLlmCallCount, 0);
    }

    /// <summary>
    /// Format token count in compact human-readable form for spinner display.
    /// </summary>
    public static string FormatCompact(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        >= 1_000 => $"{tokens / 1_000.0:0.#}k",
        _ => tokens.ToString()
    };
}
