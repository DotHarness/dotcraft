using DotCraft.Configuration;

namespace DotCraft.Acp;

[ConfigSection("Acp", DisplayName = "ACP", Order = 170)]
public sealed class AcpConfig
{
    /// <summary>
    /// Enable ACP (Agent Client Protocol) mode for editor/IDE integration via stdio.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional path to the <c>dotcraft</c> executable used to spawn the AppServer subprocess.
    /// When null, defaults to the current process path.
    /// </summary>
    public string? AppServerBin { get; set; }

    /// <summary>
    /// When set, the ACP bridge connects to an existing AppServer via WebSocket instead of
    /// spawning a subprocess. Format: <c>ws://127.0.0.1:9100/ws</c>.
    /// </summary>
    public string? AppServerUrl { get; set; }

    /// <summary>
    /// Optional bearer token for WebSocket AppServer authentication.
    /// </summary>
    public string? AppServerToken { get; set; }

    /// <summary>
    /// Timeout in seconds for individual write operations to the IDE via stdio.
    /// When the IDE pipe buffer is full, a write is cancelled after this duration
    /// to prevent indefinite lock convoy. Default: 30.
    /// </summary>
    public int WriteTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout in seconds for ext/acp/* forwarding requests (file read/write,
    /// terminal operations) sent from the agent to the IDE. Default: 120.
    /// </summary>
    public int ExtForwardTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Timeout in seconds for tool approval permission requests sent to the IDE.
    /// Default: 120.
    /// </summary>
    public int PermissionRequestTimeoutSeconds { get; set; } = 120;
}
