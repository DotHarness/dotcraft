using DotCraft.Context.Compaction;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionPipelineTests
{
    private static CompactionConfig DefaultConfig() => new()
    {
        ContextWindow = 200_000,
        SummaryReserveTokens = 20_000,
        AutoCompactBufferTokens = 13_000,
        WarningBufferTokens = 20_000,
        ErrorBufferTokens = 10_000,
        ManualCompactBufferTokens = 3_000,
        MaxConsecutiveFailures = 3,
    };

    [Fact]
    public void EvaluateThreshold_BelowWarning()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(10_000);
        Assert.False(threshold.AboveWarning);
        Assert.False(threshold.AboveError);
        Assert.False(threshold.AboveAuto);
    }

    [Fact]
    public void EvaluateThreshold_AboveAuto()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        // AutoCompact threshold = (200k - 20k) - 13k = 167k.
        var threshold = pipeline.EvaluateThreshold(180_000);
        Assert.True(threshold.AboveWarning);
        Assert.True(threshold.AboveError);
        Assert.True(threshold.AboveAuto);
    }

    [Fact]
    public void EvaluateThreshold_AboveWarningBelowAuto()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        // Auto = 167k; warning = 147k; error = 157k.
        var threshold = pipeline.EvaluateThreshold(150_000);
        Assert.True(threshold.AboveWarning);
        Assert.False(threshold.AboveError);
        Assert.False(threshold.AboveAuto);
    }

    [Fact]
    public void FailureTracker_TripsAfterConfiguredFailures()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var tracker = pipeline.Failures;
        Assert.False(tracker.IsTripped("t"));
        tracker.RecordFailure("t");
        tracker.RecordFailure("t");
        Assert.False(tracker.IsTripped("t"));
        tracker.RecordFailure("t");
        Assert.True(tracker.IsTripped("t"));

        tracker.RecordSuccess("t");
        Assert.False(tracker.IsTripped("t"));
    }

    [Fact]
    public void PercentLeft_IsZeroWhenAtWindow()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(long.MaxValue);
        Assert.Equal(0.0, threshold.PercentLeft);
    }

    [Fact]
    public void PercentLeft_IsOneWhenEmpty()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(0);
        Assert.Equal(1.0, threshold.PercentLeft, 3);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_BelowThresholdSkips()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var result = await pipeline.TryAutoCompactHistoryAsync(
            [new ChatMessage(ChatRole.User, "short")],
            "thread-1",
            10_000,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Skipped, result.Status.Outcome);
        Assert.Equal(10_000, result.Status.EstimatedTokensBefore);
        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_SingleApiRoundFallsBackToFullCompaction()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>single round summary</summary>"));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "user " + new string('u', 1200)),
            new(ChatRole.Assistant, "assistant " + new string('a', 1200)),
        };

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Null(result.Status.FailureReason);
        Assert.Single(result.Messages);
        Assert.Contains("single round summary", result.Messages[0].Text);
        Assert.False(pipeline.Failures.IsTripped("thread-1"));
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_AboveThresholdReturnsReplacementHistory()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 2_000,
            SummaryReserveTokens = 200,
            AutoCompactBufferTokens = 100,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
            MicrocompactEnabled = false
        };
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>important context</summary>"));

        var messages = new List<ChatMessage>();
        for (var i = 0; i < 6; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user {i} " + new string('u', 600)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant {i} " + new string('a', 600)));
        }

        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            10_000,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.True(result.Messages.Count < messages.Count);
        Assert.Contains("important context", result.Messages[0].Text);
        Assert.True(result.Status.EstimatedTokensAfter < result.Status.EstimatedTokensBefore);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_HotPathUsesSnapshotForkWithoutMicro()
    {
        var cfg = DefaultConfig();
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        cfg.MicrocompactEnabled = true;
        cfg.MicrocompactKeepRecent = 1;
        cfg.MicrocompactGapMinutes = 20;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SummaryChatClient("<summary>snapshot fork summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            messages.Add(new ChatMessage(ChatRole.User, $"user {i}"));
            messages.Add(AssistantCall(callId));
            messages.Add(ToolResult(callId, new string('r', 4_000)));
        }

        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1");

        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            180_000,
            lastAssistantTimestampUtc: DateTimeOffset.UtcNow,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(0, result.Status.ClearedToolResults);
        Assert.Contains("snapshot fork summary", result.Messages[0].Text);
        var request = Assert.Single(
            store.GetEvents("thread-1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.DoesNotContain("\"mode\":\"legacy\"", request.MetadataJson);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_ColdCacheMicroCanResolveWithoutSummary()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 40_000,
            SummaryReserveTokens = 4_000,
            AutoCompactBufferTokens = 2_000,
            MicrocompactEnabled = true,
            MicrocompactKeepRecent = 1,
            MicrocompactGapMinutes = 5
        };
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            messages.Add(AssistantCall(callId));
            messages.Add(ToolResult(callId, new string('r', 30_000)));
        }

        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            MessageTokenEstimator.Estimate(messages),
            lastAssistantTimestampUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Micro, result.Status.Outcome);
        Assert.True(result.Status.ClearedToolResults > 0);
        Assert.True(result.Status.EstimatedTokensAfter < result.Status.ThresholdAfter.AutoThreshold);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_RequestOverheadPreventsFalseMicroSuccess()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 40_000,
            SummaryReserveTokens = 4_000,
            AutoCompactBufferTokens = 2_000,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
            MicrocompactEnabled = true,
            MicrocompactKeepRecent = 1,
            MicrocompactGapMinutes = 5
        };
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>summary after micro was not enough</summary>"));

        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            messages.Add(AssistantCall(callId));
            messages.Add(ToolResult(callId, new string('r', 8_000)));
        }

        var localEstimate = MessageTokenEstimator.Estimate(messages);
        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            localEstimate + 32_000,
            lastAssistantTimestampUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Contains("summary after micro was not enough", result.Messages[0].Text);
        Assert.True(result.Status.EstimatedTokensAfter > 32_000);
        Assert.True(result.Status.EstimatedTokensAfter < result.Status.EstimatedTokensBefore);
        Assert.True(result.Status.ClearedToolResults > 0);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_OpenAIProtocolSkipsColdCacheMicroByDefault()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 40_000,
            SummaryReserveTokens = 4_000,
            AutoCompactBufferTokens = 2_000,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
            MicrocompactEnabled = true,
            MicrocompactKeepRecent = 1,
            MicrocompactGapMinutes = 5
        };
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>openai summary</summary>"),
            cacheOptions: new MaintenanceForkCacheOptions(
                ModelProviderProtocols.OpenAIResponses,
                new AppConfig.PromptCachingConfig(),
                "gpt-test"));
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            messages.Add(AssistantCall(callId));
            messages.Add(ToolResult(callId, new string('r', 8_000)));
        }

        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            60_000,
            lastAssistantTimestampUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(0, result.Status.ClearedToolResults);
        Assert.Contains("openai summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryAutoCompactHistoryAsync_AnthropicOneHourCacheExtendsMicroGap()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 40_000,
            SummaryReserveTokens = 4_000,
            AutoCompactBufferTokens = 2_000,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
            MicrocompactEnabled = true,
            MicrocompactKeepRecent = 1,
            MicrocompactGapMinutes = 5
        };
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>anthropic summary</summary>"),
            cacheOptions: new MaintenanceForkCacheOptions(
                ModelProviderProtocols.Anthropic,
                new AppConfig.PromptCachingConfig { Ttl = "1h" },
                "claude-test"));
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            messages.Add(AssistantCall(callId));
            messages.Add(ToolResult(callId, new string('r', 8_000)));
        }

        var result = await pipeline.TryAutoCompactHistoryAsync(
            messages,
            "thread-1",
            60_000,
            lastAssistantTimestampUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(0, result.Status.ClearedToolResults);
        Assert.Contains("anthropic summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_PartialFailureFallsBackToFullCompaction()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var chat = new SequenceChatClient(
            "",
            "<summary>full fallback summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat);
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user {i} " + new string('u', 1200)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant {i} " + new string('a', 1200)));
        }

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(2, chat.CallCount);
        Assert.Single(result.Messages);
        Assert.Contains("full fallback summary", result.Messages[0].Text);
        Assert.False(pipeline.Failures.IsTripped("thread-1"));
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_CanExcludeStaleRequestOverheadFromAfterEstimate()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var pipeline = new CompactionPipeline(
            cfg,
            new SummaryChatClient("<summary>compact summary</summary>"));
        var messages = BuildMultiRoundMessages();
        var localEstimate = MessageTokenEstimator.Estimate(messages);

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            inputTokenHint: localEstimate + 120_000,
            carryRequestOverhead: false);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(localEstimate + 120_000, result.Status.EstimatedTokensBefore);
        Assert.True(result.Status.EstimatedTokensAfter < 20_000);
        Assert.Contains("compact summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_SnapshotPartialPathRecordsMaintenanceTrace()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SummaryChatClient("<summary>partial snapshot summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = BuildMultiRoundMessages();
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                MaxOutputTokens = 64_000,
                Tools = [tool]
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1");

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        var events = store.GetEvents("thread-1");
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains("\"toolCount\":1", events.First(e => e.Type == TraceEventType.MaintenanceForkRequest).MetadataJson);
        Assert.Equal("ReadFile", chat.Options?.Tools?.Single().Name);
        Assert.Equal(12_000, chat.Options?.MaxOutputTokens);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_SnapshotPrefixBeforeLatestMessagesUsesSnapshotFork()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SummaryChatClient("<summary>partial snapshot prefix summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = BuildMultiRoundMessages();
        var snapshotMessages = messages.Take(messages.Count - 2).ToArray();
        var snapshot = PromptRequestSnapshot.Capture(
            snapshotMessages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread-1",
            turnId: null);

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        var request = Assert.Single(
            store.GetEvents("thread-1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.DoesNotContain("\"mode\":\"legacy\"", request.MetadataJson);
        Assert.Contains("\"extraTailMessageCount\":2", request.MetadataJson);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_OversizedSnapshotFallsBackToLegacyPartialSummary()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var chat = new SequenceChatClient("<summary>legacy partial summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat);
        var messages = BuildMultiRoundMessages();
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1",
            estimatedInputTokens: 500_000);

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(1, chat.CallCount);
        Assert.Contains("legacy partial summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_SnapshotThatDoesNotCoverPrefixUsesLegacySummary()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SummaryChatClient("<summary>legacy mismatch summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = BuildMultiRoundMessages();
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "different prefix")],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1");

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Contains("legacy mismatch summary", result.Messages[0].Text);
        var request = Assert.Single(
            store.GetEvents("thread-1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"mode\":\"legacy\"", request.MetadataJson);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_OversizedFullSnapshotFallsBackToLegacyFullSummary()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        var chat = new SequenceChatClient("<summary>legacy full summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "user " + new string('u', 1200)),
            new(ChatRole.Assistant, "assistant " + new string('a', 1200)),
        };
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1",
            estimatedInputTokens: 500_000);

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(1, chat.CallCount);
        Assert.Single(result.Messages);
        Assert.Contains("legacy full summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_SnapshotOverlongSummaryFallsBackToFullCompaction()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        cfg.SummaryMaxOutputTokens = 10;
        var chat = new SequenceChatClient(
            "<summary>" + new string('x', 200) + "</summary>",
            "<summary>ok</summary>");
        var pipeline = new CompactionPipeline(cfg, chat);
        var messages = BuildMultiRoundMessages();
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1");

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(2, chat.CallCount);
        Assert.Single(result.Messages);
        Assert.Contains("ok", result.Messages[0].Text);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_PartialFailureAndFullFallbackRecordTwoMaintenanceResponses()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.KeepRecentMinTokens = 1;
        cfg.KeepRecentMinGroups = 1;
        cfg.KeepRecentMaxTokens = 100_000;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SequenceChatClient(
            "",
            "<summary>full fallback summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = BuildMultiRoundMessages();
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread-1",
            turnId: "turn-1");

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            snapshot: snapshot);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        var events = store.GetEvents("thread-1");
        Assert.Equal(2, events.Count(e => e.Type == TraceEventType.MaintenanceForkRequest));
        Assert.Equal(2, events.Count(e => e.Type == TraceEventType.MaintenanceForkResponse));
        Assert.Contains("\"fallbackReason\":\"empty_response\"", events
            .Where(e => e.Type == TraceEventType.MaintenanceForkResponse)
            .First().MetadataJson);
        Assert.Contains("full fallback summary", events
            .Where(e => e.Type == TraceEventType.MaintenanceForkResponse)
            .Last().Content);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_LegacyPathRecordsMaintenanceTrace()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        cfg.SummaryMaxOutputTokens = 1_234;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chat = new SummaryChatClient("<summary>legacy summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat, collector);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "user " + new string('u', 1200)),
            new(ChatRole.Assistant, "assistant " + new string('a', 1200)),
        };

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        var request = Assert.Single(
            store.GetEvents("thread-1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"mode\":\"legacy\"", request.MetadataJson);
        Assert.Equal(1_234, chat.Options?.MaxOutputTokens);
    }

    [Fact]
    public async Task TryManualCompactHistoryAsync_LegacyPathWithoutSnapshotPassesFallbackTools()
    {
        var cfg = DefaultConfig();
        cfg.MicrocompactEnabled = false;
        var chat = new SummaryChatClient("<summary>legacy summary</summary>");
        var pipeline = new CompactionPipeline(cfg, chat);
        var tool = AIFunctionFactory.Create(() => "ok", name: "GetStatus", description: "Get status.");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "user " + new string('u', 1200)),
            new(ChatRole.Assistant, "assistant " + new string('a', 1200)),
        };

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            "thread-1",
            lastAssistantTimestampUtc: null,
            CancellationToken.None,
            fallbackTools: [tool]);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        var capturedTool = Assert.Single(chat.Options?.Tools ?? []);
        Assert.Equal("GetStatus", capturedTool.Name);
    }

    private static List<ChatMessage> BuildMultiRoundMessages()
    {
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user {i} " + new string('u', 1200)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant {i} " + new string('a', 1200)));
        }

        return messages;
    }

    private static ChatMessage AssistantCall(string callId, string toolName = "ReadFile") =>
        new(ChatRole.Assistant, (IList<AIContent>)
        [
            new FunctionCallContent(callId, toolName, new Dictionary<string, object?>())
        ]);

    private static ChatMessage ToolResult(string callId, string result) =>
        new(ChatRole.Tool, (IList<AIContent>)
        [
            new FunctionResultContent(callId, result)
        ]);

    private sealed class DummyChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class SummaryChatClient(string responseText) : Microsoft.Extensions.AI.IChatClient
    {
        public Microsoft.Extensions.AI.ChatOptions? Options { get; private set; }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class SequenceChatClient(params string[] responseTexts) : Microsoft.Extensions.AI.IChatClient
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var responseText = _index < responseTexts.Length
                ? responseTexts[_index++]
                : responseTexts[^1];
            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
