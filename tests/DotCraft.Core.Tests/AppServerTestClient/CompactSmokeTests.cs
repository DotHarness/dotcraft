using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class CompactSmokeTests
{
    [Fact]
    public void MatrixLoad_ParsesProviderMappings()
    {
        var path = Path.Combine(Path.GetTempPath(), "compact-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "providers": [
                    {
                      "protocol": "openai-chat-completions",
                      "providerId": "openai-chat",
                      "model": "gpt-test"
                    }
                  ]
                }
                """);

            var matrix = CompactSmokeMatrix.Load(path);

            var provider = Assert.Single(matrix.Providers);
            Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, provider.Protocol);
            Assert.Equal("openai-chat", provider.ProviderId);
            Assert.Equal("gpt-test", provider.Model);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

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
    public void Scenarios_DoNotIncludePromptCacheBaseline()
    {
        Assert.DoesNotContain("prompt-cache-baseline", CompactSmokeScenarios.All);
    }

    [Fact]
    public void CliOptions_DefaultsToUserSmokeRunRootAndReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "compact-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"providers":[]}""");

            var ok = CompactSmokeCliOptions.TryParse(
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
    public void TraceValidator_AcceptsManualSnapshotForkRequestWithNullTurnId()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIChatCompletions,
            "openai-chat",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "openai-chat",
                modelId = "gpt-test",
                turnId = (string?)null,
                snapshotSource = "manual_valid",
                snapshotInvalidReason = (string?)null
            }),
            Event("MaintenanceForkResponse", new
            {
                fallbackReason = (string?)null,
                usage = new
                {
                    inputTokens = 1000,
                    cachedInputTokens = 250,
                    cacheWriteInputTokens = 0,
                    cacheHitRate = 0.25
                }
            }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            provider,
            events);

        Assert.True(result.Success);
        Assert.Equal("manual_valid", result.SnapshotSource);
        Assert.Null(result.SnapshotInvalidReason);
        Assert.True(result.CacheHitRequired);
        Assert.True(result.CacheHit);
        Assert.Equal(1000, result.InputTokens);
        Assert.Equal(250, result.CachedInputTokens);
        Assert.Equal(0.25, result.CacheHitRate);
    }

    [Fact]
    public void TraceValidator_AcceptsManualSnapshotWhenSnapshotWasRebasedAndCacheHit()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIResponses,
            "responses",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "responses",
                modelId = "gpt-test",
                turnId = (string?)null,
                snapshotSource = "manual_rebased",
                snapshotInvalidReason = "history_prefix_mismatch",
                cacheShapeApplied = true,
                cacheShapeKind = "openai-responses-prompt-cache-key",
                promptCacheKeyPresent = true,
                cacheMarkerSource = "thread"
            }),
            Event("MaintenanceForkResponse", new
            {
                fallbackReason = (string?)null,
                usage = new
                {
                    inputTokens = 1000,
                    cachedInputTokens = 250,
                    cacheWriteInputTokens = 0,
                    cacheHitRate = 0.25
                }
            }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            provider,
            events);

        Assert.True(result.Success);
        Assert.Equal("manual_rebased", result.SnapshotSource);
        Assert.Equal("history_prefix_mismatch", result.SnapshotInvalidReason);
        Assert.True(result.CacheHitRequired);
        Assert.True(result.CacheHit);
        Assert.True(result.CacheShapeApplied);
        Assert.Equal("openai-responses-prompt-cache-key", result.CacheShapeKind);
        Assert.True(result.PromptCacheKeyPresent);
    }

    [Fact]
    public void TraceValidator_AcceptsAutoSnapshotForkRequestWithTurnId()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIResponses,
            "responses",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "responses",
                modelId = "gpt-test",
                turnId = "turn_001",
                snapshotSource = "captured",
                cacheShapeApplied = true,
                cacheShapeKind = "openai-responses-prompt-cache-key",
                promptCacheKeyPresent = true,
                cacheMarkerSource = "thread"
            }),
            Event("MaintenanceForkResponse", new
            {
                fallbackReason = (string?)null,
                usage = new
                {
                    inputTokens = 2000,
                    cachedInputTokens = 512,
                    cacheWriteInputTokens = 0
                }
            }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.AutoSnapshotFork,
            provider,
            events);

        Assert.True(result.Success);
        Assert.True(result.CacheHit);
        Assert.True(result.CacheShapeApplied);
        Assert.Equal("openai-responses-prompt-cache-key", result.CacheShapeKind);
        Assert.True(result.PromptCacheKeyPresent);
        Assert.Equal("thread", result.CacheMarkerSource);
        Assert.Equal(512, result.CachedInputTokens);
    }

    [Fact]
    public void TraceValidator_RejectsManualSnapshotWhenOnlyTurnScopedForkExists()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIChatCompletions,
            "openai-chat",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "openai-chat",
                modelId = "gpt-test",
                turnId = "turn_001"
            }),
            Event("MaintenanceForkResponse", new { fallbackReason = (string?)null }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("trace_missing_manual_snapshot_compact_request", result.Message);
    }

    [Fact]
    public void TraceValidator_RejectsAutoSnapshotWhenOnlyManualScopedForkExists()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIResponses,
            "responses",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "responses",
                modelId = "gpt-test"
            }),
            Event("MaintenanceForkResponse", new { fallbackReason = (string?)null }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.AutoSnapshotFork,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("trace_missing_auto_snapshot_compact_request", result.Message);
    }

    [Fact]
    public void TraceValidator_RejectsSnapshotPreflightFallback()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIResponses,
            "responses",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "responses",
                modelId = "gpt-test",
                turnId = "turn_001",
                preflightRejected = true
            }),
            Event("MaintenanceForkResponse", new { fallbackReason = (string?)null }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.AutoSnapshotFork,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("maintenance_snapshot_preflight_rejected", result.Message);
    }

    [Fact]
    public void TraceValidator_RejectsSnapshotCacheMiss()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIChatCompletions,
            "openai-chat",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "openai-chat",
                modelId = "gpt-test",
                turnId = (string?)null,
                snapshotSource = "manual_valid",
                cacheShapeApplied = true,
                cacheShapeKind = "openai-compatible-cache-control",
                promptCacheKeyPresent = false,
                cacheMarkerSource = "snapshot_prefix"
            }),
            Event("MaintenanceForkResponse", new
            {
                fallbackReason = (string?)null,
                usage = new
                {
                    inputTokens = 1000,
                    cachedInputTokens = 0,
                    cacheWriteInputTokens = 0,
                    cacheHitRate = 0
                }
            }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("maintenance_snapshot_cache_miss", result.Message);
        Assert.True(result.CacheHitRequired);
        Assert.False(result.CacheHit);
        Assert.True(result.CacheShapeApplied);
        Assert.Equal("openai-compatible-cache-control", result.CacheShapeKind);
        Assert.False(result.PromptCacheKeyPresent);
        Assert.Equal("snapshot_prefix", result.CacheMarkerSource);
        Assert.Equal(1000, result.InputTokens);
        Assert.Equal(0, result.CachedInputTokens);
    }

    [Fact]
    public void TraceValidator_RejectsSnapshotCacheUsageMissing()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIResponses,
            "responses",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "responses",
                modelId = "gpt-test",
                turnId = "turn_001",
                snapshotSource = "captured"
            }),
            Event("MaintenanceForkResponse", new { fallbackReason = (string?)null }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.AutoSnapshotFork,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("maintenance_snapshot_cache_usage_missing", result.Message);
        Assert.True(result.CacheHitRequired);
        Assert.False(result.CacheHit);
    }

    [Fact]
    public void TraceValidator_AcceptsLegacyRequest()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.Anthropic,
            "anthropic",
            "claude-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new { mode = "legacy" }),
            Event("MaintenanceForkResponse", new { fallbackReason = (string?)null }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualLegacyPartial,
            provider,
            events);

        Assert.True(result.Success);
    }

    [Fact]
    public void TraceValidator_ReportsMaintenanceFallbackReason()
    {
        var provider = new CompactSmokeProviderSelection(
            ModelProviderProtocols.OpenAIChatCompletions,
            "openai-chat",
            "gpt-test");
        var events = new[]
        {
            Event("MaintenanceForkRequest", new
            {
                mode = "agent",
                providerId = "openai-chat",
                modelId = "gpt-test"
            }),
            Event("MaintenanceForkResponse", new { fallbackReason = "tool_call_without_text" }),
            Event("ContextCompaction")
        };

        var result = CompactSmokeTraceValidator.Validate(
            CompactSmokeScenarios.ManualSnapshotPartial,
            provider,
            events);

        Assert.False(result.Success);
        Assert.Equal("maintenance_fork_fallback", result.Message);
        Assert.Equal("tool_call_without_text", result.FallbackReason);
    }

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
