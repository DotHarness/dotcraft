using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class PromptCacheSmokeTests
{
    [Fact]
    public void BuildConfigJson_DisablesCompactionAndWritesSelectionWithoutProviders()
    {
        var json = PromptCacheSmokeWorkspace.BuildConfigJson("openai", "gpt-test");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("openai", root.GetProperty("ProviderId").GetString());
        Assert.Equal("gpt-test", root.GetProperty("ProviderPreferences").GetProperty("openai").GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.Empty(root.GetProperty("McpServers").EnumerateObject());
        Assert.Empty(root.GetProperty("LspServers").EnumerateObject());
        Assert.Equal(0, root.GetProperty("ExternalChannels").GetArrayLength());
        Assert.True(root.GetProperty("Tracing").GetProperty("Enabled").GetBoolean());

        var compaction = root.GetProperty("Compaction");
        Assert.False(compaction.GetProperty("AutoCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("ReactiveCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("MicrocompactEnabled").GetBoolean());
        Assert.Equal(256000, compaction.GetProperty("ContextWindow").GetInt32());
    }

    [Fact]
    public void TraceValidator_AggregatesTokenUsage()
    {
        var events = new[]
        {
            Event("TokenUsage", inputTokens: 1000, cachedInputTokens: 250, cacheWriteInputTokens: 10),
            Event("TokenUsage", inputTokens: 3000, cachedInputTokens: 750, cacheWriteInputTokens: 20)
        };

        var result = PromptCacheSmokeTraceValidator.Validate(events, minimumCacheHitRate: 0.2);

        Assert.True(result.Success);
        Assert.True(result.CacheHitRequired);
        Assert.True(result.CacheHit);
        Assert.Equal(0.2, result.MinimumCacheHitRate);
        Assert.Equal(4000, result.InputTokens);
        Assert.Equal(1000, result.CachedInputTokens);
        Assert.Equal(30, result.CacheWriteInputTokens);
        Assert.Equal(0.25, result.CacheHitRate);
        Assert.Equal(0, result.ContextCompactionCount);
    }

    [Fact]
    public void TraceValidator_RejectsMissingUsageCompactionAndLowCacheHitRate()
    {
        var missing = PromptCacheSmokeTraceValidator.Validate([], minimumCacheHitRate: 0.2);
        Assert.False(missing.Success);
        Assert.Equal("trace_missing_token_usage", missing.Message);
        Assert.Equal(0, missing.ContextCompactionCount);

        var compacted = PromptCacheSmokeTraceValidator.Validate(
            [
                Event("TokenUsage", inputTokens: 1000, cachedInputTokens: 250, cacheWriteInputTokens: 0),
                Event("ContextCompaction"),
                Event("ContextCompaction")
            ],
            minimumCacheHitRate: 0.2);
        Assert.False(compacted.Success);
        Assert.Equal("prompt_cache_baseline_compaction_detected", compacted.Message);
        Assert.Equal(2, compacted.ContextCompactionCount);
        Assert.Equal(0.25, compacted.CacheHitRate);

        var lowHitRate = PromptCacheSmokeTraceValidator.Validate(
            [
                Event("TokenUsage", inputTokens: 1000, cachedInputTokens: 0, cacheWriteInputTokens: 0),
                Event("TokenUsage", inputTokens: 3000, cachedInputTokens: 300, cacheWriteInputTokens: 0)
            ],
            minimumCacheHitRate: 0.2);
        Assert.False(lowHitRate.Success);
        Assert.Equal("prompt_cache_baseline_cache_hit_rate_below_floor", lowHitRate.Message);
        Assert.True(lowHitRate.CacheHit);
        Assert.Equal(4000, lowHitRate.InputTokens);
        Assert.Equal(300, lowHitRate.CachedInputTokens);
        Assert.Equal(0.075, lowHitRate.CacheHitRate);
    }

    private static PromptCacheSmokeTraceEvent Event(
        string type,
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteInputTokens = null)
    {
        var eventJson = JsonSerializer.Serialize(new
        {
            Type = type,
            InputTokens = inputTokens,
            CachedInputTokens = cachedInputTokens,
            CacheWriteInputTokens = cacheWriteInputTokens
        });
        return new PromptCacheSmokeTraceEvent(1, "thread-1", type, eventJson);
    }
}
