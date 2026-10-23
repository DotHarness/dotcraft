using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Tests.Agents;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class MaintenanceForkRunnerTests : IDisposable
{
    private readonly IDisposable _promptCachePolicy = PromptCachePolicyScope.Use();

    public void Dispose() => _promptCachePolicy.Dispose();

    [Fact]
    public async Task RunAsync_WithoutABudget_SendsTheSnapshotPrefixPlusTaskAndRecordsOnlyMaintenanceEvents()
    {
        var store = new TraceStore();
        var chatClient = new RecordingChatClient("<summary>important bits</summary>");
        var runner = new MaintenanceForkRunner(chatClient, new TraceCollector(store));
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshot = PromptRequestSnapshot.Capture(
            [
                new ChatMessage(ChatRole.User, "start"),
                new ChatMessage(ChatRole.Assistant, "working")
            ],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                Tools = [tool],
                AllowMultipleToolCalls = true
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1",
            estimatedInputTokens: 10_000);

        var result = await runner.RunAsync(snapshot, CompactionTask());

        Assert.Null(result.FallbackReason);
        Assert.Equal("<summary>important bits</summary>", result.Text);
        Assert.Equal(["user:start", "assistant:working"], chatClient.Messages.Take(2).Select(m => $"{m.Role}:{m.Text}"));
        Assert.Equal(3, chatClient.Messages.Count);
        Assert.Equal(ChatRole.User, chatClient.Messages[^1].Role);
        Assert.Equal("stable base", chatClient.Options?.Instructions);
        Assert.Equal("gpt-test", chatClient.Options?.ModelId);
        Assert.True(chatClient.Options?.AllowMultipleToolCalls);
        Assert.Equal("ReadFile", Assert.Single(chatClient.Options?.Tools ?? []).Name);

        var events = store.GetEvents("thread_1");
        var request = Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        var response = Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains("Summarize older context.", request.Content);
        Assert.Equal("<summary>important bits</summary>", response.Content);
        Assert.Contains("\"providerId\":\"provider-test\"", request.MetadataJson);
        Assert.Contains("\"toolCount\":1", request.MetadataJson);
        Assert.Contains("\"estimatedInputTokens\":", request.MetadataJson);
        Assert.Contains("\"effectiveBudgetTokens\":null", request.MetadataJson);
        Assert.Contains("\"inputBudgetSource\":null", request.MetadataJson);
        Assert.Contains("\"preflightRejected\":null", request.MetadataJson);
        Assert.Contains("\"fallbackReason\":null", response.MetadataJson);
        Assert.DoesNotContain(events, e => e.Type is TraceEventType.Request or TraceEventType.Response
            or TraceEventType.ToolCallStarted or TraceEventType.TokenUsage);
    }

    [Fact]
    public async Task RunAsync_PreflightRejectsOversizedSnapshotWithTerminalTrace()
    {
        var store = new TraceStore();
        var chatClient = new RecordingChatClient("<summary>provider should not be called</summary>");
        var runner = new MaintenanceForkRunner(chatClient, new TraceCollector(store));
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1",
            estimatedInputTokens: 10_000);

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context.")
            {
                InputBudgetTokens = 1_000,
                InputBudgetSource = "test-budget",
                MaxOutputTokensOverride = 123
            });

        Assert.Equal(MaintenanceForkFallbackReasons.SnapshotTooLarge, result.FallbackReason);
        Assert.Empty(chatClient.Messages);
        var request = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"effectiveBudgetTokens\":1000", request.MetadataJson);
        Assert.Contains("\"inputBudgetSource\":\"test-budget\"", request.MetadataJson);
        Assert.Contains("\"preflightRejected\":true", request.MetadataJson);
        AssertTerminalFallback(store, MaintenanceForkFallbackReasons.SnapshotTooLarge);
    }

    [Theory]
    [InlineData(null, "empty_response")]
    [InlineData("provider returned an empty error response", MaintenanceForkFallbackReasons.EmptyErrorResponse)]
    [InlineData("context_length_exceeded: input exceeds the context window", MaintenanceForkFallbackReasons.SnapshotTooLarge)]
    public async Task RunAsync_ResponsesWithoutText_FallBack(string? error, string expectedReason)
    {
        var store = new TraceStore();
        var response = error is null
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[new ErrorContent(error)]));
        var runner = new MaintenanceForkRunner(new RecordingChatClient(response), new TraceCollector(store));

        var result = await runner.RunAsync(CreateSnapshot(), CompactionTask());

        Assert.Equal(expectedReason, result.FallbackReason);
        AssertTerminalFallback(store, expectedReason);
    }

    public static TheoryData<Exception, string> ProviderFailures => new()
    {
        { new InvalidOperationException("provider failed"), "provider failed" },
        { new OperationCanceledException("timeout"), "provider_timeout" },
        { new InvalidOperationException("context_length_exceeded"), MaintenanceForkFallbackReasons.SnapshotTooLarge }
    };

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task RunAsync_ProviderFailures_FallBackWithATerminalResponse(Exception error, string expectedReason)
    {
        var store = new TraceStore();
        var runner = new MaintenanceForkRunner(new RecordingChatClient(error), new TraceCollector(store));

        var result = await runner.RunAsync(CreateSnapshot(), CompactionTask());

        Assert.Equal(expectedReason, result.FallbackReason);
        AssertTerminalFallback(store, expectedReason);
    }

    [Fact]
    public async Task RunAsync_NormalizesNullToolCallArgumentsBeforeProviderCall()
    {
        var chatClient = new RecordingChatClient("<summary>ok</summary>");
        var runner = new MaintenanceForkRunner(chatClient);
        var originalCall = new FunctionCallContent("call-1", "GetStatus", null);
        var snapshot = PromptRequestSnapshot.Capture(
            [
                new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
                [
                    new TextContent("checking"),
                    originalCall
                ]),
                new ChatMessage(ChatRole.Tool, (IList<AIContent>)
                [
                    new FunctionResultContent("call-1", "ok")
                ])
            ],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        var call = chatClient.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Single();
        var arguments = Assert.IsAssignableFrom<IDictionary<string, object?>>(call.Arguments);
        Assert.Empty(arguments);
        Assert.Null(originalCall.Arguments);
    }

    [Fact]
    public async Task RunAsync_AnthropicCacheShapingMarksSystemSnapshotPrefixAndSkipsForkTail()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chatClient = new RecordingChatClient("<summary>important bits</summary>");
        var runner = new MaintenanceForkRunner(
            new AnthropicPromptCachingChatClient(chatClient, "claude-haiku-4-5"),
            collector,
            cacheOptions: new MaintenanceForkCacheOptions(
                ModelProviderProtocols.Anthropic,
                new AppConfig.PromptCachingConfig(),
                "claude-haiku-4-5"));
        var snapshot = PromptRequestSnapshot.Capture(
            [
                new ChatMessage(ChatRole.User, "stable prefix"),
                new ChatMessage(ChatRole.Assistant, "stable assistant")
            ],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "claude-haiku-4-5"
            },
            providerId: "anthropic",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal(4, chatClient.Messages.Count);
        Assert.Equal(ChatRole.System, chatClient.Messages[0].Role);
        AssertAnthropicCacheControl(Assert.Single(chatClient.Messages[0].Contents));
        AssertNoAnthropicCacheControl(Assert.Single(chatClient.Messages[1].Contents));
        AssertAnthropicCacheControl(Assert.Single(chatClient.Messages[2].Contents));
        AssertNoAnthropicCacheControl(Assert.Single(chatClient.Messages[3].Contents));
        Assert.Null(chatClient.Options?.Instructions);

        var request = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"cacheShapeApplied\":true", request.MetadataJson);
        Assert.Contains("\"cacheShapeKind\":\"anthropic-cache-control\"", request.MetadataJson);
        Assert.Contains("\"cacheMarkerSource\":\"system+snapshot_prefix\"", request.MetadataJson);
        Assert.Contains("\"cacheStateKeyKind\":\"maintenanceFork\"", request.MetadataJson);
        Assert.Contains("\"cacheStateKeyHash\":", request.MetadataJson);
        Assert.Contains("\"cacheWriteMode\":\"readOnlyPrefix\"", request.MetadataJson);
        Assert.Contains("\"tailCacheWriteSkipped\":true", request.MetadataJson);
        Assert.Contains("\"providerImplicitCacheWrite\":false", request.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_AnthropicOpus48ForkSerializesAdaptiveThinking()
    {
        var handler = new AnthropicCaptureHandler("<summary>important bits</summary>");
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        var config = CreateReasoningConfig();
        var runtime = Runtime(ModelProviderProtocols.Anthropic, "claude-opus-4-8");
        var chatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            new AnthropicThinkingChatClient(
                anthropicClient.AsIChatClient("claude-opus-4-8"),
                ModelThinkingAdapterResolver.ResolveAnthropicThinkingAdapter(
                    config, runtime.EndPoint, runtime.Model),
                runtime.Model,
                defaultReasoning: config.Reasoning.ToOptions(),
                useDefaultReasoning: false),
            config,
            runtime,
            useDefaultReasoning: false);
        var runner = new MaintenanceForkRunner(chatClient);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "stable prefix")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "claude-opus-4-8",
                Reasoning = config.Reasoning.ToOptions()
            },
            providerId: "anthropic",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("summarized", root.GetProperty("thinking").GetProperty("display").GetString());
        Assert.False(root.GetProperty("thinking").TryGetProperty("budget_tokens", out _));
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task RunAsync_RecordsToolCallOnlyResponseMetadata()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var toolCallMessage = new ChatMessage(
            ChatRole.Assistant,
            (IList<AIContent>)
            [
                new FunctionCallContent(
                    "call_1",
                    "ReadFile",
                    new Dictionary<string, object?> { ["path"] = "README.md" })
            ]);
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient(new ChatResponse(toolCallMessage)),
            collector);
        var snapshot = CreateSnapshot();

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal("tool_call_without_text", result.FallbackReason);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains("\"fallbackReason\":\"tool_call_without_text\"", response.MetadataJson);
        Assert.Contains("\"type\":\"function_call\"", response.MetadataJson);
        Assert.Contains("\"name\":\"ReadFile\"", response.MetadataJson);
        Assert.Contains("\"callId\":\"call_1\"", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_UserCancellationRecordsTerminalResponseAndRethrows()
    {
        var store = new TraceStore();
        using var cts = new CancellationTokenSource();
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient(new OperationCanceledException(cts.Token)),
            new TraceCollector(store));
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(CreateSnapshot(), CompactionTask(), cts.Token));

        AssertTerminalFallback(store, "cancelled");
    }

    private static MaintenanceForkTask CompactionTask() =>
        new(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context.");

    private static void AssertTerminalFallback(TraceStore store, string reason)
    {
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains($"\"fallbackReason\":\"{reason}\"", response.MetadataJson);
    }

    private static PromptRequestSnapshot CreateSnapshot() =>
        PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

    private static AppConfig CreateReasoningConfig(string model = "claude-opus-4-8") =>
        new()
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = model  } },
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ModelReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        };

    private static EffectiveModelRuntime Runtime(
        string protocol,
        string model,
        string endpoint = "http://localhost") =>
        new(
            ProviderId: protocol,
            Model: model,
            Protocol: protocol,
            DisplayName: protocol,
            ApiKey: "test-key",
            EndPoint: endpoint,
            NetworkTimeoutSeconds: 60,
            MaxOutputTokens: 64_000,
            Capabilities: ModelProviderCapabilities.ForProtocol(protocol));

    private static void AssertAnthropicCacheControl(AIContent content)
    {
        Assert.NotNull(content.AdditionalProperties);
        Assert.True(content.AdditionalProperties!.ContainsKey("anthropic:cache_control"));
    }

    private static void AssertNoAnthropicCacheControl(AIContent content)
    {
        Assert.False(content.AdditionalProperties?.ContainsKey("anthropic:cache_control") ?? false);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly ChatResponse? _response;
        private readonly Exception? _exception;

        public RecordingChatClient(string responseText)
            : this(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)))
        {
        }

        public RecordingChatClient(ChatResponse response)
        {
            _response = response;
        }

        public RecordingChatClient(Exception exception)
        {
            _exception = exception;
        }

        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            if (_exception is not null)
                throw _exception;

            return Task.FromResult(_response!);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class AnthropicCaptureHandler(string responseText) : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                        "id": "msg_maintenance_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-opus-4-8",
                        "content": [{
                            "type": "text",
                            "text": {{JsonSerializer.Serialize(responseText)}}
                        }],
                        "stop_reason": "end_turn",
                        "usage": {
                            "input_tokens": 10,
                            "output_tokens": 1
                        }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
