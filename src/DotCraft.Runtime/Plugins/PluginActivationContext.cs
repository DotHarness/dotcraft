using System.Text.Json;
using DotCraft.Contributions;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>The Host contract handed to one activation generation.</summary>
internal sealed class PluginActivationContext(
    PluginIdentity plugin,
    string contentRoot,
    string dataRoot,
    string workspaceRoot,
    JsonElement settings,
    IServiceProvider services,
    IContributionRegistrar contributions,
    PluginLifetime lifetime,
    PluginServiceExportRegistry exports,
    PluginDependencyResolver dependencies) : IPluginActivationContext
{
    public PluginIdentity Plugin { get; } = plugin;

    public string ContentRoot { get; } = contentRoot;

    public string DataRoot { get; } = dataRoot;

    public string WorkspaceRoot { get; } = workspaceRoot;

    public JsonElement Settings { get; } = settings.Clone();

    public IServiceProvider Services { get; } = services;

    public IContributionRegistrar Contributions { get; } = contributions;

    public IPluginServiceExportRegistrar Exports { get; } = exports;

    public IPluginDependencyResolver Dependencies { get; } = dependencies;

    public IPluginLifetime Lifetime { get; } = lifetime;
}
