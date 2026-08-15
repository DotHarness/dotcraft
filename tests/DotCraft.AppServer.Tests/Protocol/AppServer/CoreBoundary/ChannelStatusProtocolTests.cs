using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ChannelStatusProtocolTests
{
    [Fact]
    public async Task ChannelStatus_ReportsOptionalRuntimeStateAndStableFailureCode()
    {
        using var harness = new CoreAppServerTestHarness(
            channelStatusProvider: new FakeChannelStatusProvider(
            [
                new ChannelStatusSnapshot
                {
                    Name = "feishu",
                    Category = "external",
                    Enabled = true,
                    Running = false,
                    RuntimeState = ChannelRuntimeStates.Failed,
                    FailureCode = ChannelFailureCodes.ExternalChannelStartFailed
                },
                new ChannelStatusSnapshot
                {
                    Name = "telegram",
                    Category = "external",
                    Enabled = true,
                    Running = true,
                    RuntimeState = ChannelRuntimeStates.Running
                },
                new ChannelStatusSnapshot
                {
                    Name = "legacy",
                    Category = "external",
                    Enabled = true,
                    Running = false
                }
            ]));

        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest("channel/status", new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        var channels = response.RootElement.GetProperty("result").GetProperty("channels");

        var failed = channels[0];
        Assert.True(failed.GetProperty("enabled").GetBoolean());
        Assert.False(failed.GetProperty("running").GetBoolean());
        Assert.Equal("failed", failed.GetProperty("runtimeState").GetString());
        Assert.Equal("externalChannelStartFailed", failed.GetProperty("failureCode").GetString());

        var running = channels[1];
        Assert.True(running.GetProperty("enabled").GetBoolean());
        Assert.True(running.GetProperty("running").GetBoolean());
        Assert.Equal("running", running.GetProperty("runtimeState").GetString());
        Assert.False(running.TryGetProperty("failureCode", out _));

        var legacy = channels[2];
        Assert.True(legacy.GetProperty("enabled").GetBoolean());
        Assert.False(legacy.GetProperty("running").GetBoolean());
        Assert.False(legacy.TryGetProperty("runtimeState", out _));
        Assert.False(legacy.TryGetProperty("failureCode", out _));
    }

    private sealed class FakeChannelStatusProvider(IReadOnlyList<ChannelStatusSnapshot> statuses)
        : IChannelStatusProvider
    {
        public IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses() => statuses;
    }
}
