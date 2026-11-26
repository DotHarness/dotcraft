using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

internal sealed class PluginInvocation : IPluginTarget
{
    private readonly object _targetsGate = new();
    private readonly List<IPluginTarget> _targets = [];
    private readonly PluginCallGate _gate;
    private readonly PluginInvocation? _parent;
    private readonly ILogger? _logger;
    private bool _targetsDetached;
    private int _retiring;

    public PluginInvocation(string pluginId, string generationId, PluginCallGate gate, CancellationToken stopping = default,
        ILogger? logger = null)
    {
        PluginId = pluginId;
        GenerationId = generationId;
        _gate = gate;
        Stopping = stopping;
        _logger = logger;
    }

    private PluginInvocation(PluginInvocation parent)
        : this(parent.PluginId, parent.GenerationId, new PluginCallGate(), parent.Stopping, parent._logger)
    {
        _parent = parent;
    }

    public string PluginId { get; }

    public string GenerationId { get; }
    public CancellationToken Stopping { get; }

    public IDisposable Enter() => TryEnter()
        ?? throw new PluginContributionUnavailableException(PluginId, GenerationId);

    private IDisposable? TryEnter()
    {
        if (_parent is null)
            return _gate.TryEnter();
        if (_parent.TryEnter() is not { } parentLease)
            return null;
        if (_gate.TryEnter() is { } lease)
            return new ScopedLease(lease, parentLease);
        parentLease.Dispose();
        return null;
    }

    public PluginInvocation CreateScope()
    {
        var scope = new PluginInvocation(this);
        AddTarget(scope);
        return scope;
    }

    public void Retire()
    {
        if (_parent is not { } parent || Interlocked.Exchange(ref _retiring, 1) != 0)
            return;

        var drained = _gate.CloseAsync();
        // Generation teardown owns the scope if its gate has already closed.
        if (parent.TryEnter() is { } parentLease)
            _ = Task.Run(() => RetireAsync(parent, parentLease, drained));
    }

    private async Task RetireAsync(PluginInvocation parent, IDisposable parentLease, Task drained)
    {
        using (parentLease)
        {
            await drained.ConfigureAwait(false);
            var errors = await DisposeCapturedTargetsAsync().ConfigureAwait(false);
            lock (parent._targetsGate)
                parent._targets.Remove(this);
            foreach (var error in errors)
                _logger?.LogWarning("{Code}: Retired provider cleanup failed for .NET plugin {PluginId} generation {GenerationId}: {Error}",
                    PluginActivationBlockers.CleanupFailed, PluginId, GenerationId, error);
        }
    }

    async ValueTask IPluginTarget.DisposeAsync()
    {
        await _gate.CloseAsync().ConfigureAwait(false);
        var errors = await DisposeCapturedTargetsAsync().ConfigureAwait(false);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    public PluginTarget<T> Capture<T>(T target, bool ownsTarget = false) where T : class
    {
        var reference = new PluginTarget<T>(target, PluginId, GenerationId, ownsTarget);
        AddTarget(reference);
        return reference;
    }

    private void AddTarget(IPluginTarget target)
    {
        lock (_targetsGate)
        {
            if (_targetsDetached)
                throw new PluginContributionUnavailableException(PluginId, GenerationId);
            _targets.Add(target);
        }
    }

    private sealed class ScopedLease(IDisposable lease, IDisposable parentLease) : IDisposable
    {
        public void Dispose()
        {
            lease.Dispose();
            parentLease.Dispose();
        }
    }

    public async Task<IReadOnlyList<string>> DisposeCapturedTargetsAsync()
    {
        IPluginTarget[] targets;
        lock (_targetsGate)
        {
            if (_targetsDetached)
                return [];
            _targetsDetached = true;
            targets = [.. _targets];
            _targets.Clear();
        }
        var errors = new List<string>();
        for (var index = targets.Length - 1; index >= 0; index--)
        {
            try
            {
                await targets[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(PluginGeneration.CopyExceptionMessage(exception));
            }
        }
        return errors;
    }

    public void Invoke(Action action)
    {
        using var lease = Enter();
        try
        {
            action();
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public TResult Invoke<TResult>(Func<TResult> action)
    {
        using var lease = Enter();
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        using var lease = Enter();
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action)
    {
        using var lease = Enter();
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public async ValueTask InvokeAsync(Func<ValueTask> action)
    {
        using var lease = Enter();
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public async ValueTask<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> action)
    {
        using var lease = Enter();
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Normalize(exception);
        }
    }

    public IReadOnlyList<T> Snapshot<T>(Func<IEnumerable<T>?> action) =>
        Invoke(() => action()?.ToArray() ?? []);

    public Exception Normalize(Exception exception)
    {
        if (exception is OperationCanceledException)
            return exception;
        if (exception is PluginContributionException)
            return exception;
        return new PluginContributionException(
            PluginId,
            GenerationId,
            Describe(exception));
    }

    /// <summary>Copies the cause as text; holding the exception would pin the collectible generation.</summary>
    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().Name}: {exception.Message}";
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            text += $" -> {inner.GetType().Name}: {inner.Message}";
        return text;
    }
}

internal interface IPluginTarget
{
    ValueTask DisposeAsync();
}

internal sealed class PluginTarget<T>(
    T value,
    string pluginId,
    string generationId,
    bool ownsTarget) : IPluginTarget
    where T : class
{
    private T? _value = value;

    public T Value => Volatile.Read(ref _value)
        ?? throw new PluginContributionUnavailableException(pluginId, generationId);

    public async ValueTask DisposeAsync()
    {
        var target = Interlocked.Exchange(ref _value, null);
        if (!ownsTarget || target == null)
            return;
        if (target is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (target is IDisposable disposable)
            disposable.Dispose();
    }
}

internal class PluginContributionException(
    string pluginId,
    string generationId,
    string message)
    : InvalidOperationException($"Plugin '{pluginId}' generation '{generationId}' callback failed: {message}");

internal sealed class PluginContributionUnavailableException(
    string pluginId,
    string generationId)
    : PluginContributionException(
        pluginId,
        generationId,
        "The plugin generation is no longer active.");
