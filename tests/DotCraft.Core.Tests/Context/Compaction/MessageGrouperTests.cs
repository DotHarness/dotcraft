using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MessageGrouperTests
{
    [Fact]
    public void GroupByApiRound_EmptyReturnsEmpty()
    {
        Assert.Empty(MessageGrouper.GroupByApiRound(Array.Empty<ChatMessage>()));
    }

    [Fact]
    public void GroupByApiRound_StartsNewGroupOnAssistantResponse()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "reply 1") { MessageId = "response-1" },
            new(ChatRole.User, "second"),
            new(ChatRole.Assistant, "reply 2") { MessageId = "response-2" },
        };

        var groups = MessageGrouper.GroupByApiRound(messages);
        Assert.Equal(3, groups.Count);
        Assert.Single(groups[0].Messages);
        Assert.Equal(2, groups[1].Messages.Count);
        Assert.Single(groups[2].Messages);
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
            // Tool-result envelope surfaces as role=User but carries FunctionResultContent.
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
    public void GroupByApiRound_MissingAssistantIdsUseEachAssistantAsBoundary()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "reply 1"),
            new(ChatRole.User, "second"),
            new(ChatRole.Assistant, "reply 2"),
        };

        var groups = MessageGrouper.GroupByApiRound(messages);
        Assert.Equal(3, groups.Count);
        Assert.Single(groups[0].Messages);
        Assert.Equal(2, groups[1].Messages.Count);
        Assert.Single(groups[2].Messages);
    }

    [Fact]
    public void GroupByApiRound_UsesPaddedEstimateForGroupTokens()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "reply") { MessageId = "response-1" },
        };

        var groups = MessageGrouper.GroupByApiRound(messages);
        Assert.Equal(MessageTokenEstimator.Estimate(groups[0].Messages), groups[0].EstimatedTokens);
        Assert.Equal(MessageTokenEstimator.Estimate(groups[1].Messages), groups[1].EstimatedTokens);
    }

    [Fact]
    public void EnsurePairing_DropsDanglingFunctionCall()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("thinking"),
                new FunctionCallContent("missing", "ReadFile", new Dictionary<string, object?>()),
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

    [Fact]
    public void EnsurePairing_DropsMessageWhenOnlyContentIsDangling()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("missing", "ReadFile", new Dictionary<string, object?>()),
            }),
        };

        var paired = MessageGrouper.EnsurePairing(messages);
        Assert.Empty(paired);
    }
}
