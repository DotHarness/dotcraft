using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerRestorationTests
{
    [Fact]
    public async Task ResumeAndSelectedItemRead_UseOneFeedAndPlaceResponsesAfterQueuedDeltas()
    {
        using var harness = new CoreAppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var item = new SessionItem
        {
            Id = "item_001", TurnId = "turn_001", Type = ItemType.AgentMessage,
            Status = ItemStatus.Streaming, Payload = new AgentMessagePayload { Text = "base" }
        };
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running, Items = [item]
        });
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadResume, new { threadId = thread.Id }));
        using var initial = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(initial);
        Assert.False(initial.RootElement.GetProperty("result").TryGetProperty("initialTurnsPage", out _));
        Assert.True(harness.Connection.HasSubscription(thread.Id));

        PublishDelta("before");
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadResume, new { threadId = thread.Id }));
        using var before = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(Methods.AgentMessageDelta, before.RootElement.GetProperty("method").GetString());
        using var repeated = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(repeated);

        PublishDelta("after");
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_001", itemIds = new[] { "item_001" } }));
        using var after = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(Methods.AgentMessageDelta, after.RootElement.GetProperty("method").GetString());
        using var detail = await harness.Transport.ReadNextSentAsync();
        var restored = Assert.Single(detail.RootElement.GetProperty("result").GetProperty("data").EnumerateArray())
            .GetProperty("item");
        Assert.Equal("basebeforeafter", restored.GetProperty("payload").GetProperty("text").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadUnsubscribe, new { threadId = thread.Id }));
        using var released = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(released);
        Assert.False(harness.Connection.HasSubscription(thread.Id));
        PublishDelta("detached");
        Assert.Empty(harness.Transport.DrainSent());

        void PublishDelta(string text) => harness.Service.PublishThreadEvent(new SessionEvent
        {
            ThreadId = thread.Id, TurnId = "turn_001", ItemId = "item_001",
            EventType = SessionEventType.ItemDelta, Payload = new AgentMessageDelta { TextDelta = text }
        });
    }

    [Fact]
    public async Task CurrentItemPages_StayBoundedAndContinueByStableIdentity()
    {
        using var harness = new CoreAppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running,
            Items = Enumerable.Range(0, 205).Select(index => new SessionItem
            {
                Id = $"item_{index:D3}", TurnId = "turn_001", Type = ItemType.AgentMessage,
                Status = ItemStatus.Completed, Payload = new AgentMessagePayload { Text = index.ToString() }
            }).ToList()
        });
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadResume, new { threadId = thread.Id }));
        using var restored = await harness.Transport.ReadNextSentAsync();
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadItemsList, new
            {
                threadId = thread.Id, turnId = "turn_001", limit = 100, sortDirection = "ascending", cursor
            }));
            using var response = await harness.Transport.ReadNextSentAsync();
            var result = response.RootElement.GetProperty("result");
            var page = result.GetProperty("data").EnumerateArray().ToArray();
            Assert.InRange(page.Length, 1, 100);
            ids.AddRange(page.Select(entry => entry.GetProperty("item").GetProperty("id").GetString()!));
            cursor = result.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        } while (cursor is not null);
        Assert.Equal(Enumerable.Range(0, 205).Select(index => $"item_{index:D3}"), ids);
    }
}
