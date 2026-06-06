using System.Runtime.CompilerServices;
using DotCraft.State;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tracing;

public sealed class TracingChatClientTests
{
    [Fact]
    public async Task StreamingReasoningChunks_RecordOneThinkingSegment()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("The ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("user")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(string.Empty)])
        ], "trace-reasoning");

        var thinking = EventsOfType(store, "trace-reasoning", TraceEventType.Thinking);
        var session = store.GetSession("trace-reasoning");

        var evt = Assert.Single(thinking);
        Assert.Equal("The user", evt.Content);
        Assert.Equal(1, session?.ThinkingCount);
    }

    [Fact]
    public async Task StreamingTextChunks_RecordOneResponseSegment()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello ")])
            {
                ResponseId = "resp-1",
                ModelId = "model-a"
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("world")])
            {
                ResponseId = "resp-1",
                MessageId = "msg-1",
                ModelId = "model-a"
            },
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop
            }
        ], "trace-response");

        var responses = EventsOfType(store, "trace-response", TraceEventType.Response);
        var session = store.GetSession("trace-response");

        var evt = Assert.Single(responses);
        Assert.Equal("Hello world", evt.Content);
        Assert.Equal("resp-1", evt.ResponseId);
        Assert.Equal("msg-1", evt.MessageId);
        Assert.Equal("model-a", evt.ModelId);
        Assert.Equal(ChatFinishReason.Stop.ToString(), evt.FinishReason);
        Assert.Equal(1, session?.ResponseCount);
    }

    [Fact]
    public async Task StreamingResponse_RecordsResolvedReasoningEffort()
    {
        var store = await RunStreamingAsync(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]) { ModelId = "model-a" }
            ],
            "trace-effort",
            options: new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High }
            });

        var evt = Assert.Single(EventsOfType(store, "trace-effort", TraceEventType.Response));
        Assert.Equal("high", evt.ReasoningEffort);
    }

    [Fact]
    public async Task StreamingResponse_OmitsReasoningEffort_WhenReasoningDisabled()
    {
        var store = await RunStreamingAsync(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]) { ModelId = "model-a" }
            ],
            "trace-no-effort");

        var evt = Assert.Single(EventsOfType(store, "trace-no-effort", TraceEventType.Response));
        Assert.Null(evt.ReasoningEffort);
    }

    [Fact]
    public async Task StreamingAlternatesThinkingAndResponse_RecordsOrderedSegments()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("first thought")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("second thought")])
        ], "trace-alternating");

        var events = NonRequestEvents(store, "trace-alternating");

        Assert.Equal(
            [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Thinking],
            events.Select(e => e.Type).ToArray());
        Assert.Equal(
            ["first thought", "answer", "second thought"],
            events.Select(e => e.Content ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task StreamingToolBoundary_SplitsResponseSegmentsAroundToolEvent()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("before tool")]),
            new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?> { ["path"] = "a.txt" })
            ]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("after tool")])
        ], "trace-tool-boundary");

        var events = NonRequestEvents(store, "trace-tool-boundary");
        var session = store.GetSession("trace-tool-boundary");

        Assert.Equal(
            [TraceEventType.Response, TraceEventType.ToolCallStarted, TraceEventType.Response],
            events.Select(e => e.Type).ToArray());
        Assert.Equal("before tool", events[0].Content);
        Assert.Equal("ReadFile", events[1].ToolName);
        Assert.Equal("after tool", events[2].Content);
        Assert.Equal(2, session?.ResponseCount);
    }

    [Fact]
    public async Task StreamingException_FlushesPendingSegmentsBeforeError()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thinking")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial answer")])
        ], "trace-exception", new InvalidOperationException("boom"));

        var events = NonRequestEvents(store, "trace-exception");

        Assert.Equal(
            [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Error],
            events.Select(e => e.Type).ToArray());
        Assert.Equal("thinking", events[0].Content);
        Assert.Equal("partial answer", events[1].Content);
        Assert.Equal("boom", events[2].Content);
    }

    [Fact]
    public async Task StreamingSegments_PersistAndReloadWithSegmentCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "tracing-chat-client-tests", Guid.NewGuid().ToString("N"));
        var craftPath = Path.Combine(root, ".craft");
        var tracingPath = Path.Combine(craftPath, "tracing");
        Directory.CreateDirectory(tracingPath);

        try
        {
            var stateRuntime = new StateRuntime(craftPath);
            var writer = new TraceStore(tracingPath, 5000, false, stateRuntime);
            await RunStreamingAsync([
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("one ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("two")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("segment")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("after")])
            ], "trace-persisted", store: writer);
            writer.WaitForPendingPersistence();

            var reader = new TraceStore(tracingPath, 5000, false, stateRuntime);
            reader.LoadFromDisk();

            var session = reader.GetSession("trace-persisted");
            var events = NonRequestEvents(reader, "trace-persisted");

            Assert.Equal(2, session?.ThinkingCount);
            Assert.Equal(1, session?.ResponseCount);
            Assert.Equal(
                [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Thinking],
                events.Select(e => e.Type).ToArray());
            Assert.Equal(
                ["one two", "answer segment", "after"],
                events.Select(e => e.Content ?? string.Empty).ToArray());
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; SQLite can briefly hold the file on Windows.
            }
        }
    }

    [Fact]
    public async Task StreamingUsage_RecordsCachedInputTokens()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = 100,
                    OutputTokenCount = 20,
                    CachedInputTokenCount = 64,
                    ReasoningTokenCount = 7
                })
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        ["cache_creation_input_tokens"] = 12
                    }
                }
            ])
        ], "trace-cached");

        var usage = Assert.Single(EventsOfType(store, "trace-cached", TraceEventType.TokenUsage));
        var session = store.GetSession("trace-cached");

        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(64, usage.CachedInputTokens);
        Assert.Equal(12, usage.CacheWriteInputTokens);
        Assert.Equal(24, usage.FreshInputTokens);
        Assert.Equal(36, usage.NonCachedInputTokens);
        Assert.Equal(7, usage.ReasoningOutputTokens);
        Assert.Equal(64, session?.TotalCachedInputTokens);
        Assert.Equal(12, session?.TotalCacheWriteInputTokens);
        Assert.Equal(24, session?.TotalFreshInputTokens);
        Assert.Equal(0.64, session?.CacheHitRate);
    }

    [Fact]
    public async Task StreamingUsage_MultipleRequestBoundaries_RecordEachRequestAndCacheHitTotals()
    {
        var store = await RunStreamingAsync([
            UsageUpdate(requestIndex: 1, input: 12_000, output: 1, cachedInput: 8_000),
            UsageUpdate(requestIndex: 2, input: 20_000, output: 2, cachedInput: 18_000),
            UsageUpdate(requestIndex: 3, input: 41_000, output: 8, cachedInput: 40_000)
        ], "trace-multi-request-usage");

        var usageEvents = EventsOfType(store, "trace-multi-request-usage", TraceEventType.TokenUsage);
        var session = store.GetSession("trace-multi-request-usage");

        Assert.Equal([12_000, 20_000, 41_000], usageEvents.Select(e => e.InputTokens).ToArray());
        Assert.Equal([8_000, 18_000, 40_000], usageEvents.Select(e => e.CachedInputTokens).ToArray());
        Assert.Equal([1, 2, 3], usageEvents.Select(e => e.RequestIndex).ToArray());
        Assert.Equal([1, 2, 3], usageEvents.Select(e => e.LlmCallIndex).ToArray());
        Assert.Equal(3, session?.TokenUsageCount);
        Assert.Equal(73_000, session?.TotalInputTokens);
        Assert.Equal(66_000, session?.TotalCachedInputTokens);
        Assert.Equal(7_000, session?.TotalFreshInputTokens);
        Assert.Equal(11, session?.TotalOutputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_ReadsProviderRawCachedTokenShapes()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "usage": {
                "input_tokens": 100,
                "input_tokens_details": { "cached_tokens": 72 },
                "output_tokens": 9,
                "output_tokens_details": { "reasoning_tokens": 3 }
              }
            }
            """);

        var usage = TokenUsageExtractor.FromUsageDetails(null, rawRepresentation: doc.RootElement);

        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(9, usage.OutputTokens);
        Assert.Equal(72, usage.CachedInputTokens);
        Assert.Equal(3, usage.ReasoningOutputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_ReadsOpenAiStylePromptCacheShape()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "usage": {
                "prompt_tokens": 2006,
                "completion_tokens": 300,
                "prompt_tokens_details": { "cached_tokens": 1920 }
              }
            }
            """);

        var usage = TokenUsageExtractor.FromUsageDetails(null, rawRepresentation: doc.RootElement);

        Assert.Equal(2006, usage.InputTokens);
        Assert.Equal(300, usage.OutputTokens);
        Assert.Equal(1920, usage.CachedInputTokens);
        Assert.Equal(0, usage.CacheWriteInputTokens);
        Assert.Equal(86, usage.FreshInputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_ReconstructsAnthropicNativeTotalInput()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "usage": {
                "input_tokens": 50,
                "cache_read_input_tokens": 100000,
                "cache_creation_input_tokens": 5000,
                "output_tokens": 25
              }
            }
            """);

        var usage = TokenUsageExtractor.FromUsageDetails(null, rawRepresentation: doc.RootElement);

        Assert.Equal(105050, usage.InputTokens);
        Assert.Equal(100000, usage.CachedInputTokens);
        Assert.Equal(5000, usage.CacheWriteInputTokens);
        Assert.Equal(50, usage.FreshInputTokens);
        Assert.Equal(25, usage.OutputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_DoesNotDoubleCountLiteLlmPromptTokensWithCacheCreation()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "usage": {
                "prompt_tokens": 2006,
                "completion_tokens": 300,
                "prompt_tokens_details": { "cached_tokens": 1200 },
                "cache_creation_input_tokens": 456
              }
            }
            """);

        var usage = TokenUsageExtractor.FromUsageDetails(null, rawRepresentation: doc.RootElement);

        Assert.Equal(2006, usage.InputTokens);
        Assert.Equal(1200, usage.CachedInputTokens);
        Assert.Equal(456, usage.CacheWriteInputTokens);
        Assert.Equal(350, usage.FreshInputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_ReadsGeminiUsageMetadataCachedContentTokens()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "usage_metadata": {
                "prompt_token_count": 4096,
                "cached_content_token_count": 1024,
                "candidates_token_count": 128
              }
            }
            """);

        var usage = TokenUsageExtractor.FromUsageDetails(null, rawRepresentation: doc.RootElement);

        Assert.Equal(4096, usage.InputTokens);
        Assert.Equal(128, usage.OutputTokens);
        Assert.Equal(1024, usage.CachedInputTokens);
        Assert.Equal(3072, usage.FreshInputTokens);
    }

    [Fact]
    public void TokenUsageExtractor_IgnoresSdkStyleStaticProperties()
    {
        var metadata = new
        {
            ReasoningEffort = SdkStyleReasoningEffort.Medium,
            Usage = new
            {
                prompt_tokens = 512,
                completion_tokens = 32,
                prompt_tokens_details = new
                {
                    cached_tokens = 384
                }
            }
        };

        var usage = TokenUsageExtractor.FromUsageDetails(null, metadata);

        Assert.Equal(512, usage.InputTokens);
        Assert.Equal(32, usage.OutputTokens);
        Assert.Equal(384, usage.CachedInputTokens);
        Assert.Equal(128, usage.FreshInputTokens);
    }

    private static async Task<TraceStore> RunStreamingAsync(
        ChatResponseUpdate[] updates,
        string sessionKey,
        Exception? throwAfterUpdates = null,
        TraceStore? store = null,
        ChatOptions? options = null)
    {
        store ??= new TraceStore();
        var collector = new TraceCollector(store);
        var client = new TracingChatClient(new FakeStreamingChatClient(updates, throwAfterUpdates), collector);
        var previousSessionKey = TracingChatClient.CurrentSessionKey;

        TracingChatClient.ResetCallState(sessionKey);
        TracingChatClient.CurrentSessionKey = sessionKey;
        try
        {
            if (throwAfterUpdates == null)
            {
                await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options))
                {
                }
            }
            else
            {
                await Assert.ThrowsAsync(throwAfterUpdates.GetType(), async () =>
                {
                    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options))
                    {
                    }
                });
            }

            return store;
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    private static IReadOnlyList<TraceEvent> NonRequestEvents(TraceStore store, string sessionKey)
        => store.GetEvents(sessionKey).Where(e => e.Type != TraceEventType.Request).ToList();

    private static IReadOnlyList<TraceEvent> EventsOfType(
        TraceStore store,
        string sessionKey,
        TraceEventType type)
        => store.GetEvents(sessionKey).Where(e => e.Type == type).ToList();

    private static ChatResponseUpdate UsageUpdate(int requestIndex, long input, long output, long cachedInput)
        => new()
        {
            Role = ChatRole.Assistant,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [TokenUsageRequestMetadata.RequestIndexKey] = requestIndex
            },
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = input,
                    OutputTokenCount = output,
                    CachedInputTokenCount = cachedInput
                })
            ]
        };

    private readonly record struct SdkStyleReasoningEffort(string Value)
    {
        public static SdkStyleReasoningEffort None { get; } = new("none");

        public static SdkStyleReasoningEffort Medium { get; } = new("medium");

        public override string ToString() => Value;
    }

    private sealed class FakeStreamingChatClient(
        ChatResponseUpdate[] updates,
        Exception? throwAfterUpdates = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
                yield return update;

            if (throwAfterUpdates != null)
                throw throwAfterUpdates;

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
