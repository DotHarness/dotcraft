using DotCraft.Modules;

namespace DotCraft.Channels;

/// <summary>
/// Contributes session origins from a compiled channel module.
/// </summary>
public interface ISessionChannelModule : IDotCraftModule
{
    /// <summary>
    /// Gets session origins exposed by the channel.
    /// </summary>
    IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries();
}
