using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class PromptCacheSmokeTests
{
    [Fact]
    public void MatrixLoad_ParsesProviderMappings()
    {
        var path = Path.Combine(Path.GetTempPath(), "prompt-cache-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "providers": [
                    {
                      "protocol": "openai-responses",
                      "providerId": "openai",
                      "model": "gpt-test",
                      "minimumCacheHitRate": 0.35
                    }
                  ]
                }
                """);

            var matrix = PromptCacheSmokeMatrix.Load(path);

            var provider = Assert.Single(matrix.Providers);
            Assert.Equal(ModelProviderProtocols.OpenAIResponses, provider.Protocol);
            Assert.Equal("openai", provider.ProviderId);
            Assert.Equal("gpt-test", provider.Model);
            Assert.Equal(0.35, provider.MinimumCacheHitRate);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void BuildConfigJson_DisablesCompactionAndWritesSelectionWithoutProviders()
    {
        var json = PromptCacheSmokeWorkspace.BuildConfigJson("openai", "gpt-test");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("openai", root.GetProperty("ProviderId").GetString());
        Assert.Equal("gpt-test", root.GetProperty("Model").GetString());
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.Equal(0, root.GetProperty("McpServers").GetArrayLength());
        Assert.Equal(0, root.GetProperty("LspServers").GetArrayLength());
        Assert.Equal(0, root.GetProperty("ExternalChannels").GetArrayLength());
        Assert.True(root.GetProperty("Tracing").GetProperty("Enabled").GetBoolean());

        var compaction = root.GetProperty("Compaction");
        Assert.False(compaction.GetProperty("AutoCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("ReactiveCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("MicrocompactEnabled").GetBoolean());
        Assert.Equal(256000, compaction.GetProperty("ContextWindow").GetInt32());
        Assert.Equal(256000, compaction.GetProperty("MaxContextWindow").GetInt32());
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
    public void TraceValidator_RejectsMissingTokenUsage()
    {
        var result = PromptCacheSmokeTraceValidator.Validate([], minimumCacheHitRate: 0.2);

        Assert.False(result.Success);
        Assert.Equal("trace_missing_token_usage", result.Message);
        Assert.Equal(0.2, result.MinimumCacheHitRate);
        Assert.Equal(0, result.ContextCompactionCount);
    }

    [Fact]
    public void TraceValidator_RejectsContextCompaction()
    {
        var events = new[]
        {
            Event("TokenUsage", inputTokens: 1000, cachedInputTokens: 250, cacheWriteInputTokens: 0),
            Event("ContextCompaction"),
            Event("ContextCompaction")
        };

        var result = PromptCacheSmokeTraceValidator.Validate(events, minimumCacheHitRate: 0.2);

        Assert.False(result.Success);
        Assert.Equal("prompt_cache_baseline_compaction_detected", result.Message);
        Assert.Equal(2, result.ContextCompactionCount);
        Assert.Equal(0.2, result.MinimumCacheHitRate);
        Assert.Equal(1000, result.InputTokens);
        Assert.Equal(250, result.CachedInputTokens);
        Assert.Equal(0.25, result.CacheHitRate);
    }

    [Fact]
    public void TraceValidator_RejectsCacheHitRateBelowFloor()
    {
        var events = new[]
        {
            Event("TokenUsage", inputTokens: 1000, cachedInputTokens: 0, cacheWriteInputTokens: 0),
            Event("TokenUsage", inputTokens: 3000, cachedInputTokens: 300, cacheWriteInputTokens: 0)
        };

        var result = PromptCacheSmokeTraceValidator.Validate(events, minimumCacheHitRate: 0.2);

        Assert.False(result.Success);
        Assert.Equal("prompt_cache_baseline_cache_hit_rate_below_floor", result.Message);
        Assert.True(result.CacheHitRequired);
        Assert.True(result.CacheHit);
        Assert.Equal(0.2, result.MinimumCacheHitRate);
        Assert.Equal(4000, result.InputTokens);
        Assert.Equal(300, result.CachedInputTokens);
        Assert.Equal(0.075, result.CacheHitRate);
        Assert.Equal(0, result.ContextCompactionCount);
    }

    [Fact]
    public void CliOptions_DefaultsToUserSmokeRunRootAndReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "prompt-cache-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"providers":[]}""");

            var ok = PromptCacheSmokeCliOptions.TryParse(
                ["--matrix", path],
                out var options,
                out var error);

            Assert.True(ok, error);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
                              ?? Environment.GetEnvironmentVariable("HOME")
                              ?? Directory.GetCurrentDirectory();

            var expectedPrefix = Path.Combine(
                userProfile,
                ".craft",
                "smoke-tests",
                "runs") + Path.DirectorySeparatorChar;
            Assert.StartsWith(
                expectedPrefix,
                options.WorkRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.Matches(@"^\d{8}-\d{6}-[0-9a-f]{8}$", Path.GetFileName(options.WorkRoot));
            Assert.Equal(Path.Combine(options.WorkRoot, "report.json"), options.ReportPath);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
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
