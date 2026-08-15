using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using DotCraft.Channels;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Local;
using DotCraft.Automations.Protocol;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using AutomationTask = DotCraft.Automations.AutomationTask;
using AutomationThreadBinding = DotCraft.Automations.AutomationThreadBinding;

namespace DotCraft.Automations.Orchestrator;

/// <summary>
/// Polls local automation tasks and dispatches them to the shared session service.
/// </summary>
public sealed class AutomationOrchestrator
{
    private const string AutomationsChannelName = "automations";

    private readonly AutomationsConfig _config;
    private readonly LocalWorkflowLoader _workflowLoader;
    private readonly IToolProfileRegistry _toolProfileRegistry;
    private readonly LocalAutomationSource _source;
    private readonly IChannelRuntimeRegistry? _channelRuntimeRegistry;
    private readonly IAppConfigMonitor? _appConfigMonitor;
    private readonly ILogger<AutomationOrchestrator> _logger;
    private readonly OrchestratorState _state = new();
    private readonly ConcurrentDictionary<string, AutomationTask> _allTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    private AutomationSessionClient? _sessionClient;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private PeriodicTimer? _pollTimer;
    private int _started;
    private int _stopped;
    private DateTimeOffset _lastRetentionSweepAt = DateTimeOffset.MinValue;

    public AutomationOrchestrator(
        AutomationsConfig config,
        LocalWorkflowLoader workflowLoader,
        IToolProfileRegistry toolProfileRegistry,
        LocalAutomationSource source,
        ILogger<AutomationOrchestrator> logger,
        IChannelRuntimeRegistry? channelRuntimeRegistry = null,
        IAppConfigMonitor? appConfigMonitor = null)
    {
        _config = config;
        _workflowLoader = workflowLoader;
        _toolProfileRegistry = toolProfileRegistry;
        _source = source;
        _channelRuntimeRegistry = channelRuntimeRegistry;
        _appConfigMonitor = appConfigMonitor;
        _logger = logger;
        _concurrency = new SemaphoreSlim(config.MaxConcurrentTasks, config.MaxConcurrentTasks);
    }

    /// <summary>
    /// Fired after every task status transition. Subscribers push Wire Protocol notifications.
    /// </summary>
    public event Func<AutomationTask, AutomationTaskStatus, Task>? OnTaskStatusChanged;

    /// <summary>
    /// Supplies the session client created with the host's shared <see cref="ISessionService"/>.
    /// </summary>
    public void SetSessionClient(AutomationSessionClient client) => _sessionClient = client;

    /// <summary>
    /// Registers local automation tools.
    /// </summary>
    public void RegisterToolProfile() => _source.RegisterToolProfile(_toolProfileRegistry);

    /// <summary>
    /// Returns a snapshot of all local tasks, merging the in-memory cache with the task store.
    /// </summary>
    public async Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, AutomationTask>(StringComparer.Ordinal);

        foreach (var task in _allTasks.Values)
            merged[task.Id] = task;

        try
        {
            var sourceTasks = await _source.GetAllTasksAsync(ct);
            foreach (var t in sourceTasks)
                merged.TryAdd(t.Id, t);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAllTasksAsync failed for local automation tasks");
        }

        await HydrateLocalTaskRuntimeMetadataAsync(merged.Values, ct);

