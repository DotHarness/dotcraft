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
    public void Derive_ShowsTheIslandOnlyWhileTheMachineIsInUse()
    {
        SatelliteTrayState[] quiet =
            [SatelliteTrayState.Offline, SatelliteTrayState.Standby, SatelliteTrayState.Paused];

        foreach (var state in quiet)
        {
            var model = Derive(state, [Peer()]);

            Assert.False(model.Visible);
            Assert.Equal(IslandMode.Hidden, model.Mode);
        }

        Assert.True(Derive(SatelliteTrayState.Connected, [Peer()]).Visible);
    }

    [Fact]
    public void Derive_WhenTheLastSessionCloses_TakesTheIslandAway()
    {
        var model = Derive(SatelliteTrayState.Connected, [Peer(connected: false)]);

        Assert.False(model.Visible);
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
        Assert.Equal("2 machines", model.PanelCount);
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
        Assert.Equal(Strings["tray.status.connected"], model.PanelTitle);
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

    private static IslandStateModel Derive(
        SatelliteTrayState state,
        IReadOnlyList<RemoteToolPeer> peers,
        IReadOnlyList<RemoteToolActivity>? activities = null,
        bool pinned = false,
        IReadOnlyList<IslandApprovalEntry>? approvals = null) =>
        IslandStateModel.Derive(
            new IslandInputs(state, peers, activities ?? [], approvals ?? [], pinned, PointerOver: false, Now),
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
