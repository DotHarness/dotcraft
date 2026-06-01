using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context;

public sealed class MaintenanceForkRunnerTests
{
    [Fact]
    public async Task RunAsync_ReusesSnapshotPrefixAndAppendsMaintenanceTask()
    {
        var chatClient = new RecordingChatClient("<summary>important bits</summary>");
        var runner = new MaintenanceForkRunner(chatClient);
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
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        Assert.Equal("<summary>important bits</summary>", result.Text);
        Assert.Equal(["user:start", "assistant:working"], chatClient.Messages.Take(2).Select(m => $"{m.Role}:{m.Text}"));
        Assert.Equal(3, chatClient.Messages.Count);
        Assert.Equal(ChatRole.User, chatClient.Messages[^1].Role);
        Assert.Contains("<system-reminder>", chatClient.Messages[^1].Text);
        Assert.Contains("## Maintenance Task", chatClient.Messages[^1].Text);
        Assert.Contains("Task: context_compaction", chatClient.Messages[^1].Text);
        Assert.DoesNotContain("<dotcraft_maintenance_task>", chatClient.Messages[^1].Text);
        Assert.Contains("Summarize older context.", chatClient.Messages[^1].Text);
        Assert.Equal("stable base", chatClient.Options?.Instructions);
        Assert.Equal("gpt-test", chatClient.Options?.ModelId);
        Assert.True(chatClient.Options?.AllowMultipleToolCalls);
        var capturedTool = Assert.Single(chatClient.Options?.Tools ?? []);
        Assert.Equal("ReadFile", capturedTool.Name);
    }

