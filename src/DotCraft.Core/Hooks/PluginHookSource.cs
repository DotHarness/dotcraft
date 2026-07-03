namespace DotCraft.Hooks;

/// <summary>
/// Parsed hook declarations from one plugin hook source.
/// </summary>
public sealed record PluginHookSource(
    string PluginId,
    string PluginDisplayName,
    string PluginRoot,
    string PluginDataPath,
    string SourcePath,
    string SourceRelativePath,
    HooksFileConfig Hooks);

/// <summary>
/// Minimal hook declaration surfaced on plugin detail/list responses.
/// </summary>
public sealed record PluginHookDeclaration(string Key, string EventName);
