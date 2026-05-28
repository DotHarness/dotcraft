using DotCraft.Configuration;

namespace DotCraft.CLI;

/// <summary>
/// Configuration section for the CLI module.
/// </summary>
[ConfigSection("CLI", DisplayName = "CLI", Order = 10)]
public sealed class CliConfig
{
    /// <summary>
    /// Optional explicit path to the <c>dotcraft</c> executable used to auto-start the local Hub.
    /// When null, defaults to the current process's executable path (<c>Environment.ProcessPath</c>).
    /// </summary>
    public string? AppServerBin { get; set; }

    /// <summary>
    /// When set, the CLI connects directly to a remote AppServer via WebSocket instead of
    /// using the Hub-managed local workspace. Format: <c>ws://127.0.0.1:9100/ws</c>.
    /// Takes precedence over local Hub mode.
    /// </summary>
    public string? AppServerUrl { get; set; }

    /// <summary>
    /// Optional bearer token used when connecting to a WebSocket AppServer that requires
    /// authentication (appserver-protocol.md §15.4). Only relevant when <see cref="AppServerUrl"/> is set.
    /// </summary>
    public string? AppServerToken { get; set; }
}
