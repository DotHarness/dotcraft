using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.ViewModels;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class IslandStateModelTests
{
    private static readonly SatelliteStrings Strings =
        SatelliteStrings.For("en", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 11, 35, 0, TimeSpan.Zero);

    [Fact]
    public void Derive_WhileNobodyIsUsingTheMachine_SaysSoInOneQuietLine()
    {
        (SatelliteTrayState State, IslandMode Mode, string Key, double Width)[] quiet =
        [
            (SatelliteTrayState.Offline, IslandMode.Offline, "tray.status.offline", 160),
            (SatelliteTrayState.Paused, IslandMode.Paused, "tray.status.paused", 230),
            (SatelliteTrayState.Standby, IslandMode.Standby, "tray.status.standby", 160)
        ];

        foreach (var (state, mode, key, width) in quiet)
        {
            var model = Derive(state, [Peer(connected: false)]);

            Assert.True(model.Visible);
            Assert.Equal(mode, model.Mode);
            Assert.Equal(Strings[key], model.CompactLabel);
            Assert.Equal(Strings[key], model.Summary);
            Assert.Equal(width, model.Width);
            Assert.Empty(model.Peers);
            Assert.False(model.ShowRunning);
            Assert.False(model.Watching);
        }
    }

    [Fact]
    public void Derive_WithNoPairing_TakesTheIslandAway()
    {
        var model = Derive(SatelliteTrayState.Standby, []);

        Assert.False(model.Visible);
        Assert.Equal(IslandMode.Hidden, model.Mode);
    }

    [Fact]
    public void Derive_WhenTheLastSessionCloses_FallsBackToTheReadyLine()
    {
        var model = Derive(SatelliteTrayState.Connected, [Peer(connected: false)]);

        Assert.Equal(IslandMode.Standby, model.Mode);
        Assert.Equal(Strings["tray.status.standby"], model.Summary);
    }

    [Fact]
    public void Derive_WhileSharingIsPaused_OffersTheResumeAction()
    {
        var model = Derive(SatelliteTrayState.Paused, [Peer()]);

        Assert.Equal(Strings["tray.resume"], model.ResumeLabel);
        Assert.Empty(Derive(SatelliteTrayState.Standby, [Peer()]).ResumeLabel);
        Assert.Empty(Derive(SatelliteTrayState.Connected, [Peer()]).ResumeLabel);
    }

    [Fact]
    public void Derive_WhileQuiet_AnswersToNeitherPinningNorHoverNorRequests()
    {
        var model = Derive(
            SatelliteTrayState.Standby,
            [Peer()],
            [Activity("Exec", "npm run build")],
            pinned: true,
            pointerOver: true,
            approvals: [Request("execute", "npm run test -- --runInBand")]);

        Assert.Equal(IslandMode.Standby, model.Mode);
        Assert.False(model.IsApproval);
    }

    [Fact]
    public void Derive_WithOneMachine_NamesItAndSaysSinceWhen()
    {
        var connected = Now.AddHours(-1);

        var model = Derive(SatelliteTrayState.Connected, [Peer(connectedSince: connected)]);

        Assert.Equal(IslandMode.Compact, model.Mode);
        Assert.Equal("Ann's workstation", model.CompactLabel);
        Assert.Equal(
            "since " + connected.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
            model.SinceLabel);
        Assert.Equal(240, model.Width);
    }

    [Fact]
    public void Derive_WithSeveralMachines_CountsThemInsteadOfNamingOne()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer(), Peer(peerId: "pair_priya", name: "Priya's desktop")]);

        Assert.Equal("2 machines", model.CompactLabel);
        Assert.Empty(model.SinceLabel);
    }

    [Fact]
    public void Derive_WhileAToolRuns_NamesTheOperationAndTruncatesTheCommand()
    {
        var command = new string('x', 80);

        var model = Derive(SatelliteTrayState.Connected, [Peer()], [Activity("Exec", command)]);

        Assert.Equal(IslandMode.Running, model.Mode);
        Assert.True(model.ShowRunning);
        Assert.Equal(Strings["approval.operation.execute"], model.OperationLabel);
        Assert.Equal(IslandStateModel.CommandCap, model.CommandPreview.Length);
        Assert.EndsWith("…", model.CommandPreview, StringComparison.Ordinal);
        Assert.Equal(command, model.CommandFull);
        Assert.Equal("0:20", model.ElapsedLabel);
        Assert.Equal(340, model.Width);
    }

    [Fact]
    public void Derive_ForAToolWithNoOperationName_FallsBackToTheToolName()
    {
        var model = Derive(SatelliteTrayState.Connected, [Peer()], [Activity("Screenshot", "shot")]);

        Assert.Equal("Screenshot", model.OperationLabel);
    }

    [Fact]
    public void Derive_WithConcurrentTools_ShowsTheLatestAndCountsTheRest()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer()],
            [Activity("Exec", "npm run build"), Activity("ReadFile", "src/app.ts")]);

        Assert.Equal(Strings["approval.operation.read"], model.OperationLabel);
        Assert.Equal(1, model.Concurrency);
    }

    [Fact]
    public void Derive_WhenTheOwnerOpensIt_ListsThePeersWithoutHidingTheRunningLine()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer(), Peer(peerId: "pair_priya", name: "Priya's desktop", mode: RemoteToolAuthorization.FullAccess)],
            [Activity("Exec", "npm run build")],
            pinned: true);

        Assert.Equal(IslandMode.Expanded, model.Mode);
        Assert.True(model.ShowRunning);
        Assert.Equal(2, model.Peers.Count);
        Assert.StartsWith(Strings["consent.folderHeading"], model.Peers[0].Meta, StringComparison.Ordinal);
        Assert.StartsWith(Strings["consent.full"], model.Peers[1].Meta, StringComparison.Ordinal);
        Assert.Equal("Disconnect this one · Ann's workstation", model.Peers[0].DisconnectLabel);
    }

    [Fact]
    public void Derive_WhenARequestArrives_OutranksEveryOtherState()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer()],
            [Activity("Exec", "npm run build")],
            pinned: true,
            approvals: [Request("execute", "npm run test -- --runInBand")]);

        Assert.Equal(IslandMode.Approval, model.Mode);
        Assert.Equal(380, model.Width);
        Assert.Equal("Ann is requesting access", model.ApprovalTitle);
        Assert.Equal(Strings["approval.operation.execute"], model.ApprovalOperation);
        Assert.Equal("npm run test -- --runInBand", model.ApprovalTarget);
    }

    [Fact]
    public void Derive_WithRequestsWaiting_ShowsTheFirstAndCountsTheRest()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer()],
            approvals:
            [
                Request("execute", "first"),
                Request("edit", "second"),
                Request("read", "third")
            ]);

        Assert.Equal("first", model.ApprovalTarget);
        Assert.Equal(2, model.Queued);
    }

    [Fact]
    public void Derive_CountsTheRequestDownFromWhenItArrived()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer()],
            approvals: [Request("execute", "npm test", age: TimeSpan.FromSeconds(90))]);

        Assert.Equal("0:30", model.CountdownLabel);
        Assert.Equal(0.25, model.CountdownFraction, 3);
    }

    [Fact]
    public void Derive_CapsAnInviterNameSoItCannotPushTheLayout()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer()],
            approvals: [Request("execute", "npm test", inviter: new string('A', 400))]);

        Assert.Contains(new string('A', IslandStateModel.NameCap), model.ApprovalTitle, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('A', IslandStateModel.NameCap + 1), model.ApprovalTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void Derive_MarksTheMachineBeingWatched_WithoutChangingTheMode()
    {
        var model = Derive(
            SatelliteTrayState.Connected,
            [Peer() with { ScreenViewers = 1 }, Peer(peerId: "pair_priya", name: "Priya's desktop")]);

        Assert.True(model.Watching);
        Assert.Equal(IslandMode.Compact, model.Mode);
        Assert.Equal(Strings["island.watching"], model.WatchingLabel);
        Assert.EndsWith(" · " + model.WatchingLabel, model.Peers[0].Meta, StringComparison.Ordinal);
        Assert.DoesNotContain(model.WatchingLabel, model.Peers[1].Meta, StringComparison.Ordinal);
        Assert.EndsWith(model.WatchingLabel, model.Summary, StringComparison.Ordinal);
        Assert.False(Derive(SatelliteTrayState.Connected, [Peer()]).Watching);
    }

    private static IslandStateModel Derive(
        SatelliteTrayState state,
        IReadOnlyList<RemoteToolPeer> peers,
        IReadOnlyList<RemoteToolActivity>? activities = null,
        bool pinned = false,
        IReadOnlyList<IslandApprovalEntry>? approvals = null,
        bool pointerOver = false) =>
        IslandStateModel.Derive(
            new IslandInputs(state, peers, activities ?? [], approvals ?? [], pinned, pointerOver, Now),
            Strings);

    private static RemoteToolPeer Peer(
        string peerId = "pair_ann",
        string name = "Ann's workstation",
        string mode = RemoteToolAuthorization.WorkspacePreferred,
        DateTimeOffset? connectedSince = null,
        bool connected = true) => new(
        peerId,
        name,
        "workspace",
        Path.GetTempPath(),
        Now.AddDays(-2),
        connected ? connectedSince ?? Now.AddHours(-1) : null,
        mode);

    private static RemoteToolActivity Activity(string toolName, string command) =>
        new("pair_ann", toolName, command, Now.AddSeconds(-20));

    private static IslandApprovalEntry Request(
        string operation,
        string target,
        string inviter = "Ann",
        TimeSpan? age = null) => new(
        new RemoteToolApprovalRequest(
            "pair_ann", inviter, 1, "invocation", "shell", operation, target, Path.GetTempPath()),
        Now - (age ?? TimeSpan.Zero));
}
