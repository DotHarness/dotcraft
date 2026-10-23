using System.Text.Json;
using DotCraft.Sessions;
using SubAgentProgressEntry = DotCraft.Sessions.SubAgentProgressEntry;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerSubAgentProgressTests
{
    [Fact]
    public async Task SubAgentProgress_SerializesEntriesAndDeliversTheFinalSnapshotBeforeTurnCompleted()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(thread.Id, BuildTurn(
            thread.Id,
            [
                new SubAgentProgressEntry { Label = "agent-A", CurrentTool = "GrepFiles", InputTokens = 200, OutputTokens = 80, IsCompleted = true },
                new SubAgentProgressEntry { Label = "agent-B", InputTokens = 100, OutputTokens = 30, IsCompleted = false }
            ],
            [
                new SubAgentProgressEntry { Label = "agent-A", InputTokens = 200, OutputTokens = 80, IsCompleted = true },
                new SubAgentProgressEntry { Label = "agent-B", InputTokens = 400, OutputTokens = 160, IsCompleted = true }
            ]));

        await StartTurnAsync(harness, thread.Id);
        var all = await harness.Transport.WaitAndDrainAsync(7, TimeSpan.FromSeconds(10));
        var methods = all.Select(Method).ToList();
        var progress = all.Where(d => Method(d) == DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProgress).ToList();

        Assert.Equal(2, progress.Count);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods[^1]);

        var first = progress[0].RootElement.GetProperty("params");
        Assert.Equal(thread.Id, first.GetProperty("threadId").GetString());
        Assert.Equal("turn_001", first.GetProperty("turnId").GetString());
        var agentA = Entry(first, "agent-A");
        Assert.Equal("GrepFiles", agentA.GetProperty("currentTool").GetString());
        Assert.Equal(200, agentA.GetProperty("inputTokens").GetInt64());
        Assert.Equal(80, agentA.GetProperty("outputTokens").GetInt64());
        Assert.True(agentA.GetProperty("isCompleted").GetBoolean());
        Assert.False(Entry(first, "agent-B").GetProperty("isCompleted").GetBoolean());

        var finalAgentB = Entry(progress[^1].RootElement.GetProperty("params"), "agent-B");
        Assert.True(finalAgentB.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(400, finalAgentB.GetProperty("inputTokens").GetInt64());
        Assert.Equal(160, finalAgentB.GetProperty("outputTokens").GetInt64());
    }

    [Fact]
    public async Task SubAgentProgress_OptedOut_NotSent()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProgress]);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(thread.Id, BuildTurn(
            thread.Id,
            [new SubAgentProgressEntry { Label = "default-task", InputTokens = 100, OutputTokens = 50 }]));

        await StartTurnAsync(harness, thread.Id);
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProgress, all.Select(Method));
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, Method(all[^1]));
    }

    private static Task StartTurnAsync(AppServerTestHarness harness, string threadId) =>
        harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "Hello" } }
        }));

    private static string? Method(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("method", out var method) ? method.GetString() : null;

    private static JsonElement Entry(JsonElement progressParams, string label) =>
        Assert.Single(
            progressParams.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("label").GetString() == label);

    private static SessionEvent[] BuildTurn(string threadId, params IReadOnlyList<SubAgentProgressEntry>[] snapshots)
    {
        const string turnId = "turn_001";
        var item = AppServerTestHarness.MakeAgentMessageItem(turnId);
        var now = DateTimeOffset.UtcNow;
        var events = new List<SessionEvent>
        {
            new()
            {
                EventId = "e_started", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = AppServerTestHarness.MakeTurn(threadId, turnId)
            },
            new()
            {
                EventId = "e_item_started", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = item
            }
        };
        events.AddRange(snapshots.Select((entries, index) => new SessionEvent
        {
            EventId = $"e_progress_{index}", EventType = SessionEventType.SubAgentProgress,
            ThreadId = threadId, TurnId = turnId, Timestamp = now,
            Payload = new SubAgentProgressPayload { Entries = entries }
        }));
        events.Add(new SessionEvent
        {
            EventId = "e_item_completed", EventType = SessionEventType.ItemCompleted,
            ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now,
            Payload = AppServerTestHarness.MakeCompletedAgentMessageItem(turnId)
        });
        events.Add(new SessionEvent
        {
            EventId = "e_completed", EventType = SessionEventType.TurnCompleted,
            ThreadId = threadId, TurnId = turnId, Timestamp = now,
            Payload = AppServerTestHarness.MakeCompletedTurn(threadId, turnId)
        });
        return [.. events];
    }
}
