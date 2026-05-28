using DotCraft.Configuration;

namespace DotCraft.Automations;

/// <summary>
/// Configuration for the Automations module (bound from the <c>Automations</c> config section).
/// </summary>
[ConfigSection("Automations", DisplayName = "Automations", Order = 45)]
public sealed class AutomationsConfig
{
    /// <summary>When true, the Automations channel service is enabled (Gateway mode). Enabled by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the orchestrator polls local task files for runnable tasks.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum concurrent local tasks being dispatched.
    /// Additional tasks wait in Pending state.
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 3;

    /// <summary>
    /// Root directory for local task files. When empty, uses <c>{CraftPath}/tasks/</c>.
    /// </summary>
    [ConfigField(Hint = "Root directory for local task files. Leave blank for {CraftPath}/tasks/.")]
    public string LocalTasksRoot { get; set; } = "";

    /// <summary>
    /// Root directory for user-authored automation templates. When empty, uses
    /// <c>{CraftPath}/automations/templates/</c>. Each template is a subdirectory containing
    /// a <c>template.md</c> file.
    /// </summary>
    [ConfigField(Hint = "Root directory for user-authored templates. Leave blank for {CraftPath}/automations/templates/.")]
    public string UserTemplatesRoot { get; set; } = "";

    /// <summary>
    /// Maximum time a single agent turn may run before being cancelled.
    /// Default: 30 minutes. Set to zero or negative to disable.
    /// </summary>
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum time an agent may be inactive (no tool calls, no output) before being considered stalled.
    /// Default: 10 minutes. Set to zero or negative to disable.
    /// </summary>
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum number of retry attempts for failed tasks.
    /// Default: 3. Set to 0 to disable retries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries, with exponential backoff.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum delay between retries (caps exponential backoff).
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(10);
}
