using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class CompactSmokeTests
{
    private static readonly CompactSmokeProviderSelection ChatProvider =
        new(ModelProviderProtocols.OpenAIChatCompletions, "openai-chat", "gpt-test");

    private static readonly CompactSmokeProviderSelection ResponsesProvider =
        new(ModelProviderProtocols.OpenAIResponses, "responses", "gpt-test");

    [Fact]
    public void BuildConfigJson_WritesSelectionAndCompactionWithoutProviders()
    {
        var json = CompactSmokeWorkspace.BuildConfigJson("openai-chat", "gpt-test");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("openai-chat", root.GetProperty("ProviderId").GetString());
        Assert.Equal("gpt-test", root.GetProperty("ProviderPreferences").GetProperty("openai-chat").GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.Empty(root.GetProperty("McpServers").EnumerateObject());
        Assert.Empty(root.GetProperty("LspServers").EnumerateObject());
        Assert.Equal(0, root.GetProperty("ExternalChannels").GetArrayLength());
        Assert.True(root.GetProperty("Tracing").GetProperty("Enabled").GetBoolean());

        var compaction = root.GetProperty("Compaction");
        Assert.Equal(40000, compaction.GetProperty("ContextWindow").GetInt32());
        Assert.Equal(4000, compaction.GetProperty("SummaryReserveTokens").GetInt32());
        Assert.Equal(14000, compaction.GetProperty("AutoCompactBufferTokens").GetInt32());
        Assert.Equal(3000, compaction.GetProperty("ManualCompactBufferTokens").GetInt32());
        Assert.False(compaction.GetProperty("MicrocompactEnabled").GetBoolean());
    }

    [Fact]
    public void Summary_ComputesExitCodeForSkippedPassedAndFailedCases()
    {
        var skipped = CompactSmokeSummary.FromCases([
            CompactSmokeCaseReport.Skipped("anthropic", "", "", CompactSmokeScenarios.AutoSnapshotFork, "missing_protocol_mapping")
        ]);
        Assert.Equal(2, skipped.ExitCode);

        var passed = CompactSmokeSummary.FromCases([
            new CompactSmokeCaseReport { Status = CompactSmokeStatuses.Passed },
            CompactSmokeCaseReport.Skipped("anthropic", "", "", CompactSmokeScenarios.AutoSnapshotFork, "missing_protocol_mapping")
        ]);
        Assert.Equal(0, passed.ExitCode);

        var failed = CompactSmokeSummary.FromCases([
            new CompactSmokeCaseReport { Status = CompactSmokeStatuses.Passed },
            new CompactSmokeCaseReport { Status = CompactSmokeStatuses.Failed }
        ]);
        Assert.Equal(1, failed.ExitCode);
    }

    [Fact]
    public void TraceValidator_AcceptsScopedSnapshotForksWithCacheHitsAndLegacyRequests()
    {
        var manual = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            ChatProvider,
            Trace(
                new { mode = "agent", providerId = "openai-chat", modelId = "gpt-test", turnId = (string?)null, snapshotSource = "manual_valid", snapshotInvalidReason = (string?)null },
                new { fallbackReason = (string?)null, usage = new { inputTokens = 1000, cachedInputTokens = 250, cacheWriteInputTokens = 0, cacheHitRate = 0.25 } }));
        Assert.True(manual.Success);
        Assert.Equal("manual_valid", manual.SnapshotSource);
        Assert.Null(manual.SnapshotInvalidReason);
        Assert.True(manual.CacheHitRequired);
        Assert.True(manual.CacheHit);
        Assert.Equal(1000, manual.InputTokens);
        Assert.Equal(250, manual.CachedInputTokens);
        Assert.Equal(0.25, manual.CacheHitRate);

        var rebased = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            ResponsesProvider,
            Trace(
                new
                {
                    mode = "agent", providerId = "responses", modelId = "gpt-test", turnId = (string?)null,
                    snapshotSource = "manual_rebased", snapshotInvalidReason = "history_prefix_mismatch",
                    cacheShapeApplied = true, cacheShapeKind = "openai-responses-prompt-cache-key",
                    promptCacheKeyPresent = true, cacheMarkerSource = "thread"
                },
                new { fallbackReason = (string?)null, usage = new { inputTokens = 1000, cachedInputTokens = 250, cacheWriteInputTokens = 0, cacheHitRate = 0.25 } }));
        Assert.True(rebased.Success);
        Assert.Equal("manual_rebased", rebased.SnapshotSource);
        Assert.Equal("history_prefix_mismatch", rebased.SnapshotInvalidReason);
        Assert.True(rebased.CacheShapeApplied);
        Assert.True(rebased.PromptCacheKeyPresent);

        var auto = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.AutoSnapshotFork,
            ResponsesProvider,
            Trace(
                new
                {
                    mode = "agent", providerId = "responses", modelId = "gpt-test", turnId = "turn_001", snapshotSource = "captured",
                    cacheShapeApplied = true, cacheShapeKind = "openai-responses-prompt-cache-key",
                    promptCacheKeyPresent = true, cacheMarkerSource = "thread"
                },
                new { fallbackReason = (string?)null, usage = new { inputTokens = 2000, cachedInputTokens = 512, cacheWriteInputTokens = 0 } }));
        Assert.True(auto.Success);
        Assert.True(auto.CacheHit);
        Assert.Equal("openai-responses-prompt-cache-key", auto.CacheShapeKind);
        Assert.Equal("thread", auto.CacheMarkerSource);
        Assert.Equal(512, auto.CachedInputTokens);

        var legacy = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualLegacyPartial,
            new CompactSmokeProviderSelection(ModelProviderProtocols.Anthropic, "anthropic", "claude-test"),
            Trace(new { mode = "legacy" }, new { fallbackReason = (string?)null }));
        Assert.True(legacy.Success);
    }

    [Fact]
    public void TraceValidator_RejectsMisScopedForksPreflightFallbacksAndCacheFailures()
    {
        AssertRejected(
            CompactSmokeScenarios.ManualSnapshotPartial, ChatProvider,
            new { mode = "agent", providerId = "openai-chat", modelId = "gpt-test", turnId = "turn_001" },
            new { fallbackReason = (string?)null },
            "trace_missing_manual_snapshot_compact_request");
        AssertRejected(
            CompactSmokeScenarios.AutoSnapshotFork, ResponsesProvider,
            new { mode = "agent", providerId = "responses", modelId = "gpt-test" },
            new { fallbackReason = (string?)null },
            "trace_missing_auto_snapshot_compact_request");
        AssertRejected(
            CompactSmokeScenarios.AutoSnapshotFork, ResponsesProvider,
            new { mode = "agent", providerId = "responses", modelId = "gpt-test", turnId = "turn_001", preflightRejected = true },
            new { fallbackReason = (string?)null },
            "maintenance_snapshot_preflight_rejected");

        var usageMissing = AssertRejected(
            CompactSmokeScenarios.AutoSnapshotFork, ResponsesProvider,
            new { mode = "agent", providerId = "responses", modelId = "gpt-test", turnId = "turn_001", snapshotSource = "captured" },
            new { fallbackReason = (string?)null },
            "maintenance_snapshot_cache_usage_missing");
        Assert.True(usageMissing.CacheHitRequired);
        Assert.False(usageMissing.CacheHit);

        var cacheMiss = AssertRejected(
            CompactSmokeScenarios.ManualSnapshotPartial, ChatProvider,
            new
            {
                mode = "agent", providerId = "openai-chat", modelId = "gpt-test", turnId = (string?)null, snapshotSource = "manual_valid",
                cacheShapeApplied = true, cacheShapeKind = "openai-compatible-cache-control",
                promptCacheKeyPresent = false, cacheMarkerSource = "snapshot_prefix"
            },
            new { fallbackReason = (string?)null, usage = new { inputTokens = 1000, cachedInputTokens = 0, cacheWriteInputTokens = 0, cacheHitRate = 0 } },
            "maintenance_snapshot_cache_miss");
        Assert.True(cacheMiss.CacheHitRequired);
        Assert.False(cacheMiss.CacheHit);
        Assert.Equal("openai-compatible-cache-control", cacheMiss.CacheShapeKind);
        Assert.False(cacheMiss.PromptCacheKeyPresent);
        Assert.Equal("snapshot_prefix", cacheMiss.CacheMarkerSource);
        Assert.Equal(0, cacheMiss.CachedInputTokens);

        var fallback = AssertRejected(
            CompactSmokeScenarios.ManualSnapshotPartial, ChatProvider,
            new { mode = "agent", providerId = "openai-chat", modelId = "gpt-test" },
            new { fallbackReason = "tool_call_without_text" },
            "maintenance_fork_fallback");
        Assert.Equal("tool_call_without_text", fallback.FallbackReason);
    }

    private static CompactSmokeValidationResult AssertRejected(
        string scenario,
        CompactSmokeProviderSelection provider,
        object request,
        object response,
        string expectedMessage)
    {
        var result = CompactSmokeTraceValidator.Validate(scenario, provider, Trace(request, response));
        Assert.False(result.Success);
        Assert.Equal(expectedMessage, result.Message);
        return result;
    }

    private static CompactSmokeTraceEvent[] Trace(object request, object response) =>
    [
        Event("MaintenanceForkRequest", request),
        Event("MaintenanceForkResponse", response),
        Event("ContextCompaction")
    ];

    private static CompactSmokeTraceEvent Event(string type, object? metadata = null)
    {
        var eventJson = JsonSerializer.Serialize(new
        {
            Type = type,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, CompactSmokeJson.Options)
        });
        return new CompactSmokeTraceEvent(1, "thread-1", type, eventJson);
    }
}
