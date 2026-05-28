using DotCraft.Configuration;

namespace DotCraft.AppServer;

/// <summary>
/// Transport mode for the AppServer.
/// </summary>
public enum AppServerMode
{
    /// <summary>AppServer is not active.</summary>
    Disabled,

    /// <summary>AppServer listens on stdio (JSONL) only — the standard subprocess mode.</summary>
    Stdio,

    /// <summary>
    /// AppServer listens on a WebSocket endpoint only — no stdio transport.
    /// Stdout remains available for normal console output.
    /// Activated via <c>dotcraft app-server --listen ws://host:port</c>.
    /// </summary>
    WebSocket,

    /// <summary>
    /// AppServer listens on stdio AND a WebSocket endpoint defined in
    /// <see cref="AppServerConfig.WebSocket"/>.
    /// Activated via <c>dotcraft app-server --listen ws+stdio://host:port</c>.
    /// </summary>
    StdioAndWebSocket
}

[ConfigSection("AppServer", DisplayName = "AppServer", Order = 180)]
public sealed class AppServerConfig
{
    /// <summary>
    /// Controls which transports the AppServer listens on.
    /// <list type="bullet">
    /// <item><c>Disabled</c> — AppServer is inactive (default).</item>
    /// <item><c>Stdio</c> — standard subprocess mode; stdout is reserved for JSON-RPC.</item>
    /// <item><c>WebSocket</c> — pure WebSocket mode; no stdio transport, stdout remains available.</item>
    /// <item><c>StdioAndWebSocket</c> — stdio plus WebSocket on <c>AppServer.WebSocket.Port</c>.</item>
    /// </list>
    /// </summary>
    public AppServerMode Mode { get; set; } = AppServerMode.Disabled;

    /// <summary>WebSocket listener settings. Active when <see cref="Mode"/> is <see cref="AppServerMode.WebSocket"/> or <see cref="AppServerMode.StdioAndWebSocket"/>.</summary>
    [ConfigField(Ignore = true)]
    public WebSocketServerConfig WebSocket { get; set; } = new();
}

/// <summary>
/// Configuration for the AppServer WebSocket listener (appserver-protocol.md §15).
/// Only used when <see cref="AppServerConfig.Mode"/> is <see cref="AppServerMode.StdioAndWebSocket"/>.
/// </summary>
[ConfigSection("AppServer.WebSocket", DisplayName = "AppServer > WebSocket", Order = 181)]
public sealed class WebSocketServerConfig
{
    /// <summary>
    /// The IP address to bind to. Defaults to <c>127.0.0.1</c> (loopback only).
    /// Set to <c>0.0.0.0</c> to accept connections on all interfaces (requires <see cref="Token"/>).
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// The TCP port to listen on. Defaults to <c>9100</c>.
    /// </summary>
    [ConfigField(Min = 1, Max = 65535)]
    public int Port { get; set; } = 9100;

    /// <summary>
    /// Optional bearer token. When set, clients must supply <c>?token=&lt;value&gt;</c>
    /// in the WebSocket upgrade URL (spec §15.4). Required when binding to a non-loopback address.
    /// </summary>
    [ConfigField(Sensitive = true, Hint = "Required when Host is not loopback (127.0.0.1 / ::1)")]
    public string? Token { get; set; }
}
