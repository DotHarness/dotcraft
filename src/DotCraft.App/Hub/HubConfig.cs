using DotCraft.Configuration;

namespace DotCraft.Hub;

/// <summary>
/// Global Hub configuration.
/// </summary>
[ConfigSection("Hub", DisplayName = "Hub", Order = 190)]
public sealed class HubConfig
{
    /// <summary>
    /// Loopback host used by the Hub local API.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Local API port. Zero means allocate a random available loopback port.
    /// </summary>
    [ConfigField(Min = 0, Max = 65535)]
    public int Port { get; set; }

    /// <summary>
    /// Starts the satellite listener with Hub instead of waiting for the first invitation.
    /// </summary>
    public bool SatellitesEnabled { get; set; }

    /// <summary>
    /// Address the satellite listener binds, the one Hub endpoint that may leave loopback.
    /// </summary>
    public string SatelliteHost { get; set; } = "0.0.0.0";

    /// <summary>
    /// Fixed satellite listener port, so invitation URLs and paired endpoints survive a restart.
    /// </summary>
    [ConfigField(Min = 0, Max = 65535)]
    public int SatellitePort { get; set; } = 47600;

    /// <summary>
    /// Default validity of a pairing invitation, in hours.
    /// </summary>
    [ConfigField(Min = 1, Max = 8760)]
    public int InviteTtlHours { get; set; } = 24;
}
