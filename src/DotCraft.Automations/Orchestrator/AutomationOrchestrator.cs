using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Protocol;
using DotCraft.Cron;
using DotCraft.Protocol;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<AutomationOrchestrator> _logger;
    private readonly OrchestratorState _state = new();
    private readonly ConcurrentDictionary<string, AutomationTask> _allTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    private AutomationSessionClient? _sessionClient;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private PeriodicTimer? _pollTimer;
    private int _stopped;

    public AutomationOrchestrator(
        AutomationsConfig config,
        LocalWorkflowLoader workflowLoader,
        IToolProfileRegistry toolProfileRegistry,
        LocalAutomationSource source,
        ILogger<AutomationOrchestrator> logger,
        IChannelRuntimeRegistry? channelRuntimeRegistry = null)
    {
        _config = config;
        _workflowLoader = workflowLoader;
        _toolProfileRegistry = toolProfileRegistry;
        _source = source;
        _channelRuntimeRegistry = channelRuntimeRegistry;
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

        return merged.Values.ToList();
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
        await _source.DeleteTaskAsync(taskId, ct);
        _allTasks.TryRemove(taskId, out _);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_sessionClient == null)
            throw new InvalidOperationException("Session client must be set before starting the orchestrator.");

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

        _logger.LogInformation(
            "Poll completed in {ElapsedMs}ms. {PollDetails}",
            elapsedMs,
            sb.ToString());
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
            if (workspaceMode == AutomationWorkspaceMode.Isolated)
            {
                workspacePath = Path.Combine(localTask.TaskDirectory, "workspace");
                Directory.CreateDirectory(workspacePath);
            }
            else
            {
                workspacePath = client.ProjectWorkspacePath;
                Directory.CreateDirectory(workspacePath);
            }

            localTask.AgentWorkspacePath = workspacePath;
            automationTaskDirectory = localTask.TaskDirectory;

            var threadConfig = new ThreadConfiguration
            {
                WorkspaceOverride = workspacePath,
                ToolProfile = _source.ToolProfileName,
                ApprovalPolicy = ApprovalPolicy.AutoApprove,
                AutomationTaskDirectory = automationTaskDirectory,
                RequireApprovalOutsideWorkspace = ResolveRequireApprovalOutsideWorkspace(task)
            };

            threadId = await client.CreateOrResumeThreadAsync(
                AutomationsChannelName,
                $"task-{task.Id}",
                threadConfig,
                ct,
                displayName: task.Title);
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
        if (string.Equals(p, "autoApprove", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(p, "workspaceScope", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(p, "default", StringComparison.OrdinalIgnoreCase))
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
