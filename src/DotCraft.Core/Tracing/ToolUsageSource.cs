using DotCraft.Tools;

namespace DotCraft.Tracing;

/// <summary>
/// Usage attribution keys for tool calls and skill references, recorded on trace events at
/// invocation time so aggregation never depends on which plugins remain installed.
/// </summary>
public static class ToolUsageSource
{
    public const string Builtin = "builtin";
    public const string Client = "client";
    public const string Binding = "binding";
    public const string Unknown = "unknown";

    private const string PluginPrefix = "plugin:";

    public static string FromProvenance(ToolProvenance provenance) => provenance.Kind switch
    {
        ToolSourceKind.CoreNative => Builtin,
        ToolSourceKind.Mcp => $"mcp:{provenance.SourceId}",
        ToolSourceKind.PluginNative => PluginPrefix + provenance.SourceId,
        ToolSourceKind.RuntimeDynamic => Client,
        ToolSourceKind.LegacyAppBinding => Binding,
        _ => Unknown
    };

    /// <summary>Plugin skills key by plugin id; other skills key by their loader source such as workspace or user.</summary>
    public static string FromSkill(string? source, string? pluginId)
    {
        if (!string.IsNullOrWhiteSpace(pluginId))
            return PluginPrefix + pluginId;
        return string.IsNullOrWhiteSpace(source) ? Unknown : source;
    }

    /// <summary>Extracts the plugin id from a <c>plugin:</c> key, or null for every other source.</summary>
    public static string? PluginIdOf(string? source) =>
        source != null && source.Length > PluginPrefix.Length && source.StartsWith(PluginPrefix, StringComparison.Ordinal)
            ? source[PluginPrefix.Length..]
            : null;
}
