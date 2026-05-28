using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadStoreSerializationTests
{
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

    private sealed class UnknownTestAiContent : AIContent
    {
        public string Name { get; init; } = string.Empty;
    }
}
