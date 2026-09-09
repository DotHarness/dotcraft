using DotCraft.Configuration;
namespace DotCraft.Automations;
/// <summary>Host scheduling limits; definitions own their individual behavior.</summary>
[ConfigSection("Automations", DisplayName = "Automations", Order = 45)]
public sealed class AutomationsConfig
{
    public bool Enabled { get; set; } = true;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxConcurrentTasks { get; set; } = 3;
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public bool WorktreeRetentionEnabled { get; set; } = true;
    public TimeSpan WorktreeRetentionIdlePeriod { get; set; } = TimeSpan.FromDays(21);
}
