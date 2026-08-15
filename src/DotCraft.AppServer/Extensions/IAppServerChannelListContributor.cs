namespace DotCraft.AppServer;

/// <summary>
/// Supplies base (non-external-config) entries for <see cref="DotCraft.Protocol.AppServer.AppServerMethodNames.ChannelList"/>.
/// </summary>
public interface IAppServerChannelListContributor
{
    /// <summary>
    /// Appends built-in / social / system channel rows; must respect <paramref name="seen"/> for deduplication.
    /// </summary>
    void AppendBaseChannels(List<ChannelDescriptor> channels, HashSet<string> seen);
}

/// <summary>Internal discoverability projection before mapping to the AppServer contract.</summary>
public sealed record ChannelDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}
