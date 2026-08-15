using DotCraft.Cron;
using DotCraft.Channels;
using DotCraft.Heartbeat;
using DotCraft.Modules;

namespace DotCraft.AppServer;

/// <summary>
/// Builds <c>channel/list</c> base entries from registered <see cref="DotCraft.Modules.IDotCraftModule"/> instances
/// plus optional Core <see cref="CronService"/> / <see cref="HeartbeatService"/> (system channels).
/// </summary>
public sealed class ModuleRegistryChannelListContributor(
    ModuleRegistry moduleRegistry,
    CronService? cronService,
    HeartbeatService? heartbeatService) : IAppServerChannelListContributor
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

        foreach (var module in moduleRegistry.Modules.OfType<ISessionChannelModule>())
        {
            foreach (var e in module.GetSessionChannelListEntries())
                Add(e.Name, e.Category);
        }

        foreach (var channel in _bundledTypeScriptChannels.Value)
            Add(channel.Name, channel.Category);

        if (cronService != null)
            Add("cron", "system");

        if (heartbeatService != null)
            Add("heartbeat", "system");
    }
}
