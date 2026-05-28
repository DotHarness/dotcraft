using DotCraft.Abstractions;
using System.Collections.Concurrent;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Thread-safe registry of all configured external channel hosts (subprocess and WebSocket).
/// <para>
/// <see cref="ExternalChannelManager"/> registers each enabled <see cref="ExternalChannelHost"/>
/// here during construction. <c>AppServerHost</c> queries this registry when a WebSocket client
/// completes the <c>initialize</c> handshake with a <c>channelAdapter</c> capability, routing only
/// to hosts whose transport accepts WebSocket adapter attach.
/// <see cref="ExternalChannelToolProvider"/> uses the same registry to discover hosts for
/// channel tool injection (including subprocess adapters).
/// </para>
/// </summary>
public sealed class ExternalChannelRegistry : IChannelRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, ExternalChannelHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an external channel host for discovery and WebSocket routing (when applicable).
    /// </summary>
    public void Register(string channelName, ExternalChannelHost host)
    {
        if (!_hosts.TryAdd(channelName, host))
            throw new InvalidOperationException(
                $"External channel '{channelName}' is already registered.");
    }

    /// <summary>
    /// Attempts to find a registered external channel host by name.
    /// </summary>
    public bool TryGet(string channelName, out ExternalChannelHost? host)
        => _hosts.TryGetValue(channelName, out host);

    /// <summary>
    /// Checks whether a channel name is registered (for validation during config check).
    /// </summary>
    public bool IsRegistered(string channelName)
        => _hosts.ContainsKey(channelName);

    /// <summary>
    /// Returns a point-in-time snapshot of all registered hosts.
    /// </summary>
    public IReadOnlyList<ExternalChannelHost> SnapshotHosts()
        => _hosts.Values.ToArray();

    void IChannelRuntimeRegistry.Register(IChannelRuntime runtime)
    {
        if (runtime is not ExternalChannelHost host)
            throw new InvalidOperationException("ExternalChannelRegistry only accepts ExternalChannelHost runtimes.");

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
