using ModelContextProtocol.Client;

namespace DotCraft.Mcp;

public sealed partial class McpClientManager
{
    private readonly List<Task> _backgroundTasks = [];
    private readonly HashSet<ProtocolSession> _sessions = [];
    private readonly object _disposeLock = new();
    private TaskCompletionSource? _sessionsDrained;
    private Task? _disposeTask;

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        List<IAsyncDisposable> clients = [];
        Task[] backgroundTasks;
        Task sessionsDrained;
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            foreach (var state in _servers.Values.Concat(_sessions.Select(static session => session.State)).Distinct())
            {
                if (state.Client is { } client)
                    clients.Add(client);
                clients.AddRange(state.RetiredClients);
                state.Client = null;
                state.ProtocolClient = null;
                state.RetiredClients.Clear();
            }
            backgroundTasks = _backgroundTasks.ToArray();
            sessionsDrained = _sessionsDrained?.Task ?? Task.CompletedTask;
            _servers.Clear();
            Volatile.Write(ref _toolsSnapshot, []);
            Volatile.Write(ref _toolServerMapSnapshot, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            Volatile.Write(ref _serverConfigsSnapshot, []);
        }
        finally
        {
            _mutex.Release();
        }

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        await DisposeClientsAsync(clients.Distinct<IAsyncDisposable>(ReferenceEqualityComparer.Instance)).ConfigureAwait(false);
        try
        {
            await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
        }
        catch
        {
            // Startup/recovery reports its own failure; shutdown must still drain local leases.
        }
        await sessionsDrained.ConfigureAwait(false);
        _lifetimeCts.Dispose();
        // Snapshot leases may still inspect the disposed manager, so its managed semaphore stays usable.
    }

    private async ValueTask ReleaseProtocolSessionAsync(ProtocolSession session)
    {
        List<IAsyncDisposable>? clients = null;
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = session.State;
            state.ActiveOperations--;
            if (state.ActiveOperations == 0 && state.RetiredClients.Count > 0)
            {
                clients = [.. state.RetiredClients];
                state.RetiredClients.Clear();
            }
        }
        finally
        {
            _mutex.Release();
        }

        try
        {
            if (clients is not null)
                await DisposeClientsAsync(clients).ConfigureAwait(false);
        }
        finally
        {
            await _mutex.WaitAsync().ConfigureAwait(false);
            try
            {
                _sessions.Remove(session);
                if (_sessions.Count == 0)
                    _sessionsDrained?.TrySetResult();
            }
            finally
            {
                _mutex.Release();
            }
        }
    }

    private sealed class ProtocolSession(
        McpClientManager owner,
        ServerRuntimeState state,
        McpClient client,
        long generation,
        CancellationTokenSource cancellation) : IAsyncDisposable
    {
        private int _disposed;
        public ServerRuntimeState State { get; } = state;
        public McpClient Client { get; } = client;
        public long Generation { get; } = generation;
        public CancellationToken CancellationToken => cancellation.Token;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                await owner.ReleaseProtocolSessionAsync(this).ConfigureAwait(false);
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
