using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>Watches unloaded generations for collection, off the mutation path and on the ambient GC.</summary>
internal sealed class PluginReclaimPoller
{
    /// <summary>How many further passes one shadow copy is retried for before it is given up on.</summary>
    private const int MaxDeletionAttempts = 60;

    private readonly object _gate = new();
    private readonly List<ReclaimEntry> _entries = [];
    private readonly Dictionary<string, int> _pendingDeletions = new(StringComparer.OrdinalIgnoreCase);
    private readonly DotNetPluginRuntimeOptions _options;
    private readonly Func<PluginGenerationRemnant, CancellationToken, Task> _onReclaimed;
    private readonly Func<string, bool> _releaseCopy;
    private readonly ILogger? _logger;
    private CancellationTokenSource? _stopping;
    private TaskCompletionSource? _loopCompleted;
    private bool _stopped;

    /// <summary>Creates a poller for one runtime manager.</summary>
    /// <param name="onReclaimed">Invoked once per collected generation, outside every lock the poller holds.</param>
    public PluginReclaimPoller(
        DotNetPluginRuntimeOptions options,
        Func<PluginGenerationRemnant, CancellationToken, Task> onReclaimed,
        Func<string, bool> releaseCopy,
        ILogger? logger = null)
    {
        _options = options;
        _onReclaimed = onReclaimed;
        _releaseCopy = releaseCopy;
        _logger = logger;
    }

    /// <summary>Gets whether any generation is still waiting to be collected.</summary>
    public bool HasOutstanding
    {
        get
        {
            lock (_gate)
                return _entries.Count > 0;
        }
    }

    /// <summary>Keeps trying to delete a shadow copy the platform had not released yet.</summary>
    public void TrackDeletion(string path)
    {
        lock (_gate)
        {
            if (_stopped)
                return;
            _pendingDeletions[path] = 0;
            EnsureLoop();
        }
    }

    /// <summary>Starts watching one torn-down generation.</summary>
    public void Track(PluginGenerationRemnant remnant)
    {
        lock (_gate)
        {
            if (_stopped)
                return;
            _entries.Add(new ReclaimEntry(remnant));
            EnsureLoop();
        }
    }

    /// <summary>Counts the generations of one plugin that are still waiting to be collected.</summary>
    public int OutstandingCount(string pluginId)
    {
        lock (_gate)
        {
            return _entries.Count(entry =>
                string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Stops polling and makes one last bounded attempt at everything still outstanding.</summary>
    /// <param name="cancellationToken">Cancels the final attempt, not the stop.</param>
    /// <returns>The generations collected during the final attempt, for the caller to settle.</returns>
    public async Task<IReadOnlyList<PluginGenerationRemnant>> StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task? completed;
        lock (_gate)
        {
            _stopped = true;
            _stopping?.Cancel();
            completed = _loopCompleted?.Task;
        }

        if (completed != null)
            await completed.ConfigureAwait(false);
        return await FinalSweepAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PluginGenerationRemnant>> FinalSweepAsync(
        CancellationToken cancellationToken)
    {
        var collected = new List<PluginGenerationRemnant>();
        var deadline = Environment.TickCount64 + (long)_options.CollectionTimeout.TotalMilliseconds;
        while (true)
        {
            ReclaimEntry[] pending;
            lock (_gate)
                pending = _entries.ToArray();
            if (pending.Length == 0)
                break;

            ShutdownCollection();
            var remaining = 0;
            foreach (var entry in pending)
            {
                if (entry.Remnant.LoadContext is { IsAlive: true })
                {
                    remaining++;
                    continue;
                }

                collected.Add(entry.Remnant);
                lock (_gate)
                    _entries.Remove(entry);
            }

            if (remaining == 0
                || Environment.TickCount64 >= deadline
                || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_options.CollectionPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return collected;
    }

    private void Run(CancellationToken cancellationToken)
    {
        TaskCompletionSource? completed = null;
        var released = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReclaimEntry[] pending;
                lock (_gate)
                {
                    // Emptiness is decided under the same lock that releases the loop slot, so a
                    // generation tracked at this instant cannot fall between two loops.
                    if (_entries.Count == 0 && _pendingDeletions.Count == 0)
                    {
                        completed = Release();
                        released = true;
                        return;
                    }

                    pending = _entries.ToArray();
                }

                RetryPendingDeletions();

                foreach (var entry in pending)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    var remnant = entry.Remnant;
                    if (remnant.LoadContext is { IsAlive: true })
                        continue;

                    // The entry stops counting before the manager publishes the settled state, so a
                    // snapshot never reports a plugin as both stopped and still leaking.
                    lock (_gate)
                        _entries.Remove(entry);
                    try
                    {
                        _onReclaimed(remnant, cancellationToken).GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        lock (_gate)
                            _entries.Add(entry);
                        if (exception is not OperationCanceledException)
                            _logger?.LogError("Settling a reclaimed plugin generation failed.");
                        continue;
                    }
                }

                if (cancellationToken.WaitHandle.WaitOne(_options.CollectionPollInterval))
                    return;
            }
        }
        catch (Exception)
        {
            _logger?.LogError("The plugin reclaim poller stopped unexpectedly.");
        }
        finally
        {
            if (!released)
            {
                lock (_gate)
                    completed = Release();
            }

            completed?.TrySetResult();
        }
    }

    private void RetryPendingDeletions()
    {
        string[] paths;
        lock (_gate)
        {
            if (_pendingDeletions.Count == 0)
                return;
            paths = _pendingDeletions.Keys.ToArray();
        }

        foreach (var path in paths)
        {
            var released = false;
            try
            {
                released = _releaseCopy(path);
            }
            catch (Exception)
            {
                _logger?.LogWarning("Releasing a plugin generation copy failed.");
            }

            lock (_gate)
            {
                if (released)
                {
                    _pendingDeletions.Remove(path);
                }
                else if (++_pendingDeletions[path] >= MaxDeletionAttempts)
                {
                    _pendingDeletions.Remove(path);
                    _logger?.LogWarning("Gave up deleting a plugin generation copy.");
                }
            }
        }
    }

    /// <summary>Releases the single loop slot. Called under <see cref="_gate"/>.</summary>
    private TaskCompletionSource? Release()
    {
        var completed = _loopCompleted;
        _loopCompleted = null;
        return completed;
    }

    private void EnsureLoop()
    {
        if (_stopped || _loopCompleted != null || (_entries.Count == 0 && _pendingDeletions.Count == 0))
            return;
        _stopping ??= new CancellationTokenSource();
        var token = _stopping.Token;
        _loopCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() => Run(token))
        {
            IsBackground = true,
            Name = "dotcraft-plugin-reclaim"
        }.Start();
    }

    /// <summary>Runs the collection cycle the final sweep needs: nothing is loading, and a shadow copy
    /// left mapped past shutdown is never deleted by anyone.</summary>
    /// <remarks>Not inlined: an inlined frame can keep the load context reachable and turn teardown into a leak.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShutdownCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
    }

    private sealed class ReclaimEntry(PluginGenerationRemnant remnant)
    {
        public string PluginId { get; } = remnant.PluginId;

        public PluginGenerationRemnant Remnant { get; } = remnant;
    }
}
