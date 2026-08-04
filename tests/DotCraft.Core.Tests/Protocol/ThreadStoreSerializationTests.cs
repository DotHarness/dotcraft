using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadStoreSerializationTests
{
    [Fact]
    public void ModelHistory_RoundTrip_RestoresFunctionResultAsOwnedContentsWithoutFlatteningImage()
    {
        var message = new ChatMessage(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", new List<AIContent>
                {
                    new TextContent("screenshot"),
                    new DataContent(new byte[250_000], "image/png")
                })
            ]);
        var codec = new ModelHistoryCodec();
        var serialized = JsonSerializer.Serialize(codec.Encode(message), SessionJsonOptions.Default);
        var restored = codec.Decode(JsonSerializer.Deserialize<ModelHistoryMessage>(serialized, SessionJsonOptions.Default)!);

        var toolResult = Assert.Single(restored.Contents.OfType<FunctionResultContent>());
        var restoredContents = Assert.IsAssignableFrom<IList<AIContent>>(toolResult.Result);
        Assert.IsType<DataContent>(restoredContents[1]);
        var restoredEstimate = MessageTokenEstimator.Estimate([restored]);
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

}
