using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task SubmitInputAsync_WhenEmptyProviderResponseStopsNormally_CompletesWithoutMessage()
    {
        IChatClient chatClient = new FakeChatClient([
            UsageUpdate(requestIndex: 1, input: 10_000, output: 4, cachedInput: 9_000),
            new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop }
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Single(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var persistedThread = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(Assert.IsType<SessionThread>(persistedThread).Turns);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Null(turn.Error);
        Assert.DoesNotContain(turn.Items, item => item.Type is ItemType.Error or ItemType.AgentMessage);
        var usage = Assert.IsType<TokenUsageInfo>(turn.TokenUsage);
        Assert.Equal(10_000, usage.InputTokens);
        Assert.Equal(4, usage.OutputTokens);
        Assert.Equal(9_000, usage.CachedInputTokens);
        Assert.Equal(1, usage.LlmCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitInputAsync_WhenProviderStreamHasNoContent_MarksTurnFailed(bool usageOnly)
    {
        IChatClient chatClient = new FakeChatClient(usageOnly
            ? [UsageUpdate(requestIndex: 1, input: 10_000, output: 0, cachedInput: 9_000)]
            : []);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(updatedThread.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var errorPayload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_empty_response", errorPayload.Code);
        Assert.True(errorPayload.Fatal);
        Assert.Contains("empty streaming response", errorPayload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(turn.Items, item => item.Type == ItemType.AgentMessage);
    }
}
