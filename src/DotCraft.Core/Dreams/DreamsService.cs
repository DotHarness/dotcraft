using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging;

namespace DotCraft.Dreams;

/// <summary>
/// Runs Dreams for a workspace: scheduled and manual consolidation of its memory stores, one run at a time, and their review.
/// </summary>
public sealed partial class DreamsService : IAsyncDisposable
{
    private const int ManifestThreadLimit = 20;
    private static readonly TimeSpan MaxCheckCadence = TimeSpan.FromHours(1);

    private readonly AppConfig _config;
    private readonly IDreamsRunner _runner;
    private readonly IAppConfigMonitor? _appConfigMonitor;
    private readonly ILogger<DreamsService>? _logger;
    private readonly TimeProvider _time;
    private readonly DreamsArtifacts _artifacts;
    private readonly DreamsStateStore _states;
    private readonly DreamsStoreCatalog _stores;
    private readonly DreamsThreadCatalog _threads;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource? _lifetimeCts;
    private Task? _loopTask;
    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _activeRunCts;
    private string? _activeRunId;
    private Task _batchTask = Task.CompletedTask;
    private DateTimeOffset? _nextCheckAt;
    private bool _disposed;

    public DreamsService(
        AppConfig config,
        DotCraftPaths paths,
        ThreadStore threadStore,
        IDreamsRunner runner,
        IAppConfigMonitor? appConfigMonitor = null,
        ILogger<DreamsService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _config = config;
        _runner = runner;
        _appConfigMonitor = appConfigMonitor;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        WorkspacePath = paths.WorkspacePath;
        _artifacts = new DreamsArtifacts(paths.Data.RootPath);
        _states = new DreamsStateStore(_artifacts.DreamsDirectoryPath);
        _stores = new DreamsStoreCatalog(paths);
        _threads = new DreamsThreadCatalog(threadStore, paths);
    }

    public string WorkspacePath { get; }

    /// <summary>Whether a run is in progress or about to start.</summary>
    public bool IsRunning => _runGate.CurrentCount == 0;

    /// <summary>The next scheduled check, or null while the scheduler is stopped.</summary>
    public DateTimeOffset? NextRunAt => _loopTask == null ? null : _nextCheckAt;