    [Fact]
    public async Task RunAsync_RecordsMaintenanceRequestAndTextResponse()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chatClient = new RecordingChatClient("<summary>important bits</summary>");
        var runner = new MaintenanceForkRunner(chatClient, collector);
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                Tools = [tool]
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        var events = store.GetEvents("thread_1");
        var request = Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        var response = Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains("Summarize older context.", request.Content);
        Assert.Equal("<summary>important bits</summary>", response.Content);
        Assert.Contains("\"providerId\":\"provider-test\"", request.MetadataJson);
        Assert.Contains("\"toolCount\":1", request.MetadataJson);
        Assert.Contains("\"fallbackReason\":null", response.MetadataJson);
        Assert.DoesNotContain(events, e => e.Type == TraceEventType.Request);
        Assert.DoesNotContain(events, e => e.Type == TraceEventType.Response);
        Assert.DoesNotContain(events, e => e.Type == TraceEventType.ToolCallStarted);
        Assert.DoesNotContain(events, e => e.Type == TraceEventType.TokenUsage);
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkResponse);
    }

    [Fact]
    public async Task RunAsync_WithToolExecution_RecordsGenericToolAndTokenTrace()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chatClient = new ToolLoopChatClient(
            toolName: "ReadFile",
            callId: "call-1",
            arguments: new Dictionary<string, object?> { ["path"] = "memory/MEMORY.md" });
        var runner = new MaintenanceForkRunner(chatClient, collector);
        var tool = AIFunctionFactory.Create(ReadFile, name: "ReadFile");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                Tools = [tool]
            },
            providerId: "provider-test",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.MemoryConsolidation, "Consolidate memory."),
            messagesBeforeTask: null,
            new MaintenanceForkToolExecutionOptions(_ => ModeToolPolicyDecision.Allow),
            CancellationToken.None);

        Assert.Null(result.FallbackReason);
        Assert.Equal("""{"status":"updated"}""", result.Text);
        var events = store.GetEvents("thread_1");
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Single(events, e => e.Type == TraceEventType.Request);
        Assert.Single(events, e => e.Type == TraceEventType.Response);

        var started = Assert.Single(events, e => e.Type == TraceEventType.ToolCallStarted);
        var completed = Assert.Single(events, e => e.Type == TraceEventType.ToolCallCompleted);
        Assert.Equal("ReadFile", started.ToolName);
        Assert.Equal("ReadFile", completed.ToolName);
        Assert.Equal("memory file", completed.ToolResult);

        var usage = Assert.Single(events, e => e.Type == TraceEventType.TokenUsage);
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(64, usage.CachedInputTokens);
        Assert.Equal(12, usage.CacheWriteInputTokens);

        var request = Assert.Single(events, e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"toolCount\":1", request.MetadataJson);
        Assert.Contains("\"toolFingerprint\":\"sha256:", request.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_WithToolExecution_RecordsPolicyDeniedToolResult()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var invoked = false;
        var execTool = AIFunctionFactory.Create(() =>
        {
            invoked = true;
            return "executed";
        }, name: "Exec");
        var chatClient = new ToolLoopChatClient(
            toolName: "Exec",
            callId: "call-denied",
            arguments: new Dictionary<string, object?> { ["command"] = "dotnet test" });
        var runner = new MaintenanceForkRunner(chatClient, collector);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                Tools = [execTool]
            },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.MemoryConsolidation, "Consolidate memory."),
            messagesBeforeTask: null,
            new MaintenanceForkToolExecutionOptions(_ => ModeToolPolicyDecision.DenyRecoverable("MODE_POLICY_DENIED")),
            CancellationToken.None);

        Assert.False(invoked);
        var events = store.GetEvents("thread_1");
        var started = Assert.Single(
            events,
            e => e.Type == TraceEventType.ToolCallStarted);
        var completed = Assert.Single(
            events,
            e => e.Type == TraceEventType.ToolCallCompleted);
        Assert.Equal("Exec", started.ToolName);
        Assert.Equal("call-denied", started.CallId);
        Assert.Equal("Exec", completed.ToolName);
        Assert.Contains("MODE_POLICY_DENIED", completed.ToolResult);
    }

    [Fact]
    public async Task RunAsync_WithToolExecution_UsesIsolatedTraceCallState()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.ResetCallState("thread_1");
        TracingChatClient.CurrentSessionKey = "thread_1";
        try
        {
            var warmup = new TracingChatClient(
                new DirectToolTraceChatClient("call-shared", "ReadFile"),
                collector);
            await foreach (var _ in warmup.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "warmup")]))
            {
            }

            var maintenance = new ToolLoopChatClient(
                toolName: "ReadFile",
                callId: "call-shared",
                arguments: new Dictionary<string, object?> { ["path"] = "memory/MEMORY.md" });
            var runner = new MaintenanceForkRunner(maintenance, collector);
            var tool = AIFunctionFactory.Create(ReadFile, name: "ReadFile");
            var snapshot = PromptRequestSnapshot.Capture(
                [new ChatMessage(ChatRole.User, "start")],
                new ChatOptions
                {
                    Instructions = "stable base",
                    ModelId = "gpt-test",
                    Tools = [tool]
                },
                mode: "agent",
                threadId: "thread_1",
                turnId: "turn_1");

            await runner.RunAsync(
                snapshot,
                new MaintenanceForkTask(MaintenanceForkTaskKind.MemoryConsolidation, "Consolidate memory."),
                messagesBeforeTask: null,
                new MaintenanceForkToolExecutionOptions(_ => ModeToolPolicyDecision.Allow),
                CancellationToken.None);
        }
        finally
        {
            TracingChatClient.ResetCallState("thread_1");
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }

        var events = store.GetEvents("thread_1");
        Assert.Equal(2, events.Count(e => e.Type == TraceEventType.ToolCallStarted && e.CallId == "call-shared"));
        Assert.Equal(2, events.Count(e => e.Type == TraceEventType.ToolCallCompleted && e.CallId == "call-shared"));
    }

    [Fact]
    public async Task RunAsync_RecordsEmptyResponseFallback()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var runner = new MaintenanceForkRunner(new RecordingChatClient(""), collector);
        var snapshot = CreateSnapshot();

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal("empty_response", result.FallbackReason);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Equal("(empty)", response.Content);
        Assert.Contains("\"fallbackReason\":\"empty_response\"", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_DoesNotPreflightRejectOversizedSnapshot()
    {
        var chatClient = new RecordingChatClient("<summary>provider was called</summary>");
        var runner = new MaintenanceForkRunner(chatClient);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1",
            estimatedInputTokens: 10_000);

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        Assert.Equal("<summary>provider was called</summary>", result.Text);
        Assert.NotEmpty(chatClient.Messages);
    }

    [Fact]
    public async Task RunAsync_RecordsEstimatedInputWithoutPreflightMetadata()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient("<summary>provider was called</summary>"),
            collector);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Instructions = "stable base", ModelId = "gpt-test" },
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1",
            estimatedInputTokens: 10_000);

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        var request = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"estimatedInputTokens\":", request.MetadataJson);
        Assert.DoesNotContain("effectiveBudgetTokens", request.MetadataJson);
        Assert.DoesNotContain("preflightRejected", request.MetadataJson);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains("\"fallbackReason\":null", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_MapsContextOverflowExceptionToSnapshotTooLargeFallback()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var error = new InvalidOperationException(
            "Status Code: BadRequest {\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"Your input exceeds the context window of this model.\"}}");
        var runner = new MaintenanceForkRunner(new RecordingChatClient(error), collector);
        var snapshot = CreateSnapshot();

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal(MaintenanceForkFallbackReasons.SnapshotTooLarge, result.FallbackReason);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Contains($"\"fallbackReason\":\"{MaintenanceForkFallbackReasons.SnapshotTooLarge}\"", response.MetadataJson);
        Assert.Contains("context_length_exceeded", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_NormalizesNullToolCallArgumentsBeforeProviderCall()
    {
        var chatClient = new RecordingChatClient("<summary>ok</summary>");
        var runner = new MaintenanceForkRunner(chatClient);
        var snapshot = PromptRequestSnapshot.Capture(
            [
                new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
                [
                    new TextContent("checking"),
                    new FunctionCallContent("call-1", "GetStatus", null)
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
    }

    [Fact]
    public async Task RunAsync_AnthropicCacheShapingMarksSystemSnapshotPrefixAndForkTail()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chatClient = new RecordingChatClient("<summary>important bits</summary>");
        var runner = new MaintenanceForkRunner(
            chatClient,
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
        Assert.Contains("Maintenance Task", chatClient.Messages[3].Text);
        AssertAnthropicCacheControl(Assert.Single(chatClient.Messages[3].Contents));
        Assert.Null(chatClient.Options?.Instructions);

        var request = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkRequest);
        Assert.Contains("\"cacheShapeApplied\":true", request.MetadataJson);
        Assert.Contains("\"cacheShapeKind\":\"anthropic-cache-control\"", request.MetadataJson);
        Assert.Contains("\"cacheMarkerSource\":\"system+snapshot_prefix+fork_tail\"", request.MetadataJson);
        Assert.Contains("\"cacheStateKeyKind\":\"maintenanceFork\"", request.MetadataJson);
        Assert.Contains("\"cacheStateKeyHash\":", request.MetadataJson);
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
        var chatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            anthropicClient.AsIChatClient("claude-opus-4-8"),
            config,
            Runtime(ModelProviderProtocols.Anthropic, "claude-opus-4-8"),
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
    public async Task RunAsync_MimoForkAppliesDeepThinkingFromExplicitSnapshotReasoning()
    {
        var config = CreateReasoningConfig(model: "mimo-v2.5-pro");
        var capture = new RecordingChatClient("<summary>important bits</summary>");
        var chatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            capture,
            config,
            Runtime(ModelProviderProtocols.OpenAIChatCompletions, "mimo-v2.5-pro", "https://api.openai-compatible.test/v1"),
            useDefaultReasoning: false);
        var runner = new MaintenanceForkRunner(chatClient);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "stable prefix")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "mimo-v2.5-pro",
                Reasoning = config.Reasoning.ToOptions()
            },
            providerId: "mimo",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Null(result.FallbackReason);
        var raw = Assert.IsType<OpenAI.Chat.ChatCompletionOptions>(
            capture.Options!.RawRepresentationFactory!(capture));
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_AnthropicToolExecution_UpdatesForkLocalTailCachePoints()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var chatClient = new ToolLoopChatClient(
            toolName: "ReadFile",
            callId: "call-1",
            arguments: new Dictionary<string, object?> { ["path"] = "memory/MEMORY.md" });
        var runner = new MaintenanceForkRunner(
            chatClient,
            collector,
            cacheOptions: new MaintenanceForkCacheOptions(
                ModelProviderProtocols.Anthropic,
                new AppConfig.PromptCachingConfig(),
                "claude-haiku-4-5"));
        var tool = AIFunctionFactory.Create(ReadFile, name: "ReadFile");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "stable prefix")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "claude-haiku-4-5",
                Tools = [tool]
            },
            providerId: "anthropic",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.MemoryConsolidation, "Consolidate memory."),
            messagesBeforeTask: null,
            new MaintenanceForkToolExecutionOptions(_ => ModeToolPolicyDecision.Allow),
            CancellationToken.None);

        Assert.Equal(2, chatClient.Calls.Count);
        var continuation = chatClient.Calls[1];
        AssertAnthropicCacheControl(Assert.Single(continuation[0].Contents));
        AssertAnthropicCacheControl(Assert.Single(continuation[1].Contents));
        AssertAnthropicCacheControl(Assert.Single(continuation[2].Contents));
        var toolResult = Assert.IsType<FunctionResultContent>(Assert.Single(continuation[^1].Contents));
        AssertAnthropicCacheControl(toolResult);

        var promptCachePoints = store.GetEvents("thread_1")
            .Where(e => e.Type == TraceEventType.PromptCachePoint)
            .ToList();
        Assert.Equal(2, promptCachePoints.Count);
        Assert.Contains("\"Role\":\"tool\"", promptCachePoints[^1].MetadataJson);
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
    public async Task RunAsync_RecordsExceptionFallback()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient(new InvalidOperationException("provider failed")),
            collector);
        var snapshot = CreateSnapshot();

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal("provider failed", result.FallbackReason);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Equal("(empty)", response.Content);
        Assert.Contains("\"fallbackReason\":\"provider failed\"", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_ProviderTimeoutCancellationRecordsTerminalResponse()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient(new OperationCanceledException("timeout")),
            collector);
        var snapshot = CreateSnapshot();

        var result = await runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."));

        Assert.Equal("provider_timeout", result.FallbackReason);
        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Equal("(empty)", response.Content);
        Assert.Contains("\"fallbackReason\":\"provider_timeout\"", response.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_UserCancellationRecordsTerminalResponseAndRethrows()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        using var cts = new CancellationTokenSource();
        var runner = new MaintenanceForkRunner(
            new RecordingChatClient(new OperationCanceledException(cts.Token)),
            collector);
        var snapshot = CreateSnapshot();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(
            snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize older context."),
            cts.Token));

        var response = Assert.Single(
            store.GetEvents("thread_1"),
            e => e.Type == TraceEventType.MaintenanceForkResponse);
        Assert.Equal("(empty)", response.Content);
        Assert.Contains("\"fallbackReason\":\"cancelled\"", response.MetadataJson);
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
            Model = model,
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ReasoningEffort.High,
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
            IsImplicit: false,
            Capabilities: ModelProviderCapabilities.ForProtocol(protocol));

    private static string ReadFile(string path) => "memory file";

    private static void AssertAnthropicCacheControl(AIContent content)
    {
        Assert.NotNull(content.AdditionalProperties);
        Assert.True(content.AdditionalProperties!.ContainsKey("anthropic:cache_control"));
        Assert.False(content.AdditionalProperties.ContainsKey("cache_control"));
    }

    private static void AssertNoAnthropicCacheControl(AIContent content)
    {
        Assert.False(content.AdditionalProperties?.ContainsKey("anthropic:cache_control") ?? false);
        Assert.False(content.AdditionalProperties?.ContainsKey("cache_control") ?? false);
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

    private sealed class ToolLoopChatClient(
        string toolName,
        string callId,
        IDictionary<string, object?> arguments) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"status":"updated"}""")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)
                [
                    new FunctionCallContent(callId, toolName, new Dictionary<string, object?>(arguments))
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, """{"status":"updated"}""");
                yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)
                [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 100,
                        OutputTokenCount = 20,
                        CachedInputTokenCount = 64
                    })
                    {
                        AdditionalProperties = new AdditionalPropertiesDictionary
                        {
                            ["cache_creation_input_tokens"] = 12
                        }
                    }
                ]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DirectToolTraceChatClient(string callId, string toolName) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)
            [
                new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["path"] = "a.txt" })
            ]);
            yield return new ChatResponseUpdate(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent(callId, "warmup result")
            ]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
