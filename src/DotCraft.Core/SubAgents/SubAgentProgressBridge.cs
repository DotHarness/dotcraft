using System.Collections.Concurrent;

namespace DotCraft.Agents;

/// <summary>
/// Thread-safe static registry that relays real-time SubAgent progress
/// (current tool, accumulated tokens) from SubAgent execution threads
/// to terminal renderers which poll periodically.
/// </summary>
public static class SubAgentProgressBridge
{
    private static readonly ConcurrentDictionary<string, ProgressEntry> Entries = new();

    public sealed class ProgressEntry
    {
        public volatile string? CurrentTool;
        /// <summary>
        /// Retains the name of the most recently invoked tool, even after
        /// <see cref="CurrentTool"/> is cleared on completion. This allows
        /// periodic snapshots (200ms interval) to display meaningful activity
        /// instead of "Thinking..." during the gap between tool calls.
        /// </summary>
        public volatile string? LastTool;

        /// <summary>
        /// Human-readable formatted display text for the currently executing tool
        /// (e.g. "Read src/foo.cs lines 10-20"). Null when no display formatter
        /// is registered or no tool is running. Cleared in finally like <see cref="CurrentTool"/>.
        /// </summary>
        public volatile string? CurrentToolDisplay;
        /// <summary>
        /// Retains the formatted display text of the most recently invoked tool,
        /// mirroring <see cref="LastTool"/> semantics — never cleared, survives between tool calls.
        /// </summary>
        public volatile string? LastToolDisplay;

        public volatile bool IsCompleted;
        private long _inputTokens;
        private long _outputTokens;
        private long _cachedInputTokens;
        private long _cacheWriteInputTokens;
        private long _reasoningOutputTokens;
        private long _llmCallCount;

        public long InputTokens => Interlocked.Read(ref _inputTokens);
        public long OutputTokens => Interlocked.Read(ref _outputTokens);
        public long CachedInputTokens => Interlocked.Read(ref _cachedInputTokens);
        public long CacheWriteInputTokens => Interlocked.Read(ref _cacheWriteInputTokens);
        public long ReasoningOutputTokens => Interlocked.Read(ref _reasoningOutputTokens);
        public int LlmCallCount => (int)Interlocked.Read(ref _llmCallCount);

        public void AddTokens(long input, long output)
            => AddTokens(input, output, 0, 0, 0);

        public void AddTokens(long input, long output, long cachedInput, long cacheWriteInput, long reasoningOutput)
            => AddTokens(input, output, cachedInput, cacheWriteInput, reasoningOutput, llmCallCount: 0);

        public void AddTokens(long input, long output, long cachedInput, long cacheWriteInput, long reasoningOutput, int llmCallCount)
        {
            Interlocked.Add(ref _inputTokens, input);
            Interlocked.Add(ref _outputTokens, output);
            Interlocked.Add(ref _cachedInputTokens, cachedInput);
            Interlocked.Add(ref _cacheWriteInputTokens, cacheWriteInput);
            Interlocked.Add(ref _reasoningOutputTokens, reasoningOutput);
            if (llmCallCount > 0)
                Interlocked.Add(ref _llmCallCount, llmCallCount);
        }
    }

    public static ProgressEntry GetOrCreate(string key)
        => Entries.GetOrAdd(key, _ => new ProgressEntry());

    public static ProgressEntry? TryGet(string key)
        => Entries.GetValueOrDefault(key);

    public static void Remove(string key)
        => Entries.TryRemove(key, out _);
}
