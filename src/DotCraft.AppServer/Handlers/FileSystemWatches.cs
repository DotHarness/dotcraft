using System.Collections.Concurrent;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class FileSystemWatches
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly ConcurrentDictionary<string, Watch> _watches = new(StringComparer.Ordinal);

    public FileSystemWatches(AppServerConnection connection, IAppServerTransport transport)
    {
        _connection = connection;
        _transport = transport;
        _ = connection.Closed.ContinueWith(_ => DisposeAll(), TaskScheduler.Default);
    }

    public void Add(string watchId, string path)
    {
        var watch = new Watch(path, changedPaths => SendAsync(watchId, changedPaths));
        if (!_watches.TryAdd(watchId, watch))
        {
            _ = watch.DisposeAsync();
            throw AppServerErrors.InvalidParams($"watchId '{watchId}' is already active.");
        }

        if (_connection.IsClosed)
            DisposeAll();
    }

    public async Task RemoveAsync(string watchId)
    {
        if (_watches.TryRemove(watchId, out var watch))
            await watch.DisposeAsync().ConfigureAwait(false);
    }

    private void DisposeAll()
    {
        foreach (var watchId in _watches.Keys)
            _ = RemoveAsync(watchId);
    }

    private async Task SendAsync(string watchId, IReadOnlyList<string> changedPaths)
    {
        if (!_connection.ShouldSendNotification(Contract.AppServerRpc.FsChanged.Name))
            return;
        try
        {
            await _transport.NotifyContractAsync(
                    Contract.AppServerRpc.FsChanged,
                    new Contract.FsChangedNotification { WatchId = watchId, ChangedPaths = changedPaths },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private sealed class Watch
    {
        private readonly Func<IReadOnlyList<string>, Task> _send;
        private readonly FileSystemWatcher _watcher;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private Timer? _timer;
        private bool _disposed;

        public Watch(string path, Func<IReadOnlyList<string>, Task> send)
        {
            _send = send;
            _watcher = Directory.Exists(path)
                ? new FileSystemWatcher(path)
                : new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            _watcher.IncludeSubdirectories = false;
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                                    | NotifyFilters.Size | NotifyFilters.CreationTime;
            _watcher.Created += (_, e) => Record(e.FullPath);
            _watcher.Changed += (_, e) => Record(e.FullPath);
            _watcher.Deleted += (_, e) => Record(e.FullPath);
            _watcher.Renamed += (_, e) => Record(e.OldFullPath, e.FullPath);
            _watcher.Error += (_, _) => Record(path);
            _watcher.EnableRaisingEvents = true;
        }

        public async Task DisposeAsync()
        {
            lock (_gate)
            {
                _disposed = true;
                _timer?.Dispose();
                _timer = null;
            }

            _watcher.Dispose();
            await _sendGate.WaitAsync().ConfigureAwait(false);
            _sendGate.Release();
        }

        private void Record(params string[] paths)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _pending.UnionWith(paths);
                _timer ??= new Timer(state => _ = FlushAsync(), null, Debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private async Task FlushAsync()
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string[] changedPaths;
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    changedPaths = _pending.Order(StringComparer.Ordinal).ToArray();
                    _pending.Clear();
                    _timer?.Dispose();
                    _timer = null;
                }

                if (changedPaths.Length > 0)
                    await _send(changedPaths).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}
