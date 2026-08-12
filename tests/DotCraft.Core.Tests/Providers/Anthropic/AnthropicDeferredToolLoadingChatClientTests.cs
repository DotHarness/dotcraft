using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicDeferredToolLoadingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_BeforeActivationSendsOnlyToolSearchAndBetaHeader()
    {
        var handler = new CaptureHandler();
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime")],
            DeferredToolLoadingMode.Native);
        var client = CreateClient(handler);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Find a ticket.")],
            new ChatOptions
            {
                Tools = [new AnthropicToolSearchTool(registry)]
            });

        Assert.Contains(AnthropicDeferredToolLoadingChatClient.ToolSearchBetaHeader, handler.LastBetaHeader);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        var searchTool = Assert.Single(tools);
        Assert.Equal(AnthropicToolSearchTool.ToolName, searchTool.GetProperty("name").GetString());
        Assert.False(searchTool.TryGetProperty("defer_loading", out _));
    }

    [Fact]
    public async Task GetResponseAsync_AfterActivationSendsDeferredSchemaWithDeferLoading()
    {
        var handler = new CaptureHandler();
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime")],
            DeferredToolLoadingMode.Native);
        var searchTool = new AnthropicToolSearchTool(registry);
        registry.ActivateByName(["TicketLookup"]);
        var client = CreateClient(handler);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the activated ticket tool.")],
            new ChatOptions
            {
                Tools = [searchTool]
            });

        Assert.Contains(AnthropicDeferredToolLoadingChatClient.ToolSearchBetaHeader, handler.LastBetaHeader);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);

        var names = tools
            .Select(toolElement => toolElement.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains(AnthropicToolSearchTool.ToolName, names);
        Assert.Contains("TicketLookup", names);

        var deferredTool = tools.Single(toolElement =>
            string.Equals(toolElement.GetProperty("name").GetString(), "TicketLookup", StringComparison.Ordinal));
        Assert.True(deferredTool.GetProperty("defer_loading").GetBoolean());
        Assert.False(handler.LastRequestJson!.Contains("tool_search_output", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetResponseAsync_WithPromptCacheAndThinkingPreservesAllAnthropicBetaRequestShaping()
    {
        var handler = new CaptureHandler();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(CreateTicketLookupTool(), "runtime")],
            DeferredToolLoadingMode.Native);
        var config = new AppConfig
        {
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ModelReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        };
        var client = CreateAdaptedClient(handler, config, "claude-opus-4-7");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Find a ticket.")],
            new ChatOptions
            {
                Tools = [new AnthropicToolSearchTool(registry)],
                Reasoning = config.Reasoning.ToOptions()
            });

        Assert.Contains(AnthropicDeferredToolLoadingChatClient.ToolSearchBetaHeader, handler.LastBetaHeader);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
        var userText = root.GetProperty("messages")[0].GetProperty("content")[0];
        Assert.Equal("text", userText.GetProperty("type").GetString());
        Assert.Equal("Find a ticket.", userText.GetProperty("text").GetString());
        Assert.Equal("ephemeral", userText.GetProperty("cache_control").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetResponseAsync_AfterActivationWithPromptCacheAndThinkingSendsDeferredSchema()
    {
        var handler = new CaptureHandler();
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime")],
            DeferredToolLoadingMode.Native);
        registry.ActivateByName(["TicketLookup"]);
        var config = new AppConfig
        {
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ModelReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        };
        var client = CreateAdaptedClient(handler, config, "claude-opus-4-7");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the activated ticket tool.")],
            new ChatOptions
            {
                Tools = [new AnthropicToolSearchTool(registry)],
                Reasoning = config.Reasoning.ToOptions()
            });

        Assert.Contains(AnthropicDeferredToolLoadingChatClient.ToolSearchBetaHeader, handler.LastBetaHeader);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Contains(tools, toolElement =>
            string.Equals(toolElement.GetProperty("name").GetString(), "TicketLookup", StringComparison.Ordinal)
            && toolElement.TryGetProperty("defer_loading", out var deferLoading)
            && deferLoading.GetBoolean());
    }

    [Fact]
    public async Task ToolSearch_ReturnsAnthropicToolReferenceContentBlocks()
    {
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime")],
            DeferredToolLoadingMode.Native);
        var searchTool = new AnthropicToolSearchTool(registry);

        var result = await searchTool.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "select:TicketLookup"
        });

        var content = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AIContent>>(result));
        var reference = Assert.IsType<DeferredToolReferenceContent>(content);
        Assert.Equal("TicketLookup", reference.ToolName);
        Assert.Contains("TicketLookup", registry.GetActivatedToolNames());
    }

    [Fact]
    public async Task ToolSearch_RecordsDeferredToolLoadingTraceOnceForNewActivations()
    {
        const string sessionKey = "anthropic-deferred-loading-trace";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime", "issues")],
            DeferredToolLoadingMode.Native);
        var searchTool = new AnthropicToolSearchTool(
            registry,
            maxSearchResults: 5,
            new DeferredToolLoadingTraceContext(
                collector,
                "Auto",
                "Native",
                ModelProviderProtocols.Anthropic,
                AnthropicToolSearchTool.ToolName,
                DeferredToolCount: 1,
                MaxSearchResults: 5));
        var previousSessionKey = TracingChatClient.CurrentSessionKey;

        try
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;
            await searchTool.InvokeAsync(new AIFunctionArguments
            {
                ["query"] = "select:issues__TicketLookup",
                ["max_results"] = 5
            });
            await searchTool.InvokeAsync(new AIFunctionArguments
            {
                ["query"] = "select:issues__TicketLookup",
                ["max_results"] = 5
            });
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.DeferredToolLoading);
        Assert.Equal("1 deferred tool activated", evt.ToolName);
        Assert.Equal("TicketLookup", evt.Content);
        Assert.NotNull(evt.ChangedToolNames);
        Assert.Equal(["TicketLookup"], evt.ChangedToolNames);
        Assert.Null(evt.PromptCacheEventKind);
        Assert.Null(evt.PromptCacheChangedFields);

        Assert.NotNull(evt.MetadataJson);
        using var metadata = JsonDocument.Parse(evt.MetadataJson);
        var root = metadata.RootElement;
        Assert.Equal("Auto", root.GetProperty("strategy").GetString());
        Assert.Equal("Native", root.GetProperty("effectiveMode").GetString());
        Assert.Equal(ModelProviderProtocols.Anthropic, root.GetProperty("providerProtocol").GetString());
        Assert.Equal(AnthropicToolSearchTool.ToolName, root.GetProperty("trigger").GetString());
        Assert.Equal("anthropic_tool_reference", root.GetProperty("wireShape").GetString());
        Assert.Equal("select:issues__TicketLookup", root.GetProperty("query").GetString());
        Assert.Equal(1, root.GetProperty("deferredToolCount").GetInt32());
        Assert.Equal(5, root.GetProperty("requestedMaxResults").GetInt32());
        Assert.Equal(5, root.GetProperty("maxSearchResults").GetInt32());
        var tracedTool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("TicketLookup", tracedTool.GetProperty("name").GetString());
        Assert.Equal("runtime", tracedTool.GetProperty("source").GetString());
        Assert.Equal("issues", tracedTool.GetProperty("namespace").GetString());

        var session = store.GetSession(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(0, session.PromptDriftCount);
        Assert.Null(session.LastPromptCacheChangeKind);
        Assert.Empty(session.LastPromptCacheChangedFields);
    }

    [Fact]
    public async Task StreamingFunctionLoop_ActivatesAndExecutesDeferredTool()
    {
        var tool = CreateTicketLookupTool();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(tool, "runtime")],
            DeferredToolLoadingMode.Native);
        var searchTool = new AnthropicToolSearchTool(registry);
        var fake = new AnthropicToolLoopFakeChatClient();
        using var invokingClient = new StreamingFunctionInvokingChatClient(fake)
        {
            AdditionalTools = registry.ActivatedToolsList
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in invokingClient.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "Find and read ticket ABC-1.")],
                           new ChatOptions { Tools = [searchTool] }))
        {
            updates.Add(update);
        }

        Assert.Equal(3, fake.Calls.Count);
        Assert.Contains("TicketLookup", registry.GetActivatedToolNames());

        var searchResult = fake.Calls[1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(content => content.CallId == "search-call");
        var referenceContent = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AIContent>>(searchResult.Result));
        var reference = Assert.IsType<DeferredToolReferenceContent>(referenceContent);
        Assert.Equal("TicketLookup", reference.ToolName);

        var ticketResult = fake.Calls[2]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(content => content.CallId == "ticket-call");
        Assert.Equal("ticket ABC-1", ImageContentSanitizingChatClient.DescribeResult(ticketResult.Result));

        var finalText = string.Concat(updates
            .SelectMany(update => update.Contents)
            .OfType<TextContent>()
            .Select(text => text.Text));
        Assert.Equal("done", finalText);
    }

    [Fact]
    public async Task PrepareOptions_WithRegistryInjectsActivatedSchemaWhenMarkerIsMissing()
    {
        var handler = new CaptureHandler();
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(CreateTicketLookupTool(), "runtime")],
            DeferredToolLoadingMode.Native);
        registry.ActivateByName(["TicketLookup"]);
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        var client = new AnthropicDeferredToolLoadingChatClient(
            anthropicClient.Beta.AsIChatClient("claude-sonnet-4-5"),
            "claude-sonnet-4-5",
            defaultMaxOutputTokens: 1024,
            registry);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the activated ticket tool.")],
            new ChatOptions { Tools = [] });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var toolElement = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("TicketLookup", toolElement.GetProperty("name").GetString());
        Assert.True(toolElement.GetProperty("defer_loading").GetBoolean());
    }

    private static AnthropicDeferredToolLoadingChatClient CreateClient(HttpMessageHandler handler)
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };

        return new AnthropicDeferredToolLoadingChatClient(
            anthropicClient.Beta.AsIChatClient("claude-sonnet-4-5"),
            "claude-sonnet-4-5",
            defaultMaxOutputTokens: 1024);
    }

    private static IChatClient CreateAdaptedClient(
        CaptureHandler handler,
        AppConfig config,
        string model)
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        IChatClient inner = anthropicClient.Beta.AsIChatClient(model);
        inner = new AnthropicThinkingChatClient(
            inner,
            ModelThinkingAdapterResolver.ResolveAnthropicThinkingAdapter(config, "http://localhost", model),
            model,
            defaultMaxOutputTokens: 1024);
        inner = new PromptCachingChatClient(
            inner,
            config.PromptCaching,
            model,
            AnthropicPromptCacheDialect.Instance,
            traceCollector: null,
            sessionKeyAccessor: () => Guid.NewGuid().ToString("N"));
        return new AnthropicProviderContentChatClient(
            new AnthropicDeferredToolLoadingChatClient(inner, model, defaultMaxOutputTokens: 1024));
    }

    private static AIFunction CreateTicketLookupTool() =>
        AIFunctionFactory.Create(
            (string issueKey) => $"ticket {issueKey}",
            name: "TicketLookup",
            description: "Look up ticket details by issue key.");

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        public string LastBetaHeader { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastBetaHeader = request.Headers.TryGetValues("anthropic-beta", out var values)
                ? string.Join(",", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "id": "msg_anthropic_deferred_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-sonnet-4-5",
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

    private sealed class AnthropicToolLoopFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = options;
            _ = cancellationToken;
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent(
                        "search-call",
                        AnthropicToolSearchTool.ToolName,
                        new Dictionary<string, object?> { ["query"] = "select:TicketLookup" })
                ]);
            }
            else if (Calls.Count == 2)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent(
                        "ticket-call",
                        "TicketLookup",
                        new Dictionary<string, object?> { ["issueKey"] = "ABC-1" })
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
