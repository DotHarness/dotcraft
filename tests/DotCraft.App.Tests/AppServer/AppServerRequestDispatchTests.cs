using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerRequestDispatchTests
{
    [Fact]
    public async Task SlowRequests_DoNotPreventThreadStartOrTurnInterrupt()
    {
        var slow = new SlowExtension();
        using var harness = new AppServerTestHarness(protocolExtensions: [slow]);
        await harness.InitializeAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = AppServerRequestDispatcher.RunAsync(
            harness.Transport, harness.Connection, harness.Handler, NullLogger.Instance, cts.Token);
        try
        {
            for (var id = 1; id <= 32; id++)
                harness.Transport.InjectFromClient(InMemoryTransport.BuildRequest("test/slow", new { }, id));
            await slow.AllStarted.Task.WaitAsync(cts.Token);

            harness.Transport.InjectFromClient(InMemoryTransport.BuildRequest(
                "thread/start", new { identity = harness.Identity }, 1000));
            using var started = await harness.Transport.ReadNextSentAsync();
            Assert.Equal(1000, started.RootElement.GetProperty("id").GetInt32());
            using var startedNotification = await harness.Transport.ReadNextSentAsync();
            var threadId = started.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
            var thread = await harness.Service.GetThreadAsync(threadId);
            thread.Turns.Add(new SessionTurn
            {
                Id = "turn_active",
                ThreadId = threadId,
                Status = TurnStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            });

            harness.Transport.InjectFromClient(InMemoryTransport.BuildRequest(
                "turn/interrupt", new { threadId, turnId = "turn_active" }, 1001));
            using var interrupted = await harness.Transport.ReadNextSentAsync();
            Assert.Equal(1001, interrupted.RootElement.GetProperty("id").GetInt32());
            Assert.True(interrupted.RootElement.TryGetProperty("result", out _));
            Assert.Contains((threadId, "turn_active"), harness.Service.CancelledTurns);

            slow.Release.TrySetResult();
            for (var i = 0; i < 32; i++)
            {
                using var completed = await harness.Transport.ReadNextSentAsync();
                Assert.True(completed.RootElement.TryGetProperty("result", out _));
            }
        }
        finally
        {
            slow.Release.TrySetResult();
            await cts.CancelAsync();
            await loop;
        }
    }

    private sealed class SlowExtension : IAppServerProtocolExtension
    {
        private int _started;
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Methods { get; } = ["test/slow"];
        public void ContributeCapabilities(AppServerCapabilityBuilder builder) { }

        public async Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
        {
            if (Interlocked.Increment(ref _started) == 32)
                AllStarted.TrySetResult();
            await Release.Task;
            return new { };
        }
    }
}
