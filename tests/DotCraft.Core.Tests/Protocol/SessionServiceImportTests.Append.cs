using DotCraft.Channels;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceImportTests
{
    [Fact]
    public async Task AppendImportedTurnsAsync_ExtendsLastTurnAfterMarkerAndAppendsNewTurns()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var first = Turn("first question", minute: 0, "first answer");
        await service.ImportThreadAsync(MakeRequest(first));
        var updated = new List<SessionThread>();
        service.ThreadUpdatedForBroadcast = updated.Add;

        var result = await service.AppendImportedTurnsAsync(new ThreadImportAppendRequest
        {
            ThreadId = ThreadId,
            ExistingTurns = [first with { AgentTexts = ["first answer", "late answer"] }],
            NewTurns =
            [
                Turn("second question", minute: 10, "second answer"),
                Turn("third question", minute: 20, "third answer")
            ],
            EstimatedTokens = 900
        });

        Assert.Same(result.Thread, Assert.Single(updated));
        var loaded = await _store.LoadThreadAsync(ThreadId);
        Assert.NotNull(loaded);
        Assert.Equal(["turn_001", "turn_002", "turn_003"], loaded!.Turns.Select(turn => turn.Id));
        Assert.Equal(
            ["user:first question", "agent:first answer", $"agent:{Marker}", "agent:late answer"],
            DescribeItems(loaded.Turns[0]));
        Assert.Equal(["user:second question", "agent:second answer"], DescribeItems(loaded.Turns[1]));
        Assert.Equal(["user:third question", "agent:third answer"], DescribeItems(loaded.Turns[2]));
        Assert.All(loaded.Turns, turn => Assert.Equal(ThreadImportConstants.ChannelName, turn.OriginChannel));
        Assert.Equal(BaseTime.AddMinutes(20).AddSeconds(30), loaded.LastActiveAt);
        Assert.Equal(3, Assert.Single(await _store.LoadIndexAsync()).TurnCount);
        Assert.Equal(900, _store.LoadContextUsageSnapshot(ThreadId)!.Tokens);

        var events = await ReadEventsUntilAsync(
            service,
            evt => evt.EventType == SessionEventType.TurnCompleted && evt.TurnId == "turn_003");
        Assert.Equal(
            ["turn_001/item_004", "turn_002/item_001", "turn_002/item_002", "turn_003/item_001", "turn_003/item_002"],
            events
                .Where(evt => evt.EventType == SessionEventType.ItemCompleted)
                .Select(evt => $"{evt.TurnId}/{evt.ItemId}"));
        Assert.Equal(
            ["TurnStarted:turn_002", "TurnCompleted:turn_002", "TurnStarted:turn_003", "TurnCompleted:turn_003"],
            events
                .Where(evt => evt.EventType is SessionEventType.TurnStarted or SessionEventType.TurnCompleted)
                .Select(evt => $"{evt.EventType}:{evt.TurnId}"));
    }

    [Fact]
    public async Task AppendImportedTurnsAsync_ExtendsGrownLastTurnAfterMarkerWithoutNewTurns()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var first = Turn("first question", minute: 0, "first answer");
        await service.ImportThreadAsync(MakeRequest(first));

        await service.AppendImportedTurnsAsync(new ThreadImportAppendRequest
        {
            ThreadId = ThreadId,
            ExistingTurns = [first with { AgentTexts = ["first answer", "late answer"], CompletedAt = BaseTime.AddMinutes(3) }],
            NewTurns = []
        });

        var loaded = await _store.LoadThreadAsync(ThreadId);
        var turn = Assert.Single(loaded!.Turns);
        Assert.Equal(
            ["user:first question", "agent:first answer", $"agent:{Marker}", "agent:late answer"],
            DescribeItems(turn));
        Assert.Equal(BaseTime.AddMinutes(3), turn.CompletedAt);
        Assert.Equal(BaseTime.AddMinutes(3), turn.Items[^1].CreatedAt);
        Assert.Equal(BaseTime.AddMinutes(3), loaded.LastActiveAt);
        var page = await _store.ListThreadItemsAsync(
            ThreadId,
            "turn_001",
            cursor: null,
            limit: 10,
            ThreadHistorySortDirection.Ascending);
        Assert.Equal(["item_001", "item_002", "item_003", "item_004"], page.Data.Select(entry => entry.Item.Id));
    }

    [Fact]
    public async Task AppendImportedTurnsAsync_RefusesThreadContinuedLocally()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory, new RecordingChatClient("local answer"));
        var first = Turn("first question", minute: 0, "first answer");
        await service.ImportThreadAsync(MakeRequest(first));
        using (ChannelSessionScope.Set(new ChannelSessionInfo { Channel = "cli", UserId = "local" }))
            await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("local follow-up")]));
        IReadOnlyList<ImportedTurnInput>[] expectedTurns =
        [
            [first],
            // Matches the local turn's content, so only its origin channel refuses the append.
            [first, Turn("local follow-up", minute: 5, "local answer")]
        ];

        foreach (var existingTurns in expectedTurns)
        {
            await Assert.ThrowsAsync<ThreadImportRefusedException>(() => service.AppendImportedTurnsAsync(
                new ThreadImportAppendRequest
                {
                    ThreadId = ThreadId,
                    ExistingTurns = existingTurns,
                    NewTurns = [Turn("second question", minute: 10, "second answer")]
                }));
        }

        var loaded = await _store.LoadThreadAsync(ThreadId);
        Assert.Equal(
            ["first question", "local follow-up"],
            loaded!.Turns.Select(turn => turn.Input!.AsUserMessage!.Text));
    }

    [Fact]
    public async Task AppendImportedTurnsAsync_RefusesWhenImportedTurnsNoLongerMatchSource()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var first = Turn("first question", minute: 0, "first answer");
        var last = Turn("last question", minute: 5, "answer one", "answer two");
        await service.ImportThreadAsync(MakeRequest(first, last));
        IReadOnlyList<ImportedTurnInput>[] mismatchedTurns =
        [
            [first with { AgentTexts = ["first answer", "late answer"] }, last],
            [first, last with { AgentTexts = ["answer one", "rewritten two", "answer three"] }]
        ];

        foreach (var existingTurns in mismatchedTurns)
        {
            await Assert.ThrowsAsync<ThreadImportRefusedException>(() => service.AppendImportedTurnsAsync(
                new ThreadImportAppendRequest
                {
                    ThreadId = ThreadId,
                    ExistingTurns = existingTurns,
                    NewTurns = [Turn("next question", minute: 10, "next answer")]
                }));
        }

        var loaded = await _store.LoadThreadAsync(ThreadId);
        Assert.Equal(2, loaded!.Turns.Count);
        Assert.Equal(["user:first question", "agent:first answer"], DescribeItems(loaded.Turns[0]));
        Assert.Equal(
            ["user:last question", "agent:answer one", "agent:answer two", $"agent:{Marker}"],
            DescribeItems(loaded.Turns[1]));
    }

    private static async Task<List<SessionEvent>> ReadEventsUntilAsync(
        SessionService service,
        Func<SessionEvent, bool> isLast)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<SessionEvent>();
        await foreach (var evt in service.SubscribeThreadAsync(ThreadId, replayRecent: true, timeout.Token))
        {
            events.Add(evt);
            if (isLast(evt))
                break;
        }

        return events;
    }
}
