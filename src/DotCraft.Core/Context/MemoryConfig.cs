using DotCraft.Configuration;
using DotCraft.Memory;

namespace DotCraft.Context;

/// <summary>
/// Workspace memory settings.
/// </summary>
[ConfigSection("Memory", DisplayName = "Memory", Order = 13)]
public sealed class MemoryConfig
{
    [ConfigField(Hint = "Let new sessions use and maintain workspace memory.", Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.Dreams)]
    public bool Enabled { get; set; } = true;

    [ConfigField(Hint = "Let new memory-enabled sessions save knowledge missed while responding, every few turns.", Reload = ReloadBehavior.Hot, HasReload = true)]
    public bool AutoExtract { get; set; }

    [ConfigField(Hint = "Let new memory-enabled sessions keep what they learn about you in a personal memory that every workspace on this computer shares.", Reload = ReloadBehavior.Hot, HasReload = true)]
    public bool Personal { get; set; } = true;

    [ConfigField(Hint = "Memory types: each Name is a memory's metadata.type and each Description tells the model what belongs to it.", FieldType = "json", Reload = ReloadBehavior.Hot, HasReload = true)]
    public List<MemoryTypeDefinition> Types { get; set; } = MemoryPolicyDefaults.CreateTypes();

    [ConfigField(Hint = "What the agent saves to memory on its own initiative and how a memory is written.", FieldType = "textarea", Reload = ReloadBehavior.Hot, HasReload = true)]
    public string SaveGuidance { get; set; } = MemoryPolicyDefaults.SaveGuidance;

    [ConfigField(Hint = "What automatic extraction saves from the turns it reviews.", FieldType = "textarea", Reload = ReloadBehavior.Hot, HasReload = true)]
    public string ExtractionGuidance { get; set; } = MemoryPolicyDefaults.ExtractionGuidance;

    [ConfigField(Hint = "What Dreams keeps when it consolidates memory, and how it merges and resolves contradictions.", FieldType = "textarea", Reload = ReloadBehavior.Hot, HasReload = true)]
    public string ConsolidationGuidance { get; set; } = MemoryPolicyDefaults.ConsolidationGuidance;

    [ConfigField(Hint = "Every how many eligible turns automatic extraction runs.", Min = MemoryPolicyRules.MinExtractionInterval, Max = MemoryPolicyRules.MaxExtractionInterval, Reload = ReloadBehavior.Hot, HasReload = true)]
    public int ExtractionInterval { get; set; } = MemoryPolicyDefaults.ExtractionInterval;
}
