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
}
