using System.Text.Json;
using DotCraft.Sessions;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerThreadSearchTests : IDisposable
{
    private readonly CoreAppServerTestHarness _h = new();

    public AppServerThreadSearchTests() => _h.InitializeAsync().GetAwaiter().GetResult();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task ThreadSearch_PagesThroughVisibleThreadsWhoseConversationContainsTheTerm()
    {
        var asked = await SeedAsync(
            await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Renderer work"),
            "Where does the Retry-Budget's limit live?",
            "Let me look.");
        var answered = await SeedAsync(
            await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Config notes"),
            "Where is it configured?",
            "The retry-budget's value is in config.json.");
        await SeedAsync(
            await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Retry-budget's title only"),
            "Nothing to see.",
            "Indeed.");
        var internalThread = await _h.Service.CreateThreadAsync(_h.Identity, displayName: "Internal");
        internalThread.Metadata[ThreadVisibility.InternalMetadataKey] = "welcome-suggestions";
        await SeedAsync(internalThread, "retry-budget's internal copy", "Done.");
        await SeedAsync(
            await _h.Service.CreateThreadAsync(
                new SessionIdentity
                {
                    ChannelName = SubAgentThreadOrigin.ChannelName,
                    UserId = _h.Identity.UserId,
                    WorkspacePath = _h.Identity.WorkspacePath,
                    ChannelContext = asked.Id
                },
                displayName: "Worker",
                source: ThreadSource.ForSubAgent(new SubAgentThreadSource
                {
                    ParentThreadId = asked.Id,
                    RootThreadId = asked.Id,
                    Depth = 1,
                    AgentPath = "/root/worker"
                })),
            "retry-budget's worker copy",
            "Done.");

        var first = await SearchAsync(new { searchTerm = "  RETRY-budget's ", limit = 1 });
        var second = await SearchAsync(new { searchTerm = "RETRY-budget's", limit = 1, cursor = first.GetProperty("nextCursor").GetString() });

        var found = first.GetProperty("data").EnumerateArray().Concat(second.GetProperty("data").EnumerateArray())
            .ToDictionary(match => match.GetProperty("thread").GetProperty("id").GetString()!, match => match.GetProperty("snippet").GetString());
        Assert.Equal(1, first.GetProperty("data").GetArrayLength());
        Assert.Equal(
            new Dictionary<string, string?>
            {
                [asked.Id] = "Where does the Retry-Budget's limit live?",
                [answered.Id] = "The retry-budget's value is in config.json."
            },
            found);
        Assert.False(second.TryGetProperty("nextCursor", out _));
    }

    private async Task<SessionThread> SeedAsync(SessionThread thread, string userText, string agentText)
    {
        var now = DateTimeOffset.UtcNow;
        SessionItem Item(string id, ItemType type, object payload) => new()
        {
            Id = id,
            TurnId = "turn_1",
            Type = type,
            Status = ItemStatus.Completed,
            Payload = payload,
            CreatedAt = now
        };
        var user = Item("item_user", ItemType.UserMessage, new UserMessagePayload { Text = userText });
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_1",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            Input = user,
            Items = [user, Item("item_agent", ItemType.AgentMessage, new AgentMessagePayload { Text = agentText })],
            StartedAt = now,
            CompletedAt = now
        });
        await _h.Service.SeedThreadAsync(thread);
        return thread;
    }

    private async Task<JsonElement> SearchAsync(object parameters)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSearch, parameters));
        using var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        return doc.RootElement.GetProperty("result").Clone();
    }
}
