using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using AnthropicCacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicPromptCachingChatClientTests : IDisposable
{
    private const string AnthropicCacheControlKey = "anthropic:cache_control";
    private const string WireCacheControlKey = "cache_control";

    private readonly IDisposable _policy = PromptCachePolicyScope.Use();

    public void Dispose() => _policy.Dispose();

    private static IDisposable UsePolicy(bool enabled = true, string? ttl = null) =>
        PromptCachePolicyScope.Use(enabled, ttl);

    private static IDisposable UseDiagnostics(IModelRuntimeDiagnostics diagnostics) =>
        PromptCachePolicyScope.UseDiagnostics(diagnostics);

    [Fact]
    public void Prepare_ForClaudeModel_MarksPrefixAndLatestUser()
    {
        var client = CreateClient("anthropic/claude-opus-4-1");
        var system = new ChatMessage(ChatRole.System, "stable system prompt");
        var user = new ChatMessage(ChatRole.User, "hello");

        var prepared = client.Prepare([system, user], null);

        var systemText = Assert.IsType<TextContent>(Assert.Single(prepared.Messages[0].Contents));
        AssertAnthropicCacheControl(systemText, expectedTtl: null);
        var userText = AssertLastTextContent(prepared.Messages[1]);
        AssertAnthropicCacheControl(userText, expectedTtl: null);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
    }

    [Fact]
    public void Prepare_WithInstructions_MarksPrefixAndLatestUser()
    {
        var client = CreateClient("claude-3-5-sonnet");
        var options = new ChatOptions { Instructions = "stable system prompt" };
        var user = new ChatMessage(ChatRole.User, "hello");

        var prepared = client.Prepare([user], options);

        Assert.Null(prepared.Options!.Instructions);
        var system = Assert.Single(prepared.Messages, m => m.Role == ChatRole.System);
        var systemText = Assert.IsType<TextContent>(Assert.Single(system.Contents));
        Assert.Equal("stable system prompt", systemText.Text);
        AssertAnthropicCacheControl(systemText, expectedTtl: null);

        var userText = AssertLastTextContent(prepared.Messages.Last());
        AssertAnthropicCacheControl(userText, expectedTtl: null);
        Assert.Equal("stable system prompt", options.Instructions);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
    }

    [Fact]
    public void Prepare_WithTextContentToolResult_MarksToolResultWithoutMutatingOriginal()
    {
        var client = CreateClient("claude-opus-4-1");
        var toolResultContents = (IList<AIContent>)[new TextContent("file contents")];
        var originalResult = new FunctionResultContent("call_1", toolResultContents);
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[originalResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            tool
        ], null);

        Assert.NotSame(tool, prepared.Messages[1]);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[1].Contents));
        Assert.NotSame(originalResult, result);
        Assert.Equal("call_1", result.CallId);
        Assert.Same(toolResultContents, result.Result);
        AssertAnthropicCacheControl(result, expectedTtl: null);
        Assert.Null(originalResult.AdditionalProperties);
    }

    [Fact]
    public void Prepare_WithMixedToolResult_DoesNotMarkToolResult()
    {
        var client = CreateClient("claude-opus-4-1");
        var originalResult = new FunctionResultContent(
            "call_1",
            (IList<AIContent>)[
                new TextContent("text"),
                new DataContent(new BinaryData([1, 2, 3]), "image/png")
            ]);
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[originalResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            tool
        ], null);

        Assert.Same(tool, prepared.Messages[1]);
        Assert.Null(originalResult.AdditionalProperties);
    }

    [Fact]
    public void Prepare_WhenDisabled_LeavesMessagesAndOptionsUnchanged()
    {
        using var disabled = UsePolicy(enabled: false);
        var client = new AnthropicPromptCachingChatClient(new CaptureChatClient(), "claude-opus-4-1");
        var options = new ChatOptions { Instructions = "stable system prompt" };
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };

        var prepared = client.Prepare(messages, options);

        Assert.Same(messages, prepared.Messages);
        Assert.Same(options, prepared.Options);
    }

    [Fact]
    public void Prepare_AnthropicNative_MarksTextAndToolResultWithSdkCacheControl()
    {
        var client = CreateClient("claude-opus-4-1");
        var firstResult = new FunctionResultContent("call_1", "first");
        var secondResult = new FunctionResultContent("call_2", "second")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["existing"] = true
            },
            Exception = new InvalidOperationException("boom")
        };
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[firstResult, secondResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant response"),
            tool
        ], null);

        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[1]), expectedTtl: null);
        Assert.Equal(3, prepared.Messages.Count);
        Assert.Equal(ChatRole.Tool, prepared.Messages[2].Role);
        Assert.Equal(2, prepared.Messages[2].Contents.Count);
        var firstPrepared = Assert.IsType<FunctionResultContent>(prepared.Messages[2].Contents[0]);
        var secondPrepared = Assert.IsType<FunctionResultContent>(prepared.Messages[2].Contents[1]);
        Assert.Same(firstResult, firstPrepared);
        Assert.NotSame(secondResult, secondPrepared);
        Assert.Equal("call_2", secondPrepared.CallId);
        Assert.Equal("second", secondPrepared.Result);
        Assert.Same(secondResult.Exception, secondPrepared.Exception);
        Assert.True((bool)secondPrepared.AdditionalProperties!["existing"]!);
        AssertAnthropicCacheControl(secondPrepared, expectedTtl: null);
        Assert.False(secondResult.AdditionalProperties?.ContainsKey(AnthropicCacheControlKey) ?? false);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("1h", "1h")]
    [InlineData("5m", "5m")]
    public void Prepare_AnthropicNative_WithTtl_AddsSdkTtl(string configuredTtl, string? expectedTtl)
    {
        using var configured = UsePolicy(ttl: string.IsNullOrEmpty(configuredTtl) ? null : configuredTtl);
        var client = CreateClient("claude-opus-4-1");

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], null);

        var text = AssertLastTextContent(Assert.Single(prepared.Messages));
        AssertAnthropicCacheControl(text, expectedTtl);
    }

    [Fact]
    public void Prepare_AnthropicNative_WithInstructions_KeepsStableSystemBreakpointAndTail()
    {
        var client = CreateClient("claude-opus-4-1");

        var prepared = client.Prepare(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Instructions = "stable system prompt" });

        Assert.Equal(2, prepared.Messages.Count);
        Assert.Equal(ChatRole.System, prepared.Messages[0].Role);
        Assert.Equal(ChatRole.User, prepared.Messages[1].Role);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[1]), expectedTtl: null);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
        Assert.Equal(1, prepared.LlmCallIndex);
    }

    [Fact]
    public async Task Prepare_AnthropicNative_WithRememberedTail_PreservesSystemAndCapsAtFourBreakpoints()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);
        var options = new ChatOptions { Instructions = "stable system prompt" };

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_1", "tool one")])
        ], options);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_1", "tool one")]),
            new ChatMessage(ChatRole.Assistant, "assistant two"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_2", "tool two")])
        ], options);

        Assert.True(prepared.PendingCachePoints.Count <= 4);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Role == ChatRole.System.Value);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Role == ChatRole.Tool.Value && p.Trace.Latest);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Remembered);
        Assert.Equal(2, prepared.LlmCallIndex);
    }

    [Fact]
    public void Prepare_PromptCacheDiagnosticHashesReasoningAndFullToolSchema()
    {
        var client = CreateClient("claude-opus-4-1");
        var readV1 = AIFunctionFactory.Create(
            () => "ok",
            name: "ReadFile",
            description: "Read a file.");
        var readV2 = AIFunctionFactory.Create(
            () => "ok",
            name: "ReadFile",
            description: "Read a project file.");
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };

        var first = client.Prepare(messages, new ChatOptions
        {
            Tools = [readV1],
            Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        });
        var schemaChanged = client.Prepare(messages, new ChatOptions
        {
            Tools = [readV2],
            Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        });
        var reasoningChanged = client.Prepare(messages, new ChatOptions
        {
            Tools = [readV1],
            Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.Low,
                Output = ReasoningOutput.Full
            }
        });

        Assert.NotNull(first.PromptCacheDiagnostic);
        Assert.Equal(
            PromptRequestFingerprints.ComputeToolFingerprint([readV1]),
            first.PromptCacheDiagnostic.ToolSchemaHash);
        Assert.NotEqual(
            first.PromptCacheDiagnostic.ToolSchemaHash,
            schemaChanged.PromptCacheDiagnostic?.ToolSchemaHash);
        Assert.NotEqual(
            first.PromptCacheDiagnostic.ReasoningHash,
            reasoningChanged.PromptCacheDiagnostic?.ReasoningHash);
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_SerializesUserAndToolResultCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicNativeHttpClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "result text")
            ])
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var messages = root.GetProperty("messages");
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        var userText = messages[0].GetProperty("content")[0];
        Assert.Equal("text", userText.GetProperty("type").GetString());
        Assert.Equal("hello", userText.GetProperty("text").GetString());
        AssertWireCacheControl(userText, expectedTtl: null);

        var toolResult = FindFirstContentBlock(root, "tool_result");
        Assert.Equal("call_1", toolResult.GetProperty("tool_use_id").GetString());
        AssertWireCacheControl(toolResult, expectedTtl: null);
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_WithParallelToolResultsKeepsGroupedToolResultMessage()
    {
        await AssertAnthropicGroupedToolResultWireAsync(handler => CreateAnthropicNativeHttpClient(handler));
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicBetaNative_SerializesUserAndToolResultCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicBetaNativeHttpClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "result text")
            ])
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var userText = root.GetProperty("messages")[0].GetProperty("content")[0];
        Assert.Equal("text", userText.GetProperty("type").GetString());
        AssertWireCacheControl(userText, expectedTtl: null);

        var toolResult = FindFirstContentBlock(root, "tool_result");
        Assert.Equal("call_1", toolResult.GetProperty("tool_use_id").GetString());
        AssertWireCacheControl(toolResult, expectedTtl: null);
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicBetaNative_WithParallelToolResultsKeepsGroupedToolResultMessage()
    {
        await AssertAnthropicGroupedToolResultWireAsync(handler => CreateAnthropicBetaNativeHttpClient(handler));
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_SerializesSystemCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        using var longCache = UsePolicy(ttl: "1h");
        var client = CreateAnthropicNativeHttpClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "stable system prompt")
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var system = document.RootElement.GetProperty("system")[0];
        Assert.Equal("text", system.GetProperty("type").GetString());
        Assert.Equal("stable system prompt", system.GetProperty("text").GetString());
        AssertWireCacheControl(system, expectedTtl: "1h");
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicBetaNative_SerializesSystemCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        using var longCache = UsePolicy(ttl: "1h");
        var client = CreateAnthropicBetaNativeHttpClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "stable system prompt")
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var system = document.RootElement.GetProperty("system")[0];
        Assert.Equal("text", system.GetProperty("type").GetString());
        Assert.Equal("stable system prompt", system.GetProperty("text").GetString());
        AssertWireCacheControl(system, expectedTtl: "1h");
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_WithInstructionsSerializesSystemAndTailCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicNativeHttpClient(handler);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Instructions = "stable system prompt" });

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var system = root.GetProperty("system")[0];
        Assert.Equal("stable system prompt", system.GetProperty("text").GetString());
        AssertWireCacheControl(system, expectedTtl: null);

        var userText = root.GetProperty("messages")[0].GetProperty("content")[0];
        Assert.Equal("hello", userText.GetProperty("text").GetString());
        AssertWireCacheControl(userText, expectedTtl: null);
    }

    [Fact]
    public void Prepare_DoesNotMutateOriginalMessagesOrContents()
    {
        var client = CreateClient("claude-opus-4-1");
        var text = new TextContent("hello");
        var user = new ChatMessage(ChatRole.User, (IList<AIContent>)[text]);

        var prepared = client.Prepare([user], null);

        Assert.NotSame(user, prepared.Messages[0]);
        Assert.Same(text, user.Contents[0]);
        Assert.Null(text.AdditionalProperties);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsesSamePreparationLogic()
    {
        var capture = new CaptureChatClient();
        var client = new AnthropicPromptCachingChatClient(capture, "claude-opus-4-1");

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
        }

        var text = AssertLastTextContent(capture.LastMessages![0]);
        AssertAnthropicCacheControl(text, expectedTtl: null);
    }

    [Fact]
    public async Task RollingBreakpoints_NewUserTurnPreservesPreviousTailBridge()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.User, "next request")
        ]);

        AssertAnthropicCacheControl(AssertLastTextContent(capture.LastMessages![1]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(capture.LastMessages![2]), expectedTtl: null);
    }

    [Fact]
    public async Task RollingBreakpoints_CompactedPrefixMarksSystemAndLatestUser()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "compacted summary"),
            new ChatMessage(ChatRole.User, "next request")
        ]);

        var systemText = AssertLastTextContent(capture.LastMessages![0]);
        AssertAnthropicCacheControl(systemText, expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(capture.LastMessages![1]), expectedTtl: null);
    }

    [Fact]
    public async Task RollingBreakpoints_ContinuousEmptyAssistantToolLoopsKeepBridgeAndLatestToolResult()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);
        var firstTool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_1", "first result")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            firstTool
        ]);

        var secondTool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_2", "second result")
        ]);
        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            firstTool,
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_2", "ReadFile", new Dictionary<string, object?>())
            ]),
            secondTool
        ]);

        var restoredTool = Assert.IsType<FunctionResultContent>(Assert.Single(capture.LastMessages![2].Contents));
        var latestTool = Assert.IsType<FunctionResultContent>(Assert.Single(capture.LastMessages![4].Contents));
        AssertAnthropicCacheControl(restoredTool, expectedTtl: null);
        AssertAnthropicCacheControl(latestTool, expectedTtl: null);
    }

    [Fact]
    public async Task GetResponseAsync_WhenTraceCollectorProvided_RecordsPromptCachePointSummaries()
    {
        const string sessionKey = "trace-cache";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        using var diagnostics = UseDiagnostics(collector);
        var client = CreateClient(
            "claude-opus-4-1",
            capture: new CaptureChatClient(),
            sessionKey: sessionKey);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "secret prompt"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "secret tool result")
            ])
        ]);

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.PromptCachePoint);
        Assert.DoesNotContain("secret prompt", evt.MetadataJson);
        Assert.DoesNotContain("secret tool result", evt.MetadataJson);

        using var document = JsonDocument.Parse(evt.MetadataJson!);
        var root = document.RootElement;
        Assert.Equal(sessionKey, root.GetProperty("sessionKey").GetString());
        Assert.Equal("claude-opus-4-1", root.GetProperty("model").GetString());
        Assert.Equal(1, evt.LlmCallIndex);
        Assert.Equal(1, root.GetProperty("llmCallIndex").GetInt32());
        var points = root.GetProperty("points");
        var tail = points[points.GetArrayLength() - 1];
        Assert.Equal("tool", tail.GetProperty("Role").GetString());
        Assert.Equal("function_result", tail.GetProperty("ContentKind").GetString());
        Assert.True(tail.GetProperty("Latest").GetBoolean());
    }

    [Fact]
    public async Task GetResponseAsync_WithJsonElementToolResult_RecordsToolPromptCachePoint()
    {
        const string sessionKey = "trace-json-cache";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        using var diagnostics = UseDiagnostics(collector);
        var client = CreateClient(
            "claude-opus-4-1",
            capture: new CaptureChatClient(),
            sessionKey: sessionKey);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "secret prompt"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "RequestUserInput", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent(
                    "call_1",
                    JsonElementFrom("""{"answers":{"question_1":{"answers":["secret tool result"]}}}"""))
            ])
        ]);

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.PromptCachePoint);
        Assert.DoesNotContain("secret prompt", evt.MetadataJson);
        Assert.DoesNotContain("secret tool result", evt.MetadataJson);

        using var document = JsonDocument.Parse(evt.MetadataJson!);
        var points = document.RootElement.GetProperty("points");
        var tail = points[points.GetArrayLength() - 1];
        Assert.Equal("tool", tail.GetProperty("Role").GetString());
        Assert.Equal("function_result", tail.GetProperty("ContentKind").GetString());
        Assert.True(tail.GetProperty("Latest").GetBoolean());
    }

    [Fact]
    public async Task GetResponseAsync_WhenDisabled_DoesNotRecordPromptCachePointTrace()
    {
        var disabledStore = new TraceStore();
        using var disabled = UsePolicy(enabled: false);
        using var diagnostics = UseDiagnostics(new TraceCollector(disabledStore));
        var disabledClient = new AnthropicPromptCachingChatClient(
            new CaptureChatClient(),
            "claude-opus-4-1",
            () => "disabled");
        await disabledClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.DoesNotContain(disabledStore.GetEvents("disabled"), e => e.Type == TraceEventType.PromptCachePoint);
    }

    [Fact]
    public async Task UseCacheStateKey_Isolates_Remembered_Points_While_Keeping_TraceSession()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var capture = new CaptureChatClient();
        using var diagnostics = UseDiagnostics(collector);
        var client = CreateClient(
            "claude-opus-4-1",
            capture: capture,
            sessionKey: "thread_1");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "shared prefix")]);

        using (PromptCacheStateScope.Use(
                   "thread_1:maintenance:memory_consolidation:turn_1",
                   traceSessionKey: "thread_1"))
        {
            await client.GetResponseAsync([
                new ChatMessage(ChatRole.User, "shared prefix"),
                new ChatMessage(ChatRole.User, "fork tail")
            ]);
        }

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "shared prefix"),
            new ChatMessage(ChatRole.User, "main tail")
        ]);

        AssertAnthropicCacheControl(AssertLastTextContent(capture.LastMessages![0]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(capture.LastMessages![1]), expectedTtl: null);
        Assert.Equal(3, store.GetEvents("thread_1").Count(e => e.Type == TraceEventType.PromptCachePoint));
        Assert.Empty(store.GetEvents("thread_1:maintenance:memory_consolidation:turn_1"));
    }

    [Fact]
    public async Task UseCacheStateKey_ReadOnlyMaintenance_MarksSnapshotPrefixOnlyAndDoesNotCommit()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient(
            "claude-opus-4-1",
            capture: capture,
            sessionKey: "thread_1");
        const string cacheStateKey = "thread_1:maintenance:context_compaction:turn_1";

        using (PromptCacheStateScope.Use(
                   cacheStateKey,
                   traceSessionKey: "thread_1",
                   new PromptCacheMaintenanceScope(2)))
        {
            await client.GetResponseAsync([
                new ChatMessage(ChatRole.User, "stable prefix"),
                new ChatMessage(ChatRole.Assistant, "stable assistant"),
                new ChatMessage(ChatRole.User, "fork tail")
            ]);
        }

        AssertNoCacheControl(AssertLastTextContent(capture.LastMessages![0]));
        AssertAnthropicCacheControl(AssertSingleTextContent(capture.LastMessages![1]), expectedTtl: null);
        AssertNoCacheControl(AssertLastTextContent(capture.LastMessages![2]));

        using (PromptCacheStateScope.Use(
                   cacheStateKey,
                   traceSessionKey: "thread_1",
                   new PromptCacheMaintenanceScope(2)))
        {
            var prepared = client.Prepare([
                new ChatMessage(ChatRole.User, "stable prefix"),
                new ChatMessage(ChatRole.Assistant, "stable assistant"),
                new ChatMessage(ChatRole.User, "next fork tail")
            ], null);

            var snapshotPrefix = Assert.Single(
                prepared.PendingCachePoints,
                point => point.Trace.MessageIndex == 1);
            Assert.False(snapshotPrefix.Trace.Remembered);
        }
    }

    private static AnthropicPromptCachingChatClient CreateClient(
        string model,
        CaptureChatClient? capture = null,
        string? sessionKey = null)
    {
        var key = sessionKey ?? Guid.NewGuid().ToString("N");
        return new(capture ?? new CaptureChatClient(), model, sessionKeyAccessor: () => key);
    }

    private static JsonElement JsonElementFrom(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static AnthropicPromptCachingChatClient CreateAnthropicNativeHttpClient(
        AnthropicCaptureHandler handler,
        string model = "claude-haiku-4-5")
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        return new AnthropicPromptCachingChatClient(
            anthropicClient.AsIChatClient(model),
            model,
            sessionKeyAccessor: () => Guid.NewGuid().ToString("N"));
    }

    private static AnthropicPromptCachingChatClient CreateAnthropicBetaNativeHttpClient(
        AnthropicCaptureHandler handler,
        string model = "claude-haiku-4-5")
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        return new AnthropicPromptCachingChatClient(
            anthropicClient.Beta.AsIChatClient(model),
            model,
            sessionKeyAccessor: () => Guid.NewGuid().ToString("N"));
    }

    private static void AssertNoCacheControl(AIContent content) =>
        Assert.False(content.AdditionalProperties?.ContainsKey(AnthropicCacheControlKey) ?? false);

    private static void AssertAnthropicCacheControl(AIContent content, string? expectedTtl)
    {
        Assert.NotNull(content.AdditionalProperties);
        var cacheControl = Assert.IsType<AnthropicCacheControlEphemeral>(content.AdditionalProperties[AnthropicCacheControlKey]);
        Assert.Equal("ephemeral", cacheControl.Type.GetString());
        if (expectedTtl is null)
            Assert.Null(cacheControl.Ttl);
        else
            Assert.Equal(expectedTtl, cacheControl.Ttl!.Raw());
    }

    private static void AssertWireCacheControl(JsonElement block, string? expectedTtl)
    {
        var cacheControl = block.GetProperty(WireCacheControlKey);
        Assert.Equal("ephemeral", cacheControl.GetProperty("type").GetString());
        if (expectedTtl is null)
            Assert.False(cacheControl.TryGetProperty("ttl", out _));
        else
            Assert.Equal(expectedTtl, cacheControl.GetProperty("ttl").GetString());
    }

    private static JsonElement FindFirstContentBlock(JsonElement root, string type)
    {
        foreach (var message in root.GetProperty("messages").EnumerateArray())
        {
            foreach (var block in message.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var blockType) &&
                    string.Equals(blockType.GetString(), type, StringComparison.Ordinal))
                {
                    return block;
                }
            }
        }

        throw new InvalidOperationException($"Content block '{type}' was not found.");
    }

    private static async Task AssertAnthropicGroupedToolResultWireAsync(
        Func<AnthropicCaptureHandler, AnthropicPromptCachingChatClient> createClient)
    {
        var handler = new AnthropicCaptureHandler();
        var client = createClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new TextContent("I will inspect both paths."),
                new FunctionCallContent("call_1", "FindFiles", new Dictionary<string, object?>()),
                new FunctionCallContent("call_2", "GrepFiles", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "first result"),
                new FunctionResultContent("call_2", "second result")
            ])
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());

        var resultContent = messages[2].GetProperty("content");
        Assert.Equal(2, resultContent.GetArrayLength());
        var firstResult = resultContent[0];
        var secondResult = resultContent[1];
        Assert.Equal("tool_result", firstResult.GetProperty("type").GetString());
        Assert.Equal("tool_result", secondResult.GetProperty("type").GetString());
        Assert.Equal("call_1", firstResult.GetProperty("tool_use_id").GetString());
        Assert.Equal("call_2", secondResult.GetProperty("tool_use_id").GetString());
        Assert.False(firstResult.TryGetProperty(WireCacheControlKey, out _));
        AssertWireCacheControl(secondResult, expectedTtl: null);
    }

    private static TextContent AssertLastTextContent(ChatMessage message) =>
        Assert.IsType<TextContent>(message.Contents.Last());

    private static TextContent AssertSingleTextContent(ChatMessage message) =>
        Assert.Single(message.Contents.OfType<TextContent>());

    private sealed class CaptureChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class AnthropicCaptureHandler : HttpMessageHandler
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
                    """
                    {
                        "id": "msg_cache_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-haiku-4-5",
                        "content": [{
                            "type": "text",
                            "text": "ok"
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
