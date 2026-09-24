using DotCraft.Configuration;

namespace DotCraft.Context;

/// <summary>
/// Workspace memory settings.
/// </summary>
[ConfigSection("Memory", DisplayName = "Memory", Order = 13)]
public sealed class MemoryConfig
{
    [ConfigField(Hint = "Let new sessions use and maintain workspace memory.", Reload = ReloadBehavior.Hot, HasReload = true)]
    public bool Enabled { get; set; } = true;
}
