using System.Collections.Concurrent;

namespace DotCraft.Abstractions;

public sealed class ChannelRuntimeRegistry : IChannelRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, IChannelRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IChannelRuntime runtime)
    {
        _runtimes[runtime.Name] = runtime;
    }

    public bool TryRemove(string channelName)
        => _runtimes.TryRemove(channelName, out _);

    public bool TryGet(string channelName, out IChannelRuntime? runtime)
    {
        var found = _runtimes.TryGetValue(channelName, out var value);
        runtime = value;
        return found;
    }

    public IReadOnlyList<IChannelRuntime> Snapshot() => _runtimes.Values.ToArray();
}
