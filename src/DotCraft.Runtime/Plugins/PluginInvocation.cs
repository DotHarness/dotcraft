namespace DotCraft.Runtime;

/// <summary>Enters one generation for the complete duration of a plugin callback.</summary>
internal sealed class PluginInvocation
{
    private readonly object _targetsGate = new();
    private readonly List<IPluginTarget> _targets = [];
    private readonly PluginCallGate _gate;
    private bool _targetsDetached;

    public PluginInvocation(string pluginId, string generationId, PluginCallGate gate)
    {
        PluginId = pluginId;
        GenerationId = generationId;
        _gate = gate;
    }

    public string PluginId { get; }

    public string GenerationId { get; }

    public IDisposable Enter() => _gate.TryEnter()
        ?? throw new PluginContributionUnavailableException(PluginId, GenerationId);

    public PluginTarget<T> Capture<T>(T target, bool ownsTarget = false) where T : class
    {
        var reference = new PluginTarget<T>(target, PluginId, GenerationId, ownsTarget);
        lock (_targetsGate)
        {
            if (_targetsDetached)
                throw new PluginContributionUnavailableException(PluginId, GenerationId);
            else
                _targets.Add(reference);
        }
        return reference;
    }

    /// <summary>Releases every raw plugin reference held by retained host adapters.</summary>
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
            "Plugin callback failed.");
    }
}

internal interface IPluginTarget
{
    ValueTask DisposeAsync();
}

/// <summary>A clearable reference from a host adapter to one collectible plugin object.</summary>
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

/// <summary>A host-owned failure copied from a plugin callback without retaining the plugin exception.</summary>
internal class PluginContributionException(
    string pluginId,
    string generationId,
    string message)
    : InvalidOperationException($"Plugin '{pluginId}' generation '{generationId}' callback failed: {message}");

/// <summary>Thrown by a retained host adapter after its plugin generation has been revoked.</summary>
internal sealed class PluginContributionUnavailableException(
    string pluginId,
    string generationId)
    : PluginContributionException(
        pluginId,
        generationId,
        "The plugin generation is no longer active.");
