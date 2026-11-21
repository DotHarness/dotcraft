using System.Collections.Concurrent;

namespace DotCraft.Channels;

/// <summary>Resolves workspace channel hosts for adapter connections and channel tools.</summary>
public sealed class ChannelHostRegistry : IChannelRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, ChannelHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an external channel host for discovery and WebSocket routing (when applicable).
    /// </summary>
    public void Register(string channelName, ChannelHost host)
    {
        if (!_hosts.TryAdd(channelName, host))
            throw new InvalidOperationException(
                $"External channel '{channelName}' is already registered.");
    }

    /// <summary>
    /// Attempts to find a registered external channel host by name.
    /// </summary>
    public bool TryGet(string channelName, out ChannelHost? host)
        => _hosts.TryGetValue(channelName, out host);

    /// <summary>
    /// Checks whether a channel name is registered (for validation during config check).
    /// </summary>
    public bool IsRegistered(string channelName)
        => _hosts.ContainsKey(channelName);

    /// <summary>
    /// Returns a point-in-time snapshot of all registered hosts.
    /// </summary>
    public IReadOnlyList<ChannelHost> SnapshotHosts()
        => _hosts.Values.ToArray();

    void IChannelRuntimeRegistry.Register(IChannelRuntime runtime)
    {
        if (runtime is not ChannelHost host)
            throw new InvalidOperationException("ChannelHostRegistry only accepts ChannelHost runtimes.");

        Register(host.Name, host);
    }

    bool IChannelRuntimeRegistry.TryRemove(string channelName)
        => Unregister(channelName);

    bool IChannelRuntimeRegistry.TryGet(string channelName, out IChannelRuntime? runtime)
    {
        var found = _hosts.TryGetValue(channelName, out var host);
        runtime = host;
        return found;
    }

    IReadOnlyList<IChannelRuntime> IChannelRuntimeRegistry.Snapshot()
        => _hosts.Values.Cast<IChannelRuntime>().ToArray();

    /// <summary>
    /// Removes a channel from the registry (e.g. on shutdown).
    /// </summary>
    public bool Unregister(string channelName)
        => _hosts.TryRemove(channelName, out _);
}
