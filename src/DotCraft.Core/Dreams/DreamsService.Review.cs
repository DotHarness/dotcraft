using DotCraft.Configuration;

namespace DotCraft.Dreams;

public sealed partial class DreamsService
{
    private const string StoreChangedMessage = "The memory store changed after the run started. Run Dreams again.";

    private readonly object _reviewSync = new();

    public DreamsRunState? LoadLatestState() => _states.Load();

    public DreamsRunState? LoadRun(string runId) => _states.Load(runId);

    public IReadOnlyList<DreamsRunState> ListRuns(bool includeArchived = false) => _states.List(includeArchived);

    /// <summary>A run with each file that differs between its baseline and candidate, or null when the run is unknown.</summary>
    public DreamsRunReview? GetRunReview(string runId)
    {
        var state = _states.Load(runId);
        return state == null
            ? null
            : new DreamsRunReview(
                state,
                DreamsStoreFiles.Review(
                    DreamsStoreFiles.Read(_artifacts.BaselineDirectory(state.Id)),
                    DreamsStoreFiles.Read(_artifacts.CandidateDirectory(state.Id))));
    }

    /// <summary>Replaces the store's files with a pending run's candidate while the store still matches the run's baseline.</summary>
    /// <exception cref="DreamsStoreChangedException">The store changed after the run started; the run stays pending.</exception>
    public DreamsRunState? ApplyRun(string runId)
    {
        ThrowIfDisposed();
        lock (_reviewSync)
        {
            var state = _states.Load(runId);
            if (state == null)
                return null;
            if (state.Status != DreamsRunStatuses.Succeeded || state.ReviewStatus != DreamsReviewStatuses.Pending)
                throw new InvalidOperationException("Only pending Dream runs can be applied.");

            if (!DreamsStoreFiles.TryReplace(
                    _stores.DirectoryOf(state.Scope),
                    DreamsStoreFiles.Read(_artifacts.BaselineDirectory(state.Id)),
                    DreamsStoreFiles.Read(_artifacts.CandidateDirectory(state.Id))))
            {
                RecordStoreChanged(state);
                _states.SaveRun(state);
                throw new DreamsStoreChangedException(StoreChangedMessage);
            }

            state.ReviewStatus = DreamsReviewStatuses.Applied;
            state.ErrorType = null;
            state.Message = null;
            _states.SaveRun(state);
            NotifyStoreChanged();
            return state;
        }
    }

    /// <summary>Writes an applied run's baseline back while the store still matches the run's candidate; the run becomes discarded.</summary>
    /// <exception cref="DreamsStoreChangedException">The store changed after the run was applied.</exception>
    public DreamsRunState? RestoreRun(string runId)
    {
        ThrowIfDisposed();
        lock (_reviewSync)
        {
            var state = _states.Load(runId);
            if (state == null)
                return null;
            if (state.ReviewStatus != DreamsReviewStatuses.Applied)
                throw new InvalidOperationException("Only applied Dream runs can be restored.");

            if (!DreamsStoreFiles.TryReplace(
                    _stores.DirectoryOf(state.Scope),
                    DreamsStoreFiles.Read(_artifacts.CandidateDirectory(state.Id)),
                    DreamsStoreFiles.Read(_artifacts.BaselineDirectory(state.Id))))
            {
                throw new DreamsStoreChangedException("The memory store changed after the run was applied.");
            }

            state.ReviewStatus = DreamsReviewStatuses.Discarded;
            _states.SaveRun(state);
            NotifyStoreChanged();
            return state;
        }
    }

    public DreamsRunState? DiscardRun(string runId)
    {
        ThrowIfDisposed();
        lock (_reviewSync)
        {
            var state = _states.Load(runId);
            if (state == null)
                return null;
            if (state.ReviewStatus != DreamsReviewStatuses.Pending)
                throw new InvalidOperationException("Only pending Dream runs can be discarded.");

            state.ReviewStatus = DreamsReviewStatuses.Discarded;
            _states.SaveRun(state);
            return state;
        }
    }

    public DreamsRunState? ArchiveRun(string runId)
    {
        ThrowIfDisposed();
        lock (_reviewSync)
        {
            var state = _states.Load(runId);
            if (state == null)
                return null;
            if (state.IsRunning)
                throw new InvalidOperationException("A running Dream run cannot be archived.");

            state.ReviewStatus = DreamsReviewStatuses.Archived;
            _states.SaveRun(state);
            return state;
        }
    }

    /// <summary>Cancels a running run, best-effort.</summary>
    public async Task<DreamsRunState?> CancelRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var state = _states.Load(runId);
        if (state == null)
            return null;

        if (state.IsRunning && string.Equals(_activeRunId, state.Id, StringComparison.Ordinal) && _activeRunCts is { } cts)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return _states.Load(runId) ?? state;
    }

    /// <summary>Permanently deletes a run's record and artifacts; never touches a store.</summary>
    /// <exception cref="InvalidOperationException">The run is running.</exception>
    public DreamsRunState? DeleteRun(string runId)
    {
        ThrowIfDisposed();
        lock (_reviewSync)
        {
            var state = _states.Load(runId);
            if (state == null)
                return null;
            if (state.IsRunning && IsRunning)
                throw new InvalidOperationException("A running Dream run cannot be deleted.");

            _states.DeleteRun(state.Id);
            return state;
        }
    }

    /// <summary>Permanently deletes every run, archived ones included; never touches a store.</summary>
    /// <exception cref="InvalidOperationException">A run is running, so nothing was deleted.</exception>
    public async Task<IReadOnlyList<DreamsRunState>> DeleteAllRunsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Dream runs cannot be deleted while a run is active.");

        try
        {
            lock (_reviewSync)
            {
                var states = _states.List(includeArchived: true);
                _states.DeleteAllRuns();
                return states;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private static void RecordStoreChanged(DreamsRunState state)
    {
        state.ErrorType = DreamsConstants.StoreChangedErrorType;
        state.Message = StoreChangedMessage;
    }

    private void NotifyStoreChanged() =>
        _appConfigMonitor?.NotifyChanged("dreams", [ConfigChangeRegions.Memory]);
}
