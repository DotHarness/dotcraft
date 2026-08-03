using System.Text.Json.Serialization;
using DotCraft.Sessions;

namespace DotCraft.Hooks;

/// <summary>
/// A single hook entry: a shell command to execute when triggered.
/// </summary>
public sealed class HookEntry
{
    /// <summary>
    /// Hook type. Currently only "command" (shell command) is supported.
    /// </summary>
    public string Type { get; set; } = "command";

    /// <summary>
    /// The shell command to execute.
    /// Runs via /bin/bash on Linux/macOS or powershell.exe on Windows.
    /// Receives JSON context on stdin.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Optional command condition, such as <c>Bash(git commit:*)</c>.
    /// </summary>
    [JsonPropertyName("if")]
    public string? If { get; set; }

    /// <summary>
    /// Optional shell override.
    /// </summary>
    public string? Shell { get; set; }

    /// <summary>
    /// Timeout in seconds for this hook command (default: 30).
    /// </summary>
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Extra environment variables injected when executing the hook.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Optional status message surfaced to clients while the hook runs.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Whether the hook can run asynchronously without blocking the current action.
    /// </summary>
    public bool Async { get; set; }

    /// <summary>
    /// Whether hook output can enqueue a hook-origin follow-up turn.
    /// </summary>
    public bool AsyncRewake { get; set; }

    /// <summary>
    /// Optional prefix for hook-origin follow-up feedback.
    /// </summary>
    public string? RewakeMessage { get; set; }

    /// <summary>
    /// Optional short summary for hook-origin follow-up feedback.
    /// </summary>
    public string? RewakeSummary { get; set; }

    /// <summary>
    /// Reserved for future per-session de-duplication.
    /// </summary>
    public bool Once { get; set; }

    /// <summary>
    /// Runtime-only stable hook key.
    /// </summary>
    [JsonIgnore]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Runtime-only source path for diagnostics and listing.
    /// </summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Runtime-only source kind, such as <c>user</c>, <c>workspace</c>, or <c>plugin</c>.
    /// </summary>
    [JsonIgnore]
    public string Source { get; set; } = HookSources.Unknown;

    /// <summary>
    /// Runtime-only plugin id for plugin-contributed hooks.
    /// </summary>
    [JsonIgnore]
    public string? PluginId { get; set; }
}

/// <summary>
/// A matcher group: regex pattern to match tool names + list of hook entries.
/// </summary>
public sealed class HookMatcherGroup
{
    /// <summary>
    /// Regex pattern to match tool names (e.g. "WriteFile|EditFile").
    /// Empty string matches all tools. Only used for PreToolUse/PostToolUse/PostToolUseFailure events.
    /// </summary>
    public string Matcher { get; set; } = string.Empty;

    /// <summary>
    /// List of hook commands to execute when the matcher matches.
    /// </summary>
    public List<HookEntry> Hooks { get; set; } = [];
}

/// <summary>
/// Root configuration for hooks, loaded from hooks.json files.
/// Keys are <see cref="HookEvent"/> names (e.g. "PreToolUse", "PostToolUse").
/// </summary>
public sealed class HooksFileConfig
{
    /// <summary>
    /// Map of event name to list of matcher groups.
    /// </summary>
    public Dictionary<string, List<HookMatcherGroup>> Hooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-hook user state stored in <c>Hooks.State</c>.
/// </summary>
public sealed class HookStateConfig
{
    /// <summary>
    /// Optional enablement override. Missing or <c>true</c> means the hook may run.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Hash of the last user-trusted hook definition.
    /// </summary>
    public string? TrustedHash { get; set; }
}
