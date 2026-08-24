using System.Text.Json;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Realtime;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Tests;

public sealed class BoardEventHubTests
{
    [Fact]
    public async Task Publish_FansOutToActiveSubscribers()
    {
        var broker = new BoardEventHub();
        using var first = broker.Subscribe();
        using var second = broker.Subscribe();
        var item = new OratorioItem
        {
            ItemId = "item-1",
            ShortId = "DEF-1",
            State = ItemState.Dispatching,
            CheckState = CheckState.Pending,
            BoardSortOrder = 1000
        };

        broker.PublishTaskUpdated(item, DateTimeOffset.Parse("2026-05-09T00:00:00Z"));

        var firstEvent = await ReadOneAsync(first);
        var secondEvent = await ReadOneAsync(second);
        Assert.Equal(BoardEvent.TaskUpdatedType, firstEvent.Type);
        Assert.Equal("item-1", firstEvent.TaskId);
        Assert.Equal("DEF-1", firstEvent.ShortId);
        Assert.Equal(BoardTaskStatus.InProgress, firstEvent.TaskStatus);
        Assert.Equal("running", firstEvent.MicroStatus);
        Assert.Equal(firstEvent, secondEvent);
    }

    [Fact]
    public void Dispose_RemovesSubscriber()
    {
        var broker = new BoardEventHub();
        using (broker.Subscribe())
        {
            Assert.Equal(1, broker.SubscriberCount);
        }

        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public async Task PublishDrawer_OnlyForwardsFocusedRun()
    {
        var broker = new BoardEventHub();
        using var focused = broker.Subscribe();
        using var other = broker.Subscribe();
        focused.Focus("run-1");

        broker.PublishDrawer(
            "run-1",
            new DrawerEvent(
                DrawerEvent.ItemCompletedType,
                "run-1",
                "item-1",
                "DEF-1",
                JsonSerializer.SerializeToElement(new { id = "message-1", type = "agentMessage" }),
                DateTimeOffset.Parse("2026-05-09T00:00:00Z")));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.True(await focused.DrawerReader.WaitToReadAsync(cts.Token));
        Assert.True(focused.DrawerReader.TryRead(out var drawerEvent));
        Assert.Equal("run-1", drawerEvent.RunId);
        Assert.False(other.DrawerReader.TryRead(out _));
    }

    private static async Task<BoardEvent> ReadOneAsync(BoardEventSubscription subscription)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.True(await subscription.Reader.WaitToReadAsync(cts.Token));
        Assert.True(subscription.Reader.TryRead(out var boardEvent));
        return boardEvent;
    }
}
