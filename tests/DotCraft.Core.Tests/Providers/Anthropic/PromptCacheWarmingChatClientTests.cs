using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class PromptCacheWarmingChatClientTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Due = TimeSpan.FromSeconds(270);

    [Fact]
    public async Task LargeTurnRequest_ReplaysOnceWhenDue_AndANewRequestRearms()
    {
        var clock = new ManualClock();
        var inner = new ScriptedChatClient(promptTokens: 20_000);
        var client = new PromptCacheWarmingChatClient(inner, Ttl, clock);
        var diagnostics = new RecordingDiagnostics();
        using var request = UseTurnRequest(diagnostics);
        using var warming = PromptCacheWarmingScope.Begin();
        List<ChatMessage> first = [new(ChatRole.User, "first")];
        var options = new ChatOptions { MaxOutputTokens = 4096, Instructions = "system" };

        await DrainAsync(client.GetStreamingResponseAsync(first, options));
        clock.Advance(Due - TimeSpan.FromSeconds(1));
        Assert.Empty(inner.Warms);

        clock.Advance(TimeSpan.FromSeconds(1));
        WaitUntil(() => diagnostics.WarmOutcomes.Count == 1 && clock.PendingTimers == 1);
        var warm = Assert.Single(inner.Warms);
        Assert.Equal(first, warm.Messages);
        Assert.Equal(1, warm.Options!.MaxOutputTokens);
        Assert.Equal("system", warm.Options.Instructions);
        Assert.Equal(4096, options.MaxOutputTokens);
        Assert.Equal(ProviderRequestKind.CacheWarm, warm.RequestKind);
        Assert.Equal("succeeded", Assert.Single(diagnostics.WarmOutcomes));

        clock.Advance(TimeSpan.FromSeconds(200));
        List<ChatMessage> second = [.. first, new(ChatRole.Assistant, "reply"), new(ChatRole.User, "second")];
        await DrainAsync(client.GetStreamingResponseAsync(second, options));
        clock.Advance(TimeSpan.FromSeconds(100));
        Assert.Single(inner.Warms);

        clock.Advance(Due - TimeSpan.FromSeconds(100));
        WaitUntil(() => diagnostics.WarmOutcomes.Count == 2);
        Assert.Equal(2, inner.Warms.Count);
        Assert.Equal(second, inner.Warms[1].Messages);
    }

    [Fact]
    public async Task RequestsWithoutTurnScopeOrBelowThreshold_DoNotArm()
    {
        var clock = new ManualClock();
        var diagnostics = new RecordingDiagnostics();
        using var request = UseTurnRequest(diagnostics);
        List<ChatMessage> messages = [new(ChatRole.User, "hello")];

        var large = new ScriptedChatClient(promptTokens: 20_000);
        await DrainAsync(new PromptCacheWarmingChatClient(large, Ttl, clock).GetStreamingResponseAsync(messages));

        var small = new ScriptedChatClient(promptTokens: 15_999);
        using (PromptCacheWarmingScope.Begin())
        {
            await DrainAsync(new PromptCacheWarmingChatClient(small, Ttl, clock).GetStreamingResponseAsync(messages));
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        Assert.Empty(large.Warms);
        Assert.Empty(small.Warms);
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task LateFirePastDeadline_SendsNothing()
    {
        var clock = new ManualClock();
        var inner = new ScriptedChatClient(promptTokens: 20_000);
        var diagnostics = new RecordingDiagnostics();
        using var request = UseTurnRequest(diagnostics);
        using var warming = PromptCacheWarmingScope.Begin();

        await DrainAsync(new PromptCacheWarmingChatClient(inner, Ttl, clock)
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]));
        clock.Advance(Due + TimeSpan.FromSeconds(16));
        WaitUntil(() => diagnostics.WarmOutcomes.Count == 1);

        Assert.Empty(inner.Warms);
        Assert.Equal("late", Assert.Single(diagnostics.WarmOutcomes));
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task DisposingTurnScope_StopsWarming()
    {
        var clock = new ManualClock();
        var inner = new ScriptedChatClient(promptTokens: 20_000);
        using var request = UseTurnRequest(new RecordingDiagnostics());
        var warming = PromptCacheWarmingScope.Begin();

        await DrainAsync(new PromptCacheWarmingChatClient(inner, Ttl, clock)
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]));
        warming.Dispose();
        clock.Advance(Due);

        Assert.Empty(inner.Warms);
        Assert.Null(PromptCacheWarmingScope.Current);
    }

    [Fact]
    public async Task AnthropicReplay_ReusesCommittedMarkersAndLeavesNextSelectionUnchanged()
    {
        var clock = new ManualClock();
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        using var request = UseTurnRequest(collector);
        using var warming = PromptCacheWarmingScope.Begin();
        var options = new ChatOptions { Instructions = "stable system prompt" };
        List<ChatMessage> first =
        [
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "assistant one"),
            new(ChatRole.User, "inspect the repository")
        ];
        List<ChatMessage> second =
        [
            .. first,
            new(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_1", "file contents")])
        ];

        var warmedHandler = new AnthropicCaptureHandler();
        var warmed = new PromptCacheWarmingChatClient(CreateAnthropicChain(warmedHandler), Ttl, clock);
        await warmed.GetResponseAsync(first, options);
        clock.Advance(Due);
        WaitUntil(() => store.GetEvents(ThreadId).Any(e => e.MetadataJson?.Contains("prompt_cache_warm") == true));
        await warmed.GetResponseAsync(second, options);

        var controlHandler = new AnthropicCaptureHandler();
        var control = CreateAnthropicChain(controlHandler);
        using (UseTurnRequest(new RecordingDiagnostics(), threadId: "control"))
        {
            await control.GetResponseAsync(first, options);
            await control.GetResponseAsync(second, options);
        }

        Assert.Equal(3, warmedHandler.Requests.Count);
        using var original = JsonDocument.Parse(warmedHandler.Requests[0]);
        using var replay = JsonDocument.Parse(warmedHandler.Requests[1]);
        Assert.Equal(1, replay.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(
            original.RootElement.GetProperty("system").GetRawText(),
            replay.RootElement.GetProperty("system").GetRawText());
        Assert.Equal(
            original.RootElement.GetProperty("messages").GetRawText(),
            replay.RootElement.GetProperty("messages").GetRawText());
        Assert.NotEmpty(CacheMarkerPositions(replay.RootElement));
        Assert.Equal(
            CacheMarkerPositions(JsonDocument.Parse(controlHandler.Requests[1]).RootElement),
            CacheMarkerPositions(JsonDocument.Parse(warmedHandler.Requests[2]).RootElement));

        var events = store.GetEvents(ThreadId);
        Assert.Equal(
            [1, 2],
            events.Where(e => e.Type == TraceEventType.PromptCachePoint).Select(e => e.LlmCallIndex ?? 0).ToArray());
        var warmEvent = Assert.Single(events, e => e.MetadataJson?.Contains("prompt_cache_warm") == true);
        using var metadata = JsonDocument.Parse(warmEvent.MetadataJson!);
        Assert.Equal("succeeded", metadata.RootElement.GetProperty("outcome").GetString());
        Assert.True(metadata.RootElement.GetProperty("inputTokens").GetInt64() > 0);
        Assert.DoesNotContain(events, e => e.Type == TraceEventType.TokenUsage);
    }

    private const string ThreadId = "thread";

    private static IDisposable UseTurnRequest(IModelRuntimeDiagnostics diagnostics, string threadId = ThreadId) =>
        ProviderRequestContextScope.Push(new ProviderRequestContext(
            new ProviderConversationIdentity(
                threadId, threadId, null, null, "turn_1", "window", ProviderRequestKind.Turn, 0, "test", null),
            Diagnostics: diagnostics));

    private static IChatClient CreateAnthropicChain(AnthropicCaptureHandler handler)
    {
        const string model = "claude-haiku-4-5";
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        return new ProviderPipelineOptionsChatClient(
            new AnthropicPromptCachingChatClient(anthropicClient.AsIChatClient(model), model),
            new ProviderPipelineOptions(
                new EffectiveModelRuntime(
                    "anthropic",
                    model,
                    ModelProviderProtocols.Anthropic,
                    "anthropic",
                    string.Empty,
                    string.Empty,
                    60,
                    null,
                    ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic)),
                null,
                null,
                false,
                "Standard",
                true,
                null));
    }

    private static string[] CacheMarkerPositions(JsonElement root)
    {
        var positions = new List<string>();
        if (root.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.Array)
        {
            for (var i = 0; i < system.GetArrayLength(); i++)
            {
                if (system[i].TryGetProperty("cache_control", out _))
                    positions.Add($"system:{i}");
            }
        }

        var messages = root.GetProperty("messages");
        for (var m = 0; m < messages.GetArrayLength(); m++)
        {
            var content = messages[m].GetProperty("content");
            if (content.ValueKind != JsonValueKind.Array)
                continue;
            for (var c = 0; c < content.GetArrayLength(); c++)
            {
                if (content[c].TryGetProperty("cache_control", out _))
                    positions.Add($"{m}:{c}");
            }
        }

        return [.. positions];
    }

    private static void WaitUntil(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)));

    private static async Task DrainAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    private sealed record WarmCall(
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options,
        ProviderRequestKind? RequestKind);

    private sealed class ScriptedChatClient(long promptTokens) : IChatClient
    {
        public List<WarmCall> Warms { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Warms.Add(new WarmCall(
                messages.ToList(),
                options,
                ProviderRequestContextScope.Current?.CurrentIdentity.RequestKind));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "o"))
            {
                FinishReason = ChatFinishReason.Length,
                Usage = new UsageDetails { InputTokenCount = promptTokens, OutputTokenCount = 1 }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            yield return new ChatResponseUpdate(ChatRole.Assistant, [
                new UsageContent(new UsageDetails { InputTokenCount = promptTokens, OutputTokenCount = 2 })
            ])
            {
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingDiagnostics : IModelRuntimeDiagnostics
    {
        public List<string> WarmOutcomes { get; } = [];

        public void Record(ModelRuntimeDiagnostic diagnostic)
        {
            if (diagnostic.Name == PromptCacheWarmingChatClient.DiagnosticName)
                WarmOutcomes.Add((string)diagnostic.Properties["outcome"]!);
        }
    }

    private sealed class AnthropicCaptureHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "id": "msg_warm_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-haiku-4-5",
                        "content": [{ "type": "text", "text": "o" }],
                        "stop_reason": "max_tokens",
                        "usage": {
                            "input_tokens": 20000,
                            "output_tokens": 1
                        }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        public int PendingTimers
        {
            get { lock (_gate) return _timers.Count(static timer => timer.DueAt.HasValue); }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
                _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _now += duration;
                due = _timers.Where(timer => timer.DueAt <= _now).OrderBy(static timer => timer.DueAt).ToList();
                foreach (var timer in due)
                    timer.DueAt = null;
            }

            foreach (var timer in due)
                timer.Fire();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (clock._gate)
                {
                    DueAt = null;
                    clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
