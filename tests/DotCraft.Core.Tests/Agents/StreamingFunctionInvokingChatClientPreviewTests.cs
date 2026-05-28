using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_TextOnly_NoInjectedDelta()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")])
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true
        };

        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            collected.Add(update);

        Assert.Single(collected);
        Assert.DoesNotContain(collected[0].Contents, c => c is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_SingleToolCall_InjectsChunks()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"path\":\"a.txt\",\"content\":\"hel"))
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("b")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, null, null, "lo\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true,
            IsStreamableTool = name => name == "WriteFile"
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Equal(2, deltas.Count);
        Assert.Equal("WriteFile", deltas[0].ToolName);
        Assert.Equal("call-1", deltas[0].CallId);
        Assert.Equal("{\"path\":\"a.txt\",\"content\":\"hel", deltas[0].ArgumentsDelta);
        Assert.Null(deltas[1].ToolName);
        Assert.Null(deltas[1].CallId);
        Assert.Equal("lo\"}", deltas[1].ArgumentsDelta);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ExposesDeltaToConsumer_ButRemovesAfterYield()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
        {
            RawRepresentation = new FakeRawDeltaSource(
                new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"path\":\"a.txt\"}"))
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: [update]))
        {
            EnableToolCallArgumentPreviews = true
        };

        var observed = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var item in client.GetStreamingResponseAsync([]))
            observed.AddRange(item.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Single(observed);
        Assert.DoesNotContain(update.Contents, content => content is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FilteredTool_DoesNotInject()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "ReadFile", "call-1", "{\"path\":\"a.txt\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true,
            IsStreamableTool = name => name == "WriteFile"
        };

        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            collected.Add(update);

        Assert.Single(collected);
        Assert.DoesNotContain(collected[0].Contents, c => c is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoPredicate_StreamsAllTools()
    {
        // Default behaviour: predicate and legacy set both null -> every tool eligible.
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "MyMcpTool", "call-1", "{\"query\":\"hi\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Single(deltas);
        Assert.Equal("MyMcpTool", deltas[0].ToolName);
        Assert.Equal("{\"query\":\"hi\"}", deltas[0].ArgumentsDelta);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_LegacyStreamableToolNamesSet_StillRespected()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "ReadFile", "call-1", "{\"path\":\"a.txt\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true,
            StreamableToolNames = new HashSet<string>(["WriteFile"], StringComparer.Ordinal)
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Empty(deltas);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ParallelIndexes_AreTrackedIndependently()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"content\":\"a"),
                    new ToolCallDeltaChunk(1, "EditFile", "call-2", "{\"old\":\"x"))
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("b")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, null, null, "b\"}"),
                    new ToolCallDeltaChunk(1, null, null, "y\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates))
        {
            EnableToolCallArgumentPreviews = true
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Equal(4, deltas.Count);
        Assert.Equal(0, deltas[0].ToolCallIndex);
        Assert.Equal("WriteFile", deltas[0].ToolName);
        Assert.Equal(1, deltas[1].ToolCallIndex);
        Assert.Equal("EditFile", deltas[1].ToolName);
        Assert.Equal(0, deltas[2].ToolCallIndex);
        Assert.Null(deltas[2].ToolName);
        Assert.Equal(1, deltas[3].ToolCallIndex);
        Assert.Null(deltas[3].ToolName);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_MultiRound_ResetsTrackers()
    {
        var inner = new MultiRoundPreviewFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            EnableToolCallArgumentPreviews = true,
            AdditionalTools = [AIFunctionFactory.Create(FakeToolMethods.WriteFile)]
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Equal(4, deltas.Count);

        Assert.Equal(0, deltas[0].ToolCallIndex);
        Assert.Equal("WriteFile", deltas[0].ToolName);
        Assert.Equal("call-1", deltas[0].CallId);

        Assert.Equal(0, deltas[1].ToolCallIndex);
        Assert.Null(deltas[1].ToolName);
        Assert.Null(deltas[1].CallId);

        Assert.Equal(0, deltas[2].ToolCallIndex);
        Assert.Equal("EditFile", deltas[2].ToolName);
        Assert.Equal("call-2", deltas[2].CallId);

        Assert.Equal(0, deltas[3].ToolCallIndex);
        Assert.Null(deltas[3].ToolName);
        Assert.Null(deltas[3].CallId);

        Assert.Equal(2, inner.Calls.Count);
        Assert.DoesNotContain(
            inner.Calls[1].SelectMany(message => message.Contents),
            content => content is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PreviewsDisabledByDefault()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"path\":\"a.txt\"}"))
            }
        };
        var client = new StreamingFunctionInvokingChatClient(new FakeChatClient(streamUpdates: updates));

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Empty(deltas);
    }

    [Fact]
    public void BuildStreamOptOutToolNames_PicksUpAttribute()
    {
        // Tool whose method is decorated with [StreamArguments(false)] opts out;
        // an undecorated tool stays streamable.
        var optOutTool = AIFunctionFactory.Create(FakeToolMethods.OptedOut);
        var streamingTool = AIFunctionFactory.Create(FakeToolMethods.Streaming);

        var optOut = AgentFactory.BuildStreamOptOutToolNames([optOutTool, streamingTool]);

        Assert.Contains(optOutTool.Name, optOut);
        Assert.DoesNotContain(streamingTool.Name, optOut);
    }

    [Fact]
    public void BuildStreamOptOutToolNames_IgnoresToolsWithoutUnderlyingMethod()
    {
        // Tools without UnderlyingMethod (MCP-shaped) should never appear in the opt-out set.
        var mcpShapedTool = new MethodlessFakeFunction("search_docs");

        var optOut = AgentFactory.BuildStreamOptOutToolNames([mcpShapedTool]);

        Assert.Empty(optOut);
    }

    [Fact]
    public void BuildStreamOptOutToolNames_DoesNotIncludeAgentTools()
    {
        var agentTools = new AgentTools();
        var agentControlTools = new[]
        {
            AIFunctionFactory.Create(agentTools.SpawnAgent),
            AIFunctionFactory.Create(agentTools.SendInput),
            AIFunctionFactory.Create(agentTools.WaitAgent),
            AIFunctionFactory.Create(agentTools.ResumeAgent),
            AIFunctionFactory.Create(agentTools.CloseAgent)
        };

        var optOut = AgentFactory.BuildStreamOptOutToolNames(agentControlTools);

        foreach (var tool in agentControlTools)
            Assert.DoesNotContain(tool.Name, optOut);
    }

    private static class FakeToolMethods
    {
        [StreamArguments(false)]
        public static string OptedOut(string query) => query;

        public static string Streaming(string query) => query;

        public static string WriteFile(string content) => content;
    }

    private sealed class MethodlessFakeFunction : AIFunction
    {
        public MethodlessFakeFunction(string name) { Name = name; }
        public override string Name { get; }
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(null);
    }

    private sealed class FakeRawDeltaSource(params ToolCallDeltaChunk[] chunks) : IToolCallDeltaChunkSource
    {
        public IEnumerable<ToolCallDeltaChunk> GetToolCallDeltaChunks() => chunks;
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly ChatResponse? _response;
        private readonly ChatResponseUpdate[] _streamUpdates;

        public FakeChatClient(ChatResponse? response = null, ChatResponseUpdate[]? streamUpdates = null)
        {
            _response = response;
            _streamUpdates = streamUpdates ?? [];
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response ?? new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var update in _streamUpdates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class MultiRoundPreviewFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("r1-a")])
                {
                    RawRepresentation = new FakeRawDeltaSource(
                        new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"content\":\"hel"))
                };
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new TextContent("r1-b"),
                    new FunctionCallContent("call-1", "WriteFile", new Dictionary<string, object?> { ["content"] = "hello" })
                ])
                {
                    RawRepresentation = new FakeRawDeltaSource(
                        new ToolCallDeltaChunk(0, null, null, "lo\"}"))
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("r2-a")])
                {
                    RawRepresentation = new FakeRawDeltaSource(
                        new ToolCallDeltaChunk(0, "EditFile", "call-2", "{\"old\":\"a"))
                };
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("r2-b")])
                {
                    RawRepresentation = new FakeRawDeltaSource(
                        new ToolCallDeltaChunk(0, null, null, "b\"}"))
                };
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

