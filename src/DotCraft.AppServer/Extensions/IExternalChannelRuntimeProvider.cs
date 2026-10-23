namespace DotCraft.AppServer;

public interface IExternalChannelRuntimeProvider
{
    /// <summary>
    /// Returns recent log lines for the named external channel.
    /// </summary>
    IReadOnlyList<string> GetRecentExternalChannelLogs(string channelName, int? tail = null);

    Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(string channelName, string key, CancellationToken ct);
}
