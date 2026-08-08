using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.Realtime;
using Oratorio.Server.Services;

namespace Oratorio.Server.DotCraft;

public sealed class AppServerRunWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IDotCraftAppServerProcessManager processManager,
    IDotCraftAppServerClientFactory clientFactory,
    IDotCraftWorkspaceResolver workspaceResolver,
    IWorktreeManager worktreeManager,
    IOptionsMonitor<DotCraftOptions> options,
    IOptionsMonitor<OratorioAutomationOptions> automationOptions,
    DrawerStateService drawerState,
    BoardEventHub boardEvents,
    IAppServerRunCoordinator runCoordinator,
    OratorioDynamicToolCatalog dynamicToolCatalog,
    ILogger<AppServerRunWorker> logger) : BackgroundService
{
    private static readonly RunStatus[] ActiveWorkerStatuses = [RunStatus.Dispatching, RunStatus.Running];
    private static readonly TimeSpan TimeoutInterruptWait = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly SemaphoreSlim _schedulerLock = new(1, 1);
    private readonly Dictionary<string, Task> _activeTasks = [];
    private readonly object _activeTasksGate = new();
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():n}";

    private enum TimeoutTerminalResult
    {
        Cancelled,
        Failed,
        Completed,
        Disconnected,
        WaitTimedOut,
        InterruptFailed,
        MissingTurn
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRunsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!await _schedulerLock.WaitAsync(0, stoppingToken))
            {
                continue;
            }

            try
            {
                RemoveCompletedTasks();
                await ReconcileStalledRunsAsync(stoppingToken);
                await ScheduleRunnableRunsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AppServer run scheduler tick failed.");
            }
            finally
            {
                _schedulerLock.Release();
            }
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken ct)
    {
        string[] activeRunIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            activeRunIds = await db.Runs.AsNoTracking()
            .Where(x => x.RunnerKind == "appServer" && ActiveWorkerStatuses.Contains(x.Status))
                .Select(x => x.RunId)
                .ToArrayAsync(ct);
        }

        if (activeRunIds.Length == 0)
        {
            return;
        }

        foreach (var runId in activeRunIds)
        {
            await FailRunAsync(
                runId,
                RunStatus.Failed,
                "appServerRunnerInterrupted",
                "The DotCraft AppServer runner was interrupted before it completed.",
                allowRetry: true,
                ct);
        }
    }

    private async Task ReconcileStalledRunsAsync(CancellationToken ct)
    {
        var value = options.CurrentValue;
        var staleBefore = clock.UtcNow - value.StallTimeout;
        string[] stalledRunIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            stalledRunIds = await db.Runs.AsNoTracking()
                .Where(x =>
                    x.RunnerKind == "appServer" &&
                    ActiveWorkerStatuses.Contains(x.Status) &&
                    x.LastHeartbeatAt != null &&
                    x.LastHeartbeatAt < staleBefore)
                .Select(x => x.RunId)
                .ToArrayAsync(ct);
        }

        foreach (var runId in stalledRunIds)
        {
            await FailRunAsync(
                runId,
                RunStatus.TimedOut,
                "appServerStalled",
                "DotCraft AppServer run heartbeat stalled.",
                allowRetry: true,
                ct);
        }
    }

    private async Task ScheduleRunnableRunsAsync(CancellationToken ct)
    {
        var value = options.CurrentValue;
        var now = clock.UtcNow;
        var runIdsToStart = new List<string>();
        var supersededItems = new List<OratorioItem>();

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var stateChanged = false;
            var activeRuns = await db.Runs.AsNoTracking()
                .Include(x => x.Item)
                .Where(x => x.RunnerKind == "appServer" && ActiveWorkerStatuses.Contains(x.Status))
                .ToListAsync(ct);
            var activeTotal = activeRuns.Count;
            var activeByRepository = activeRuns
                .GroupBy(x => RepositoryKey(x.Item?.Repository))
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            var activeBySource = activeRuns
                .GroupBy(x => SourceKey(x.Item?.Source))
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

            var candidates = await db.Runs
                .Include(x => x.Item)
                .ThenInclude(x => x!.TimelineEvents)
                .Include(x => x.Round)
                .Where(x =>
                    x.RunnerKind == "appServer" &&
                    x.Status == RunStatus.Queued &&
                    (x.NextRetryAt == null || x.NextRetryAt <= now))
                .OrderBy(x => x.StartedAt)
                .Take(value.EffectiveGlobalMaxActiveRuns * 4)
                .ToListAsync(ct);

            foreach (var run in candidates)
            {
                if (await SupersedeObsoleteReviewRetryAsync(scope, run, now, ct))
                {
                    stateChanged = true;
                    supersededItems.Add(run.Item!);
                    continue;
                }

                if (activeTotal >= value.EffectiveGlobalMaxActiveRuns || run.Item is null)
                {
                    break;
                }

                var repositoryKey = RepositoryKey(run.Item.Repository);
                var sourceKey = SourceKey(run.Item.Source);
                if (activeByRepository.GetValueOrDefault(repositoryKey) >= value.EffectiveMaxActiveRunsPerRepository ||
                    activeBySource.GetValueOrDefault(sourceKey) >= value.EffectiveMaxActiveRunsPerSource)
                {
                    continue;
                }

                run.Status = RunStatus.Dispatching;
                run.LeaseOwner = _leaseOwner;
                run.LeaseAcquiredAt = now;
                run.ProgressPercent = Math.Max(run.ProgressPercent, 5);
                run.StatusMessage = "AppServer runner lease acquired.";
                run.LastHeartbeatAt = now;
                run.Item.State = ItemState.Dispatching;
                run.Item.CheckState = CheckState.Pending;
                run.Item.UpdatedAt = now;
                runIdsToStart.Add(run.RunId);

                activeTotal++;
                activeByRepository[repositoryKey] = activeByRepository.GetValueOrDefault(repositoryKey) + 1;
                activeBySource[sourceKey] = activeBySource.GetValueOrDefault(sourceKey) + 1;
            }

            if (stateChanged || runIdsToStart.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        foreach (var item in supersededItems)
        {
            boardEvents.PublishTaskUpdated(item, now);
        }

        foreach (var runId in runIdsToStart)
        {
            StartRunTask(runId, ct);
        }
    }

    private async Task<bool> SupersedeObsoleteReviewRetryAsync(
        IServiceScope scope,
        OratorioRun run,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (run.RetryCount <= 0 ||
            !RequiresReviewDraft(run) ||
            string.IsNullOrWhiteSpace(run.TargetHeadSha) ||
            string.IsNullOrWhiteSpace(run.Item?.HeadSha) ||
            SameHead(run.TargetHeadSha, run.Item.HeadSha))
        {
            return false;
        }

        var message = $"Review retry target changed from {run.TargetHeadSha} to {run.Item!.HeadSha}; the queued retry was superseded.";
        run.Status = RunStatus.Cancelled;
        run.CompletedAt = now;
        run.Summary = message;
        run.ErrorCode = "reviewRetrySuperseded";
        run.ErrorMessage = message;
        run.ProgressPercent = 100;
        run.StatusMessage = message;
        run.LastHeartbeatAt = now;
        run.LeaseOwner = null;
        run.LeaseAcquiredAt = null;

        run.Round!.Status = RoundStatus.Superseded;
        run.Round.Summary = message;
        run.Round.CompletedAt = now;

        run.Item.State = ItemState.Discovered;
        if (run.Item.CurrentRunId == run.RunId)
        {
            run.Item.CurrentRunId = null;
        }
        run.Item.LatestSummary = message;
        run.Item.CheckState = CheckState.Attention;
        run.Item.UpdatedAt = now;

        AddTimeline(run, TimelineEventKind.RunCancelled, ActorKind.System, "oratorio/retry", "Review retry superseded", message, now);
        AddTimeline(run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check superseded", "The queued retry targeted an older review head.", now);
        var writes = scope.ServiceProvider.GetRequiredService<GitHubWriteService>();
        await writes.RecordReviewGateRunCancelledAsync(run, ct);
        return true;
    }

    private void StartRunTask(string runId, CancellationToken ct)
    {
        lock (_activeTasksGate)
        {
            if (_activeTasks.ContainsKey(runId))
            {
                return;
            }

            _activeTasks[runId] = Task.Run(async () =>
            {
                try
                {
                    await RunAppServerTurnAsync(runId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled AppServer run task failure for {RunId}.", runId);
                    await FailRunAsync(runId, RunStatus.Failed, "appServerFailed", ex.Message, allowRetry: true, CancellationToken.None);
                }
            }, ct);
        }
    }

    private void RemoveCompletedTasks()
    {
        lock (_activeTasksGate)
        {
            foreach (var runId in _activeTasks.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToArray())
            {
                _activeTasks.Remove(runId);
            }
        }
    }

    private async Task RunAppServerTurnAsync(string runId, CancellationToken ct)
    {
        var value = options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(value.RunTimeout);
        var registered = false;
        IDotCraftAppServerClient? client = null;
        string? threadId = null;
        string? turnId = null;

        try
        {
            var baseWorkspacePath = await ResolveBaseWorkspacePathAsync(runId, timeout.Token);
            var executionWorkspacePath = await PrepareExecutionWorkspaceAsync(runId, baseWorkspacePath, timeout.Token);
            var endpoint = await processManager.EnsureAvailableAsync(baseWorkspacePath, timeout.Token);
            var appServerIdentity = BuildLocalAppServerIdentity(baseWorkspacePath);
            await MarkDispatchingAsync(runId, endpoint.Url, appServerIdentity, timeout.Token);

            client = await clientFactory.ConnectAsync(endpoint.Url, timeout.Token, endpoint.Token);
            if (!client.SupportsRuntimeAdditionalContext)
            {
                await FailRunAsync(
                    runId,
                    RunStatus.Failed,
                    "runtimeAdditionalContextUnsupported",
                    "The DotCraft AppServer does not support runtime additional context required by Oratorio.",
                    allowRetry: false,
                    CancellationToken.None);
                return;
            }

            var dynamicTools = await BuildDynamicToolSetAsync(runId, timeout.Token);
            var requiredDynamicTools = dynamicTools.QualifiedIds;
            var reusableThread = await FindCompatibleThreadAsync(
                runId,
                baseWorkspacePath,
                executionWorkspacePath,
                requiredDynamicTools,
                timeout.Token);
            var threadCreationReason = "No compatible compact AppServer thread was found.";
            if (reusableThread is not null && requiredDynamicTools.Count > 0 && !client.SupportsDynamicToolRebind)
            {
                reusableThread = null;
                threadCreationReason = "A compatible compact AppServer thread was found, but the AppServer does not support dynamic tool rebind.";
            }
            var prompt = await BuildPromptAsync(runId, executionWorkspacePath, requiredDynamicTools, reusableThread is not null, timeout.Token);

            string? boundThreadId = null;
            client.SetDynamicToolHandler(async (call, handlerCt) =>
            {
                using var scope = scopeFactory.CreateScope();
                return await dynamicToolCatalog.InvokeAsync(
                    call,
                    new OratorioToolInvocationContext(
                        scope.ServiceProvider,
                        call,
                        OratorioToolSurface.Run,
                        RunId: runId,
                        ExpectedThreadId: boundThreadId),
                    dynamicTools.AllowedLocalNames,
                    handlerCt);
            });
            if (reusableThread is null)
            {
                threadId = await client.StartThreadAsync(new AppServerThreadStartRequest(
                    DisplayName: prompt.DisplayName,
                    BaseWorkspacePath: baseWorkspacePath,
                    ExecutionWorkspacePath: executionWorkspacePath,
                    ApprovalPolicy: string.IsNullOrWhiteSpace(value.ApprovalPolicy) ? "interrupt" : value.ApprovalPolicy,
                    AgentInstructions: "You are connected through Oratorio. Follow the prompt exactly and use Oratorio dynamic tools when instructed.",
                    DynamicTools: dynamicTools.Declarations,
                    RuntimeAdditionalContext: prompt.RuntimeAdditionalContext), timeout.Token);
                boundThreadId = threadId;
                await MarkThreadCreatedAsync(runId, threadId, prompt.ContextJson, endpoint.Url, appServerIdentity, threadCreationReason, timeout.Token);
            }
            else
            {
                threadId = reusableThread.ThreadId;
                boundThreadId = threadId;
                try
                {
                    await client.ResumeThreadAsync(threadId, dynamicTools.Declarations, prompt.RuntimeAdditionalContext, timeout.Token);
                    await MarkThreadReusedAsync(runId, reusableThread, prompt.ContextJson, endpoint.Url, appServerIdentity, timeout.Token);
                }
                catch (Exception ex) when (IsThreadResumeRejection(ex))
                {
                    logger.LogInformation(ex, "Compatible AppServer thread {ThreadId} could not be resumed for run {RunId}; creating a fresh thread in the same attempt.", threadId, runId);
                    threadCreationReason = $"Compatible thread {threadId} could not be resumed: {ex.Message}";
                    reusableThread = null;
                    boundThreadId = null;
                    prompt = await BuildPromptAsync(runId, executionWorkspacePath, requiredDynamicTools, incremental: false, timeout.Token);
                    threadId = await client.StartThreadAsync(new AppServerThreadStartRequest(
                        DisplayName: prompt.DisplayName,
                        BaseWorkspacePath: baseWorkspacePath,
                        ExecutionWorkspacePath: executionWorkspacePath,
                        ApprovalPolicy: string.IsNullOrWhiteSpace(value.ApprovalPolicy) ? "interrupt" : value.ApprovalPolicy,
                        AgentInstructions: "You are connected through Oratorio. Follow the prompt exactly and use Oratorio dynamic tools when instructed.",
                        DynamicTools: dynamicTools.Declarations,
                        RuntimeAdditionalContext: prompt.RuntimeAdditionalContext), timeout.Token);
                    boundThreadId = threadId;
                    await MarkThreadCreatedAsync(runId, threadId, prompt.ContextJson, endpoint.Url, appServerIdentity, threadCreationReason, timeout.Token);
                }
            }

            boundThreadId = threadId;

            await client.SubscribeThreadAsync(threadId, timeout.Token);
            runCoordinator.RegisterRun(runId, client, threadId, null);
            registered = true;
            turnId = await client.StartTurnAsync(threadId, prompt.Prompt, timeout.Token);
            runCoordinator.UpdateRunStatus(runId, turnId, "running");
            await MarkRunningAsync(runId, threadId, turnId, timeout.Token);

            await ConsumeNotificationsAsync(runId, client, threadId, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await HandleAppServerTimeoutAsync(runId, client, threadId, turnId, ct);
        }
        catch (WorktreeException ex)
        {
            await FailRunAsync(runId, RunStatus.Failed, ex.Code, ex.Message, allowRetry: IsTransientPreparationError(ex.Code), CancellationToken.None);
        }
        catch (DotCraftWorkspaceResolutionException ex)
        {
            await FailRunAsync(runId, RunStatus.Failed, ex.Code, ex.Message, allowRetry: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await FailRunAsync(runId, RunStatus.Failed, "appServerFailed", ex.Message, allowRetry: true, CancellationToken.None);
        }
        finally
        {
            if (registered)
            {
                runCoordinator.UnregisterRun(runId);
            }

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            drawerState.ScheduleEviction(runId, TimeSpan.FromMinutes(5));
        }
    }

    private async Task HandleAppServerTimeoutAsync(
        string runId,
        IDotCraftAppServerClient? client,
        string? threadId,
        string? turnId,
        CancellationToken ct)
    {
        if (client is null || string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
        {
            await FailRunAsync(
                runId,
                RunStatus.TimedOut,
                "appServerTimedOut",
                BuildTimeoutMessage(TimeoutTerminalResult.MissingTurn),
                allowRetry: true,
                CancellationToken.None);
            return;
        }

        const string timeoutMessage = "DotCraft AppServer run timed out.";
        runCoordinator.UpdateRunStatus(runId, turnId, "timedOut");
        PublishRunStatus(runId, "timedOut", turnId, "appServerTimedOut", timeoutMessage, 95, "DotCraft AppServer run timed out; interrupting DotCraft turn.");
        await UpdateRunHeartbeatAsync(runId, 95, "DotCraft AppServer run timed out; interrupting DotCraft turn.", turnId, CancellationToken.None);

        var terminalResult = TimeoutTerminalResult.WaitTimedOut;
        Exception? interruptError = null;
        try
        {
            using var interruptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            interruptTimeout.CancelAfter(TimeoutInterruptWait);
            await client.InterruptTurnAsync(threadId, turnId, interruptTimeout.Token);
            terminalResult = await WaitForTimeoutTerminalAsync(runId, client, interruptTimeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            terminalResult = TimeoutTerminalResult.WaitTimedOut;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Timed-out AppServer run {RunId} could not interrupt DotCraft turn {TurnId}.", runId, turnId);
            interruptError = ex;
            terminalResult = TimeoutTerminalResult.InterruptFailed;
        }

        await FailRunAsync(
            runId,
            RunStatus.TimedOut,
            "appServerTimedOut",
            BuildTimeoutMessage(terminalResult, interruptError),
            allowRetry: true,
            CancellationToken.None);
    }

    private async Task<TimeoutTerminalResult> WaitForTimeoutTerminalAsync(
        string runId,
        IDotCraftAppServerClient client,
        CancellationToken ct)
    {
        await foreach (var runEvent in client.ReadEventsAsync(ct))
        {
            if (runEvent.Type == "turn/completed")
            {
                runCoordinator.UpdateRunStatus(runId, null, "timedOut");
                PublishRunStatus(runId, "timedOut", null, "appServerTimedOut", "DotCraft turn completed after Oratorio timeout.", 100, "DotCraft turn completed after Oratorio timeout.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn completed after Oratorio timeout.", null, CancellationToken.None);
                return TimeoutTerminalResult.Completed;
            }

            if (runEvent is DotCraftRunEvent<TurnNotification> { Type: "turn/failed" } failed)
            {
                runCoordinator.UpdateRunStatus(runId, null, "timedOut");
                PublishRunStatus(runId, "timedOut", null, "appServerTimedOut", TurnError(failed.Params), 100, "DotCraft turn failed after Oratorio timeout.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn failed after Oratorio timeout.", null, CancellationToken.None);
                return TimeoutTerminalResult.Failed;
            }

            if (runEvent.Type == "turn/cancelled")
            {
                runCoordinator.UpdateRunStatus(runId, null, "timedOut");
                PublishRunStatus(runId, "timedOut", null, "appServerTimedOut", "DotCraft turn was cancelled after Oratorio timeout.", 100, "DotCraft turn acknowledged timeout interrupt.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn acknowledged timeout interrupt.", null, CancellationToken.None);
                return TimeoutTerminalResult.Cancelled;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/started" } started)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemStartedType, started.Params, streaming: true);
                await UpdateRunHeartbeatAsync(runId, 95, "DotCraft item started after Oratorio timeout.", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemDeltaNotification> deltaEvent)
            {
                var delta = deltaEvent.Params.Delta;
                if (!string.IsNullOrEmpty(delta))
                {
                    PublishItemDelta(runId, runEvent.Type, deltaEvent.Params);
                }

                await UpdateRunHeartbeatAsync(runId, 95, "DotCraft agent produced output after Oratorio timeout.", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/completed" } completed)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemCompletedType, completed.Params, streaming: false);
                await UpdateRunHeartbeatAsync(runId, 96, "DotCraft item completed after Oratorio timeout.", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<PlanUpdatedNotification> { Type: "plan/updated" } plan)
            {
                PublishPlan(runId, plan.Params);
            }
        }

        return TimeoutTerminalResult.Disconnected;
    }

    private static string BuildTimeoutMessage(TimeoutTerminalResult result, Exception? interruptError = null) =>
        result switch
        {
            TimeoutTerminalResult.Cancelled => "DotCraft AppServer run timed out. DotCraft turn was interrupted and cancelled.",
            TimeoutTerminalResult.Failed => "DotCraft AppServer run timed out. DotCraft turn failed after the timeout interrupt.",
            TimeoutTerminalResult.Completed => "DotCraft AppServer run timed out. DotCraft turn completed after the timeout and the late result was discarded.",
            TimeoutTerminalResult.Disconnected => "DotCraft AppServer run timed out. DotCraft turn interrupt was requested, but the AppServer disconnected before a terminal notification.",
            TimeoutTerminalResult.WaitTimedOut => "DotCraft AppServer run timed out. DotCraft turn interrupt was requested, but no terminal notification arrived within 30 seconds.",
            TimeoutTerminalResult.InterruptFailed => $"DotCraft AppServer run timed out. DotCraft turn interrupt failed: {interruptError?.Message ?? "unknown error"}.",
            TimeoutTerminalResult.MissingTurn => "DotCraft AppServer run timed out before a DotCraft turn could be interrupted.",
            _ => "DotCraft AppServer run timed out."
        };

    private async Task<string> PrepareExecutionWorkspaceAsync(string runId, string baseWorkspacePath, CancellationToken ct)
    {
        if (!options.CurrentValue.ManagedWorktreesEnabled)
        {
            await MarkWorktreeNotRequiredAsync(runId, baseWorkspacePath, ct);
            return baseWorkspacePath;
        }

        WorktreePrepareRequest request;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var run = await LoadRunAsync(db, runId, ct);
            var now = clock.UtcNow;
            run.BaseWorkspacePath = baseWorkspacePath;
            run.WorktreeStatus = WorktreeStatus.Preparing;
            run.WorktreeErrorCode = null;
            run.WorktreeErrorMessage = null;
            run.ProgressPercent = Math.Max(run.ProgressPercent, 8);
            run.StatusMessage = "Preparing managed Git worktree.";
            run.LastHeartbeatAt = now;
            run.Item!.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            var item = run.Item;
            string? stackOntoBranch = null;
            string? stackOntoSha = null;
            if (run.Purpose == RunPurpose.Implementation && run.DispatchTrigger == RunDispatchTrigger.AutoFollowUp)
            {
                var generatedPr = await db.Items.AsNoTracking()
                    .Where(x =>
                        x.ParentItemId == item.ItemId &&
                        x.Kind == ItemKind.PullRequest &&
                        (x.Source == "github" || x.Source == "gitlab") &&
                        x.SourceState == SourceState.Open)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (generatedPr is not null)
                {
                    stackOntoBranch = generatedPr.Branch;
                    stackOntoSha = generatedPr.HeadSha;
                }
            }

            request = new WorktreePrepareRequest(
                run.RunId,
                item.ItemId,
                item.Source,
                item.ExternalId,
                item.Repository,
                item.Branch,
                item.HeadSha,
                baseWorkspacePath,
                stackOntoBranch,
                stackOntoSha,
                await ResolveWorktreeFetchRefAsync(db, run, ct));
        }

        try
        {
            var result = await worktreeManager.PrepareAsync(request, ct);
            await MarkWorktreeReadyAsync(runId, result, ct);
            return result.WorktreePath;
        }
        catch (WorktreeException ex)
        {
            await MarkWorktreeFailedAsync(runId, baseWorkspacePath, ex.Code, ex.Message, ct);
            throw;
        }
    }

    private async Task<OratorioDynamicToolSet> BuildDynamicToolSetAsync(string runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.AsNoTracking().Include(x => x.Item).FirstAsync(x => x.RunId == runId, ct);
        var item = run.Item
            ?? throw new InvalidOperationException($"Oratorio run '{runId}' does not have an item.");
        return dynamicToolCatalog.CreateRunToolSet(run.Purpose, item.Kind, item.Source);
    }

    private async Task<string> ResolveBaseWorkspacePathAsync(string runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var repository = await db.Runs.AsNoTracking()
            .Where(x => x.RunId == runId)
            .Select(x => x.Item!.Repository)
            .FirstOrDefaultAsync(ct);
        return workspaceResolver.ResolveWorkspacePath(repository);
    }

    private async Task<(string DisplayName, string ContextJson, string Prompt, IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry> RuntimeAdditionalContext)> BuildPromptAsync(
        string runId,
        string workspacePath,
        IReadOnlyList<string> requiredDynamicTools,
        bool incremental,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var builder = scope.ServiceProvider.GetRequiredService<AppServerPromptBuilder>();
        var run = await db.Runs.AsNoTracking()
            .Include(x => x.Item)
            .FirstAsync(x => x.RunId == runId, ct);
        var latestQueueEvent = await db.TimelineEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Kind == TimelineEventKind.RunQueued)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var prompt = await builder.BuildAsync(run, latestQueueEvent?.Body, workspacePath, requiredDynamicTools, incremental, ct);
        return (run.Item?.Title ?? "Oratorio run", prompt.ContextJson, prompt.Prompt, prompt.RuntimeAdditionalContext);
    }

    private async Task<AppServerThreadReuseCandidate?> FindCompatibleThreadAsync(
        string runId,
        string baseWorkspacePath,
        string executionWorkspacePath,
        IReadOnlyList<string> requiredDynamicTools,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.AsNoTracking().FirstAsync(x => x.RunId == runId, ct);

        if (run.RetryCount > 0)
        {
            var previousAttempt = await db.Runs.AsNoTracking()
                .Where(x =>
                    x.RoundId == run.RoundId &&
                    x.Attempt == run.Attempt - 1 &&
                    x.RunnerKind == "appServer" &&
                    x.ThreadId != null &&
                    x.PromptContextJson != null)
                .FirstOrDefaultAsync(ct);
            if (previousAttempt is not null &&
                IsReviewThreadRecoveryError(previousAttempt.ErrorCode) &&
                SameHead(previousAttempt.TargetHeadSha, run.TargetHeadSha) &&
                IsCompatiblePromptContext(
                    previousAttempt.PromptContextJson!,
                    baseWorkspacePath,
                    executionWorkspacePath,
                    requiredDynamicTools))
            {
                return new AppServerThreadReuseCandidate(previousAttempt.ThreadId!, previousAttempt.RunId, IsRetryRecovery: true);
            }

            return null;
        }

        var candidates = await db.Runs.AsNoTracking()
            .Where(x =>
                x.ItemId == run.ItemId &&
                x.RunId != runId &&
                x.RunnerKind == "appServer" &&
                x.Status == RunStatus.Succeeded &&
                x.ThreadId != null &&
                x.PromptContextJson != null)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
            .Take(10)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            if (IsCompatiblePromptContext(
                candidate.PromptContextJson!,
                baseWorkspacePath,
                executionWorkspacePath,
                requiredDynamicTools))
            {
                return new AppServerThreadReuseCandidate(candidate.ThreadId!, candidate.RunId, IsRetryRecovery: false);
            }
        }

        return null;
    }

    private static bool IsCompatiblePromptContext(
        string contextJson,
        string baseWorkspacePath,
        string executionWorkspacePath,
        IReadOnlyList<string> requiredDynamicTools)
    {
        try
        {
            using var document = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            if (!TryGetProperty(root, "promptMode", "PromptMode", out var promptMode) ||
                !string.Equals(promptMode.GetString(), "compact", StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryGetProperty(root, "runtimeContextVersion", "RuntimeContextVersion", out var runtimeContextVersion) ||
                runtimeContextVersion.ValueKind != JsonValueKind.String ||
                !string.Equals(runtimeContextVersion.GetString(), AppServerPromptBuilder.RuntimeContextVersion, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryGetProperty(root, "threadStateWorkspaceMode", "ThreadStateWorkspaceMode", out var workspaceMode) ||
                !string.Equals(workspaceMode.GetString(), AppServerPromptBuilder.ThreadStateWorkspaceMode, StringComparison.Ordinal))
            {
                return false;
            }

            var path = ExtractNestedString(root, "workspace", "path")
                ?? ExtractNestedString(root, "Workspace", "Path");
            var basePath = ExtractNestedString(root, "workspace", "basePath")
                ?? ExtractNestedString(root, "Workspace", "BasePath");
            if (!SamePath(path, executionWorkspacePath) || !SamePath(basePath, baseWorkspacePath))
            {
                return false;
            }

            var previousTools = TryGetProperty(root, "requiredDynamicTools", "RequiredDynamicTools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array
                ? toolsElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Order(StringComparer.Ordinal).ToArray()
                : [];
            var currentTools = requiredDynamicTools.Order(StringComparer.Ordinal).ToArray();
            return previousTools.SequenceEqual(currentTools, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task MarkWorktreeNotRequiredAsync(string runId, string baseWorkspacePath, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        run.BaseWorkspacePath = baseWorkspacePath;
        run.WorktreeStatus = WorktreeStatus.NotRequired;
        run.LastHeartbeatAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkWorktreeReadyAsync(string runId, WorktreePrepareResult result, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.BaseWorkspacePath = result.BaseWorkspacePath;
        run.WorktreePath = result.WorktreePath;
        run.WorktreeBranch = result.WorktreeBranch;
        run.BaseRef = result.BaseRef;
        run.BaseSha = result.BaseSha;
        run.WorktreeStatus = WorktreeStatus.Ready;
        run.WorktreeErrorCode = null;
        run.WorktreeErrorMessage = null;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 15);
        run.StatusMessage = "Managed Git worktree is ready.";
        run.LastHeartbeatAt = now;
        run.Item!.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.System, "oratorio/worktree", "Managed worktree ready", result.WorktreePath, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkWorktreeFailedAsync(string runId, string baseWorkspacePath, string code, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.BaseWorkspacePath = baseWorkspacePath;
        run.WorktreeStatus = WorktreeStatus.Failed;
        run.WorktreeErrorCode = code;
        run.WorktreeErrorMessage = message;
        run.ProgressPercent = 100;
        run.StatusMessage = message;
        run.LastHeartbeatAt = now;
        run.Item!.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunFailed, ActorKind.System, "oratorio/worktree", "Managed worktree preparation failed", message, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkDispatchingAsync(string runId, string endpoint, string appServerIdentity, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.Status = RunStatus.Dispatching;
        run.AppServerEndpoint = endpoint;
        run.AppServerIdentity = appServerIdentity;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 20);
        run.StatusMessage = "Connecting to DotCraft AppServer.";
        run.LastHeartbeatAt = now;
        run.Item!.State = ItemState.Dispatching;
        run.Item.CheckState = CheckState.Pending;
        run.Item.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.Agent, "DotCraft", "Connecting to DotCraft AppServer", endpoint, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkThreadCreatedAsync(string runId, string threadId, string contextJson, string endpoint, string appServerIdentity, string? reason, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.ThreadId = threadId;
        run.AppServerEndpoint = endpoint;
        run.AppServerIdentity = appServerIdentity;
        run.PromptContextJson = contextJson;
        run.Round!.PromptContextJson = contextJson;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 30);
        run.StatusMessage = "DotCraft thread created.";
        run.LastHeartbeatAt = now;
        var body = string.IsNullOrWhiteSpace(reason) ? ShortId(threadId) : $"{threadId} ({reason})";
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.Agent, "DotCraft", "DotCraft thread created", body, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkThreadReusedAsync(string runId, AppServerThreadReuseCandidate candidate, string contextJson, string endpoint, string appServerIdentity, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.ThreadId = candidate.ThreadId;
        run.AppServerEndpoint = endpoint;
        run.AppServerIdentity = appServerIdentity;
        run.PromptContextJson = contextJson;
        run.Round!.PromptContextJson = contextJson;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 30);
        run.StatusMessage = "DotCraft thread reused.";
        run.LastHeartbeatAt = now;
        var reason = candidate.IsRetryRecovery
            ? $"{candidate.ThreadId} resumed from failed attempt {ShortId(candidate.RunId)} with matching target, workspace, and dynamic tools."
            : $"{candidate.ThreadId} reused from run {ShortId(candidate.RunId)} with matching workspace and dynamic tools.";
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.Agent, "DotCraft", "DotCraft thread reused", reason, now);
        await db.SaveChangesAsync(ct);
    }

    private static string BuildLocalAppServerIdentity(string workspacePath) =>
        $"local:{Path.GetFullPath(workspacePath)}";

    private async Task MarkRunningAsync(string runId, string threadId, string? turnId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.Status = RunStatus.Running;
        run.ThreadId = threadId;
        run.TurnId = turnId;
        run.ProgressPercent = Math.Max(run.ProgressPercent, 40);
        run.StatusMessage = "DotCraft turn is running.";
        run.LastHeartbeatAt = now;
        run.Item!.State = ItemState.Running;
        run.Item.UpdatedAt = now;
        AddTimeline(run, TimelineEventKind.RunStarted, ActorKind.Agent, "DotCraft", "Turn started", ShortId(turnId), now);
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateRunHeartbeatAsync(string runId, int progress, string message, string? turnId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.Include(x => x.Item).FirstOrDefaultAsync(x => x.RunId == runId, ct);
        if (run is null || !ActiveWorkerStatuses.Contains(run.Status))
        {
            return;
        }

        var now = clock.UtcNow;
        run.ProgressPercent = Math.Max(run.ProgressPercent, progress);
        run.StatusMessage = message;
        run.LastHeartbeatAt = now;
        if (!string.IsNullOrWhiteSpace(turnId))
        {
            run.TurnId = turnId;
        }

        if (run.Item is not null)
        {
            run.Item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SucceedRunAsync(string runId, string summary, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return;
        }

        if (run.Purpose == RunPurpose.Implementation)
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ImplementationDraftService>();
            try
            {
                summary = await drafts.CompleteRunDeliveryAsync(runId, summary, ct);
            }
            catch (OratorioApiException ex)
            {
                FailRun(run, clock.UtcNow, RunStatus.Failed, ex.Code, ex.Message, allowRetry: false);
                await RecordFailedReviewGateAsync(scope, run, ct);
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        if (RequiresReviewDraft(run))
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ReviewDraftService>();
            if (!await drafts.RunHasDraftAsync(runId, ct))
            {
                FailRun(
                    run,
                    clock.UtcNow,
                    RunStatus.Failed,
                    "reviewDraftRequired",
                    "Source review runs must submit oratorio_run.SubmitReviewDraft before completing.",
                    allowRetry: true);
                await RecordFailedReviewGateAsync(scope, run, ct);
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        await CompleteRunAsync(scope, db, run, summary, ct);
    }

    private async Task CompleteRunAsync(IServiceScope scope, OratorioDbContext db, OratorioRun run, string summary, CancellationToken ct)
    {
        var now = clock.UtcNow;

        run.Status = RunStatus.Succeeded;
        run.CompletedAt = now;
        run.Summary = summary;
        run.ProgressPercent = 100;
        run.StatusMessage = run.Purpose == RunPurpose.Implementation ? "Implementation handoff is ready." : "DotCraft analysis is ready.";
        run.LastHeartbeatAt = now;
        run.LeaseOwner = null;
        run.LeaseAcquiredAt = null;
        if (run.WorktreeStatus == WorktreeStatus.Ready)
        {
            run.WorktreeStatus = WorktreeStatus.CleanupPending;
            run.WorktreeCleanupAfterAt = now + options.CurrentValue.SucceededWorktreeRetention;
        }

        run.Round!.Status = RoundStatus.AwaitingReview;
        run.Round.Summary = summary;
        run.Round.CompletedAt = now;

        run.Item!.State = ItemState.AwaitingReview;
        run.Item.CurrentRunId = null;
        run.Item.LatestSummary = summary;
        run.Item.CheckState = CheckState.Attention;
        run.Item.UpdatedAt = now;

        AddTimeline(run, TimelineEventKind.RunCompleted, ActorKind.Agent, "DotCraft", run.Purpose == RunPurpose.Implementation ? "Implementation handoff captured" : "Agent summary captured", summary, now);
        AddTimeline(run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check waiting on review", "The DotCraft run completed and needs operator review.", now);
        await RecordCompletedReviewGateAsync(scope, run, ct);
        await db.SaveChangesAsync(ct);

        if (run.Purpose == RunPurpose.ReviewAnalysis)
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ReviewDraftService>();
            await drafts.AutoPublishRunDraftsAsync(run.RunId, ct);
        }
    }

    private async Task<bool> FailRunAsync(string runId, RunStatus status, string errorCode, string errorMessage, bool allowRetry, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await LoadRunAsync(db, runId, ct);
        if (IsTerminal(run.Status))
        {
            return false;
        }

        if (await TryCompleteReviewFromDraftAsync(scope, db, run, errorCode, errorMessage, ct))
        {
            return true;
        }

        FailRun(run, clock.UtcNow, status, errorCode, errorMessage, allowRetry);
        await RecordFailedReviewGateAsync(scope, run, ct);
        await db.SaveChangesAsync(ct);
        return false;
    }

    private async Task<bool> TryCompleteReviewFromDraftAsync(
        IServiceScope scope,
        OratorioDbContext db,
        OratorioRun run,
        string terminalErrorCode,
        string terminalErrorMessage,
        CancellationToken ct)
    {
        if (!RequiresReviewDraft(run))
        {
            return false;
        }

        var draftSummary = await db.ReviewDrafts.AsNoTracking()
            .Where(x =>
                x.RunId == run.RunId &&
                x.Status != ReviewDraftStatus.Discarded)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.SummaryBody)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(draftSummary))
        {
            return false;
        }

        AddTimeline(
            run,
            TimelineEventKind.ItemUpdated,
            ActorKind.System,
            "oratorio/review",
            "Review draft retained after AppServer termination",
            $"A valid Review Draft was submitted before the AppServer terminated: {terminalErrorMessage}",
            clock.UtcNow,
            JsonSerializer.Serialize(new { code = terminalErrorCode, message = terminalErrorMessage }, JsonOptions));
        await CompleteRunAsync(scope, db, run, draftSummary, ct);
        return true;
    }

    private void FailRun(OratorioRun run, DateTimeOffset now, RunStatus status, string errorCode, string errorMessage, bool allowRetry)
    {
        var shouldRetry = allowRetry && ShouldRetry(run, status, errorCode);
        run.Status = status;
        run.CompletedAt = now;
        run.ErrorCode = errorCode;
        run.ErrorMessage = errorMessage;
        run.Summary = errorMessage;
        run.ProgressPercent = 100;
        run.StatusMessage = shouldRetry ? $"{errorMessage} Retrying after backoff." : errorMessage;
        run.LastHeartbeatAt = now;
        run.LeaseOwner = null;
        run.LeaseAcquiredAt = null;
        if (run.WorktreeStatus is WorktreeStatus.Ready or WorktreeStatus.Preparing)
        {
            run.WorktreeStatus = run.WorktreePath is null ? WorktreeStatus.Failed : WorktreeStatus.CleanupPending;
            run.WorktreeCleanupAfterAt = now + options.CurrentValue.FailedWorktreeRetention;
        }

        AddTimeline(run, TimelineEventKind.RunFailed, ActorKind.Agent, "DotCraft", status == RunStatus.TimedOut ? "AppServer timed out" : "AppServer failed", errorMessage, now);

        if (shouldRetry)
        {
            var retryRun = CreateRetryRun(run, now);
            run.Item!.Runs.Add(retryRun);
            run.Round!.Status = RoundStatus.Running;
            run.Item.State = ItemState.Dispatching;
            run.Item.CurrentRunId = retryRun.RunId;
            run.Item.CheckState = CheckState.Pending;
            run.Item.UpdatedAt = now;
            run.Item.TimelineEvents.Add(new OratorioTimelineEvent
            {
                ItemId = retryRun.ItemId,
                RoundId = retryRun.RoundId,
                RunId = retryRun.RunId,
                Kind = TimelineEventKind.RunQueued,
                ActorKind = ActorKind.System,
                ActorName = "oratorio/retry",
                Title = "Retry scheduled",
                Body = $"Attempt {retryRun.Attempt} will start after {retryRun.NextRetryAt:O}.",
                CreatedAt = now
            });
            return;
        }

        run.Round!.Status = RoundStatus.Failed;
        run.Round.Summary = errorMessage;
        run.Round.CompletedAt = now;

        run.Item!.State = ItemState.Failed;
        run.Item.CurrentRunId = null;
        run.Item.LatestSummary = errorMessage;
        run.Item.CheckState = CheckState.Failing;
        run.Item.UpdatedAt = now;

        AddTimeline(run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check failed", errorMessage, now);
    }

    private OratorioRun CreateRetryRun(OratorioRun failedRun, DateTimeOffset now)
    {
        var retryCount = failedRun.RetryCount + 1;
        var delaySeconds = Math.Min(
            options.CurrentValue.MaxRetryBackoff.TotalSeconds,
            options.CurrentValue.RetryBackoff.TotalSeconds * Math.Pow(2, Math.Max(0, retryCount - 1)));
        return new OratorioRun
        {
            ItemId = failedRun.ItemId,
            RoundId = failedRun.RoundId,
            Attempt = failedRun.Attempt + 1,
            Status = RunStatus.Queued,
            RunnerKind = "appServer",
            StartedAt = now,
            ProgressPercent = 0,
            StatusMessage = "Retry queued after transient AppServer failure.",
            RetryCount = retryCount,
            NextRetryAt = now + TimeSpan.FromSeconds(delaySeconds),
            WorktreeStatus = WorktreeStatus.NotRequired,
            Purpose = failedRun.Purpose,
            DispatchTrigger = failedRun.DispatchTrigger,
            TargetHeadSha = failedRun.TargetHeadSha,
            DeliveryPolicy = failedRun.DeliveryPolicy,
            ImplementationTurnCount = failedRun.ImplementationTurnCount
        };
    }

    private static async Task RecordFailedReviewGateAsync(IServiceScope scope, OratorioRun run, CancellationToken ct)
    {
        if (run.Item?.State != ItemState.Failed)
        {
            return;
        }

        var writes = scope.ServiceProvider.GetRequiredService<GitHubWriteService>();
        await writes.RecordReviewGateRunFailedAsync(run, ct);
    }

    private static async Task RecordCompletedReviewGateAsync(IServiceScope scope, OratorioRun run, CancellationToken ct)
    {
        if (run.Item?.State != ItemState.AwaitingReview)
        {
            return;
        }

        var writes = scope.ServiceProvider.GetRequiredService<GitHubWriteService>();
        await writes.RecordReviewGateRunCompletedAsync(run, ct);
    }

    private static bool RequiresReviewDraft(OratorioRun run) =>
        run.Purpose == RunPurpose.ReviewAnalysis &&
        run.Item?.Source is "github" or "gitlab" &&
        run.Item.Kind == ItemKind.PullRequest &&
        run.RunnerKind == "appServer";

    private bool ShouldRetry(OratorioRun run, RunStatus status, string errorCode)
    {
        if (run.Attempt >= options.CurrentValue.EffectiveMaxRunAttempts)
        {
            return false;
        }

        return status == RunStatus.TimedOut ||
            errorCode is "appServerDisconnected" or
                "appServerFailed" or
                "appServerTimedOut" or
                "appServerStalled" or
                "appServerRunnerInterrupted" or
                "gitCommandFailed" ||
            (RequiresReviewDraft(run) && IsReviewThreadRecoveryError(errorCode));
    }

    private static bool IsReviewThreadRecoveryError(string? errorCode) =>
        errorCode is "reviewDraftRequired" or "appServerTurnFailed" or "appServerTurnCancelled";

    private static bool IsThreadResumeRejection(Exception ex) =>
        ex is JsonRpcException or InvalidOperationException;

    private static bool SameHead(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientPreparationError(string code) =>
        code is "gitCommandFailed";

    private async Task ConsumeNotificationsAsync(string runId, IDotCraftAppServerClient client, string threadId, CancellationToken ct)
    {
        var summary = new StringBuilder();
        await foreach (var runEvent in client.ReadEventsAsync(ct))
        {
            if (runEvent.Type is "initialized" or "thread/started")
            {
                continue;
            }

            if (runEvent is DotCraftRunEvent<TurnNotification> { Type: "turn/started" } startedTurn)
            {
                var turnId = startedTurn.Params.Turn.Id;
                runCoordinator.UpdateRunStatus(runId, turnId, "running");
                PublishRunStatus(runId, "running", turnId, null, null, 45, "DotCraft turn started.");
                await UpdateRunHeartbeatAsync(runId, 45, "DotCraft turn started.", turnId, ct);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/started" } startedItem)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemStartedType, startedItem.Params, streaming: true);
                await UpdateRunHeartbeatAsync(runId, 55, "DotCraft item started.", null, ct);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemDeltaNotification> deltaEvent)
            {
                var delta = deltaEvent.Params.Delta;
                if (!string.IsNullOrWhiteSpace(delta) && runEvent.Type.Contains("agentMessage", StringComparison.OrdinalIgnoreCase))
                {
                    summary.Append(delta);
                }

                if (!string.IsNullOrEmpty(delta))
                {
                    PublishItemDelta(runId, runEvent.Type, deltaEvent.Params);
                }

                await UpdateRunHeartbeatAsync(runId, 65, "DotCraft agent is producing output.", null, ct);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/completed" } completedItem)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemCompletedType, completedItem.Params, streaming: false);
                var text = AgentMessageText(completedItem.Params.Item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    summary.AppendLine(text);
                }

                await UpdateRunHeartbeatAsync(runId, 82, "DotCraft item completed.", null, ct);
                continue;
            }

            if (runEvent.Type == "turn/completed")
            {
                runCoordinator.UpdateRunStatus(runId, null, "completed");
                PublishRunStatus(runId, "completed", null, null, null, 100, "DotCraft turn completed.");
                var text = summary.ToString().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = runEvent is DotCraftRunEvent<TurnNotification> terminal
                        ? TurnError(terminal.Params)
                        : null;
                }

                if (await TryContinueImplementationRunAsync(runId, client, threadId, ct))
                {
                    summary.Clear();
                    continue;
                }

                await SucceedRunAsync(runId, string.IsNullOrWhiteSpace(text) ? "DotCraft analysis completed." : text.Trim(), ct);
                return;
            }

            if (runEvent is DotCraftRunEvent<TurnNotification> { Type: "turn/failed" } failed)
            {
                var errorMessage = TurnError(failed.Params) ?? "DotCraft turn failed.";
                var completedFromDraft = await FailRunAsync(runId, RunStatus.Failed, "appServerTurnFailed", errorMessage, allowRetry: true, ct);
                runCoordinator.UpdateRunStatus(runId, null, completedFromDraft ? "completed" : "failed");
                PublishRunStatus(
                    runId,
                    completedFromDraft ? "completed" : "failed",
                    null,
                    completedFromDraft ? null : "appServerTurnFailed",
                    completedFromDraft ? null : errorMessage,
                    100,
                    completedFromDraft ? "Review draft retained after DotCraft turn failure." : "DotCraft turn failed.");
                return;
            }

            if (runEvent.Type == "turn/cancelled")
            {
                const string errorMessage = "DotCraft turn was cancelled.";
                var completedFromDraft = await FailRunAsync(runId, RunStatus.Failed, "appServerTurnCancelled", errorMessage, allowRetry: true, ct);
                runCoordinator.UpdateRunStatus(runId, null, completedFromDraft ? "completed" : "cancelled");
                PublishRunStatus(
                    runId,
                    completedFromDraft ? "completed" : "cancelled",
                    null,
                    completedFromDraft ? null : "appServerTurnCancelled",
                    completedFromDraft ? null : errorMessage,
                    100,
                    completedFromDraft ? "Review draft retained after DotCraft turn cancellation." : errorMessage);
                return;
            }

            if (runEvent is DotCraftRunEvent<PlanUpdatedNotification> { Type: "plan/updated" } plan)
            {
                PublishPlan(runId, plan.Params);
                continue;
            }
        }

        await FailRunAsync(runId, RunStatus.Failed, "appServerDisconnected", "DotCraft AppServer disconnected before the run completed.", allowRetry: true, ct);
    }

    private void PublishItemSnapshot(string runId, string eventType, ItemNotification parameters, bool streaming)
    {
        try
        {
            var item = parameters.Item;
            var itemId = item.Id;
            var itemType = item.Type;
            var rawPayload = item.Payload.IsSet && item.Payload.Value is { } payloadElement
                ? payloadElement
                : JsonSerializer.SerializeToElement(new { }, JsonOptions);
            var payload = DrawerPayloadSanitizer.SafeClonePayload(itemType, rawPayload, out var payloadError);
            if (payloadError is not null)
            {
                logger.LogWarning(
                    payloadError,
                    "AppServer drawer item payload could not be cloned for run {RunId}, item {ItemId}, type {ItemType}; using placeholder payload.",
                    runId,
                    itemId,
                    itemType);
            }

            var snapshot = new ConversationItemDto(
                itemId,
                item.TurnId,
                itemType,
                item.Status,
                payload,
                item.CreatedAt,
                item.CompletedAt,
                streaming);
            var timestamp = snapshot.CompletedAt ?? snapshot.CreatedAt ?? clock.UtcNow;
            var updated = drawerState.UpsertItem(runId, snapshot, timestamp);
            boardEvents.PublishDrawer(runId, new DrawerEvent(
                eventType,
                runId,
                null,
                null,
                SerializeDrawerItemPayload(runId, itemId, updated),
                timestamp));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AppServer drawer item snapshot publish failed for run {RunId}; continuing the AppServer run.", runId);
        }
    }

    private void PublishItemDelta(string runId, string method, ItemDeltaNotification parameters)
    {
        try
        {
            var itemId = parameters.ItemId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var itemType = method.Contains("agentMessage", StringComparison.OrdinalIgnoreCase)
                ? "agentMessage"
                : method.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                    ? "reasoningContent"
                    : method.Contains("commandExecution", StringComparison.OrdinalIgnoreCase)
                        ? "commandExecution"
                        : method.Contains("toolCall", StringComparison.OrdinalIgnoreCase)
                            ? "toolCall"
                            : "unknown";
            var deltaKind = parameters.DeltaKind ?? itemType;
            var timestamp = clock.UtcNow;
            var updated = drawerState.AppendDelta(
                runId,
                itemId,
                parameters.TurnId,
                itemType,
                deltaKind,
                parameters.Delta,
                timestamp);
            boardEvents.PublishDrawer(runId, new DrawerEvent(
                DrawerEvent.ItemDeltaType,
                runId,
                null,
                null,
                SerializeDrawerItemPayload(runId, itemId, updated),
                timestamp));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AppServer drawer item delta publish failed for run {RunId}; continuing the AppServer run.", runId);
        }
    }

    private JsonElement SerializeDrawerItemPayload(string runId, string itemId, ConversationItemDto item)
    {
        try
        {
            return JsonSerializer.SerializeToElement(item, JsonOptions);
        }
        catch (Exception ex) when (DrawerPayloadSanitizer.IsPayloadException(ex))
        {
            logger.LogWarning(
                ex,
                "AppServer drawer item payload could not be serialized for run {RunId}, item {ItemId}, type {ItemType}; using placeholder payload.",
                runId,
                itemId,
                item.Type);
            var fallback = item with { Payload = DrawerPayloadSanitizer.CreateUnavailablePayload(item.Type) };
            return JsonSerializer.SerializeToElement(fallback, JsonOptions);
        }
    }

    private void PublishPlan(string runId, PlanUpdatedNotification parameters)
    {
        var todos = new List<PlanTodoDto>();
        if (parameters.Todos.IsSet && parameters.Todos.Value is { } planTodos)
        {
            foreach (var todo in planTodos)
            {
                todos.Add(new PlanTodoDto(
                    OptionalString(todo.Id) ?? Guid.NewGuid().ToString("n"),
                    OptionalString(todo.Content) ?? "",
                    OptionalString(todo.Priority) ?? "medium",
                    OptionalString(todo.Status) ?? "pending"));
            }
        }

        var plan = new PlanSnapshotDto(
            OptionalString(parameters.Title),
            OptionalString(parameters.Overview),
            OptionalString(parameters.Content),
            todos);
        var timestamp = clock.UtcNow;
        drawerState.ReplacePlan(runId, plan, timestamp);
        boardEvents.PublishDrawer(runId, new DrawerEvent(
            DrawerEvent.PlanUpdatedType,
            runId,
            null,
            null,
            JsonSerializer.SerializeToElement(plan, JsonOptions),
            timestamp));
    }

    private static string? AgentMessageText(SessionItem item)
    {
        var payload = SessionItemPayloadParser.Parse(item);
        return payload.TryGet<AgentMessagePayload>(out var message) ? message!.Text : null;
    }

    private static string? TurnError(TurnNotification notification) =>
        notification.Error ?? notification.Turn.Error;

    private static string? OptionalString(Optional<string> value) =>
        value.IsSet ? value.Value : null;

    private void PublishRunStatus(
        string runId,
        string turnStatus,
        string? turnId,
        string? errorCode,
        string? errorMessage,
        int progress,
        string statusMessage)
    {
        var timestamp = clock.UtcNow;
        var summary = new RunSummaryDto(
            runId,
            turnStatus == "running" ? RunStatus.Running.ToString() : turnStatus,
            null,
            turnId,
            turnStatus,
            errorCode,
            errorMessage,
            progress,
            statusMessage,
            timestamp);
        drawerState.UpdateRunStatus(runId, summary, timestamp);
        boardEvents.PublishDrawer(runId, new DrawerEvent(
            DrawerEvent.RunStatusType,
            runId,
            null,
            null,
            JsonSerializer.SerializeToElement(summary, JsonOptions),
            timestamp));
    }

    private async Task<bool> TryContinueImplementationRunAsync(string runId, IDotCraftAppServerClient client, string threadId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.FirstOrDefaultAsync(x => x.RunId == runId, ct);
        if (run is null || run.Purpose != RunPurpose.Implementation || IsTerminal(run.Status))
        {
            return false;
        }

        var drafts = scope.ServiceProvider.GetRequiredService<ImplementationDraftService>();
        if (await drafts.RunHasDraftAsync(runId, ct))
        {
            return false;
        }

        var maxTurns = automationOptions.CurrentValue.EffectiveMaxImplementationTurns;
        run.ImplementationTurnCount++;
        if (run.ImplementationTurnCount >= maxTurns)
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        var now = clock.UtcNow;
        run.StatusMessage = $"No implementation draft submitted. Continuing turn {run.ImplementationTurnCount + 1} of {maxTurns}.";
        run.LastHeartbeatAt = now;
        await db.SaveChangesAsync(ct);

        var prompt = "Continue the implementation round using the Oratorio implementation runtime context. If you are blocked, record the blocker in the implementation draft risks.";
        var nextTurnId = await client.StartTurnAsync(threadId, prompt, ct);
        runCoordinator.UpdateRunStatus(runId, nextTurnId, "running");
        await MarkRunningAsync(runId, threadId, nextTurnId, ct);
        return true;
    }

    private static async Task<OratorioRun> LoadRunAsync(OratorioDbContext db, string runId, CancellationToken ct) =>
        await db.Runs
            .Include(x => x.Item)
            .ThenInclude(x => x!.Runs)
            .Include(x => x.Item)
            .ThenInclude(x => x!.TimelineEvents)
            .Include(x => x.Round)
            .FirstAsync(x => x.RunId == runId, ct);

    private static void AddTimeline(
        OratorioRun run,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt,
        string? metadataJson = null)
    {
        run.Item!.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = run.ItemId,
            RoundId = run.RoundId,
            RunId = run.RunId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            MetadataJson = metadataJson,
            CreatedAt = createdAt
        });
    }

    private static string RepositoryKey(string? repository) =>
        string.IsNullOrWhiteSpace(repository) ? "(none)" : repository;

    private static string SourceKey(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "(none)" : source;

    private static bool IsTerminal(RunStatus status) =>
        status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled or RunStatus.TimedOut;

    private static async Task<string?> ResolveWorktreeFetchRefAsync(OratorioDbContext db, OratorioRun run, CancellationToken ct)
    {
        var item = run.Item;
        if (item is null || string.IsNullOrWhiteSpace(item.HeadSha))
        {
            return null;
        }

        if (run.Purpose == RunPurpose.ReviewAnalysis && item.Kind == ItemKind.PullRequest)
        {
            return ResolveReviewTargetFetchRef(item.Source, item.ExternalId);
        }

        if (item.Kind != ItemKind.LocalTask ||
            string.IsNullOrWhiteSpace(item.ParentItemId) ||
            string.IsNullOrWhiteSpace(item.GeneratedFromDraftId))
        {
            return null;
        }

        var parent = await db.Items.AsNoTracking()
            .Where(x => x.ItemId == item.ParentItemId)
            .Select(x => new
            {
                x.Source,
                x.ExternalId,
                x.Kind,
                x.Repository,
                x.HeadSha
            })
            .FirstOrDefaultAsync(ct);

        if (parent is null ||
            parent.Kind != ItemKind.PullRequest ||
            !SameRepository(parent.Repository, item.Repository) ||
            !string.Equals(NullIfWhiteSpace(parent.HeadSha), NullIfWhiteSpace(item.HeadSha), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveReviewTargetFetchRef(parent.Source, parent.ExternalId);
    }

    private static string? ResolveReviewTargetFetchRef(string source, string externalId)
    {
        if (source == "github" && TryParseSourceNumber(externalId, '#', out var pullRequestNumber))
        {
            return $"refs/pull/{pullRequestNumber}/head";
        }

        if (source == "gitlab" && TryParseSourceNumber(externalId, '!', out var mergeRequestIid))
        {
            return $"refs/merge-requests/{mergeRequestIid}/head";
        }

        return null;
    }

    private static bool SameRepository(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseSourceNumber(string externalId, char marker, out int number)
    {
        number = 0;
        var index = externalId.LastIndexOf(marker);
        return index >= 0 && int.TryParse(externalId[(index + 1)..], out number);
    }

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            left = Path.GetFullPath(left);
            right = Path.GetFullPath(right);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement element, string camelName, string pascalName, out JsonElement property)
    {
        if (element.TryGetProperty(camelName, out property))
        {
            return true;
        }

        return element.TryGetProperty(pascalName, out property);
    }

    private static string? ExtractString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? ExtractNestedString(JsonElement element, string container, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(container, out var nested))
        {
            return null;
        }

        return ExtractString(nested, propertyName);
    }

    private static string? ShortId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 12 ? value : value[..12];
    }
}

internal sealed record AppServerThreadReuseCandidate(string ThreadId, string RunId, bool IsRetryRecovery);
