namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Provides recent external-channel adapter logs for Desktop diagnostics.
/// </summary>
public interface IExternalChannelLogProvider
{
    /// <summary>
    /// Returns recent log lines for the named external channel.
    /// </summary>
    IReadOnlyList<string> GetRecentExternalChannelLogs(string channelName, int? tail = null);
}
