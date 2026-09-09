using DotCraft.Modules;
using DotCraft.Configuration;

namespace DotCraft.AppServer;

/// <summary>
/// Builds <c>channel/list</c> base entries from registered <see cref="DotCraft.Modules.IDotCraftModule"/> instances
/// </summary>
public sealed class ModuleRegistryChannelListContributor(
    ModuleRegistry moduleRegistry, AppConfig? config = null) : IAppServerChannelListContributor
{
    private readonly Lazy<IReadOnlyList<ChannelDescriptor>> _bundledTypeScriptChannels =
        new(BundledTypeScriptModuleScanner.ScanFromEnvironment);

    /// <inheritdoc />
    public void AppendBaseChannels(List<ChannelDescriptor> channels, HashSet<string> seen)
    {
        void Add(string name, string category)
        {
            if (!seen.Add(name))
                return;
            channels.Add(new ChannelDescriptor { Name = name, Category = category });
        }

        foreach (var module in moduleRegistry.GetEnabledModules(config ?? new AppConfig()).OfType<ISessionChannelModule>())
        {
            foreach (var e in module.GetSessionChannelListEntries())
                Add(e.Name, e.Category);
        }

        foreach (var channel in _bundledTypeScriptChannels.Value)
            Add(channel.Name, channel.Category);

    }
}
