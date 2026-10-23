using DotCraft.Configuration;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class AppConfigMonitorTests
{

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
            monitor.NotifyChanged("config/value/write", ["ProviderId"]));

        Assert.Null(ex);
        Assert.Equal(1, invoked);
    }

}
