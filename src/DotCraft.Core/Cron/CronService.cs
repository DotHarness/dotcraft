using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Cron;

public sealed class CronService : IDisposable
{
    private readonly string _storePath;

    private readonly ILogger<CronService>? _logger;

    private readonly CronStore _store;

    private CancellationTokenSource? _cts;

    // Guards _store.Jobs list mutations and reads (fast, sync-only).
    private readonly object _storeLock = new();

    // Prevents concurrent job execution — at most one job runs at a time.
    private readonly SemaphoreSlim _execLock = new(1, 1);

    // FileSystemWatcher for detecting external store mutations (e.g. manual edits).
    private FileSystemWatcher? _watcher;

    // Set to true while this process is writing the store file so the FSW handler
    // ignores the change event and does not trigger a self-reload.
    private volatile bool _selfWriting;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Runs the agent for a due job. Return <see cref="CronOnJobResult"/> to persist last thread/result.
    /// </summary>
    public Func<CronJob, Task<CronOnJobResult>>? OnJob { get; set; }

    /// <summary>
    /// Fired after <see cref="ExecuteJobAsync"/> persists the store. <paramref name="removedFromStore"/> is true when the job was deleted (one-shot / delete-after-run).
    /// </summary>
    public Action<CronJob?, string, bool>? CronJobPersistedAfterExecution { get; set; }

    public CronService(string storePath, ILogger<CronService>? logger = null)
    {
        _storePath = storePath;
        _logger = logger;
        _store = LoadStore();
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        StartWatching();
        _ = RunTimerAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Reloads the cron store from disk, replacing the current in-memory job list.
    /// Safe to call from any thread. In AppServer mode this is triggered automatically
    /// by the FileSystemWatcher when an external process writes to the store file.
    /// Command-line callers can use this before reads to see server-side changes.
    /// </summary>
    public void ReloadStore()
    {
        var fresh = LoadStore();
        lock (_storeLock)
        {
            _store.Jobs.Clear();
            _store.Jobs.AddRange(fresh.Jobs);
        }
    }

    public CronJob AddJob(string name, CronSchedule schedule, CronPayload payload, bool deleteAfterRun = false)
    {
        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Schedule = schedule,
            Payload = payload,
            CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DeleteAfterRun = deleteAfterRun,
            State = new CronJobState()
        };
        ComputeNextRun(job);
        lock (_storeLock)
        {
            _store.Jobs.Add(job);
        }
        SaveStore();
        return job;
    }

    public bool RemoveJob(string jobId)
    {
        bool removed;
        lock (_storeLock)
        {
            removed = _store.Jobs.RemoveAll(j => j.Id == jobId) > 0;
        }
        if (removed) SaveStore();
        return removed;
    }

