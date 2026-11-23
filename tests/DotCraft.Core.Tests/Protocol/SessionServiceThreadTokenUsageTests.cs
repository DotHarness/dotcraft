using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceThreadTokenUsageTests : SessionServiceRuntimeSignalTestBase
{
    [Fact]
    public async Task SubmitInputAsync_ThreadTokenUsageTotalGrowsToThePersistedTurnSum()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]),
            UsageUpdate(requestIndex: 1, input: 100, output: 10, cachedInput: 40),
            UsageUpdate(requestIndex: 2, input: 200, output: 20, cachedInput: 150)
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var updates = new ConcurrentQueue<ThreadTokenUsageUpdate>();
        svc.ThreadTokenUsageUpdatedForBroadcast = updates.Enqueue;
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("third")]));

        var seen = updates.ToArray();
        Assert.All(seen, update =>
        {
            Assert.Equal(thread.Id, update.ThreadId);
            Assert.Null(update.ParentThreadId);
            Assert.True(update.ModelContextWindow > 0);
        });
        Assert.Equal([100L, 200, 100, 200, 100, 200], seen.Select(update => update.Last.InputTokens));
        Assert.Equal([10L, 20, 10, 20, 10, 20], seen.Select(update => update.Last.OutputTokens));
        Assert.Equal([40L, 150, 40, 150, 40, 150], seen.Select(update => update.Last.CachedInputTokens));
        Assert.Equal([100L, 300, 400, 600, 700, 900], seen.Select(update => update.Total.InputTokens));
        Assert.Equal([110L, 330, 440, 660, 770, 990], seen.Select(update => update.Total.TotalTokens));

        var persisted = (await svc.ListThreadTurnsAsync(
            thread.Id, cursor: null, limit: 10, ThreadHistorySortDirection.Ascending)).Data;
        Assert.Equal(3, persisted.Count);
        Assert.Equal(
            persisted.Select(turn => turn.Id),
            seen.Select(update => update.TurnId).Distinct());
        var persistedTotal = persisted
            .Select(turn => turn.TokenUsage!)
            .Aggregate((left, right) => left + right);
        var final = seen[^1].Total;
        Assert.Equal(persistedTotal.InputTokens, final.InputTokens);
        Assert.Equal(persistedTotal.OutputTokens, final.OutputTokens);
        Assert.Equal(persistedTotal.CachedInputTokens, final.CachedInputTokens);
        Assert.Equal(persistedTotal.TotalTokens, final.TotalTokens);

        await svc.RollbackThreadAsync(thread.Id, 3);
        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("again")]));

        Assert.Equal(330, updates.Last().Total.TotalTokens);
    }

    [Fact]
    public async Task SubmitInputAsync_KeepsReportedUsageOnATurnThatEndsEarly()
    {
        IChatClient chatClient = new ThrowingAfterUpdatesChatClient(
            [UsageUpdate(requestIndex: 1, input: 120, output: 12, cachedInput: 30)],
            new InvalidOperationException("boom"));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var updates = new ConcurrentQueue<ThreadTokenUsageUpdate>();
        svc.ThreadTokenUsageUpdatedForBroadcast = updates.Enqueue;
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("work")]));

        var turn = Assert.Single((await svc.GetThreadAsync(thread.Id)).Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(132, turn.TokenUsage?.TotalTokens);
        Assert.Equal(132, Assert.Single(updates).Total.TotalTokens);
    }

    [Fact]
    public async Task SubmitInputAsync_SubAgentChildThreadTokenUsageNamesItsParent()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]),
            UsageUpdate(requestIndex: 1, input: 50, output: 5, cachedInput: 0)
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var updates = new ConcurrentQueue<ThreadTokenUsageUpdate>();
        svc.ThreadTokenUsageUpdatedForBroadcast = updates.Enqueue;
        var child = await CreateSubAgentChildAsync(svc, parentThreadId: "parent-usage", childThreadId: "child-usage");

        await DrainAsync(svc.SubmitInputAsync(child.Id, [new TextContent("work")]));

        var update = Assert.Single(updates);
        Assert.Equal(child.Id, update.ThreadId);
        Assert.Equal("parent-usage", update.ParentThreadId);
        Assert.Equal(55, update.Total.TotalTokens);
    }

    private Task<SessionThread> CreateSubAgentChildAsync(SessionService svc, string parentThreadId, string childThreadId) =>
        svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = "u",
                ChannelContext = parentThreadId,
                WorkspacePath = _tempDir
            },
            config: new ThreadConfiguration(),
            threadId: childThreadId,
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parentThreadId,
                RootThreadId = parentThreadId,
                Depth = 1
            }));
}
