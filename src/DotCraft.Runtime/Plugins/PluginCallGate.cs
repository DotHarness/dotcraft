using System.Collections.Concurrent;

namespace DotCraft.Runtime;

/// <summary>The admission gate one activation generation keeps over the calls that reach into it.</summary>
internal sealed class PluginCallGate
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _open = true;

    /// <summary>Gets whether the gate still admits new calls.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
                return _open;
        }
    }

    /// <summary>Enters the gate for one call.</summary>
    /// <returns>A lease that must be disposed when the call returns, or <see langword="null"/> when admission has closed.</returns>
    public IDisposable? TryEnter()
    {
        lock (_gate)
        {
            if (!_open)
                return null;
            _active++;
        }

        return new Lease(this);
    }

    /// <summary>Closes admission and completes once the calls already inside have returned. Idempotent.</summary>
    public Task CloseAsync()
    {
        lock (_gate)
        {
            _open = false;
            if (_active == 0)
                _drained.TrySetResult();
        }

        return _drained.Task;
    }

    private void Exit()
    {
        lock (_gate)
        {
            _active--;
            if (!_open && _active == 0)
                _drained.TrySetResult();
        }
    }

    private sealed class Lease(PluginCallGate owner) : IDisposable
    {
        private PluginCallGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}

/// <summary>Routes a call to the admission gate of the generation that owns it, by identifier rather than reference.</summary>
internal sealed class PluginCallGateRegistry
{
    private readonly ConcurrentDictionary<string, PluginCallGate> _gates = new(StringComparer.Ordinal);

    /// <summary>Enters the named generation for one call.</summary>
    /// <returns>A lease to dispose when the call returns, or <see langword="null"/> to answer <c>tool_unavailable</c>.</returns>
    public IDisposable? TryEnterCall(string pluginId, string generationId) =>
        _gates.TryGetValue(Key(pluginId, generationId), out var gate) ? gate.TryEnter() : null;

    /// <summary>Determines whether the named generation currently admits calls.</summary>
    public bool IsCallable(string pluginId, string generationId) =>
        _gates.TryGetValue(Key(pluginId, generationId), out var gate) && gate.IsOpen;

    internal void Publish(string pluginId, string generationId, PluginCallGate gate) =>
        _gates[Key(pluginId, generationId)] = gate;

    internal void Revoke(string pluginId, string generationId) =>
        _gates.TryRemove(Key(pluginId, generationId), out _);

    private static string Key(string pluginId, string generationId) => $"{pluginId}\0{generationId}";
}
