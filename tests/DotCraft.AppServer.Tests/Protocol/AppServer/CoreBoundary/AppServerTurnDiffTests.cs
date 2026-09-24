using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerTurnDiffTests
{
    private const string Diff =
        "diff --git a/a.txt b/a.txt\nnew file mode 100644\nindex 0000000000000000000000000000000000000000..257cc5642cb1a054f08cc83f2d943e56fd3ebe99\n--- /dev/null\n+++ b/a.txt\n@@ -0,0 +1 @@\n+foo\n";

    [Theory]
    [InlineData(Diff)]
    [InlineData("")]
    public async Task RunAsync_TurnDiffUpdated_SendsThreadTurnAndDiffOnly(string diff)
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await DispatchAsync(harness, BuildTurnDiffUpdatedEvent(diff));

        var message = Assert.Single(harness.Transport.DrainSent());
        AppServerTestHarness.AssertIsNotification(message, DotCraft.Protocol.AppServer.AppServerMethodNames.TurnDiffUpdated);
        var parameters = message.RootElement.GetProperty("params");
        Assert.Equal(
            new[] { "diff", "threadId", "turnId" },
            parameters.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("thread_001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn_001", parameters.GetProperty("turnId").GetString());
        Assert.Equal(JsonValueKind.String, parameters.GetProperty("diff").ValueKind);
        Assert.Equal(diff, parameters.GetProperty("diff").GetString());
    }

    [Fact]
    public async Task RunAsync_TurnDiffUpdatedOptedOut_SendsNothing()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [DotCraft.Protocol.AppServer.AppServerMethodNames.TurnDiffUpdated]);

        await DispatchAsync(harness, BuildTurnDiffUpdatedEvent(Diff));

        Assert.Empty(harness.Transport.DrainSent());
    }

    private static Task DispatchAsync(AppServerTestHarness harness, SessionEvent evt) =>
        new AppServerEventDispatcher(
            Stream(evt),
            harness.Connection,
            harness.Transport,
            harness.Service).RunAsync();

    private static SessionEvent BuildTurnDiffUpdatedEvent(string diff) => new()
    {
        EventId = "e_turn_diff",
        EventType = SessionEventType.TurnDiffUpdated,
        ThreadId = "thread_001",
        TurnId = "turn_001",
        Timestamp = DateTimeOffset.UtcNow,
        Payload = new TurnDiffUpdatedPayload { Diff = diff }
    };

    private static async IAsyncEnumerable<SessionEvent> Stream(SessionEvent evt)
    {
        await Task.Yield();
        yield return evt;
    }
}
