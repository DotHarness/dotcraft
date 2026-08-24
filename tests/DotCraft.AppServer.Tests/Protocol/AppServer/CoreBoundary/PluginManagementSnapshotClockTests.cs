using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class PluginManagementSnapshotClockTests
{
    [Fact]
    public void Clock_ObservesSubsystemRevisionsAndAdvancesBatchesMonotonically()
    {
        var clock = new PluginManagementSnapshotClock();

        Assert.Equal(4, clock.Observe(4));
        Assert.Equal(4, clock.Observe(2));
        Assert.Equal(5, clock.Advance(3));
        Assert.Equal(11, clock.Advance(10));
        Assert.Equal(11, clock.Observe(0));
    }
}
