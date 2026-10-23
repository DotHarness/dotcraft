using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MessageGrouperTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GroupByApiRound_StartsNewGroupOnEachAssistantResponse(bool withResponseIds)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "reply 1") { MessageId = withResponseIds ? "response-1" : null },
            new(ChatRole.User, "second"),
            new(ChatRole.Assistant, "reply 2") { MessageId = withResponseIds ? "response-2" : null },
        };

        var groups = MessageGrouper.GroupByApiRound(messages);

        Assert.Equal(3, groups.Count);
        Assert.Single(groups[0].Messages);
        Assert.Equal(2, groups[1].Messages.Count);
        Assert.Single(groups[2].Messages);
        Assert.All(groups, group => Assert.Equal(MessageTokenEstimator.Estimate(group.Messages), group.EstimatedTokens));
        Assert.Empty(MessageGrouper.GroupByApiRound(Array.Empty<ChatMessage>()));
    }

    [Fact]
    public void GroupByApiRound_KeepsSameAssistantResponseChunksTogether()
    {
        var callId = "call-1";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "question"),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent(callId, "ReadFile", new Dictionary<string, object?> { ["path"] = "x" }),
            })
            {
                MessageId = "response-1",
            },
            new(ChatRole.User, new List<AIContent>
            {
                new FunctionResultContent(callId, "result text"),
            }),
            new(ChatRole.Assistant, "final answer")
            {
                MessageId = "response-1",
            },
        };

        var groups = MessageGrouper.GroupByApiRound(messages);
        Assert.Equal(2, groups.Count);
        Assert.Single(groups[0].Messages);
        Assert.Equal(3, groups[1].Messages.Count);
    }

    [Fact]
    public void EnsurePairing_DropsDanglingFunctionCallsAndMessagesLeftEmpty()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("thinking"),
                new FunctionCallContent("missing", "ReadFile", new Dictionary<string, object?>()),
            }),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("missing-2", "ReadFile", new Dictionary<string, object?>()),
            }),
        };

        var paired = MessageGrouper.EnsurePairing(messages);
        Assert.Single(paired);
        Assert.Single(paired[0].Contents);
        Assert.IsType<TextContent>(paired[0].Contents[0]);
    }

    [Fact]
    public void EnsurePairing_KeepsPairedFunctionCall()
    {
        var callId = "call-1";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent(callId, "ReadFile", new Dictionary<string, object?>()),
            }),
            new(ChatRole.User, new List<AIContent>
            {
                new FunctionResultContent(callId, "ok"),
            }),
        };

        var paired = MessageGrouper.EnsurePairing(messages);
        Assert.Equal(2, paired.Count);
        Assert.IsType<FunctionCallContent>(paired[0].Contents[0]);
    }
}
