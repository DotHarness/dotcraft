using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadStoreSerializationTests
{
    [Fact]
    public async Task AgentSession_RoundTrip_RestoresFunctionResultAsJsonArrayWithoutFlatteningImage()
    {
        using var client = new PassiveChatClient();
        var agent = client.AsAIAgent(new ChatClientAgentOptions());
        var session = await agent.CreateSessionAsync();
        session.SetInMemoryChatHistory(
        [
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", new List<AIContent>
                {
                    new TextContent("screenshot"),
                    new DataContent(new byte[250_000], "image/png")
                })
            ])
        ], jsonSerializerOptions: SessionPersistenceJsonOptions.Default);

        var serialized = await agent.SerializeSessionAsync(session, SessionPersistenceJsonOptions.Default);
        var restored = await agent.DeserializeSessionAsync(serialized, SessionPersistenceJsonOptions.Default);

        Assert.True(restored.TryGetInMemoryChatHistory(
            out var history,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        var toolResult = Assert.Single(Assert.Single(history).Contents.OfType<FunctionResultContent>());
        var jsonResult = Assert.IsType<JsonElement>(toolResult.Result);
        Assert.Equal(JsonValueKind.Array, jsonResult.ValueKind);
        var restoredContents = jsonResult.Deserialize<List<AIContent>>(SessionPersistenceJsonOptions.Default);
        Assert.NotNull(restoredContents);
        Assert.IsType<DataContent>(restoredContents![1]);
        var restoredEstimate = MessageTokenEstimator.Estimate(history.ToList());
        Assert.InRange(restoredEstimate, 2_000, 20_000);
    }

    [Fact]
    public void SessionPersistenceJsonOptions_SerializesToolCallDeltaContent()
    {
        List<AIContent> contents =
        [
            new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = 0,
                ToolName = "WriteFile",
                CallId = "call-1",
                ArgumentsDelta = "{\"path\":\"a.txt\"}"
            }
        ];

        var json = JsonSerializer.Serialize(contents, SessionPersistenceJsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<List<AIContent>>(json, SessionPersistenceJsonOptions.Default);
        Assert.NotNull(roundTrip);

        var content = Assert.Single(roundTrip);
        var delta = Assert.IsType<ToolCallArgumentsDeltaContent>(content);
        Assert.Equal("WriteFile", delta.ToolName);
        Assert.Equal("call-1", delta.CallId);
        Assert.Equal("{\"path\":\"a.txt\"}", delta.ArgumentsDelta);
    }

    [Fact]
    public void SessionPersistenceJsonOptions_UnknownAiContent_FallsBackWithoutThrowing()
    {
        AIContent content = new UnknownTestAiContent
        {
            Name = "custom"
        };

        var exception = Record.Exception(() =>
            JsonSerializer.Serialize(content, SessionPersistenceJsonOptions.Default));

        Assert.Null(exception);
    }

    [Fact]
    public void SessionJsonOptions_ReadsLegacyNumericReasoningConfig()
    {
        const string json = """
            {
              "reasoning": {
                "enabled": true,
                "effort": 2,
                "output": 2
              }
            }
            """;

        var config = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);

        Assert.NotNull(config?.Reasoning);
        Assert.True(config!.Reasoning!.Enabled);
        Assert.Equal(ReasoningEffort.Medium, config.Reasoning.Effort);
        Assert.Equal(ReasoningOutput.Full, config.Reasoning.Output);
    }

    [Theory]
    [InlineData("extraHigh", ReasoningEffort.ExtraHigh)]
    [InlineData("extra_high", ReasoningEffort.ExtraHigh)]
    [InlineData("xhigh", ReasoningEffort.ExtraHigh)]
    [InlineData("medium", ReasoningEffort.Medium)]
    public void SessionPersistenceJsonOptions_ReadsReasoningEffortStringsAndAliases(
        string rawEffort,
        ReasoningEffort expected)
    {
        var json = $$"""
            {
              "reasoning": {
                "enabled": true,
                "effort": "{{rawEffort}}",
                "output": "full"
              }
            }
            """;

        var config = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionPersistenceJsonOptions.Default);

        Assert.NotNull(config?.Reasoning);
        Assert.Equal(expected, config!.Reasoning!.Effort);
        Assert.Equal(ReasoningOutput.Full, config.Reasoning.Output);
    }

    [Fact]
    public void SessionJsonOptions_WritesCanonicalReasoningStrings()
    {
        var config = new ThreadConfiguration
        {
            Reasoning = new()
            {
                Enabled = true,
                Effort = ReasoningEffort.ExtraHigh,
                Output = ReasoningOutput.Summary
            }
        };

        var json = JsonSerializer.Serialize(config, SessionJsonOptions.Default);

        Assert.Contains("\"effort\":\"extraHigh\"", json);
        Assert.Contains("\"output\":\"summary\"", json);
    }

    private sealed class UnknownTestAiContent : AIContent
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class PassiveChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
