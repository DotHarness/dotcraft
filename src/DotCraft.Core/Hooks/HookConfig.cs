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
    /// Timeout in seconds for this hook command (default: 30).
    /// </summary>
    public int Timeout { get; set; } = 30;
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
