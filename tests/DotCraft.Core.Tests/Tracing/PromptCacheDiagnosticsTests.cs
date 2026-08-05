using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class PromptCacheDiagnosticsTests
{
    [Fact]
    public void SubAgentPrefixDiagnostic_ParentPrefixWithChildTailMatchesOnce()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestShape("parent", RequestShape(["a", "b"]), 3, 1);
        collector.BindChildSession("child", "parent", "parent");
        collector.RecordPromptCacheRequestShape("child", RequestShape(["a", "b", "guidance", "task"]), 1, 1);
        collector.RecordPromptCacheRequestShape("child", RequestShape(["a", "b", "guidance", "task", "reply"]), 2, 1);

        var diagnostic = Assert.Single(Events(store, "child", TraceEventType.SubAgentPrefixDiagnostic));
        using var metadata = JsonDocument.Parse(diagnostic.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("match", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("matchedInputItemCount").GetInt32());
        Assert.Equal(2, root.GetProperty("parentInputItemCount").GetInt32());
        Assert.Equal(4, root.GetProperty("childInputItemCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("divergenceIndex").ValueKind);
        Assert.Empty(root.GetProperty("changedFields").EnumerateArray());
        Assert.Equal(3, root.GetProperty("parentRequestIndex").GetInt32());
        Assert.Equal(1, root.GetProperty("childRequestIndex").GetInt32());
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("model")]
    [InlineData("cacheKey")]
    [InlineData("instructions")]
    [InlineData("tools")]
    [InlineData("reasoning")]
    public void SubAgentPrefixDiagnostic_ComponentChangeReportsExactField(string changedField)
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestShape("parent", RequestShape(["a"]), 1, 1);
        collector.BindChildSession("child", "parent", "parent");
        collector.RecordPromptCacheRequestShape("child", RequestShape(["a"], changedField), 1, 1);

        var diagnostic = Assert.Single(Events(store, "child", TraceEventType.SubAgentPrefixDiagnostic));
        using var metadata = JsonDocument.Parse(diagnostic.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("mismatch", root.GetProperty("status").GetString());
        Assert.Equal([changedField], root.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("different", 1)]
    [InlineData("shorter", 1)]
    public void SubAgentPrefixDiagnostic_InputDivergenceReportsFirstIndex(string scenario, int divergenceIndex)
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestShape("parent", RequestShape(["a", "b", "c"]), 1, 1);
        collector.BindChildSession("child", "parent", "parent");
        collector.RecordPromptCacheRequestShape(
            "child",
            RequestShape(scenario == "shorter" ? ["a"] : ["a", "x", "c"]),
            1,
            1);

        var diagnostic = Assert.Single(Events(store, "child", TraceEventType.SubAgentPrefixDiagnostic));
        using var metadata = JsonDocument.Parse(diagnostic.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("mismatch", root.GetProperty("status").GetString());
        Assert.Equal(divergenceIndex, root.GetProperty("divergenceIndex").GetInt32());
        Assert.Equal(["inputPrefix"], root.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void SubAgentPrefixDiagnostic_MissingParentShapeIsUnavailable()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.BindChildSession("child", "parent", "parent");
        collector.RecordPromptCacheRequestShape("child", RequestShape(["task"]), 1, 2);

        var diagnostic = Assert.Single(Events(store, "child", TraceEventType.SubAgentPrefixDiagnostic));
        using var metadata = JsonDocument.Parse(diagnostic.MetadataJson!);
        Assert.Equal("unavailable", metadata.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("parentInputItemCount").ValueKind);
        Assert.Equal(2, metadata.RootElement.GetProperty("childAttemptNumber").GetInt32());
    }

    [Fact]
    public void SubAgentPrefixDiagnostic_NestedChildUsesDirectParentAnchor()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestShape("root", RequestShape(["root"]), 1, 1);
        collector.BindChildSession("parent", "root", "root");
        collector.RecordPromptCacheRequestShape("parent", RequestShape(["root", "parent"]), 1, 1);
        collector.BindChildSession("child", "root", "parent");
        collector.RecordPromptCacheRequestShape("child", RequestShape(["root", "parent", "child"]), 1, 1);

        var diagnostic = Assert.Single(Events(store, "child", TraceEventType.SubAgentPrefixDiagnostic));
        using var metadata = JsonDocument.Parse(diagnostic.MetadataJson!);
        Assert.Equal("parent", metadata.RootElement.GetProperty("parentSessionKey").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("matchedInputItemCount").GetInt32());
    }

    [Fact]
    public void RecordSessionMetadata_RecordsBaselineOnceAndSkipsUnchangedMetadata()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordSessionMetadata("session", "system", ["ReadFile", "EditFile"]);
        collector.RecordSessionMetadata("session", "system", ["ReadFile", "EditFile"]);

        var metadata = Events(store, "session", TraceEventType.SessionMetadata);
        var evt = Assert.Single(metadata);
        Assert.Equal(PromptCacheEventKinds.Baseline, evt.PromptCacheEventKind);
        Assert.False(evt.PromptDriftDetected);
        Assert.Equal(0, store.GetSession("session")!.PromptDriftCount);
    }

    [Fact]
    public void RecordSessionMetadata_SystemPromptChangeRecordsDrift()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordSessionMetadata("session", "system-v1", ["ReadFile"]);
        collector.RecordSessionMetadata("session", "system-v2", ["ReadFile"]);

        var drift = Events(store, "session", TraceEventType.SessionMetadata).Last();
        Assert.Equal(PromptCacheEventKinds.Drift, drift.PromptCacheEventKind);
        Assert.NotNull(drift.PromptCacheChangedFields);
        Assert.Equal([PromptCacheChangedFields.Prompt], drift.PromptCacheChangedFields);
        Assert.True(drift.PromptDriftDetected);
        Assert.NotEqual(drift.PreviousSystemPromptHash, drift.CurrentSystemPromptHash);
        Assert.Equal(1, store.GetSession("session")!.PromptDriftCount);
    }

    [Theory]
    [InlineData("ReadFile")]
    [InlineData("EditFile|ReadFile")]
    [InlineData("WriteFile|EditFile")]
    public void RecordSessionMetadata_NonAppendOnlyToolChangeRecordsDrift(string newToolsCsv)
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordSessionMetadata("session", "system", ["ReadFile", "EditFile"]);
        var newTools = newToolsCsv.Split('|');
        collector.RecordSessionMetadata("session", "system", newTools);

        var drift = Events(store, "session", TraceEventType.SessionMetadata).Last();
        Assert.Equal(PromptCacheEventKinds.Drift, drift.PromptCacheEventKind);
        Assert.NotNull(drift.PromptCacheChangedFields);
        Assert.Equal([PromptCacheChangedFields.Tools], drift.PromptCacheChangedFields);
        Assert.True(drift.PromptDriftDetected);
        Assert.Equal(1, store.GetSession("session")!.PromptDriftCount);
    }

    [Fact]
    public void RecordSessionMetadata_AppendOnlyToolChangeRecordsToolExtension()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordSessionMetadata("session", "system", ["ReadFile"]);
        collector.RecordSessionMetadata("session", "system", ["ReadFile", "EditFile", "WriteFile"]);

        var extension = Events(store, "session", TraceEventType.SessionMetadata).Last();
        Assert.Equal(PromptCacheEventKinds.ToolExtension, extension.PromptCacheEventKind);
        Assert.NotNull(extension.PromptCacheChangedFields);
        Assert.NotNull(extension.ChangedToolNames);
        Assert.Equal([PromptCacheChangedFields.Tools], extension.PromptCacheChangedFields);
        Assert.Equal(["EditFile", "WriteFile"], extension.ChangedToolNames);
        Assert.False(extension.PromptDriftDetected);
        Assert.Equal(0, store.GetSession("session")!.PromptDriftCount);
        Assert.Equal(PromptCacheEventKinds.ToolExtension, store.GetSession("session")!.LastPromptCacheChangeKind);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_FirstUsageClassifiesColdStart()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(10_000, 10, 0, 0, CacheWriteInputTokens: 8_000));

        var diagnostic = Assert.Single(Events(store, "session", TraceEventType.PromptCacheDiagnostic));
        Assert.Equal(PromptCacheDiagnosticKinds.ColdStart, diagnostic.PromptCacheEventKind);
        Assert.False(diagnostic.PromptCacheBreakDetected);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_HighReadToZeroWithUnchangedKeysClassifiesLikelyServerSide()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, hashPrefix: "aaaa1111", newSelectedCount: 1, latestSelectedPointIsNew: true));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 11_758, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, hashPrefix: "aaaa1111"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_100, 1, 0, 0));

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.LikelyServerSide, diagnostic.PromptCacheEventKind);
        Assert.True(diagnostic.PromptCacheBreakDetected);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_SmallReadDropDoesNotReportBreak()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 10_000, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_100, 1, 9_600, 0));

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.CacheHitStable, diagnostic.PromptCacheEventKind);
        Assert.False(diagnostic.PromptCacheBreakDetected);
    }

    [Theory]
    [InlineData("", 6)]
    [InlineData("5m", 6)]
    [InlineData("1h", 61)]
    public void PromptCacheUsageDiagnostic_TtlGapClassifiesPossibleTtl(string ttl, int elapsedMinutes)
    {
        var now = DateTimeOffset.Parse("2026-05-13T08:00:00Z");
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, ttl: ttl));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 10_000, 0), timestamp: now);
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, ttl: ttl));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_100, 1, 0, 0), timestamp: now.AddMinutes(elapsedMinutes));

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.TtlPossible, diagnostic.PromptCacheEventKind);
        Assert.True(diagnostic.PromptCacheBreakDetected);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_KeyChangeClassifiesPromptOrToolsChanged()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, systemHash: "system-a", toolSchemaHash: "tools-a"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 10_000, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, systemHash: "system-b", toolSchemaHash: "tools-a"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_100, 1, 0, 0));

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.PromptOrToolsChanged, diagnostic.PromptCacheEventKind);
        Assert.NotNull(diagnostic.PromptCacheChangedFields);
        Assert.Equal([PromptCacheChangedFields.Prompt], diagnostic.PromptCacheChangedFields!);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_ReasoningChangeClassifiesPromptOrToolsChanged()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, reasoningHash: "reasoning-a"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 10_000, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, reasoningHash: "reasoning-b"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_100, 1, 0, 0));

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.PromptOrToolsChanged, diagnostic.PromptCacheEventKind);
        Assert.NotNull(diagnostic.PromptCacheChangedFields);
        Assert.Equal([PromptCacheChangedFields.Reasoning], diagnostic.PromptCacheChangedFields!);
        Assert.Contains("\"reasoningChanged\":true", diagnostic.MetadataJson);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_NewLatestPrefixClassifiesWarmWrite()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, hashPrefix: "aaaa1111"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 0, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, hashPrefix: "bbbb2222", newSelectedCount: 1, latestSelectedPointIsNew: true));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(29_193, 1, 0, 0, CacheWriteInputTokens: 29_193), requestIndex: 1);

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.WarmWriteOrNewPrefix, diagnostic.PromptCacheEventKind);
        Assert.False(diagnostic.PromptCacheBreakDetected);
        Assert.Equal(1, diagnostic.RequestIndex);
        Assert.Contains("LLM #2 request 1", diagnostic.Content);
    }

    [Fact]
    public void PromptCacheUsageDiagnostic_NewLatestPrefixAfterHighReadReportsBreak()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 1, hashPrefix: "aaaa1111"));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(12_000, 1, 11_758, 0));
        collector.RecordPromptCacheRequestSnapshot("session", Snapshot(llmCallIndex: 2, hashPrefix: "bbbb2222", newSelectedCount: 1, latestSelectedPointIsNew: true));
        collector.RecordTokenUsage("session", new TokenUsageSnapshot(29_193, 1, 0, 0), requestIndex: 1);

        var diagnostic = Events(store, "session", TraceEventType.PromptCacheDiagnostic).Last();
        Assert.Equal(PromptCacheDiagnosticKinds.LikelyServerSide, diagnostic.PromptCacheEventKind);
        Assert.True(diagnostic.PromptCacheBreakDetected);
    }

    [Fact]
    public async Task PromptCachingAndTracing_RecordsUsageDiagnosticsWithoutPromptText()
    {
        const string sessionKey = "prompt-cache-diagnostic-integration";
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var usageClient = new UsageChatClient(
            new TokenUsageSnapshot(12_000, 1, 8_000, 0),
            new TokenUsageSnapshot(13_000, 1, 0, 0, CacheWriteInputTokens: 12_900));
        var promptCaching = new PromptCachingChatClient(
            usageClient,
            new AppConfig.PromptCachingConfig(),
            "claude-opus-4-1",
            collector,
            () => sessionKey);
        var client = new TracingChatClient(promptCaching, collector);

        try
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;

            await client.GetResponseAsync([
                new ChatMessage(ChatRole.System, "secret stable system"),
                new ChatMessage(ChatRole.User, "secret user prompt")
            ]);
            await client.GetResponseAsync([
                new ChatMessage(ChatRole.System, "secret stable system"),
                new ChatMessage(ChatRole.User, "secret user prompt"),
                new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                    new TextContent("secret assistant text"),
                    new FunctionCallContent("call_1", "RequestUserInput", new Dictionary<string, object?>())
                ]),
                new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                    new FunctionResultContent("call_1", JsonElementFrom("""{"answer":"secret tool result"}"""))
                ])
            ]);
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }

        var usageEvents = Events(store, sessionKey, TraceEventType.TokenUsage);
        var diagnostics = Events(store, sessionKey, TraceEventType.PromptCacheDiagnostic);
        Assert.Equal(usageEvents.Count, diagnostics.Count);
        Assert.Equal(usageEvents.Select(static e => e.LlmCallIndex), diagnostics.Select(static e => e.LlmCallIndex));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.NotNull(diagnostic.MetadataJson);
            Assert.DoesNotContain("secret user prompt", diagnostic.MetadataJson);
            Assert.DoesNotContain("secret assistant text", diagnostic.MetadataJson);
            Assert.DoesNotContain("secret tool result", diagnostic.MetadataJson);
            Assert.DoesNotContain("secret stable system", diagnostic.MetadataJson);
        });
    }

    private static IReadOnlyList<TraceEvent> Events(TraceStore store, string sessionKey, TraceEventType type) =>
        store.GetEvents(sessionKey).Where(e => e.Type == type).ToList();

    private static PromptCacheRequestShapeSnapshot RequestShape(
        IReadOnlyList<string> inputItemHashes,
        string? changedField = null) => new(
        1,
        changedField == "protocol" ? "other-responses" : "openai-responses",
        changedField == "model" ? "gpt-other" : "gpt-test",
        changedField == "cacheKey" ? "cache-other" : "cache",
        "thread",
        changedField == "instructions" ? "instructions-other" : "instructions",
        changedField == "tools" ? "tools-other" : "tools",
        changedField == "reasoning" ? "reasoning-other" : "reasoning",
        "input",
        inputItemHashes.Count,
        inputItemHashes,
        100,
        0,
        0,
        0,
        0,
        0,
        null,
        false,
        false,
        null,
        "auto",
        1,
        true);

    private static PromptCacheRequestDiagnosticSnapshot Snapshot(
        int llmCallIndex,
        string? ttl = null,
        string systemHash = "system-hash",
        string toolSchemaHash = "tool-hash",
        string? reasoningHash = null,
        string hashPrefix = "abc123def456",
        int newSelectedCount = 0,
        bool latestSelectedPointIsNew = false)
    {
        var selectedPoints = new[]
        {
            new PromptCacheSelectedPointDiagnostic(
                "system",
                "text",
                0,
                0,
                0,
                hashPrefix,
                newSelectedCount == 0,
                latestSelectedPointIsNew)
        };
        return new PromptCacheRequestDiagnosticSnapshot(
            "claude-opus-4-1",
            "OpenAICompatible",
            string.IsNullOrWhiteSpace(ttl) ? null : ttl,
            llmCallIndex,
            selectedPoints.Length,
            selectedPoints.Length,
            newSelectedCount,
            selectedPoints.Length - newSelectedCount,
            latestSelectedPointIsNew,
            systemHash,
            toolSchemaHash,
            reasoningHash,
            2,
            selectedPoints,
            [new PromptCacheCandidateCountDiagnostic("system", "text", 1)]);
    }

    private static JsonElement JsonElementFrom(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class UsageChatClient(params TokenUsageSnapshot[] usages) : IChatClient
    {
        private int _index;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var usage = usages[Math.Min(_index++, usages.Length - 1)];
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = usage.InputTokens,
                    OutputTokenCount = usage.OutputTokens,
                    CachedInputTokenCount = usage.CachedInputTokens,
                    ReasoningTokenCount = usage.ReasoningOutputTokens
                },
                AdditionalProperties = usage.CacheWriteInputTokens > 0
                    ? new AdditionalPropertiesDictionary
                    {
                        ["cache_creation_input_tokens"] = usage.CacheWriteInputTokens
                    }
                    : null
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