    private bool Enabled => _config.Dreams.Enabled && _config.Memory.Enabled;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_loopTask != null || !Enabled)
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
            _loopTask = null;
            _lifetimeCts = null;
            _nextCheckAt = null;
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);
        CancelBatch();
        try
        {
            await _batchTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Dreams run batch ended with an error during disposal.");
        }

        _disposed = true;
    }

    public async Task<IReadOnlyList<DreamsRunState>> RunScheduledCheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var runs = new List<DreamsRunState>();
        foreach (var store in _stores.List())
        {
            if (!Enabled || cancellationToken.IsCancellationRequested)
                break;

            var last = ConsolidationLock.LastConsolidatedAt(store.Directory);
            if (last is { } consolidatedAt && _time.GetUtcNow() - consolidatedAt < NormalizeInterval(_config.Dreams.Interval))
                continue;

            var minSessions = Math.Max(1, _config.Dreams.MinSessionsSinceLastRun);
            var threads = await _threads.FindAsync(store, last, null, minSessions, cancellationToken).ConfigureAwait(false);
            if (threads.Count < minSessions)
                continue;
            if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                break;

            try
            {
                var held = ConsolidationLock.TryAcquire(store.Directory, _time.GetUtcNow());
                if (held == null)
                    continue;

                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _batchCts = batchCts;
                runs.Add(await RunAsync(new PlannedRun(store, DreamsConstants.TriggerScheduled, null, held), null, batchCts.Token)
                    .ConfigureAwait(false));
            }
            finally
            {
                _batchCts = null;
                _runGate.Release();
            }
        }

        return runs;
    }

    /// <summary>
    /// Requests runs for every store that holds memory files or has eligible Threads. Returns the first run,
    /// the active run when one is in progress, or null when Dreams or memory is disabled.
    /// </summary>
    public async Task<DreamsRunState?> RequestRunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!Enabled)
            return null;
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return LoadActiveRun();

        var planned = new List<PlannedRun>();
        try
        {
            foreach (var store in _stores.List())
            {
                if (DreamsStoreFiles.HasMemoryFiles(store.Directory)
                    || (await _threads.FindAsync(store, ConsolidationLock.LastConsolidatedAt(store.Directory), null, 1, cancellationToken)
                        .ConfigureAwait(false)).Count > 0)
                {
                    planned.Add(new PlannedRun(store, DreamsConstants.TriggerManual, null, null));
                }
            }
        }
        catch
        {
            _runGate.Release();
            throw;
        }

        return StartBatch(planned.Count > 0 ? planned : [new PlannedRun(_stores.Workspace, DreamsConstants.TriggerManual, null, null)]);
    }

    /// <summary>
    /// Requests a run for one store. Returns the new run, the active run when one is in progress,
    /// or null when Dreams or memory is disabled.
    /// </summary>
    /// <exception cref="ArgumentException">The scope names no consolidatable store.</exception>
    public async Task<DreamsRunState?> CreateRunAsync(DreamsRunRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var store = _stores.Find(request.Scope)
                    ?? throw new ArgumentException($"'{request.Scope}' is not a consolidatable memory store.", nameof(request));
        if (!Enabled)
            return null;
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return LoadActiveRun();

        return StartBatch([new PlannedRun(store, DreamsConstants.TriggerManual, request, null)]);
    }

    /// <summary>Cancels and awaits an active run, then clears the stores with <paramref name="clearStores"/> together with every Dreams run.</summary>
    public async Task ResetAsync(Action clearStores, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelBatch();
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_reviewSync)
            {
                clearStores();
                _artifacts.ClearAll();
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private DreamsRunState StartBatch(IReadOnlyList<PlannedRun> runs)
    {
        DreamsRunState first;
        try
        {
            first = CreateRunState(runs[0]);
        }
        catch
        {
            _runGate.Release();
            throw;
        }

        var batchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts?.Token ?? CancellationToken.None);
        _batchCts = batchCts;
        _batchTask = Task.Run(async () =>
        {
            try
            {
                await RunAsync(runs[0], first, batchCts.Token).ConfigureAwait(false);
                foreach (var run in runs.Skip(1).TakeWhile(_ => !batchCts.IsCancellationRequested))
                    await RunAsync(run, null, batchCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dreams run batch failed.");
            }
            finally
            {
                _batchCts = null;
                batchCts.Dispose();
                _runGate.Release();
            }
        }, CancellationToken.None);
        return first;
    }

    private async Task<DreamsRunState> RunAsync(PlannedRun plan, DreamsRunState? state, CancellationToken batchToken)
    {
        try
        {
            state ??= CreateRunState(plan);
        }
        catch
        {
            plan.Lock?.Rollback();
            throw;
        }

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
        _activeRunCts = runCts;
        _activeRunId = state.Id;
        var held = plan.Lock;
        try
        {
            var store = plan.Store;
            var since = held != null ? held.PreviousConsolidatedAt : ConsolidationLock.LastConsolidatedAt(store.Directory);
            var threads = await _threads.FindAsync(store, since, plan.Request?.ThreadIds, ManifestThreadLimit, runCts.Token)
                .ConfigureAwait(false);
            if (!DreamsStoreFiles.HasMemoryFiles(store.Directory) && threads.Count == 0)
                return Fail(state, held, null, "nothing_to_consolidate", DreamsRunStatuses.Skipped);

            held ??= ConsolidationLock.TryAcquire(store.Directory, _time.GetUtcNow());
            if (held == null)
                return Finish(state, DreamsRunStatuses.Skipped, "consolidation_locked");

            var input = PrepareInput(state, store, since, threads);
            _states.Save(state);
            var result = await _runner.GenerateAsync(input, state.Model ?? _runner.ModelId, binding =>
                {
                    state.ThreadId = binding.ThreadId;
                    state.TurnId = binding.TurnId ?? state.TurnId;
                    _states.Save(state);
                }, runCts.Token)
                .ConfigureAwait(false);
            runCts.Token.ThrowIfCancellationRequested();
            Record(state, result);
            if (!result.Succeeded)
                return Fail(state, held, result.ErrorType ?? "generation_failed", result.Message ?? "dream_generation_failed");
            if (DreamsStoreFiles.Validate(input.CandidateDirectory) is { } invalid)
                return Fail(state, held, "invalid_candidate", invalid);

            var baseline = DreamsStoreFiles.Read(input.BaselineDirectory);
            var candidate = DreamsStoreFiles.Read(input.CandidateDirectory);
            state.Changes = DreamsStoreFiles.Diff(baseline, candidate);
            state.Summary = result.Summary;
            Settle(held, commit: true);
            held = null;
            Review(state, store, baseline, candidate);
            return Finish(state, DreamsRunStatuses.Succeeded, state.Message);
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            return Fail(state, held, null, "dream_run_cancelled", DreamsRunStatuses.Canceled);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Dreams run {RunId} failed.", state.Id);
            return Fail(state, held, "internal_error", ex.Message);
        }
        finally
        {
            _activeRunId = null;
            _activeRunCts = null;
        }
    }

    private DreamsRunInput PrepareInput(
        DreamsRunState state,
        DreamsStore store,
        DateTimeOffset? since,
        IReadOnlyList<DreamsThreadInput> threads)
    {
        var files = MemoryFiles.List(store.Directory);
        var snapshot = DreamsStoreFiles.Read(store.Directory);
        DreamsStoreFiles.Write(_artifacts.BaselineDirectory(state.Id), snapshot);
        DreamsStoreFiles.Write(_artifacts.CandidateDirectory(state.Id), snapshot);
        var manifest = DreamsManifest.Write(_artifacts, state, since, files, threads);
        state.CandidateThreadCount = threads.Count;
        state.InputManifestPath = _artifacts.ManifestPath(state.Id);
        return new DreamsRunInput(
            state.Id,
            state.Trigger,
            WorkspacePath,
            _artifacts.BaselineDirectory(state.Id),
            _artifacts.CandidateDirectory(state.Id),
            manifest,
            threads,
            state.Instructions);
    }

    private void Review(
        DreamsRunState state,
        DreamsStore store,
        IReadOnlyDictionary<string, byte[]> baseline,
        IReadOnlyDictionary<string, byte[]> candidate)
    {
        if (state.Changes is not { Any: true })
        {
            state.ReviewStatus = DreamsReviewStatuses.Applied;
            return;
        }

        state.ReviewStatus = DreamsReviewStatuses.Pending;
        if (!_config.Dreams.AutoApply)
            return;

        if (DreamsStoreFiles.TryReplace(store.Directory, baseline, candidate))
        {
            state.ReviewStatus = DreamsReviewStatuses.Applied;
            state.AutoApplied = true;
            NotifyStoreChanged();
        }
        else
        {
            RecordStoreChanged(state);
        }
    }

    private static void Record(DreamsRunState state, DreamsGenerationResult result)
    {
        state.ThreadId = result.ThreadId ?? state.ThreadId;
        state.TurnId = result.TurnId ?? state.TurnId;
        state.Usage = result.Usage;
        if (result.Diagnostics is { } diagnostics)
        {
            state.EvidenceThreadIds = diagnostics.EvidenceThreadIds.ToList();
            state.EvidenceSearchCount = diagnostics.EvidenceSearchCount;
            state.EvidenceReadCount = diagnostics.EvidenceReadCount;
        }
    }

    private DreamsRunState Fail(
        DreamsRunState state,
        ConsolidationLock? held,
        string? errorType,
        string message,
        string status = DreamsRunStatuses.Failed)
    {
        Settle(held, commit: false);
        state.ErrorType = errorType;
        return Finish(state, status, message);
    }

    private void Settle(ConsolidationLock? held, bool commit)
    {
        if (held == null)
            return;

        try
        {
            if (commit)
                held.Commit();
            else
                held.Rollback();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to release the Dreams consolidation lock.");
        }
    }

    private DreamsRunState CreateRunState(PlannedRun plan)
    {
        var startedAt = _time.GetUtcNow();
        var state = new DreamsRunState
        {
            Id = $"dream_{startedAt:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31],
            Scope = plan.Store.Scope,
            Status = DreamsRunStatuses.Running,
            Trigger = plan.Trigger,
            StartedAt = startedAt,
            Model = string.IsNullOrWhiteSpace(plan.Request?.Model) ? _runner.ModelId : plan.Request.Model.Trim(),
            Instructions = NormalizeInstructions(plan.Request?.Instructions)
        };
        _states.Save(state);
        return state;
    }

    private DreamsRunState Finish(DreamsRunState state, string status, string? message)
    {
        state.Status = status;
        state.EndedAt = _time.GetUtcNow();
        state.Message = message;
        _states.Save(state);
        return state;
    }

    private DreamsRunState? LoadActiveRun() =>
        _activeRunId is { } id ? _states.Load(id) ?? _states.Load() : _states.Load();

    private void CancelBatch()
    {
        try
        {
            _batchCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var delay = NormalizeDelay(_config.Dreams.StartupDelay);
            while (!cancellationToken.IsCancellationRequested)
            {
                _nextCheckAt = _time.GetUtcNow().Add(delay);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);

                try
                {
                    await RunScheduledCheckAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogError(ex, "Dreams scheduled check failed.");
                }

                var interval = NormalizeInterval(_config.Dreams.Interval);
                delay = interval < MaxCheckCadence ? interval : MaxCheckCadence;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string? NormalizeInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            return null;

        var trimmed = instructions.Trim();
        return trimmed.Length <= 4_000 ? trimmed : trimmed[..4_000] + "\n[trimmed]";
    }

    private static TimeSpan NormalizeDelay(TimeSpan value) =>
        value <= TimeSpan.Zero ? TimeSpan.Zero : value;

    private static TimeSpan NormalizeInterval(TimeSpan value) =>
        value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record PlannedRun(DreamsStore Store, string Trigger, DreamsRunRequest? Request, ConsolidationLock? Lock);
}