        return merged.Values.ToList();
    }

    private async Task HydrateLocalTaskRuntimeMetadataAsync(
        IEnumerable<AutomationTask> tasks,
        CancellationToken ct)
    {
        foreach (var task in tasks)
        {
            if (task is not LocalAutomationTask local)
                continue;

            try
            {
                local.WorkspaceMode = await _workflowLoader.GetWorkspaceModeAsync(local, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read workspace mode for automation task {TaskId}", local.Id);
                local.WorkspaceMode = AutomationWorkspaceMode.Project;
            }

            local.Worktree = null;
            if (_sessionClient == null
                || local.ThreadBinding != null
                || local.WorkspaceMode != AutomationWorkspaceMode.Worktree
                || string.IsNullOrWhiteSpace(local.ThreadId))
            {
                continue;
            }

            var thread = await _sessionClient.TryGetThreadAsync(local.ThreadId, ct);
            local.Worktree = thread?.Worktree;
        }
    }

    /// <summary>
    /// Runs one poll cycle immediately.
    /// </summary>
    public Task TriggerImmediatePollAsync(CancellationToken cancellationToken = default) =>
        PollOnceAsync(cancellationToken);

    /// <summary>
    /// Deletes a local task and removes it from the orchestrator cache.
    /// </summary>
    public async Task DeleteTaskAsync(string taskId, CancellationToken ct)
    {
        if (_sessionClient != null)
        {
            var task = (await GetAllTasksAsync(ct))
                .FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
            if (task is LocalAutomationTask local
                && local.ThreadBinding == null
                && (local.WorkspaceMode == AutomationWorkspaceMode.Worktree || local.Worktree != null))
            {
                try
                {
                    await _sessionClient.RemoveTaskWorktreeAsync(local.Id, local.ThreadId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove managed worktree for automation task {TaskId}", taskId);
                }
            }
        }

        await _source.DeleteTaskAsync(taskId, ct);
        _allTasks.TryRemove(taskId, out _);
    }

    /// <summary>
    /// Removes a task's managed worktree and branch while preserving the task itself.
    /// The next run recreates the worktree from the current workspace HEAD.
    /// </summary>
    public async Task<AutomationTask> DiscardTaskWorktreeAsync(string taskId, CancellationToken ct)
    {
        var client = _sessionClient
            ?? throw new InvalidOperationException("Session client is not set.");
        var task = (await GetAllTasksAsync(ct))
            .FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal))
            as LocalAutomationTask;
        if (task == null)
            throw new KeyNotFoundException(taskId);
        if (task.Status == AutomationTaskStatus.Running)
            throw new InvalidOperationException("Cannot discard a task worktree while the task is running.");
        if (task.ThreadBinding != null
            || (task.WorkspaceMode != AutomationWorkspaceMode.Worktree && task.Worktree == null))
            throw new InvalidOperationException("Task does not have a managed worktree to discard.");

        await client.RemoveTaskWorktreeAsync(task.Id, task.ThreadId, ct);
        task.Worktree = null;
        TrackTask(task);
        await RaiseStatusChangedAsync(task, task.Status);
        return task;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("Automations orchestrator has already been started.");

        if (_sessionClient == null)
        {
            Volatile.Write(ref _started, 0);
            throw new InvalidOperationException("Session client must be set before starting the orchestrator.");
        }

        try
        {
            RegisterToolProfile();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pollTimer = new PeriodicTimer(_config.PollingInterval);
            _pollTask = PollLoopAsync(_cts.Token);
            _logger.LogInformation(
                "Automations orchestrator started (interval: {Interval}, max concurrent: {Max})",
                _config.PollingInterval,
                _config.MaxConcurrentTasks);
            return Task.CompletedTask;
        }
        catch
        {
            Volatile.Write(ref _started, 0);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        if (_cts != null)
            await _cts.CancelAsync();

        _pollTimer?.Dispose();

        if (_pollTask != null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;

        _logger.LogInformation("Automations orchestrator stopped");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Automations poll loop running");
        try
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automations initial poll failed");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _pollTimer!.WaitForNextTickAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await PollOnceAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Automations poll tick failed");
                }
            }
        }
        finally
        {
            _logger.LogDebug("Automations poll loop exited");
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        await _pollGate.WaitAsync(ct);
        try
        {
            var sw = Stopwatch.StartNew();
            var taskIds = new List<string>();
            const int maxTaskIds = 10;

            IReadOnlyList<AutomationTask> pending;
            try
            {
                pending = await _source.GetPendingTasksAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPendingTasksAsync failed for local automation tasks");
                return;
            }

            await TryRunRetentionSweepAsync(ct);

            EnsureNextRunAtInitialized(pending);

            var pendingOnly = pending
                .Where(t => t.Status == AutomationTaskStatus.Pending)
                .ToList();

            foreach (var t in pendingOnly.Take(maxTaskIds))
                taskIds.Add(TruncateTaskIdForLog(t.Id));

            foreach (var task in pending)
            {
                if (ct.IsCancellationRequested)
                {
                    sw.Stop();
                    LogPollSummary(sw.ElapsedMilliseconds, taskIds, pendingOnly.Count);
                    return;
                }

                if (task.Status != AutomationTaskStatus.Pending)
                    continue;

                if (task.Schedule != null && task.NextRunAt is { } next && next > DateTimeOffset.UtcNow)
                {
                    _logger.LogDebug(
                        "Task {TaskId} not due yet (next: {Next})",
                        task.Id,
                        next);
                    continue;
                }

                if (task.ThreadBinding != null && !await IsBoundChannelReadyAsync(task.ThreadBinding, ct))
                {
                    _logger.LogDebug(
                        "Task {TaskId} bound thread's channel not ready yet; deferring",
                        task.Id);
                    continue;
                }

                if (_sessionClient == null)
                {
                    _logger.LogDebug(
                        "Task {TaskId} deferred because automation session client is not configured",
                        task.Id);
                    continue;
                }

                if (_state.GetRetryCount(task.Id) > 0 &&
                    !_state.IsEligibleForRetry(task.Id, _config.RetryInitialDelay, _config.RetryMaxDelay, DateTimeOffset.UtcNow))
                {
                    _logger.LogDebug("Task {TaskId} waiting for retry backoff", task.Id);
                    continue;
                }

                if (!_state.TryBeginTask(task.Id))
                {
                    _logger.LogDebug("Task {TaskId} skipped because it is already active", task.Id);
                    continue;
                }

                _ = Task.Run(() => RunDispatchAsync(task, ct), ct);
            }

            sw.Stop();
            LogPollSummary(sw.ElapsedMilliseconds, taskIds, pendingOnly.Count);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private void LogPollSummary(long elapsedMs, List<string> taskIds, int totalPendingEligible)
    {
        var sb = new StringBuilder();
        sb.Append("local pending=").Append(totalPendingEligible);
        if (taskIds.Count > 0)
        {
            sb.Append(" taskIds=[");
            sb.AppendJoin(',', taskIds);
            sb.Append(']');
            var more = totalPendingEligible - taskIds.Count;
            if (more > 0)
                sb.Append(" (+").Append(more).Append(" more)");
        }

        _logger.LogDebug(
            "Poll completed in {ElapsedMs}ms. {PollDetails}",
            elapsedMs,
            sb.ToString());
    }

    private async Task TryRunRetentionSweepAsync(CancellationToken ct)
    {
        if (_sessionClient == null
            || !_config.WorktreeRetentionEnabled
            || _config.WorktreeRetentionIdlePeriod < TimeSpan.FromDays(14))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastRetentionSweepAt < TimeSpan.FromHours(1))
            return;
        _lastRetentionSweepAt = now;

        IReadOnlyList<AutomationTask> tasks;
        try
        {
            tasks = await _source.GetAllTasksAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping automation worktree retention sweep because task loading failed.");
            return;
        }

        var byId = tasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        IReadOnlyList<ThreadWorktreeStatus> statuses;
        try
        {
            statuses = await _sessionClient.ListManagedWorktreesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping automation worktree retention sweep because worktree status loading failed.");
            return;
        }

        foreach (var status in statuses)
        {
            ct.ThrowIfCancellationRequested();
            var worktree = status.Worktree;
            if (!string.Equals(worktree.OwnerKind, "automationTask", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(worktree.OwnerId))
            {
                continue;
            }

            byId.TryGetValue(worktree.OwnerId, out var task);
            if (task?.Status == AutomationTaskStatus.Running)
                continue;
            if (status.HasUncommittedChanges || status.HasCommitsAheadOfBase)
                continue;

            var lastActivity = task?.UpdatedAt ?? worktree.CreatedAt;
            if (now - lastActivity < _config.WorktreeRetentionIdlePeriod)
                continue;

            try
            {
                await _sessionClient.RemoveTaskWorktreeAsync(worktree.OwnerId, status.ThreadId, ct);
                _logger.LogInformation(
                    "Removed idle clean automation worktree for task {TaskId} at {Path}.",
                    worktree.OwnerId,
                    worktree.Path);
                if (task is LocalAutomationTask local)
                {
                    local.Worktree = null;
                    TrackTask(local);
                    await RaiseStatusChangedAsync(local, local.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to remove idle automation worktree for task {TaskId} at {Path}.",
                    worktree.OwnerId,
                    worktree.Path);
            }
        }
    }

    /// <summary>
    /// Gate used before dispatching a bound task: returns true when the target thread's
    /// origin channel runtime is ready to accept a turn.
    /// </summary>
    internal async Task<bool> IsBoundChannelReadyAsync(AutomationThreadBinding binding, CancellationToken ct)
    {
        if (_channelRuntimeRegistry == null || _sessionClient == null)
            return true;

        var thread = await _sessionClient.TryGetThreadAsync(binding.ThreadId, ct);
        if (thread == null)
            return true;

        var originChannel = thread.OriginChannel;
        if (string.IsNullOrWhiteSpace(originChannel))
            return true;

        if (!_channelRuntimeRegistry.TryGet(originChannel, out var runtime) || runtime == null)
            return false;

        return runtime.IsReady;
    }

    private static string TruncateTaskIdForLog(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;
        return id.Length <= 64 ? id : id[..64];
    }

    private async Task RunDispatchAsync(AutomationTask task, CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            await DispatchTaskAsync(task, ct);
            _state.ClearRetries(task.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dispatch cancelled for task {TaskId}", task.Id);
            try
            {
                task.Status = AutomationTaskStatus.Failed;
                TrackTask(task);
                await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Cleanup failed for cancelled task {TaskId}", task.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatch failed for task {TaskId}", task.Id);

            var shouldRetry = false;
            if (_config.MaxRetries > 0)
            {
                var retryAttempt = _state.ScheduleRetry(task.Id, _config.MaxRetries, DateTimeOffset.UtcNow);
                if (retryAttempt > 0)
                {
                    shouldRetry = true;
                    _logger.LogInformation(
                        "Task {TaskId} scheduled for retry attempt {Attempt}/{Max}",
                        task.Id,
                        retryAttempt,
                        _config.MaxRetries);
                }
            }

            try
            {
                if (shouldRetry)
                {
                    task.Status = AutomationTaskStatus.Pending;
                    TrackTask(task);
                    await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Pending, CancellationToken.None);
                    await RaiseStatusChangedAsync(task, AutomationTaskStatus.Pending);
                }
                else
                {
                    task.Status = AutomationTaskStatus.Failed;
                    TrackTask(task);
                    await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
                    await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
                }
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "OnStatusChangedAsync failed for task {TaskId}", task.Id);
            }
        }
        finally
        {
            _state.EndTask(task.Id);
            _concurrency.Release();
        }
    }

    private async Task DispatchTaskAsync(AutomationTask task, CancellationToken ct)
    {
        var client = _sessionClient;
        if (client == null)
            throw new InvalidOperationException("Session client is not set.");

        _logger.LogInformation(
            "Dispatch starting for task {TaskId} (bound: {Bound})",
            task.Id,
            task.ThreadBinding != null);

        task.Status = AutomationTaskStatus.Running;
        TrackTask(task);
        await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Running, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.Running);
        _logger.LogInformation("Task {TaskId} status: Running", task.Id);

        string workspacePath;
        string threadId;
        string? automationTaskDirectory = null;

        if (task.ThreadBinding != null)
        {
            var targetId = task.ThreadBinding.ThreadId;
            var thread = await client.TryGetThreadAsync(targetId, ct);
            if (thread == null)
            {
                _logger.LogWarning(
                    "Task {TaskId} bound thread {ThreadId} is missing; marking as failed",
                    task.Id,
                    targetId);
                task.Status = AutomationTaskStatus.Failed;
                TrackTask(task);
                await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
                return;
            }

            threadId = targetId;
            workspacePath = thread.WorkspacePath;
            if (task is LocalAutomationTask localBound)
            {
                localBound.AgentWorkspacePath = workspacePath;
                automationTaskDirectory = localBound.TaskDirectory;
            }
        }
        else if (task is LocalAutomationTask localTask)
        {
            var workspaceMode = await _workflowLoader.GetWorkspaceModeAsync(localTask, ct);
            localTask.WorkspaceMode = workspaceMode;
            automationTaskDirectory = localTask.TaskDirectory;

            ThreadConfiguration threadConfig;
            var boundProfileId = string.IsNullOrWhiteSpace(localTask.AgentProfileId)
                ? null
                : localTask.AgentProfileId.Trim();
            if (boundProfileId != null)
            {
                try
                {
                    // Resolve the bound Agent Profile fresh each dispatch so profile edits take effect on
                    // the next run. The profile governs capabilities (tools/MCP/skills/model/instructions);
                    // ApplyAutomationOperationalOverrides forces the automation-owned fields on top.
                    var workspaceCraftPath = Path.Combine(client.ProjectWorkspacePath, ".craft");
                    var profileStore = new AgentProfileStore(workspaceCraftPath);
                    var profile = profileStore.Read(boundProfileId);
                    threadConfig = profile.ProviderPreference == null
                        ? profileStore.ResolveProfileConfiguration(boundProfileId)
                        : profileStore.ResolveProfileConfiguration(
                            boundProfileId,
                            _appConfigMonitor?.Current
                            ?? throw new InvalidOperationException("Provider configuration is unavailable for the fixed Agent Profile model preset."));
                    threadConfig.AgentProfileId = boundProfileId;
                }
                catch (Exception ex)
                {
                    // A bound profile that no longer resolves (deleted / invalid) fails the run rather than
                    // silently downgrading to the default agent — the task explicitly requested that capability set.
                    _logger.LogWarning(
                        ex,
                        "Agent profile '{ProfileId}' for task {TaskId} could not be resolved; failing the run",
                        boundProfileId,
                        task.Id);
                    task.AgentSummary = $"Agent profile '{boundProfileId}' is missing or invalid.";
                    task.Status = AutomationTaskStatus.Failed;
                    TrackTask(task);
                    await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
                    await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
                    return;
                }
            }
            else
            {
                threadConfig = new ThreadConfiguration();
            }

            ApplyAutomationOperationalOverrides(threadConfig, automationTaskDirectory, task);

            threadId = await client.CreateOrResumeThreadAsync(
                AutomationsChannelName,
                $"task-{task.Id}",
                threadConfig,
                ct,
                displayName: task.Title);

            if (workspaceMode == AutomationWorkspaceMode.Worktree)
            {
                try
                {
                    var worktree = await client.EnsureTaskWorktreeAsync(threadId, task.Id, ct);
                    localTask.Worktree = worktree;
                    workspacePath = worktree.Path;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Worktree provisioning failed for task {TaskId}; falling back to legacy sandbox",
                        task.Id);
                    workspacePath = Path.Combine(localTask.TaskDirectory, "workspace");
                    Directory.CreateDirectory(workspacePath);
                    localTask.Worktree = null;
                    await client.ConfigureTaskExecutionWorkspaceAsync(threadId, workspacePath, ct);
                }
            }
            else
            {
                workspacePath = client.ProjectWorkspacePath;
                Directory.CreateDirectory(workspacePath);
                localTask.Worktree = null;
                await client.ConfigureTaskExecutionWorkspaceAsync(threadId, executionWorkspacePath: null, ct);
            }

            localTask.AgentWorkspacePath = workspacePath;
        }
        else
        {
            throw new InvalidOperationException("Automations only supports local tasks.");
        }

        _logger.LogInformation(
            "Thread ready for task {TaskId} (threadId: {ThreadId})",
            task.Id,
            threadId);

        task.ThreadId = threadId;
        TrackTask(task);
        await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Running, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.Running);
        _logger.LogInformation("Task {TaskId} thread bound while running", task.Id);

        var workflow = await _source.GetWorkflowAsync(task, ct);
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException("Workflow has no steps.");

        string? summary = null;
        var round = 0;
        var stopWorkflow = false;
        var turnFailed = false;
        var turnCancelled = false;

        var triggerInfo = new TurnTriggerInfo
        {
            Kind = "automation",
            RefId = task.Id,
            Label = task.Title
        };

        try
        {
            while (round < workflow.MaxRounds && !ct.IsCancellationRequested && !stopWorkflow)
            {
                round++;
                foreach (var step in workflow.Steps)
                {
                    await foreach (var evt in client.SubmitTurnAsync(threadId, step.Prompt, ct, triggerInfo))
                    {
                        if (evt.EventType == SessionEventType.TurnCompleted && evt.TurnPayload is { } turn)
                            summary = ExtractSummaryFromTurn(turn);

                        if (evt.EventType == SessionEventType.TurnFailed)
                        {
                            turnFailed = true;
                            stopWorkflow = true;
                            break;
                        }

                        if (evt.EventType == SessionEventType.TurnCancelled)
                        {
                            turnCancelled = true;
                            stopWorkflow = true;
                            break;
                        }
                    }

                    if (await _source.ShouldStopWorkflowAfterTurnAsync(task, ct))
                    {
                        stopWorkflow = true;
                        break;
                    }

                    if (stopWorkflow || ct.IsCancellationRequested)
                        break;
                }

                _logger.LogInformation(
                    "Workflow round {Round} completed for task {TaskId} (maxRounds: {MaxRounds})",
                    round,
                    task.Id,
                    workflow.MaxRounds);
            }
        }
        finally
        {
        }

        if (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: cancellation requested (threadId: {ThreadId})",
                task.Id,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            try
            {
                await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Cleanup failed for cancelled workflow task {TaskId}", task.Id);
            }

            return;
        }

        if (turnFailed)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: turn failed (threadId: {ThreadId})",
                task.Id,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            return;
        }

        if (turnCancelled && task.Status != AutomationTaskStatus.Completed)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: turn cancelled (threadId: {ThreadId})",
                task.Id,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            return;
        }

        await _source.OnAgentCompletedAsync(task, summary ?? string.Empty, ct);
        _logger.LogInformation(
            "Task {TaskId} agent completed (summaryLength: {SummaryLength})",
            task.Id,
            summary?.Length ?? 0);

        if (task.Schedule != null)
        {
            RearmSchedule(task);
            task.Status = AutomationTaskStatus.Pending;
            TrackTask(task);
            await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Pending, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Pending);
            _logger.LogInformation(
                "Task {TaskId} rearmed for next run at {NextRunAt}",
                task.Id,
                task.NextRunAt);
        }
        else
        {
            task.Status = AutomationTaskStatus.Completed;
            TrackTask(task);
            await _source.OnStatusChangedAsync(task, AutomationTaskStatus.Completed, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Completed);
            _logger.LogInformation("Task {TaskId} completed", task.Id);
        }
    }

    /// <summary>
    /// Applies the automation-owned operational fields on top of a (possibly profile-resolved) thread
    /// configuration. These always win over an Agent Profile: unattended runs must auto-approve, the task
    /// directory must be set so <c>CompleteLocalTask</c> is injected, and the completion tool must remain
    /// reachable even under a restrictive profile tool policy.
    /// </summary>
    private void ApplyAutomationOperationalOverrides(
        ThreadConfiguration config,
        string? automationTaskDirectory,
        AutomationTask task)
    {
        // The local-task tool profile injects CompleteLocalTask; it is operational infrastructure, not a
        // capability the user chose, so it is applied regardless of any bound Agent Profile.
        config.ToolProfile = _source.ToolProfileName;
        config.UseToolProfileOnly = false;
        // Automation always executes, never plans; a profile compiled in plan mode must not no-op the run.
        config.Mode = "agent";
        config.ApprovalPolicy = ApprovalPolicy.AutoApprove;
        config.AutomationTaskDirectory = automationTaskDirectory;
        config.RequireApprovalOutsideWorkspace = ResolveRequireApprovalOutsideWorkspace(task);
        EnsureCompletionToolAllowed(config);
    }

    /// <summary>
    /// Ensures a bound profile's allow-list cannot hide <c>CompleteLocalTask</c>, which would leave the run
    /// unable to finish. Deny rules are left untouched: an explicit deny is treated as a deliberate choice.
    /// </summary>
    private static void EnsureCompletionToolAllowed(ThreadConfiguration config)
    {
        const string completionTool = "CompleteLocalTask";

        if (config.ToolPolicy?.Allow is { Length: > 0 } allow
            && !allow.Contains(completionTool, StringComparer.Ordinal))
        {
            config.ToolPolicy.Allow = [.. allow, completionTool];
        }

        if (config.ToolAllowList is { } legacyAllow
            && legacyAllow.Any(v => !string.IsNullOrWhiteSpace(v))
            && !legacyAllow.Contains(completionTool, StringComparer.Ordinal))
        {
            config.ToolAllowList = [.. legacyAllow, completionTool];
        }
    }

    /// <summary>
    /// Local automation tool policy: when <c>false</c>, file/shell operations outside the thread workspace are rejected
    /// without prompting; when <c>true</c>, outside-workspace operations are auto-approved.
    /// </summary>
    private static bool? ResolveRequireApprovalOutsideWorkspace(AutomationTask task)
    {
        if (task is not LocalAutomationTask local)
            return null;

        var p = local.ApprovalPolicy?.Trim();
        if (string.IsNullOrEmpty(p))
            return false;

        if (string.Equals(p, "fullAuto", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(p, "workspaceScope", StringComparison.OrdinalIgnoreCase))
            return false;

        return false;
    }

    private static string ExtractSummaryFromTurn(SessionTurn turn)
    {
        for (var i = turn.Items.Count - 1; i >= 0; i--)
        {
            var item = turn.Items[i];
            if (item.Type != ItemType.AgentMessage)
                continue;
            var text = item.AsAgentMessage?.Text;
            if (!string.IsNullOrWhiteSpace(text))
                return text!;
        }

        return string.Empty;
    }

    private void TrackTask(AutomationTask task) =>
        _allTasks[task.Id] = task;

    private async Task RaiseStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus)
    {
        var handler = OnTaskStatusChanged;
        if (handler != null)
        {
            try
            {
                await handler(task, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnTaskStatusChanged handler failed for task {TaskId}", task.Id);
            }
        }
    }

    /// <summary>
    /// Ensures each scheduled task has a <see cref="AutomationTask.NextRunAt"/> computed.
    /// </summary>
    internal static void EnsureNextRunAtInitialized(IEnumerable<AutomationTask> tasks)
    {
        var now = DateTimeOffset.UtcNow;
        var nowMs = now.ToUnixTimeMilliseconds();
        foreach (var task in tasks)
        {
            if (task.Schedule == null)
            {
                task.NextRunAt = null;
                continue;
            }
            if (task.NextRunAt != null)
                continue;

            if (string.Equals(task.Schedule.Kind, "every", StringComparison.OrdinalIgnoreCase))
            {
                if (task.Schedule.InitialDelayMs is { } initialDelay && initialDelay > 0)
                    task.NextRunAt = now.AddMilliseconds(initialDelay);
                else
                    task.NextRunAt = task.CreatedAt ?? now;
                continue;
            }

            var ms = CronScheduleHelpers.ComputeNextRunMs(task.Schedule, lastRunAtMs: null, nowUtcMs: nowMs);
            task.NextRunAt = ms.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value)
                : null;
        }
    }

    /// <summary>
    /// Recomputes <see cref="AutomationTask.NextRunAt"/> after a run completed.
    /// </summary>
    internal static void RearmSchedule(AutomationTask task)
    {
        if (task.Schedule == null)
        {
            task.NextRunAt = null;
            return;
        }
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ms = CronScheduleHelpers.ComputeNextRunMs(task.Schedule, lastRunAtMs: nowMs, nowUtcMs: nowMs);
        task.NextRunAt = ms.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value)
            : null;
    }
}
