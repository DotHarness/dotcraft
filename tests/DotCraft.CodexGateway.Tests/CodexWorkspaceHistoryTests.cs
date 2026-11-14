using System.Text.Json;
using System.Threading.Channels;
using Xunit;
using static DotCraft.CodexGateway.Tests.GatewayHistoryFixture;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceHistoryTests
{
    [Fact]
    public async Task MetadataResumeBootstrapsLatestTurnsAndSharesOneItemSnapshotAcrossTurnFilters()
    {
        using var fixture = new GatewayHistoryFixture { SourcePageSize = 2 };
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        Assert.Empty(resumed.GetProperty("thread").GetProperty("turns").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("initialTurnsPage").ValueKind);
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method is "thread/items/list" or "thread/turns/list");

        var turns = await CallAsync(session, "thread/turns/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("turnsBackwardsCursor").GetString(),
            sortDirection = "desc", limit = 1, itemsView = "notLoaded"
        });
        var latest = turns.GetProperty("data")[0].GetProperty("id").GetString();
        var earlier = await CallAsync(session, "thread/turns/list", new
        {
            threadId = thread, cursor = turns.GetProperty("nextCursor").GetString(),
            sortDirection = "desc", limit = 1, itemsView = "notLoaded"
        });
        var oldest = earlier.GetProperty("data")[0].GetProperty("id").GetString();
        var bootstrap = resumed.GetProperty("itemsBackwardsCursor").GetString();
        fixture.AppendLateCommand("turn-1", "not in the opening snapshot");
        var seen = new HashSet<string>();
        foreach (var turn in new[] { latest, oldest })
        {
            string? cursor = bootstrap;
            do
            {
                var page = await CallAsync(session, "thread/items/list", new
                {
                    threadId = thread, turnId = turn, cursor, sortDirection = "desc", limit = 1
                });
                foreach (var row in page.GetProperty("data").EnumerateArray())
                {
                    Assert.Equal(turn, row.GetProperty("turnId").GetString());
                    Assert.True(seen.Add(row.GetProperty("item").GetProperty("id").GetString()!));
                    Assert.NotEqual("commandExecution", row.GetProperty("item").GetProperty("type").GetString());
                }
                cursor = page.GetProperty("nextCursor").GetString();
                if (cursor is not null)
                    await Assert.ThrowsAsync<ArgumentException>(() => CallAsync(session, "thread/items/list", new
                    {
                        threadId = thread, turnId = turn == latest ? oldest : latest, cursor, sortDirection = "desc"
                    }));
            } while (cursor is not null);
        }
        Assert.Equal(8, seen.Count);
        Assert.DoesNotContain(fixture.Transport.Requests, request =>
            request.Method == "thread/turns/list" && request.Parameters.GetProperty("itemsView").GetString() != "none");
    }

    [Theory]
    [InlineData("asc", "summary")]
    [InlineData("desc", "notLoaded")]
    [InlineData("desc", "full")]
    public async Task ExcludeTurnsDoesNotDiscardAnExplicitInitialPage(string direction, string view)
    {
        using var fixture = new GatewayHistoryFixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new
        {
            threadId = thread, excludeTurns = true,
            initialTurnsPage = new { limit = 1, itemsView = view, sortDirection = direction }
        });
        Assert.Empty(resumed.GetProperty("thread").GetProperty("turns").EnumerateArray());
        var initial = resumed.GetProperty("initialTurnsPage");
        Assert.Single(initial.GetProperty("data").EnumerateArray());
        Assert.Equal(view, initial.GetProperty("data")[0].GetProperty("itemsView").GetString());
        Assert.NotNull(initial.GetProperty("nextCursor").GetString());
        var latest = await CallAsync(session, "thread/turns/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("turnsBackwardsCursor").GetString(),
            limit = 1, itemsView = "notLoaded", sortDirection = "desc"
        });
        Assert.Equal(direction == "desc",
            initial.GetProperty("data")[0].GetProperty("id").GetString() == latest.GetProperty("data")[0].GetProperty("id").GetString());
        if (view == "full") Assert.Equal(4, initial.GetProperty("data")[0].GetProperty("items").GetArrayLength());
        else Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "thread/items/list");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public async Task ClientOpeningTimelineNormalizesPageSizeAndContinuesEarlier(int requestedLimit)
    {
        using var fixture = new GatewayHistoryFixture(turns: 1, itemsPerTurn: 510) { SourcePageSize = 500 };
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        var newest = await CallAsync(session, "thread/timeline/list", new { threadId = thread, limit = requestedLimit });
        var newestRows = newest.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(500, newestRows.Length);
        Assert.Equal(500, fixture.Transport.Requests.Last().Parameters.GetProperty("limit").GetInt32());
        var earlier = await CallAsync(session, "thread/timeline/list", new
        {
            threadId = thread, limit = 500, cursor = newest.GetProperty("nextCursor").GetString()
        });
        var earlierRows = earlier.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(12, earlierRows.Length);
        Assert.True(earlierRows.Max(row => row.GetProperty("position").GetInt64()) <
            newestRows.Min(row => row.GetProperty("position").GetInt64()));
        Assert.Equal("turnStarted", earlierRows[0].GetProperty("type").GetString());
        Assert.Equal("turnCompleted", newestRows[^1].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, earlier.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task TimelinePagesKeepCanonicalOrderAndLateCompletionAndMatchTurnItemDetails()
    {
        using var fixture = new GatewayHistoryFixture();
        fixture.AppendLateCommand("turn-1", "late output");
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        var timeline = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await CallAsync(session, "thread/timeline/list", new { threadId = thread, cursor, limit = 2 });
            Assert.Equal(JsonValueKind.Null, page.GetProperty("activeRealtimeSessionAtPageStart").ValueKind);
            var rows = page.GetProperty("data").EnumerateArray().ToArray();
            var positions = rows.Select(row => row.GetProperty("position").GetInt64()).ToArray();
            Assert.Equal(positions.Order(), positions);
            Assert.All(rows, row => Assert.False(row.TryGetProperty("outputDeferred", out _)));
            if (timeline.Count > 0) Assert.True(positions.Max() < timeline.Min(row => row.GetProperty("position").GetInt64()));
            timeline.AddRange(rows);
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(13, timeline.Count);
        Assert.Equal(13, timeline.Select(row => row.GetProperty("position").GetInt64()).Distinct().Count());
        var command = Assert.Single(timeline, row => row.GetProperty("type").GetString() == "item" &&
            row.GetProperty("item").GetProperty("type").GetString() == "commandExecution");
        var commandTurn = command.GetProperty("turnId").GetString();
        var completed = Assert.Single(timeline, row => row.GetProperty("type").GetString() == "turnCompleted" &&
            row.GetProperty("turnId").GetString() == commandTurn);
        Assert.True(command.GetProperty("position").GetInt64() > completed.GetProperty("position").GetInt64());
        var items = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, turnId = commandTurn,
            cursor = resumed.GetProperty("itemsBackwardsCursor").GetString(), sortDirection = "desc"
        });
        var fromItems = items.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("item").GetProperty("id").GetString()).Order().ToArray();
        var fromTimeline = timeline.Where(row => row.GetProperty("type").GetString() == "item" && row.GetProperty("turnId").GetString() == commandTurn)
            .Select(row => row.GetProperty("item").GetProperty("id").GetString()).Order().ToArray();
        Assert.Equal(fromTimeline, fromItems);
        var detail = await CallAsync(session, "thread/items/read", new
        {
            threadId = thread, turnId = commandTurn, itemIds = new[] { command.GetProperty("item").GetProperty("id").GetString() }
        });
        var detailed = detail.GetProperty("data")[0].GetProperty("item");
        foreach (var field in new[] { "id", "type", "command", "status", "aggregatedOutput" })
            Assert.Equal(command.GetProperty("item").GetProperty(field).GetString(), detailed.GetProperty(field).GetString());
    }

    [Fact]
    public async Task LargeTurnCrossesSourceAndTimelinePagesWithoutFullTurnHydration()
    {
        using var fixture = new GatewayHistoryFixture(turns: 1, itemsPerTurn: 130);
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        var shells = await CallAsync(session, "thread/turns/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("turnsBackwardsCursor").GetString(),
            itemsView = "notLoaded", sortDirection = "desc"
        });
        var turn = shells.GetProperty("data")[0].GetProperty("id").GetString();
        var items = new HashSet<string>();
        string? cursor = resumed.GetProperty("itemsBackwardsCursor").GetString();
        do
        {
            var page = await CallAsync(session, "thread/items/list", new
            {
                threadId = thread, turnId = turn, cursor, sortDirection = "desc", limit = 37
            });
            foreach (var row in page.GetProperty("data").EnumerateArray())
                Assert.True(items.Add(row.GetProperty("item").GetProperty("id").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(130, items.Count);
        var timelineItems = new HashSet<string>();
        cursor = null;
        var boundaries = new List<string>();
        do
        {
            var page = await CallAsync(session, "thread/timeline/list", new { threadId = thread, cursor, limit = 37 });
            foreach (var row in page.GetProperty("data").EnumerateArray())
            {
                if (row.GetProperty("type").GetString() == "item")
                    Assert.True(timelineItems.Add(row.GetProperty("item").GetProperty("id").GetString()!));
                else boundaries.Add(row.GetProperty("type").GetString()!);
            }
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.True(items.SetEquals(timelineItems));
        Assert.Equal(new[] { "turnCompleted", "turnStarted" }, boundaries);
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "thread/turns/list" &&
            request.Parameters.GetProperty("itemsView").GetString() != "none");
    }

    [Fact]
    public async Task BootstrapItemContinuationKeepsSourceBoundaryWhenAlreadyReadRowsAreRemoved()
    {
        using var fixture = new GatewayHistoryFixture(turns: 1, itemsPerTurn: 4);
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        var first = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("itemsBackwardsCursor").GetString(),
            sortDirection = "desc", limit = 1
        });
        Assert.Equal("turn-1 content 3", first.GetProperty("data")[0].GetProperty("item").GetProperty("text").GetString());
        fixture.RemoveItem("turn-1-item-3");
        var continued = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, cursor = first.GetProperty("nextCursor").GetString(),
            sortDirection = "desc", limit = 1
        });
        Assert.Equal("turn-1 content 2", Assert.Single(continued.GetProperty("data").EnumerateArray())
            .GetProperty("item").GetProperty("text").GetString());
    }

    [Fact]
    public async Task EmptyRawPageRetainsItsBoundedContinuation()
    {
        using var fixture = new GatewayHistoryFixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        fixture.EmptyNextItemPage = true;
        var empty = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("itemsBackwardsCursor").GetString(), sortDirection = "desc"
        });
        Assert.Empty(empty.GetProperty("data").EnumerateArray());
        Assert.NotNull(empty.GetProperty("nextCursor").GetString());
        var continued = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, cursor = empty.GetProperty("nextCursor").GetString(), sortDirection = "desc"
        });
        Assert.Equal(8, continued.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task ExplicitFullReadTraversesAllPagesInsteadOfSilentlyReturningTwentyTurns()
    {
        using var fixture = new GatewayHistoryFixture(turns: 23, itemsPerTurn: 5) { SourcePageSize = 3 };
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var read = await CallAsync(session, "thread/read", new { threadId = thread, includeTurns = true });
        var turns = read.GetProperty("thread").GetProperty("turns").EnumerateArray().ToArray();
        Assert.Equal(23, turns.Length);
        Assert.All(turns, turn =>
        {
            Assert.Equal("full", turn.GetProperty("itemsView").GetString());
            Assert.Equal(5, turn.GetProperty("items").GetArrayLength());
            Assert.Equal("final_answer", turn.GetProperty("items")[4].GetProperty("phase").GetString());
        });
        Assert.True(fixture.Transport.Requests.Count(request => request.Method == "thread/turns/list") > 1);
        Assert.True(fixture.Transport.Requests.Count(request => request.Method == "thread/items/list") > turns.Length);
    }

    [Fact]
    public async Task LargeOutputLoadsOnDemandAndReconnectDoesNotReplayOrSubmitWork()
    {
        using var fixture = new GatewayHistoryFixture();
        var output = new string('x', 5 * 1024 * 1024);
        fixture.AppendLateCommand("turn-1", output);
        var statuses = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message =>
        {
            if (message.GetProperty("method").GetString() == "thread/status/changed") statuses.Writer.TryWrite(message);
        });
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "thread/items/list");
        var shells = await CallAsync(session, "thread/turns/list", new
        {
            threadId = thread, cursor = resumed.GetProperty("turnsBackwardsCursor").GetString(),
            itemsView = "notLoaded", sortDirection = "desc"
        });
        var turn = shells.GetProperty("data")[1].GetProperty("id").GetString();
        var page = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, turnId = turn, cursor = resumed.GetProperty("itemsBackwardsCursor").GetString(),
            sortDirection = "desc", deferOutput = new { commandExecution = true }
        });
        var command = Assert.Single(page.GetProperty("data").EnumerateArray(), row => row.GetProperty("item").GetProperty("type").GetString() == "commandExecution");
        Assert.True(command.GetProperty("outputDeferred").GetBoolean());
        Assert.Equal(JsonValueKind.Null, command.GetProperty("item").GetProperty("aggregatedOutput").ValueKind);
        var itemId = command.GetProperty("item").GetProperty("id").GetString();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await statuses.Reader.ReadAsync(deadline.Token);
        fixture.Transport.Disconnect();
        await statuses.Reader.ReadAsync(deadline.Token);
        Assert.Equal(1, fixture.Transport.ReconnectCount);
        var detail = await CallAsync(session, "thread/items/read", new { threadId = thread, turnId = turn, itemIds = new[] { itemId } });
        Assert.Equal(output, detail.GetProperty("data")[0].GetProperty("item").GetProperty("aggregatedOutput").GetString());
        var reopened = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        var again = await CallAsync(session, "thread/items/list", new
        {
            threadId = thread, turnId = turn, cursor = reopened.GetProperty("itemsBackwardsCursor").GetString(),
            sortDirection = "desc", limit = 1, deferOutput = new { commandExecution = true }
        });
        Assert.Equal(itemId, again.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString());
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method is "turn/start" or "turn/interrupt");
    }

    [Fact]
    public async Task EmptyHistoryReturnsNullBootstrapAndNoFabricatedRealtime()
    {
        using var fixture = new GatewayHistoryFixture(turns: 0);
        await using var session = await fixture.ConnectAsync();
        var thread = await DiscoverAsync(session);
        var resumed = await CallAsync(session, "thread/resume", new { threadId = thread, excludeTurns = true });
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("turnsBackwardsCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("itemsBackwardsCursor").ValueKind);
        var timeline = await CallAsync(session, "thread/timeline/list", new { threadId = thread });
        Assert.Empty(timeline.GetProperty("data").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, timeline.GetProperty("nextCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, timeline.GetProperty("activeRealtimeSessionAtPageStart").ValueKind);
    }
}
