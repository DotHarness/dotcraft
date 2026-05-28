using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public sealed class AppConfigMonitorTests
{
    [Fact]
    public void Current_ReturnsInjectedInstance()
    {
        var config = new AppConfig { Model = "gpt-4o-mini" };
        var monitor = new AppConfigMonitor(config);

        Assert.Same(config, monitor.Current);
    }

    [Fact]
    public void NotifyChanged_RaisesChanged_WithCorrectPayload()
    {
        var monitor = new AppConfigMonitor(new AppConfig());
        AppConfigChangedEventArgs? received = null;
        monitor.Changed += (_, args) => received = args;

        monitor.NotifyChanged("skills/setEnabled", [ConfigChangeRegions.Skills]);

        Assert.NotNull(received);
        Assert.Equal("skills/setEnabled", received!.Source);
        Assert.Contains(ConfigChangeRegions.Skills, received.Regions);
        Assert.True(received.ChangedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public void SubscriberException_IsSwallowed_AndOtherSubscribersStillInvoked()
    {
        var monitor = new AppConfigMonitor(new AppConfig());
        var invoked = 0;

        monitor.Changed += (_, _) => throw new InvalidOperationException("boom");
        monitor.Changed += (_, _) => invoked++;

        var ex = Record.Exception(() =>
            monitor.NotifyChanged("workspace/config/update", [ConfigChangeRegions.WorkspaceModel]));

        Assert.Null(ex);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void ChangedAt_IsSetPerCall()
    {
        var monitor = new AppConfigMonitor(new AppConfig());
        var timestamps = new List<DateTimeOffset>();
        monitor.Changed += (_, args) => timestamps.Add(args.ChangedAt);

        monitor.NotifyChanged("mcp/upsert", [ConfigChangeRegions.Mcp]);
        Thread.Sleep(5);
        monitor.NotifyChanged("mcp/remove", [ConfigChangeRegions.Mcp]);

        Assert.Equal(2, timestamps.Count);
        Assert.True(timestamps[1] >= timestamps[0]);
    }
}
