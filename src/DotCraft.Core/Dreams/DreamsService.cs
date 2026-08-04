using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Dreams;

/// <summary>
/// Runs scheduled workspace Dreams for the current workspace runtime.
/// </summary>
public sealed class DreamsService(
    AppConfig config,
    DreamsInputCollector inputCollector,
    IDreamsRunner dreamRunner,
    DreamStore dreamStore,
    DreamsStateStore stateStore,
    ILogger<DreamsService>? logger = null,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _activeRunCts;
    private string? _activeRunId;
    private Task? _loopTask;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_loopTask != null || !config.Dreams.Enabled)
            return Task.CompletedTask;

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_lifetimeCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = _lifetimeCts;
        var loop = _loopTask;
        if (cts == null || loop == null)
            return;

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
            _lifetimeCts = null;
            _loopTask = null;
        }
    }

    public Task<DreamsRunState> RunOnceAsync(bool force = false, CancellationToken cancellationToken = default) =>
        RunOnceCoreAsync(
            force,
            nextRunAt: null,
            trigger: force ? DreamsConstants.TriggerManual : DreamsConstants.TriggerScheduled,
            request: null,
            waitForCompletion: true,
            cancellationToken);

    /// <summary>
    /// Returns the latest persisted Dreams run state, or <c>null</c> when Dreams has never run.
    /// </summary>
    public DreamsRunState? LoadLatestState() => stateStore.Load();

    public DreamsRunState? LoadRun(string runId) => stateStore.Load(runId);

    public IReadOnlyList<DreamsRunState> ListRuns(bool includeArchived = false) =>
        stateStore.List(includeArchived);

    /// <summary>
    /// Requests an immediate forced Dream Run without waiting for completion.
    /// </summary>
    public Task<DreamsRunState> RequestRunAsync(
        DreamsRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return RunOnceCoreAsync(
            force: true,
            nextRunAt: null,
            trigger: DreamsConstants.TriggerManual,
            request,
            waitForCompletion: false,
            cancellationToken);
    }

    public async Task<DreamsRunState?> CancelRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var state = stateStore.Load(runId);
        if (state == null)
            return null;

        if (state.Status == DreamsRunStatuses.Running
            && string.Equals(_activeRunId, runId, StringComparison.Ordinal))
        {
            var cts = _activeRunCts;
            if (cts != null)
                await cts.CancelAsync().ConfigureAwait(false);
        }

        return stateStore.Load(runId) ?? state;
    }

    public DreamsRunState? ApplyRun(string runId)
    {
        ThrowIfDisposed();
        var state = stateStore.Load(runId);
        if (state?.OutputStoreId == null)
            return state;
        if (state.Status != DreamsRunStatuses.Succeeded)
            throw new InvalidOperationException("Only succeeded Dream runs can be applied.");
        if (state.ReviewStatus is DreamsReviewStatuses.Discarded or DreamsReviewStatuses.Archived)
            throw new InvalidOperationException("Discarded or archived Dream runs cannot be applied.");

        dreamStore.SetActiveStore(state.OutputStoreId);
        state.ReviewStatus = DreamsReviewStatuses.Applied;
        stateStore.SaveRun(state);
        return state;
    }

    public DreamsRunState? DiscardRun(string runId)
    {
        ThrowIfDisposed();
        var state = stateStore.Load(runId);
        if (state == null)
            return null;
        if (state.ReviewStatus != DreamsReviewStatuses.Applied)
            state.ReviewStatus = DreamsReviewStatuses.Discarded;
        stateStore.SaveRun(state);
        return state;
    }

    public DreamsRunState? ArchiveRun(string runId)
    {
        ThrowIfDisposed();
        var state = stateStore.Load(runId);
        if (state == null)
            return null;
        state.ReviewStatus = DreamsReviewStatuses.Archived;
        stateStore.SaveRun(state);
        return state;
    }

    public async Task<DreamsRunDeletionResult?> DeleteRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Dream runs cannot be deleted while a run is active.");

        try
        {
            var state = stateStore.Load(runId);
            if (state == null)
                return null;
            if (state.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue)
                throw new InvalidOperationException("A running Dream run cannot be deleted.");

            return DeleteRunCore(state);
        }
        finally
        {
            _runGate.Release();
        }
    }

    public async Task<IReadOnlyList<DreamsRunDeletionResult>> DeleteAllRunsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Dream runs cannot be deleted while a run is active.");

        try
        {
            var states = stateStore.List(includeArchived: true);
            if (states.Any(static state => state.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue))
                throw new InvalidOperationException("Dream runs cannot be deleted while a run is active.");

            stateStore.DeleteAllRuns();
            return states.Select(DeleteRunArtifactsCore).ToArray();
        }
        finally
        {
            _runGate.Release();
        }
    }

    private DreamsRunDeletionResult DeleteRunCore(DreamsRunState state)
    {
        if (!stateStore.DeleteRun(state.Id))
            throw new InvalidOperationException("Dream run not found.");
        return DeleteRunArtifactsCore(state);
    }

    private DreamsRunDeletionResult DeleteRunArtifactsCore(DreamsRunState state)
    {
        var warnings = new List<string>();
        var activeStoreId = dreamStore.GetActiveStoreId();
        var activeStorePreserved = !string.IsNullOrWhiteSpace(state.OutputStoreId)
            && string.Equals(state.OutputStoreId, activeStoreId, StringComparison.OrdinalIgnoreCase);
        var outputStoreDeleted = false;

        if (!string.IsNullOrWhiteSpace(state.OutputStoreId) && !activeStorePreserved)
        {
            try
            {
                outputStoreDeleted = dreamStore.DeleteStore(state.OutputStoreId);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to delete output store '{state.OutputStoreId}': {ex.Message}");
                logger?.LogWarning(ex, "Failed to delete output store {StoreId} for Dream run {RunId}.", state.OutputStoreId, state.Id);
            }
        }

        return new DreamsRunDeletionResult(state, outputStoreDeleted, activeStorePreserved, warnings);
    }

    /// <summary>
    /// Requests an immediate forced Dream Run without waiting for completion.
    /// </summary>
    public void RequestRun()
    {
        ThrowIfDisposed();
        _ = RequestRunAsync(cancellationToken: CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);
        _runGate.Dispose();
        _disposed = true;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var initialDelay = ComputeInitialSchedulerDelay();
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var interval = NormalizeInterval(config.Dreams.Interval);
                var nextRunAt = _timeProvider.GetUtcNow().Add(interval);
                await RunOnceCoreAsync(
                        force: false,
                        nextRunAt,
                        DreamsConstants.TriggerScheduled,
                        request: null,
                        waitForCompletion: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                var delay = ComputeDelayUntilNextScheduledRun(interval);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Dreams scheduler loop failed.");
        }
    }

    private async Task<DreamsRunState> RunOnceCoreAsync(
        bool force,
        DateTimeOffset? nextRunAt,
        string trigger,
        DreamsRunRequest? request,
        bool waitForCompletion,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var previous = stateStore.Load();
        if (!config.Dreams.Enabled)
            return CompleteSkipped("dreams_disabled", previous, nextRunAt, trigger);

        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return IsRunning(previous)
                ? previous!
                : CreateSkipped("dream_run_already_active", previous, nextRunAt, trigger);

        var startedAt = _timeProvider.GetUtcNow();
        var state = new DreamsRunState
        {
            Id = $"dream_{startedAt:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31],
            Status = DreamsRunStatuses.Running,
            StartedAt = startedAt,
            NextRunAt = nextRunAt,
            CompletedTurnWatermark = previous?.CompletedTurnWatermark ?? 0,
            Model = string.IsNullOrWhiteSpace(request?.Model) ? dreamRunner.ModelId : request.Model.Trim(),
            Trigger = trigger,
            Instructions = request?.Instructions
        };
        stateStore.Save(state);

        var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRunCts = linkedTokenSource;
        _activeRunId = state.Id;

        if (waitForCompletion)
        {
            return await CompleteRunAsync(state, previous, force, nextRunAt, trigger, request, linkedTokenSource.Token)
                .ConfigureAwait(false);
        }

        var runToken = _lifetimeCts?.Token ?? CancellationToken.None;
        linkedTokenSource.Dispose();
        linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        _activeRunCts = linkedTokenSource;
        _ = Task.Run(
            () => CompleteRunAsync(state, previous, force, nextRunAt, trigger, request, linkedTokenSource.Token),
            CancellationToken.None);
        return state;
    }

    private async Task<DreamsRunState> CompleteRunAsync(
        DreamsRunState state,
        DreamsRunState? previous,
        bool force,
        DateTimeOffset? nextRunAt,
        string trigger,
        DreamsRunRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await inputCollector.CollectAsync(request, cancellationToken).ConfigureAwait(false);
            state.ProcessedThreadCount = input.Threads.Count;
            state.CandidateThreadCount = input.Threads.Count;
            state.ProcessedThreadIds = input.Threads.Select(static thread => thread.ThreadId).ToList();
            stateStore.Save(state);

            if (!input.HasEvidence)
                return Finish(state, DreamsRunStatuses.Skipped, "no_input_evidence", input.CompletedTurnCount, nextRunAt);

            if (!force)
            {
                var completedTurnDelta = input.CompletedTurnCount - (previous?.CompletedTurnWatermark ?? 0);
                if (completedTurnDelta < config.Dreams.MinCompletedTurnsSinceLastRun)
                {
                    return Finish(
                        state,
                        DreamsRunStatuses.Skipped,
                        "insufficient_new_completed_turns",
                        previous?.CompletedTurnWatermark ?? 0,
                        nextRunAt);
                }
            }

            var result = await dreamRunner.GenerateAsync(
                    input,
                    state.Id,
                    trigger,
                    outputStoreId: null,
                    modelId: state.Model,
                    binding =>
                    {
                        state.ThreadId = binding.ThreadId;
                        if (!string.IsNullOrWhiteSpace(binding.TurnId))
                            state.TurnId = binding.TurnId;
                        stateStore.Save(state);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            state.ThreadId = result.ThreadId;
            state.TurnId = result.TurnId;
            state.OutputStoreId = result.OutputStoreId;
            state.TurnIds = result.TurnIds?.ToList() ?? [];
            state.Usage = result.Usage;
            state.ErrorType = result.ErrorType;
            if (result.Diagnostics != null)
            {
                state.CandidateThreadCount = result.Diagnostics.CandidateThreadCount;
                state.EvidenceThreadIds = result.Diagnostics.EvidenceThreadIds.ToList();
                state.EvidenceSearchCount = result.Diagnostics.EvidenceSearchCount;
                state.EvidenceReadCount = result.Diagnostics.EvidenceReadCount;
            }
            stateStore.Save(state);

            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.DreamMarkdown))
            {
                return Finish(
                    state,
                    DreamsRunStatuses.Failed,
                    result.Message ?? "dream_generation_failed",
                    previous?.CompletedTurnWatermark ?? 0,
                    nextRunAt);
            }

            if (config.Dreams.AutoApply && !string.IsNullOrWhiteSpace(result.OutputStoreId))
            {
                dreamStore.SetActiveStore(result.OutputStoreId);
                state.ReviewStatus = DreamsReviewStatuses.Applied;
                state.AutoApplied = true;
            }
            else
            {
                state.ReviewStatus = DreamsReviewStatuses.Pending;
                state.AutoApplied = false;
            }
            state.DreamWritten = true;
            state.HistoryWritten = false;
            if (!string.IsNullOrWhiteSpace(result.OutputStoreId))
            {
                var topics = dreamStore.ListTopicFiles(result.OutputStoreId);
                state.TopicFilesWritten = topics.Count;
                state.WrittenPaths = [$"stores/{result.OutputStoreId}/INDEX.md"];
                state.WrittenPaths.AddRange(topics.Select(topic => $"stores/{result.OutputStoreId}/memory/{topic.Path}"));
                state.InputManifestPath = Path.Combine(dreamStore.GetRunInputDirectory(state.Id), "MANIFEST.md");
            }
            return Finish(state, DreamsRunStatuses.Succeeded, null, input.CompletedTurnCount, nextRunAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Finish(
                state,
                DreamsRunStatuses.Canceled,
                "dream_run_cancelled",
                previous?.CompletedTurnWatermark ?? 0,
                nextRunAt);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Dreams run failed.");
            return Finish(
                state,
                DreamsRunStatuses.Failed,
                ex.Message,
                previous?.CompletedTurnWatermark ?? 0,
                nextRunAt);
        }
        finally
        {
            if (string.Equals(_activeRunId, state.Id, StringComparison.Ordinal))
            {
                _activeRunId = null;
                _activeRunCts?.Dispose();
                _activeRunCts = null;
            }
            _runGate.Release();
        }
    }

    private DreamsRunState Finish(
        DreamsRunState state,
        string status,
        string? message,
        int completedTurnWatermark,
        DateTimeOffset? nextRunAt)
    {
        state.Status = status;
        state.EndedAt = _timeProvider.GetUtcNow();
        state.Message = message;
        state.NextRunAt = nextRunAt;
        state.CompletedTurnWatermark = completedTurnWatermark;
        stateStore.Save(state);
        return state;
    }

    private DreamsRunState CreateSkipped(
        string message,
        DreamsRunState? previous,
        DateTimeOffset? nextRunAt,
        string trigger)
    {
        var now = _timeProvider.GetUtcNow();
        return new DreamsRunState
        {
            Id = $"dream_{now:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31],
            Status = DreamsRunStatuses.Skipped,
            StartedAt = now,
            EndedAt = now,
            Message = message,
            NextRunAt = nextRunAt,
            CompletedTurnWatermark = previous?.CompletedTurnWatermark ?? 0,
            Model = dreamRunner.ModelId,
            Trigger = trigger
        };
    }

    private DreamsRunState CompleteSkipped(
        string message,
        DreamsRunState? previous,
        DateTimeOffset? nextRunAt,
        string trigger)
    {
        var state = CreateSkipped(message, previous, nextRunAt, trigger);
        stateStore.Save(state);
        return state;
    }

    private static bool IsRunning(DreamsRunState? state) =>
        state?.Status == DreamsRunStatuses.Running && !state.EndedAt.HasValue;

    private static TimeSpan NormalizeDelay(TimeSpan value) =>
        value <= TimeSpan.Zero ? TimeSpan.Zero : value;

    private static TimeSpan NormalizeInterval(TimeSpan value) =>
        value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;

    private TimeSpan ComputeInitialSchedulerDelay()
    {
        var now = _timeProvider.GetUtcNow();
        var previous = stateStore.Load();
        if (previous?.NextRunAt is { } nextRunAt && nextRunAt > now)
            return nextRunAt - now;

        return NormalizeDelay(config.Dreams.StartupDelay);
    }

    private TimeSpan ComputeDelayUntilNextScheduledRun(TimeSpan interval)
    {
        var now = _timeProvider.GetUtcNow();
        var nextRunAt = stateStore.Load()?.NextRunAt ?? now.Add(interval);
        return nextRunAt > now ? nextRunAt - now : TimeSpan.Zero;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DreamsService));
    }
}
