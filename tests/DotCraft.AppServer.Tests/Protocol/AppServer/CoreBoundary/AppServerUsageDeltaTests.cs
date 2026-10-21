using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerUsageDeltaTests
{
    [Fact]
    public async Task RunAsync_UsageDeltaWithRequestTiming_SendsTiming()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await DispatchAsync(harness, BuildUsageDeltaEvent(new UsageRequestTiming(820.5, 4_000)));

        var message = Assert.Single(harness.Transport.DrainSent());
        AppServerTestHarness.AssertIsNotification(message, DotCraft.Protocol.AppServer.AppServerMethodNames.ItemUsageDelta);
        var timing = message.RootElement.GetProperty("params").GetProperty("requestTiming");
        Assert.Equal(820.5, timing.GetProperty("firstOutputLatencyMs").GetDouble());
        Assert.Equal(4_000, timing.GetProperty("generationMs").GetDouble());
    }

    private static Task DispatchAsync(AppServerTestHarness harness, SessionEvent evt) =>
        new AppServerEventDispatcher(
            Stream(evt),
            harness.Connection,
            harness.Transport,
            harness.Service).RunAsync();

    private static SessionEvent BuildUsageDeltaEvent(UsageRequestTiming timing) => new()
    {
        EventId = "e_usage",
        EventType = SessionEventType.UsageDelta,
        ThreadId = "thread_001",
        TurnId = "turn_001",
        Timestamp = DateTimeOffset.UtcNow,
        Payload = new UsageDeltaPayload
        {
            InputTokens = 1_000,
            OutputTokens = 200,
            LlmCallDelta = 1,
            TurnLlmCalls = 1,
            RequestTiming = timing
        }
    };

    private static async IAsyncEnumerable<SessionEvent> Stream(SessionEvent evt)
    {
        await Task.Yield();
        yield return evt;
    }
}
