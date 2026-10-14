using DotCraft.Configuration;

namespace DotCraft.Dreams;

/// <summary>
/// Configuration for Dreams, the background consolidation of memory stores.
/// </summary>
[ConfigSection("Dreams", DisplayName = "Dreams", Order = 14, DefaultReload = ReloadBehavior.SubsystemRestart, HasDefaultReload = true, DefaultSubsystemKey = ConfigurationSubsystems.Dreams)]
public sealed class DreamsConfig
{
    [ConfigField(Hint = "Enable scheduled Dreams consolidation of memory stores.")]
    public bool Enabled { get; set; }

    [ConfigField(Hint = "Minimum time since a store's last consolidation before a scheduled Dreams run.")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    [ConfigField(Hint = "Automatically apply successful Dreams runs.")]
    public bool AutoApply { get; set; }

    [ConfigField(Min = 1, Hint = "Minimum conversations that used a store since its last consolidation before a scheduled Dreams run.")]
    public int MinSessionsSinceLastRun { get; set; } = 3;

    [ConfigField(Hint = "Delay before the first Dreams eligibility check after startup.")]
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(5);
}
