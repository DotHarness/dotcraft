using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Owns the resources and background work of one activation generation.</summary>
internal sealed class PluginLifetime : IPluginLifetime, IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<object> _owned = new(ReferenceEqualityComparer.Instance);
    private readonly List<OwnedResource> _resources = [];
    private readonly List<Func<CancellationToken, Task>> _operations = [];
    private List<Task> _running = [];
    private object? _entryInstance;
    private bool _open = true;

    public CancellationToken Stopping => _stopping.Token;

    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is IAsyncDisposable)
            throw new InvalidOperationException("A resource implementing IAsyncDisposable must be registered through OwnAsync.");
        Register(resource, new OwnedResource(resource, null));
    }

    public void OwnAsync(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Register(resource, new OwnedResource(null, resource));
    }

    public void Run(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            EnsureOpen();
            _operations.Add(operation);
        }
    }

    public void SetEntryInstance(object entryInstance)
    {
        lock (_gate)
        {
            EnsureOpen();
            _entryInstance = entryInstance;
        }
    }

    public bool Owns(object resource)
    {
        lock (_gate)
            return _owned.Contains(resource);
    }

    public void Seal()
    {
        lock (_gate)
        {
            EnsureOpen();
            _open = false;
        }
    }

    public void StartWork(Action<string> onFailure)
    {
        Func<CancellationToken, Task>[] operations;
        lock (_gate)
        {
            if (_open)
                throw new InvalidOperationException("Plugin lifetime must be sealed before work starts.");
            operations = _operations.ToArray();
            _operations.Clear();
        }

        _running = operations.Select(operation => RunWithoutCallerContext(operation, onFailure)).ToList();
    }

    private Task RunWithoutCallerContext(
        Func<CancellationToken, Task> operation,
        Action<string> onFailure)
    {
        Task Start() => Task.Run(async () =>
        {
            try
            {
                await operation(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                onFailure(PluginGeneration.CopyExceptionMessage(exception));
            }
        });

        if (ExecutionContext.IsFlowSuppressed())
            return Start();

        using (ExecutionContext.SuppressFlow())
            return Start();
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        SignalStopping();
        if (_running.Count > 0)
            await Task.WhenAll(_running).WaitAsync(cancellationToken).ConfigureAwait(false);
        _running.Clear();
    }

    public void SignalStopping() => _stopping.Cancel();

    public async Task<IReadOnlyList<string>> DisposeResourcesAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            try
            {
                var resource = _resources[index];
                if (resource.Async != null)
                    await resource.Async.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                else
                    resource.Sync!.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(PluginGeneration.CopyExceptionMessage(exception));
            }
        }

        _resources.Clear();
        _owned.Clear();
        return errors;
    }

    public void Dispose() => _stopping.Dispose();

    private void Register(object identity, OwnedResource resource)
    {
        lock (_gate)
        {
            EnsureOpen();
            if (ReferenceEquals(identity, _entryInstance))
                throw new InvalidOperationException("The plugin entry instance cannot be registered as an owned resource.");
            if (!_owned.Add(identity))
                throw new InvalidOperationException("The same resource cannot be registered more than once.");
            _resources.Add(resource);
        }
    }

    private void EnsureOpen()
    {
        if (!_open)
            throw new InvalidOperationException("Plugin lifetime registration is closed.");
    }

    private sealed record OwnedResource(IDisposable? Sync, IAsyncDisposable? Async);
}
