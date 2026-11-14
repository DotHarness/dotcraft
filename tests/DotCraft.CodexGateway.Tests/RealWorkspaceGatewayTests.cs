using System.Text.Json;
using DotCraft.Sdk;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class RealWorkspaceGatewayTests
{
    [Fact]
    public async Task OneHostSharesTwoRealWorkspaceSessionsWithoutCollidingTurnOrItemRoutes()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        await using var desktopA = await fixture.A.ConnectAsync(null, ct);
        await using var desktopB = await fixture.B.ConnectAsync(null, ct);
        var sourceA = await CreateSourceThreadAsync(desktopA, fixture.A.Root, "desktop A", ct);
        var sourceB = await CreateSourceThreadAsync(desktopB, fixture.B.Root, "desktop B", ct);
        var turnA = await SourceTurnAsync(desktopA, sourceA, "first", ct);
        var turnB = await SourceTurnAsync(desktopB, sourceB, "first", ct);
        Assert.Equal("turn_001", turnA.GetProperty("id").GetString());
        Assert.Equal(turnA.GetProperty("id").GetString(), turnB.GetProperty("id").GetString());
        Assert.Equal("item_001", turnA.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(turnA.GetProperty("items")[0].GetProperty("id").GetString(),
            turnB.GetProperty("items")[0].GetProperty("id").GetString());

        await using var phone = new RealGatewayPeer(fixture.State, fixture.A, fixture.B);
        await InitializeAsync(phone, ct);
        var requirements = await phone.RequestAsync("configRequirements/read", new { }, ct);
        var profile = requirements.GetProperty("requirements").GetProperty("defaultPermissions").GetString();
        var profiles = await phone.RequestAsync("permissionProfile/list", new { }, ct);
        Assert.Contains(profiles.GetProperty("data").EnumerateArray(), item =>
            item.GetProperty("id").GetString() == profile && item.GetProperty("allowed").GetBoolean());
        var projects = await phone.RequestAsync("project/list", new { }, ct);
        Assert.Equal(2, projects.GetProperty("data").GetArrayLength());
        Assert.Equal(0, phone.SourceConnections);
        var firstPage = await phone.RequestAsync("thread/list", new { limit = 1 }, ct);
        var nextPage = await phone.RequestAsync("thread/list",
            new { limit = 1, cursor = firstPage.GetProperty("nextCursor").GetString() }, ct);
        var threads = firstPage.GetProperty("data").EnumerateArray()
            .Concat(nextPage.GetProperty("data").EnumerateArray()).ToArray();
        var externalA = threads.Single(thread => thread.GetProperty("name").GetString() == "desktop A")
            .GetProperty("id").GetString()!;
        var externalB = threads.Single(thread => thread.GetProperty("name").GetString() == "desktop B")
            .GetProperty("id").GetString()!;
        var historyA = await phone.RequestAsync("thread/items/list", new { threadId = externalA }, ct);
        var historyB = await phone.RequestAsync("thread/items/list", new { threadId = externalB }, ct);
        Assert.Contains("A:first", historyA.GetRawText());
        Assert.DoesNotContain("B:first", historyA.GetRawText());
        Assert.Contains("B:first", historyB.GetRawText());
        Assert.NotEqual(historyA.GetProperty("data")[0].GetProperty("turnId").GetString(),
            historyB.GetProperty("data")[0].GetProperty("turnId").GetString());
        Assert.NotEqual(historyA.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString(),
            historyB.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString());

        await phone.RequestAsync("thread/resume", new { threadId = externalA }, ct);
        var models = await phone.RequestAsync("model/list", new { }, ct);
        var model = Assert.Single(models.GetProperty("data").EnumerateArray()).GetProperty("id").GetString();
        var accepted = await phone.RequestAsync("turn/start", new
        {
            threadId = externalA, model, input = new[] { new { type = "text", text = "phone continuation" } }
        }, ct);
        var delta = await phone.NextNotificationAsync("item/agentMessage/delta", externalA, ct);
        var completed = await phone.NextNotificationAsync("turn/completed", externalA, ct);
        var projectedHistory = await phone.RequestAsync("thread/items/list", new { threadId = externalA }, ct);
        Assert.Contains(projectedHistory.GetProperty("data").EnumerateArray(), row =>
            row.GetProperty("item").GetProperty("id").GetString() == delta.GetProperty("itemId").GetString());
        Assert.Equal(accepted.GetProperty("turn").GetProperty("id").GetString(),
            completed.GetProperty("turn").GetProperty("id").GetString());
        var sourceHistory = await desktopA.RequestRawAsync("thread/items/list", new { threadId = sourceA }, ct);
        Assert.Contains("A:phone continuation", sourceHistory.GetRawText());
        var untouched = await desktopB.RequestRawAsync("thread/turns/list", new { threadId = sourceB }, ct);
        Assert.Single(untouched.GetProperty("data").EnumerateArray());

        var projectB = phone.Catalog.Workspaces.Single(workspace => workspace.Path == fixture.B.Root);
        var created = await phone.RequestAsync("thread/start", new
        {
            projectId = projectB.Id, model, permissions = profile, summary = "none", serviceTier = (string?)null,
            collaborationMode = new { mode = "default", settings = new { model, reasoning_effort = (string?)null, developer_instructions = (string?)null } }
        }, ct);
        var newThread = created.GetProperty("thread").GetProperty("id").GetString()!;
        await phone.RequestAsync("thread/name/set", new { threadId = newThread, name = "phone B" }, ct);
        var sourceList = await desktopB.RequestRawAsync("thread/list", new
        {
            identity = new { channelName = "appserver", userId = Environment.UserName, workspacePath = fixture.B.Root },
            scope = "workspace"
        }, ct);
        Assert.Contains(sourceList.GetProperty("data").EnumerateArray(),
            thread => thread.GetProperty("displayName").GetString() == "phone B");
        await phone.RequestAsync("turn/start", new
        {
            threadId = newThread, sandboxPolicy = created.GetProperty("sandbox"),
            approvalPolicy = created.GetProperty("approvalPolicy"), summary = "none", serviceTier = (string?)null,
            collaborationMode = created.GetProperty("collaborationMode"),
            input = new[] { new { type = "text", text = "hello" } }
        }, ct);
        var firstReply = await phone.NextNotificationAsync("turn/completed", newThread, ct);
        Assert.Equal("completed", firstReply.GetProperty("turn").GetProperty("status").GetString());
        Assert.Contains("B:hello", firstReply.GetRawText());
        var blocking = await phone.RequestAsync("turn/start", new
        {
            threadId = newThread, permissions = "dotcraft", approvalPolicy = created.GetProperty("approvalPolicy"),
            summary = "none", serviceTier = (string?)null, collaborationMode = created.GetProperty("collaborationMode"),
            input = new[] { new { type = "text", text = "hold" } }
        }, ct);
        await fixture.B.Model.Blocked.Task.WaitAsync(ct);
        await phone.RequestAsync("turn/interrupt", new
        {
            threadId = newThread, turnId = blocking.GetProperty("turn").GetProperty("id").GetString()
        }, ct);
        var interrupted = await phone.NextNotificationAsync("turn/completed", newThread, ct);
        Assert.Equal("interrupted", interrupted.GetProperty("turn").GetProperty("status").GetString());
    }

    [Fact]
    public async Task RealSourceQuestionCanBeAnsweredByPhoneAndObservedByAnotherClient()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        await using var desktop = await fixture.A.ConnectAsync(null, ct);
        var source = await CreateSourceThreadAsync(desktop, fixture.A.Root, "shared question", ct);
        await using var phone = new RealGatewayPeer(fixture.State, fixture.A, fixture.B);
        await InitializeAsync(phone, ct);
        var listed = await phone.RequestAsync("thread/list", new { }, ct);
        var external = Assert.Single(listed.GetProperty("data").EnumerateArray()).GetProperty("id").GetString()!;
        await phone.RequestAsync("thread/resume", new { threadId = external }, ct);
        var started = await phone.RequestAsync("turn/start", new
        {
            threadId = external, input = new[] { new { type = "text", text = "question" } }
        }, ct);
        var question = await phone.NextRequestAsync("item/tool/requestUserInput", ct);
        Assert.Equal(external, question.GetProperty("params").GetProperty("threadId").GetString());
        Assert.Equal("choice", question.GetProperty("params").GetProperty("questions")[0].GetProperty("id").GetString());
        await phone.AnswerAsync(question, new
        {
            answers = new Dictionary<string, object> { ["choice"] = new { answers = new[] { "B" } } }
        }, ct);
        var completed = await phone.NextNotificationAsync("turn/completed", external, ct);
        Assert.Equal(started.GetProperty("turn").GetProperty("id").GetString(),
            completed.GetProperty("turn").GetProperty("id").GetString());
        var persisted = await desktop.RequestRawAsync("thread/items/list", new { threadId = source }, ct);
        var answer = Assert.Single(persisted.GetProperty("data").EnumerateArray(),
            row => row.GetProperty("item").GetProperty("type").GetString() == "userInputResponse");
        Assert.Equal("B", answer.GetProperty("item").GetProperty("payload").GetProperty("response")
            .GetProperty("answers").GetProperty("choice").GetProperty("answers")[0].GetString());
        Assert.Equal("completed", completed.GetProperty("turn").GetProperty("status").GetString());
        var final = Assert.Single(phone.AssistantEvents, item => item.Method == "item/completed");
        Assert.Equal("final_answer", final.Phase);
        var begin = Assert.Single(phone.AssistantEvents, item => item.Method == "item/started");
        Assert.Equal(final.ItemId, begin.ItemId);
        Assert.Null(begin.Phase);
    }

    [Fact]
    public async Task LargeHistoryRestoresSummaryThenReadsDetailWithoutHydratingOnMetadataResume()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        await using var desktop = await fixture.A.ConnectAsync(null, ct);
        await CreateSourceThreadAsync(desktop, fixture.A.Root, "large history", ct);
        string thread;
        string turn;
        await using (var phone = new RealGatewayPeer(fixture.State, fixture.A))
        {
            await InitializeAsync(phone, ct);
            var listed = await phone.RequestAsync("thread/list", new { }, ct);
            thread = Assert.Single(listed.GetProperty("data").EnumerateArray()).GetProperty("id").GetString()!;
            await phone.RequestAsync("thread/resume", new { threadId = thread, excludeTurns = true }, ct);
            var admitted = await phone.RequestAsync("turn/start", new
            {
                threadId = thread, permissions = "dotcraft", summary = "none", serviceTier = (string?)null,
                input = new[] { new { type = "text", text = "large-history" } }
            }, ct);
            turn = admitted.GetProperty("turn").GetProperty("id").GetString()!;
            var question = await phone.NextRequestAsync("item/tool/requestUserInput", ct);
            await phone.AnswerAsync(question, new
            {
                answers = new Dictionary<string, object> { ["choice"] = new { answers = new[] { "A" } } }
            }, ct);
            var completed = await phone.NextNotificationAsync("turn/completed", thread, ct);
            var outcome = completed.GetProperty("turn");
            Assert.True(outcome.GetProperty("status").GetString() == "completed",
                outcome.TryGetProperty("error", out var error) ? error.ToString() : "Source did not complete.");
        }

        fixture.A.Requests.Clear();
        await using var reopened = new RealGatewayPeer(fixture.State, fixture.A);
        await InitializeAsync(reopened, ct);
        var metadata = await reopened.RequestAsync("thread/resume", new { threadId = thread, excludeTurns = true }, ct);
        Assert.Empty(metadata.GetProperty("thread").GetProperty("turns").EnumerateArray());
        Assert.DoesNotContain(fixture.A.Requests, request => request.Method is "thread/items/list" or "thread/turns/list");
        Assert.All(fixture.A.Requests.Where(request => request.Method == "thread/resume"), request =>
            Assert.True(!request.Parameters.TryGetProperty("initialTurnsPage", out var initial) || initial.ValueKind == JsonValueKind.Null));

        Assert.NotNull(metadata.GetProperty("turnsBackwardsCursor").GetString());
        Assert.NotNull(metadata.GetProperty("itemsBackwardsCursor").GetString());
        var summary = await reopened.RequestAsync("thread/turns/list", new
        {
            threadId = thread, itemsView = "summary", sortDirection = "desc",
            cursor = metadata.GetProperty("turnsBackwardsCursor").GetString()
        }, ct);
        var summaryTurn = Assert.Single(summary.GetProperty("data").EnumerateArray());
        Assert.Equal("completed", summaryTurn.GetProperty("status").GetString());
        var items = summaryTurn.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("userMessage", items[0].GetProperty("type").GetString());
        Assert.Equal("final_answer", items[1].GetProperty("phase").GetString());
        Assert.True(items[1].GetProperty("text").GetString()!.Length < 1024);
        var timeline = await reopened.RequestAsync("thread/timeline/list", new { threadId = thread, limit = 500 }, ct);
        Assert.Equal(JsonValueKind.Null, timeline.GetProperty("activeRealtimeSessionAtPageStart").ValueKind);
        Assert.Contains(timeline.GetProperty("data").EnumerateArray(), entry => entry.GetProperty("type").GetString() == "turnStarted");
        Assert.Contains(timeline.GetProperty("data").EnumerateArray(), entry => entry.GetProperty("type").GetString() == "turnCompleted");
        var timelineAnswer = Assert.Single(timeline.GetProperty("data").EnumerateArray(), entry =>
            entry.GetProperty("type").GetString() == "item" && entry.GetProperty("item").GetProperty("id").GetString() ==
            items[1].GetProperty("id").GetString());
        Assert.Equal("final_answer", timelineAnswer.GetProperty("item").GetProperty("phase").GetString());

        var page = await reopened.RequestAsync("thread/items/list", new { threadId = thread, turnId = turn, limit = 2 }, ct);
        var detail = Assert.Single(page.GetProperty("data").EnumerateArray(), entry =>
            entry.GetProperty("item").GetProperty("type").GetString() == "agentMessage");
        Assert.Equal(GatewayTestModel.LargeReplyLength, detail.GetProperty("item").GetProperty("text").GetString()!.Length);
        Assert.NotNull(page.GetProperty("nextCursor").GetString());
        var reread = await reopened.RequestAsync("thread/items/read", new
        {
            threadId = thread, turnId = turn,
            itemIds = new[] { detail.GetProperty("item").GetProperty("id").GetString() }
        }, ct);
        Assert.Equal(GatewayTestModel.LargeReplyLength, reread.GetProperty("data")[0].GetProperty("item").GetProperty("text").GetString()!.Length);
    }

    private static Task<JsonElement> InitializeAsync(RealGatewayPeer peer, CancellationToken ct) =>
        peer.RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex-mobile-fixture", version = "1" },
            capabilities = new { experimentalApi = true }
        }, ct);

    private static async Task<string> CreateSourceThreadAsync(DotCraftClient client, string root, string name, CancellationToken ct)
    {
        var result = await client.RequestRawAsync("thread/start", new
        {
            identity = new { channelName = "appserver", userId = Environment.UserName, workspacePath = root }
        }, ct);
        var id = result.GetProperty("thread").GetProperty("id").GetString()!;
        await client.RequestRawAsync("thread/rename", new { threadId = id, displayName = name }, ct);
        return id;
    }

    private static async Task<JsonElement> SourceTurnAsync(DotCraftClient client, string thread, string input, CancellationToken ct)
    {
        var started = await client.RequestRawAsync("turn/start", new
        {
            threadId = thread, input = new[] { new { type = "text", text = input } }
        }, ct);
        await foreach (var notification in client.ReadNotificationsAsync(ct))
            if (notification.Method == "turn/completed" &&
                notification.Params.GetProperty("turn").GetProperty("id").GetString() ==
                started.GetProperty("turn").GetProperty("id").GetString())
                return notification.Params.GetProperty("turn").Clone();
        throw new InvalidOperationException("Source disconnected before its turn completed.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gateway-real-" + Guid.NewGuid().ToString("N"));
        private Fixture()
        {
            A = new(Path.Combine(_root, "A"), "A");
            B = new(Path.Combine(_root, "B"), "B");
        }

        public RealWorkspaceAppServer A { get; }
        public RealWorkspaceAppServer B { get; }
        public string State => Path.Combine(_root, "gateway");

        public static async Task<Fixture> StartAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            await fixture.A.StartAsync(ct);
            await fixture.B.StartAsync(ct);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await B.DisposeAsync();
            await A.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
