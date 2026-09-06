using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.ViewModels;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class TrayMenuModelTests
{
    private static readonly SatelliteStrings Strings =
        SatelliteStrings.For("en", CultureInfo.InvariantCulture);

    [Fact]
    public void Build_WithoutPairing_OffersOnlyPasteAndQuit()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Offline, [], null, Strings);

        Assert.Equal(Strings["tray.status.offline"], items[0].Text);
        Assert.Contains(items, item => item.Text == Strings["tray.noPeers"]);
        Assert.All(
            Enabled(items),
            item => Assert.Contains(
                item.Command,
                (TrayMenuCommand[])[TrayMenuCommand.PasteInvite, TrayMenuCommand.Quit]));
    }

    [Fact]
    public void Build_WhenConnected_NamesThePeerAndEnablesEveryAction()
    {
        var since = new DateTimeOffset(2026, 9, 5, 14, 32, 0, TimeSpan.Zero);
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Connected,
            [Peer(connectedSince: since)],
            new RemoteToolActivity("sat_1", "Exec", "npm test", since),
            Strings);

        Assert.Contains(items, item => item.Text.StartsWith("Ann · since ", StringComparison.Ordinal));
        Assert.Contains(items, item => item.Text == "Running: npm test");
        Assert.True(Find(items, TrayMenuCommand.Disconnect).Enabled);
        Assert.True(Find(items, TrayMenuCommand.PauseSharing).Enabled);
        Assert.True(Find(items, TrayMenuCommand.OpenFolder).Enabled);
        var revoke = Find(items, TrayMenuCommand.Revoke);
        Assert.True(revoke.Enabled);
        Assert.Equal("sat_1", Assert.Single(revoke.Children!).PeerId);
    }

    [Fact]
    public void Build_WhenPairedButOffline_KeepsDisconnectUnavailable()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Offline, [Peer()], null, Strings);

        Assert.False(Find(items, TrayMenuCommand.Disconnect).Enabled);
        Assert.True(Find(items, TrayMenuCommand.PauseSharing).Enabled);
        Assert.True(Find(items, TrayMenuCommand.Revoke).Enabled);
        Assert.DoesNotContain(items, item => item.Text == Strings["tray.noPeers"]);
    }

    [Fact]
    public void Build_WhenPaused_OffersResumeInsteadOfPause()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Paused, [Peer()], null, Strings);

        Assert.Equal(Strings["tray.resume"], Find(items, TrayMenuCommand.ResumeSharing).Text);
        Assert.DoesNotContain(items, item => item.Command == TrayMenuCommand.PauseSharing);
    }

    [Fact]
    public void Build_WhenIdle_HidesTheActivityLine()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Standby, [Peer()], null, Strings);

        Assert.DoesNotContain(items, item => item.Text.StartsWith("Running:", StringComparison.Ordinal));
    }

    private static RemoteToolPeer Peer(DateTimeOffset? connectedSince = null) => new(
        "sat_1",
        "Ann",
        "repo",
        Path.GetTempPath(),
        DateTimeOffset.UtcNow,
        connectedSince);

    private static TrayMenuItem Find(IReadOnlyList<TrayMenuItem> items, TrayMenuCommand command) =>
        items.First(item => item.Command == command);

    private static IEnumerable<TrayMenuItem> Enabled(IReadOnlyList<TrayMenuItem> items) =>
        items.Where(item => item.Enabled && item.Command != TrayMenuCommand.Separator);
}
