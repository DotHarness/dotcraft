using DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public interface IChannelRuntimeProvider
{
    ChannelPluginWorkspace? ChannelPlugins { get; }

    ChannelSetupArtifact? GetChannelSetupArtifact(string pluginId);

    IReadOnlyList<string> GetRecentChannelLogs(string channelName, int? tail = null);

    Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(string channelName, string key, CancellationToken ct);
}
