namespace DotCraft.Plugins;

/// <summary>Provides safe workflow metadata for plugin inspection surfaces.</summary>
public interface IPluginWorkflowSummaryProvider
{
    IReadOnlyList<PluginWorkflowSummary> ListForPlugin(string pluginId);
}

public sealed record PluginWorkflowSummary(
    string Name,
    string Command,
    string Description,
    string? WhenToUse);
