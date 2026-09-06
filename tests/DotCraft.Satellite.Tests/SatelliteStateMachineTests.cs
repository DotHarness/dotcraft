using DotCraft.RemoteTools;
using DotCraft.Satellite.ViewModels;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class SatelliteStateMachineTests
{
    [Fact]
    public void Evaluate_MirrorsTheRuntime_WhenNoPauseIsRequested()
    {
        Assert.Equal(
            SatelliteTrayState.Offline,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Offline, pauseRequested: false));
        Assert.Equal(
            SatelliteTrayState.Standby,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Standby, pauseRequested: false));
        Assert.Equal(
            SatelliteTrayState.Connected,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Connected, pauseRequested: false));
        Assert.Equal(
            SatelliteTrayState.Paused,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Paused, pauseRequested: false));
    }

    [Fact]
    public void Evaluate_PutsOfflineAboveAPause()
    {
        Assert.Equal(
            SatelliteTrayState.Offline,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Offline, pauseRequested: true));
    }

    [Fact]
    public void Evaluate_PutsARequestedPauseAboveConnectedAndStandby()
    {
        Assert.Equal(
            SatelliteTrayState.Paused,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Connected, pauseRequested: true));
        Assert.Equal(
            SatelliteTrayState.Paused,
            SatelliteStateMachine.Evaluate(RemoteToolHostStatus.Standby, pauseRequested: true));
    }

    [Fact]
    public void EveryState_HasItsOwnIconAndStatusLine()
    {
        var states = Enum.GetValues<SatelliteTrayState>();

        Assert.Equal(
            states.Length,
            states.Select(SatelliteStateMachine.IconName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            states.Length,
            states.Select(SatelliteStateMachine.StatusKey).Distinct(StringComparer.Ordinal).Count());
    }
}
