using DotCraft.RemoteTools;

namespace DotCraft.Satellite.ViewModels;

internal enum SatelliteTrayState
{
    Offline,
    Paused,
    Connected,
    Standby
}

internal static class SatelliteStateMachine
{
    /// <summary>
    /// Offline outranks a pause, so the owner is never told that resuming would help while the Hub
    /// is unreachable, and the pause intent is honored before the runtime reports it.
    /// </summary>
    public static SatelliteTrayState Evaluate(RemoteToolHostStatus runtime, bool pauseRequested) => runtime switch
    {
        RemoteToolHostStatus.Offline => SatelliteTrayState.Offline,
        RemoteToolHostStatus.Paused => SatelliteTrayState.Paused,
        _ when pauseRequested => SatelliteTrayState.Paused,
        RemoteToolHostStatus.Connected => SatelliteTrayState.Connected,
        _ => SatelliteTrayState.Standby
    };

    public static string IconName(SatelliteTrayState state) => state switch
    {
        SatelliteTrayState.Offline => "tray-offline",
        SatelliteTrayState.Paused => "tray-paused",
        SatelliteTrayState.Connected => "tray-connected",
        _ => "tray-standby"
    };

    public static string StatusKey(SatelliteTrayState state) => state switch
    {
        SatelliteTrayState.Offline => "tray.status.offline",
        SatelliteTrayState.Paused => "tray.status.paused",
        SatelliteTrayState.Connected => "tray.status.connected",
        _ => "tray.status.standby"
    };
}
