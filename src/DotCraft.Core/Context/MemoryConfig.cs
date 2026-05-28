using DotCraft.Configuration;

namespace DotCraft.Context;

/// <summary>
/// Configuration for long-term memory consolidation.
/// </summary>
[ConfigSection("Memory", DisplayName = "Memory", Order = 13)]
public sealed class MemoryConfig
{
    /// <summary>
    /// Enables automatic turn-count-based memory consolidation.
    /// </summary>
    [ConfigField(Hint = "Enable automatic long-term memory consolidation after successful turns.", Reload = ReloadBehavior.Hot, HasReload = true)]
    public bool AutoConsolidateEnabled { get; set; } = true;

    /// <summary>
    /// Number of successful turns between automatic consolidation attempts per thread.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Successful turns between automatic memory consolidation attempts.")]
    public int ConsolidateEveryNTurns { get; set; } = 5;
}
