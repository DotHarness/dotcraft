namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Supplies base (non-external-config) entries for <see cref="AppServerMethods.ChannelList"/>.
/// </summary>
public interface IAppServerChannelListContributor
{
    /// <summary>
    /// Appends built-in / social / system channel rows; must respect <paramref name="seen"/> for deduplication.
    /// </summary>
    void AppendBaseChannels(List<ChannelInfo> channels, HashSet<string> seen);
}
