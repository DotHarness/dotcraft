using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DotCraft.Contributions;

/// <summary>The default in-process <see cref="IContributionRegistry"/>.</summary>
internal sealed class ContributionRegistry(ILogger<ContributionRegistry>? logger = null) : IContributionRegistry
{
    private readonly ConcurrentDictionary<Type, ContributionPoint> _points = new();
    private readonly object _batchGate = new();
    private readonly HashSet<Type> _pendingPoints = [];
    private long _nextSequence;
    private long _version;
    private int _batchDepth;

    /// <inheritdoc />
    public event EventHandler<ContributionsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public IContributionHandle Add<TContract>(TContract contribution, ContributionOptions? options = null)
        where TContract : class, IContributionContract =>
        Add(contribution, options, ContributionOrigin.Builtin);

    /// <inheritdoc />
    public IContributionRegistrar CreateRegistrar(ContributionOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new ContributionRegistrar(this, origin);
    }

    /// <inheritdoc />
    public IDisposable BeginBatch()
    {
        lock (_batchGate)
            _batchDepth++;
        return new BatchScope(this);
    }

    /// <inheritdoc />
    public IReadOnlyList<TContract> Resolve<TContract>(string? threadId = null)
        where TContract : class, IContributionContract =>
        _points.TryGetValue(typeof(TContract), out var point)
            ? ((ContributionPoint<TContract>)point).Resolve(NormalizeThreadId(threadId))
            : [];

    /// <inheritdoc />
    public IReadOnlyList<ContributionEntry<TContract>> ResolveEntries<TContract>(string? threadId = null)
        where TContract : class, IContributionContract =>
        _points.TryGetValue(typeof(TContract), out var point)
            ? ((ContributionPoint<TContract>)point).ResolveEntries(NormalizeThreadId(threadId))
            : [];

    /// <inheritdoc />
    public long GetRevision<TContract>() where TContract : class, IContributionContract =>
        _points.TryGetValue(typeof(TContract), out var point) ? point.Revision : 0;

    /// <inheritdoc />
    public int ReleaseThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));

        using var batch = BeginBatch();
        var released = 0;
        foreach (var point in _points.Values)
        {
            foreach (var entry in point.CollectThreadEntries(threadId))
            {
                entry.Handle.Dispose();
                released++;
            }
        }

        return released;
    }

    internal IContributionHandle Add<TContract>(
        TContract contribution,
        ContributionOptions? options,
        ContributionOrigin origin)
        where TContract : class, IContributionContract
    {
        ArgumentNullException.ThrowIfNull(contribution);
        var effectiveOptions = options ?? ContributionOptions.Default;
        ValidateScope(effectiveOptions);

        var contractType = typeof(TContract);
        var entry = new ContributionEntry(
            new ContributionId(Interlocked.Increment(ref _nextSequence)),
            contractType,
            contribution,
            origin,
            effectiveOptions);
        var handle = new ContributionHandle(this, entry);
        entry.Handle = handle;

        var point = _points.GetOrAdd(contractType, static _ => new ContributionPoint<TContract>());
        point.Add(entry, logger);
        MarkChanged(contractType);
        return handle;
    }

    /// <summary>Removes an entry and tears down the contribution it owns.</summary>
    internal void Remove(ContributionEntry entry)
    {
        if (!_points.TryGetValue(entry.ContractType, out var point) || !point.Remove(entry, logger))
            return;

        MarkChanged(entry.ContractType);
        if (!entry.Options.OwnsContribution || entry.Contribution is not IDisposable disposable)
            return;

        try
        {
            disposable.Dispose();
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "{Code}: contribution {Contribution} from {Origin} on contribution point {Contract} threw during teardown.",
                ContributionDiagnosticCodes.TeardownFailed,
                entry.Id,
                entry.Origin,
                entry.ContractType);
        }
    }

    private static string? NormalizeThreadId(string? threadId) =>
        string.IsNullOrWhiteSpace(threadId) ? null : threadId;

    private static void ValidateScope(ContributionOptions options)
    {
        if (options.Scope == ContributionScope.Thread && string.IsNullOrWhiteSpace(options.ThreadId))
        {
            throw new ArgumentException(
                "A thread-scoped contribution requires a thread identifier.",
                nameof(options));
        }

        if (options.Scope == ContributionScope.Workspace && options.ThreadId is not null)
        {
            throw new ArgumentException(
                "A thread identifier is only valid for a thread-scoped contribution.",
                nameof(options));
        }
    }

    private void MarkChanged(Type contractType)
    {
        Type[] changed;
        long version;
        lock (_batchGate)
        {
            _pendingPoints.Add(contractType);
            if (_batchDepth > 0)
                return;
            changed = [.. _pendingPoints];
            _pendingPoints.Clear();
            version = ++_version;
        }

        RaiseChanged(changed, version);
    }

    private void EndBatch()
    {
        Type[] changed;
        long version;
        lock (_batchGate)
        {
            if (--_batchDepth > 0 || _pendingPoints.Count == 0)
                return;
            changed = [.. _pendingPoints];
            _pendingPoints.Clear();
            version = ++_version;
        }

        RaiseChanged(changed, version);
    }

    private void RaiseChanged(Type[] changedContracts, long version)
    {
        var handler = Changed;
        if (handler is null)
            return;

        var args = new ContributionsChangedEventArgs(changedContracts, version);
        // Every subscriber observes the mutation: one that throws must not starve the ones behind it.
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ContributionsChangedEventArgs>)subscriber)(this, args);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    exception,
                    "{Code}: contribution change subscriber {Subscriber} threw.",
                    ContributionDiagnosticCodes.ObserverFailed,
                    DescribeSubscriber(subscriber));
            }
        }
    }

    private static string DescribeSubscriber(Delegate subscriber) =>
        subscriber.Target?.GetType().FullName ?? subscriber.Method.DeclaringType?.FullName ?? "<unknown>";

    private sealed class BatchScope(ContributionRegistry registry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            registry.EndBatch();
        }
    }

    private sealed class ContributionHandle(ContributionRegistry registry, ContributionEntry entry)
        : IContributionHandle
    {
        private int _disposed;

        public ContributionId Id => entry.Id;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            registry.Remove(entry);
        }
    }

    private sealed class ContributionRegistrar(ContributionRegistry registry, ContributionOrigin origin)
        : IContributionRegistrar
    {
        private readonly List<IContributionHandle> _handles = [];
        private readonly object _gate = new();
        private bool _disposed;

        public ContributionOrigin Origin => origin;

        public IContributionHandle Add<TContract>(TContract contribution, ContributionOptions? options = null)
            where TContract : class, IContributionContract
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);

            var handle = registry.Add(contribution, options, origin);
            lock (_gate)
            {
                if (_disposed)
                {
                    // Lost a race with disposal: keep the group empty by tearing the handle down.
                    handle.Dispose();
                    return handle;
                }

                _handles.Add(handle);
            }

            return handle;
        }

        public void Dispose()
        {
            IContributionHandle[] handles;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                handles = [.. _handles];
                _handles.Clear();
            }

            using var batch = registry.BeginBatch();
            for (var index = handles.Length - 1; index >= 0; index--)
                handles[index].Dispose();
        }
    }
}