    public CronJob? EnableJob(string jobId, bool enabled)
    {
        CronJob? job;
        lock (_storeLock)
        {
            job = _store.Jobs.Find(j => j.Id == jobId);
            if (job == null) return null;
            job.Enabled = enabled;
            if (enabled)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (!job.State.NextRunAtMs.HasValue || job.State.NextRunAtMs.Value <= now)
                    ComputeNextRun(job);
            }
        }
        SaveStore();
        return job;
    }

    /// <summary>
    /// Queues a job for one immediate manual execution without changing its enabled state.
    /// Returns null when the job does not exist.
    /// </summary>
    public CronJob? RunJobNow(string jobId)
    {
        CronJob? job;
        lock (_storeLock)
        {
            job = _store.Jobs.Find(j => j.Id == jobId);
        }

        if (job == null)
            return null;

        _ = Task.Run(async () =>
        {
            await _execLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ExecuteJobAsync(jobId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Manual cron job {JobId} execution failed", jobId);
            }
            finally
            {
                _execLock.Release();
            }
        });

        return job;
    }

    public List<CronJob> ListJobs(bool includeDisabled = false)
    {
        lock (_storeLock)
        {
            return includeDisabled
                ? _store.Jobs.ToList()
                : _store.Jobs.Where(j => j.Enabled).ToList();
        }
    }

    private async Task RunTimerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                await CheckAndRunDueJobsAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cron timer error");
            }
        }
    }

    private async Task CheckAndRunDueJobsAsync()
    {
        // Snapshot due job IDs (not references) under _storeLock so a concurrent
        // ReloadStore() or wire mutation doesn't corrupt the iteration.
        List<string> dueJobIds;
        lock (_storeLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            dueJobIds = _store.Jobs
                .Where(j => j.Enabled && j.State.NextRunAtMs.HasValue && j.State.NextRunAtMs.Value <= now)
                .Select(j => j.Id)
                .ToList();
        }

        foreach (var jobId in dueJobIds)
        {
            // _execLock ensures jobs run sequentially, not concurrently.
            await _execLock.WaitAsync();
            try
            {
                // Re-check that the job still exists and is enabled after acquiring the lock,
                // since a concurrent RemoveJob or ReloadStore may have removed it.
                bool stillValid;
                lock (_storeLock)
                {
                    stillValid = _store.Jobs.Any(j => j.Id == jobId && j.Enabled);
                }
                if (stillValid)
                    await ExecuteJobAsync(jobId);
            }
            finally
            {
                _execLock.Release();
            }
        }
    }

    private async Task ExecuteJobAsync(string jobId)
    {
        // Capture a copy of the immutable fields needed to run the job.
        CronJob? jobSnapshot;
        lock (_storeLock)
        {
            jobSnapshot = _store.Jobs.Find(j => j.Id == jobId);
        }
        if (jobSnapshot == null) return;

        try
        {
            CronOnJobResult? outcome = null;
            if (OnJob != null)
                outcome = await OnJob(jobSnapshot);

            // Re-lookup by ID so mutations are applied to the current in-list object,
            // even if ReloadStore() replaced the reference while OnJob was running.
            lock (_storeLock)
            {
                var current = _store.Jobs.Find(j => j.Id == jobId);
                if (current != null)
                {
                    current.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (outcome != null)
                    {
                        current.State.LastThreadId = outcome.LastThreadId;
                        current.State.LastResult = TruncateLastResult(outcome.LastResult);
                        current.State.LastStatus = outcome.Ok ? "ok" : "error";
                        current.State.LastError = outcome.Ok ? null : outcome.LastError;
                    }
                    else
                    {
                        current.State.LastStatus = "ok";
                        current.State.LastError = null;
                    }

                    if (current.DeleteAfterRun || current.Schedule.Kind == "at")
                        _store.Jobs.Remove(current);
                    else
                        ComputeNextRun(current);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Cron job {JobId} ({JobName}) execution failed", jobSnapshot.Id, jobSnapshot.Name);
            lock (_storeLock)
            {
                var current = _store.Jobs.Find(j => j.Id == jobId);
                if (current != null)
                {
                    current.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    current.State.LastStatus = "error";
                    current.State.LastError = ex.Message;
                    ComputeNextRun(current);
                }
            }
        }
        SaveStore();
        NotifyCronJobPersisted(jobId);
    }

    private void NotifyCronJobPersisted(string jobId)
    {
        lock (_storeLock)
        {
            var j = _store.Jobs.Find(x => x.Id == jobId);
            CronJobPersistedAfterExecution?.Invoke(j, jobId, j == null);
        }
    }

    private static string? TruncateLastResult(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        const int max = 500;
        return s.Length <= max ? s : s[..max];
    }

    private static void ComputeNextRun(CronJob job)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        job.State.NextRunAtMs = CronScheduleHelpers.ComputeNextRunMs(
            job.Schedule,
            job.State.LastRunAtMs,
            now);
    }

    /// <summary>
    /// Starts watching the store file for external changes (e.g. manual edits).
    /// Own writes are suppressed via <see cref="_selfWriting"/> so SaveStore() does
    /// not trigger a reload loop.
    /// </summary>
    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(_storePath);
        var file = Path.GetFileName(_storePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        // The directory may not exist yet if no jobs have been created.
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // Debounce: wait 200ms after the last write event before reloading, to avoid
        // reading a partially-written file. Skip events caused by our own SaveStore().
        _watcher.Changed += (_, _) =>
        {
            if (_selfWriting) return;
            Task.Delay(200).ContinueWith(_ =>
            {
                if (!_selfWriting) ReloadStore();
            });
        };
    }

    private CronStore LoadStore()
    {
        if (!File.Exists(_storePath))
            return new CronStore();
        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<CronStore>(json, JsonOptions) ?? new CronStore();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load cron store from {Path}, using empty store", _storePath);
            return new CronStore();
        }
    }

    private void SaveStore()
    {
        // Deep-copy each CronJob (including its mutable CronJobState) under _storeLock so
        // the serialized snapshot is consistent and doesn't race with ExecuteJobAsync which
        // mutates State fields outside the lock during OnJob execution.
        // File I/O is done outside the lock to avoid blocking other operations during a slow write.
        CronStore snapshot;
        lock (_storeLock)
        {
            snapshot = new CronStore
            {
                Version = _store.Version,
                Jobs = _store.Jobs.Select(j => new CronJob
                {
                    Id = j.Id,
                    Name = j.Name,
                    Enabled = j.Enabled,
                    Schedule = j.Schedule,       // immutable after creation
                    Payload = j.Payload,         // immutable after creation
                    CreatedAtMs = j.CreatedAtMs,
                    DeleteAfterRun = j.DeleteAfterRun,
                    State = new CronJobState
                    {
                        NextRunAtMs = j.State.NextRunAtMs,
                        LastRunAtMs = j.State.LastRunAtMs,
                        LastStatus = j.State.LastStatus,
                        LastError = j.State.LastError,
                        LastThreadId = j.State.LastThreadId,
                        LastResult = j.State.LastResult
                    }
                }).ToList()
            };
        }

        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Set the flag before writing so the FSW Changed event is ignored.
        _selfWriting = true;
        try
        {
            File.WriteAllText(_storePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        finally
        {
            _selfWriting = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _execLock.Dispose();
    }
}
