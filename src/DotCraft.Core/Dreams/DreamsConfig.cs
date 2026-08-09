using DotCraft.Configuration;

namespace DotCraft.Dreams;

/// <summary>
/// Configuration for the internal Dreams runtime that powers user-facing Dreams.
/// </summary>
[ConfigSection("Dreams", DisplayName = "Dreams", Order = 14)]
public sealed class DreamsConfig
{
    /// <summary>
    /// Enables scheduled Dreams for the workspace.
    /// </summary>
    [ConfigField(Hint = "Enable scheduled Dreams background memory organization.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public bool Enabled { get; set; }

    /// <summary>
    /// Minimum elapsed time between scheduled Dream runs that may call the model.
    /// </summary>
    [ConfigField(Hint = "Minimum interval between scheduled Dreams runs.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum recent eligible top-level threads inspected per run.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Maximum recent eligible threads inspected per Dreams run.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public int ThreadLookbackCount { get; set; } = 20;

    /// <summary>
    /// Automatically applies successful Dream runs as the active Dream Store.
    /// </summary>
    [ConfigField(Hint = "Automatically apply successful Dreams runs as active memory.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public bool AutoApply { get; set; }

    /// <summary>
    /// Maximum memory/HISTORY.md tail characters included in a run.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Maximum memory/HISTORY.md tail characters included in a Dreams run.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public int HistoryTailChars { get; set; } = 20_000;

    /// <summary>
    /// Minimum new completed turns across eligible threads before scheduled model work.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Minimum new completed turns before scheduled Dreams call the model.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public int MinCompletedTurnsSinceLastRun { get; set; } = 5;

    /// <summary>
    /// Delay before the first scheduler eligibility check after workspace runtime startup.
    /// </summary>
    [ConfigField(Hint = "Delay before the first Dreams eligibility check after startup.", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(5);
}
