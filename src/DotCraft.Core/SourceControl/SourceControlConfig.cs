using DotCraft.Configuration;

namespace DotCraft.SourceControl;

/// <summary>
/// Workspace source control binding configuration: provider selection plus the
/// Perforce connection parameters. This section never stores a password or ticket —
/// long-lived Perforce authentication relies on the server-side ticket.
/// </summary>
[ConfigSection("SourceControl", DisplayName = "Source Control", Order = 60)]
public sealed class SourceControlConfig
{
    /// <summary>
    /// Selected provider: <c>none</c>, <c>git</c>, or <c>perforce</c>. Defaults to <c>git</c>.
    /// </summary>
    [ConfigField(
        Hint = "Source control provider: none, git, or perforce.",
        Reload = ReloadBehavior.ProcessRestart,
        HasReload = true)]
    public string Provider { get; set; } = SourceControlProviders.Git;

    /// <summary>
    /// Perforce connection mode: <c>p4config</c> (P4CONFIG / default connection) or <c>manual</c>.
    /// </summary>
    [ConfigField(Hint = "Perforce connection mode: p4config or manual.")]
    public string ConnectionMode { get; set; } = SourceControlConnectionModes.P4Config;

    /// <summary>
    /// Perforce connection parameters. Never includes a password.
    /// </summary>
    [ConfigField(Ignore = true)]
    public PerforceConnectionConfig Perforce { get; set; } = new();
}

/// <summary>
/// Non-sensitive Perforce connection parameters, mirroring the P4 environment variables.
/// Contains no password or ticket.
/// </summary>
public sealed class PerforceConnectionConfig
{
    /// <summary>P4PORT, e.g. <c>ssl:p4.example.com:1666</c>.</summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>P4CLIENT (workspace name).</summary>
    public string Client { get; set; } = string.Empty;

    /// <summary>P4USER.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>P4CHARSET. Empty means the server/connection default.</summary>
    public string Charset { get; set; } = string.Empty;

    /// <summary>
    /// P4CONFIG file name to search for from the workspace directory upward
    /// (used in <see cref="SourceControlConnectionModes.P4Config"/> mode).
    /// </summary>
    public string P4ConfigName { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the <c>p4</c> executable. Empty means resolve from the
    /// AppServer environment PATH.
    /// </summary>
    public string P4ExecutablePath { get; set; } = string.Empty;

    /// <summary>Per-command timeout in seconds applied to connection tests.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Whether the binding is considered online (server reachable / used).</summary>
    public bool Online { get; set; } = true;

    /// <summary>Whether to fall back to offline automatically when the server is unreachable.</summary>
    public bool AutoOffline { get; set; } = true;
}

/// <summary>Stable provider identifiers for <see cref="SourceControlConfig.Provider"/>.</summary>
public static class SourceControlProviders
{
    /// <summary>Legacy value retained only for normalizing old configs; resolves to <see cref="Git"/>.</summary>
    public const string Auto = "auto";
    public const string None = "none";
    public const string Git = "git";
    public const string Perforce = "perforce";

    /// <summary>
    /// Normalizes arbitrary input to a known provider id. Unknown values and the legacy
    /// <c>auto</c> id both resolve to <see cref="Git"/> (the default).
    /// </summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        None => None,
        Perforce => Perforce,
        _ => Git
    };
}

/// <summary>Stable connection modes for <see cref="SourceControlConfig.ConnectionMode"/>.</summary>
public static class SourceControlConnectionModes
{
    public const string P4Config = "p4config";
    public const string Manual = "manual";

    /// <summary>Normalizes arbitrary input to a known mode, defaulting to <see cref="P4Config"/>.</summary>
    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Manual, StringComparison.OrdinalIgnoreCase) ? Manual : P4Config;
}

/// <summary>Stable binding status values surfaced to clients.</summary>
public static class SourceControlStatuses
{
    public const string NotConfigured = "notConfigured";
    public const string NotTested = "notTested";
    public const string Connected = "connected";
    public const string LoginRequired = "loginRequired";
    public const string Offline = "offline";
    public const string Error = "error";
}
