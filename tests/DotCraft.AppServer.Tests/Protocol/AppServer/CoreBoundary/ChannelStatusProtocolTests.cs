using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ChannelStatusProtocolTests
{
    [Fact]
    public async Task ChannelStatus_ReportsStoppedAndBlockedSelectionsAndTheirStableFailureCodes()
    {
        using var harness = new CoreAppServerTestHarness(
            channelStatusProvider: new FakeChannelStatusProvider(
            [
                new ChannelStatusSnapshot
                {
                    Name = "feishu",
                    Category = "social",
                    PluginId = "channel-feishu",
                    Enabled = true,
                    Running = false,
                    RuntimeState = ChannelRuntimeStates.Failed,
                    FailureCode = ChannelFailureCodes.ChannelStartFailed
                },
                new ChannelStatusSnapshot
                {
                    Name = "telegram",
                    Category = "social",
                    PluginId = "channel-telegram",
                    Enabled = false,
                    Running = false,
                    RuntimeState = ChannelRuntimeStates.Stopped
                },
                new ChannelStatusSnapshot
                {
                    Name = "builtin",
                    Category = "builtin",
                    Enabled = true,
                    Running = false
                }
            ]));

        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest("channel/status", new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        var channels = response.RootElement.GetProperty("result").GetProperty("channels")
            .EnumerateArray().ToDictionary(channel => channel.GetProperty("name").GetString()!);

        var failed = channels["feishu"];
        Assert.True(failed.GetProperty("enabled").GetBoolean());
        Assert.False(failed.GetProperty("running").GetBoolean());
        Assert.Equal("failed", failed.GetProperty("runtimeState").GetString());
        Assert.Equal("channelStartFailed", failed.GetProperty("failureCode").GetString());
        Assert.Equal("channel-feishu", failed.GetProperty("pluginId").GetString());

        var stopped = channels["telegram"];
        Assert.False(stopped.GetProperty("enabled").GetBoolean());
        Assert.False(stopped.GetProperty("running").GetBoolean());
        Assert.Equal("stopped", stopped.GetProperty("runtimeState").GetString());
        Assert.Equal("channel-telegram", stopped.GetProperty("pluginId").GetString());
        Assert.False(stopped.TryGetProperty("failureCode", out _));

        var builtin = channels["builtin"];
        Assert.False(builtin.TryGetProperty("pluginId", out _));
    }

    private sealed class FakeChannelStatusProvider(IReadOnlyList<ChannelStatusSnapshot> statuses)
        : IChannelStatusProvider
    {
        public IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses() => statuses;
    }
}
