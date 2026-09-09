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
    public void Build_GivesEachMachineOneSubmenuOfItsOwnActions()
    {
        var since = new DateTimeOffset(2026, 9, 5, 14, 32, 0, TimeSpan.Zero);
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Connected,
            [Peer(connectedSince: since)],
            new RemoteToolActivity("sat_1", "Exec", "npm test", since),
            Strings);

        Assert.Contains(items, item => item.Text == "Running: npm test");
        var machine = Find(items, TrayMenuCommand.Machine);
        Assert.StartsWith("Ann · since ", machine.Text, StringComparison.Ordinal);

        // Every action that names a machine lives under that machine, and carries its id.
        var actions = machine.Children!
            .Where(child => child.Command != TrayMenuCommand.Separator
                && child.Command != TrayMenuCommand.Header)
            .ToArray();
        Assert.Equal(
            (TrayMenuCommand[])
            [
                TrayMenuCommand.OpenFolder,
                TrayMenuCommand.ManageAccess,
                TrayMenuCommand.Disconnect,
                TrayMenuCommand.Revoke
            ],
            actions.Select(child => child.Command));
        Assert.All(actions, child => Assert.Equal("sat_1", child.PeerId));
        Assert.All(actions, child => Assert.True(child.Enabled));
        Assert.Equal(Strings["consent.preferred"], machine.Children![0].Text);
    }

    [Fact]
    public void Build_WhenAPairingHasNoMode_SaysItNeedsReauthorization()
    {
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Standby, [Peer(authorizationMode: null)], null, Strings);

        Assert.Equal(Strings["consent.review"], Find(items, TrayMenuCommand.Machine).Children![0].Text);
    }

    [Fact]
    public void Build_KeepsOnlyMachineIndependentActionsAtTheTopLevel()
    {
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Standby,
            [Peer(connectedSince: DateTimeOffset.UtcNow)],
            null,
            Strings);

        Assert.Equal(
            (TrayMenuCommand[])
            [
                TrayMenuCommand.Header,
                TrayMenuCommand.Machine,
                TrayMenuCommand.PauseSharing,
                TrayMenuCommand.PasteInvite,
                TrayMenuCommand.Quit
            ],
            items.Where(item => item.Command != TrayMenuCommand.Separator).Select(item => item.Command));
    }

    [Fact]
    public void Build_WhenAMachineIsOffline_NamesItWithoutATimeAndBlocksDisconnect()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Offline, [Peer()], null, Strings);

        var machine = Find(items, TrayMenuCommand.Machine);
        Assert.Equal("Ann", machine.Text);
        Assert.False(Child(machine, TrayMenuCommand.Disconnect).Enabled);
        Assert.True(Child(machine, TrayMenuCommand.Revoke).Enabled);
        Assert.DoesNotContain(items, item => item.Text == Strings["tray.noPeers"]);
    }

    [Fact]
    public void Build_WhenPaused_OffersResumeInsteadOfPause()
    {
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Paused, [Peer(connectedSince: DateTimeOffset.UtcNow)], null, Strings);

        Assert.Equal(Strings["tray.resume"], Find(items, TrayMenuCommand.ResumeSharing).Text);
        Assert.DoesNotContain(items, item => item.Command == TrayMenuCommand.PauseSharing);
    }

    [Fact]
    public void Build_WhenIdle_HidesTheActivityLine()
    {
        var items = TrayMenuModel.Build(SatelliteTrayState.Standby, [Peer()], null, Strings);

        Assert.DoesNotContain(items, item => item.Text.StartsWith("Running:", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ListsEveryPairedMachine()
    {
        var items = TrayMenuModel.Build(
            SatelliteTrayState.Standby,
            [Peer(), Peer(peerId: "sat_2", displayName: "Bo")],
            null,
            Strings);

        Assert.Equal(
            ["sat_1", "sat_2"],
            items.Where(item => item.Command == TrayMenuCommand.Machine)
                .Select(item => Child(item, TrayMenuCommand.Revoke).PeerId));
    }

    private static TrayMenuItem Child(TrayMenuItem machine, TrayMenuCommand command) =>
        machine.Children!.First(child => child.Command == command);

    private static RemoteToolPeer Peer(
        DateTimeOffset? connectedSince = null,
        string peerId = "sat_1",
        string displayName = "Ann",
        string? authorizationMode = RemoteToolAuthorization.WorkspacePreferred) => new(
        peerId,
        displayName,
        "repo",
        Path.GetTempPath(),
        DateTimeOffset.UtcNow,
        connectedSince,
        authorizationMode);

    private static int Index(IReadOnlyList<TrayMenuItem> items, TrayMenuCommand command)
    {
        for (var position = 0; position < items.Count; position++)
        {
            if (items[position].Command == command)
                return position;
        }

        return -1;
    }

    private static TrayMenuItem Find(IReadOnlyList<TrayMenuItem> items, TrayMenuCommand command) =>
        items.First(item => item.Command == command);

    private static IEnumerable<TrayMenuItem> Enabled(IReadOnlyList<TrayMenuItem> items) =>
        items.Where(item => item.Enabled && item.Command != TrayMenuCommand.Separator);
}
