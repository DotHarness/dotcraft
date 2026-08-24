namespace DotCraft.AppServer;

/// <summary>
/// Workspace-scoped coordination state shared by all AppServer connections that manage plugins.
/// </summary>
public sealed class AppServerPluginManagementState
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly AsyncLocal<MutationLease?> _mutationScope = new();

    internal PluginManagementSnapshotClock SnapshotClock { get; } = new();

    /// <summary>Gets whether the current async flow owns the plugin-management mutation gate.</summary>
    public bool IsMutationInProgress => _mutationScope.Value is { IsCompleted: false };

    /// <summary>Advances the workspace plugin snapshot revision above an observed subsystem revision.</summary>
    public long AdvanceSnapshotRevision(long observedRevision) => SnapshotClock.Advance(observedRevision);

    internal async Task<TResult> RunSnapshotReadAsync<TResult>(
        Func<CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await read(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    internal async Task<TResult> RunMutationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var lease = new MutationLease(_mutationGate);
        _mutationScope.Value = lease;
        try
        {
            return await mutation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lease.Complete();
            if (ReferenceEquals(_mutationScope.Value, lease))
                _mutationScope.Value = null;
        }
    }

    internal void CompleteCurrentMutation()
    {
        var lease = _mutationScope.Value
                    ?? throw new InvalidOperationException("No plugin-management mutation is active.");
        lease.Complete();
    }

    private sealed class MutationLease(SemaphoreSlim gate)
    {
        private int _completed;

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                gate.Release();
        }
    }
}
