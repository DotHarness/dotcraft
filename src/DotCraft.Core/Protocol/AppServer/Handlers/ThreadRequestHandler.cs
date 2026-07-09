using System.Globalization;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Logging;

namespace DotCraft.Protocol.AppServer;

internal sealed class ThreadRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    AppServerResponseWriter responseWriter,
    AppServerThreadBinder threadBinder,
    AppServerThreadWireProjector threadProjector,
    WorkspaceConfigEditor workspaceConfig,
    IAppConfigMonitor? appConfigMonitor,
    string? hostWorkspacePath,
    string? workspaceCraftPath,
    SessionStreamDebugLogger? streamDebugLogger,
    SessionApprovalDecision defaultApprovalDecision) : IAppServerDomainHandler
{
    private const int ThreadListDefaultPageLimit = 50;
    private const int ThreadListMaxPageLimit = 100;
    private const int ThreadReadMaxPageLimit = 100;
    private const int MaxWidgetStateBytes = 8 * 1024;
    private const string ThreadListCursorKind = "thread-list";
    private const string ThreadReadCursorKind = "thread-read";
    private const string ThreadAppBindingsChanged = "thread/appBindings/changed";
    private readonly object _pendingInteractiveReplayLock = new();
    private readonly Dictionary<string, Task> _pendingInteractiveReplayTasks = new(StringComparer.Ordinal);

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.ThreadStart, HandleThreadStartAsync);
        table.Map(AppServerMethods.ThreadFork, HandleThreadForkAsync);
        table.Map(AppServerMethods.ThreadResume, HandleThreadResumeAsync);
        table.Map(AppServerMethods.ThreadList, HandleThreadListAsync);
        table.Map(AppServerMethods.ThreadRead, HandleThreadReadAsync);
        table.Map(AppServerMethods.ThreadGoalGet, HandleThreadGoalGetAsync);
        table.Map(AppServerMethods.ThreadGoalSet, HandleThreadGoalSetAsync);
        table.Map(AppServerMethods.ThreadGoalClear, HandleThreadGoalClearAsync);
        table.Map(AppServerMethods.ItemWidgetStateSet, HandleItemWidgetStateSetAsync);
        table.Map(AppServerMethods.ThreadCompactStart, HandleThreadCompactStartAsync);
        table.Map(AppServerMethods.ThreadMemoryConsolidateStart, HandleThreadMemoryConsolidateStartAsync);
        table.Map(AppServerMethods.ThreadMaintenanceInterrupt, HandleThreadMaintenanceInterruptAsync);
        table.Map(AppServerMethods.ThreadRollback, HandleThreadRollbackAsync);
        table.Map(AppServerMethods.ThreadSubscribe, HandleThreadSubscribeAsync);
        table.Map(AppServerMethods.ThreadUnsubscribe, HandleThreadUnsubscribeAsync);
        table.Map(AppServerMethods.ThreadPause, HandleThreadPauseAsync);
        table.Map(AppServerMethods.ThreadArchive, HandleThreadArchiveAsync);
        table.Map(AppServerMethods.ThreadUnarchive, HandleThreadUnarchiveAsync);
        table.Map(AppServerMethods.ThreadDelete, HandleThreadDeleteAsync);
        table.Map(AppServerMethods.ThreadRename, HandleThreadRenameAsync);
        table.Map(AppServerMethods.ThreadModeSet, HandleThreadModeSetAsync);
        table.Map(AppServerMethods.ThreadConfigUpdate, HandleThreadConfigUpdateAsync);
    }

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath))
            return identity with { WorkspacePath = hostWorkspacePath };
        return identity;
    }

    private async Task<object?> HandleThreadStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadStartParams>(msg);
        threadBinder.ValidateRuntimeInputs(p.DynamicTools, p.AdditionalContext);

        var identity = NormalizeIdentityWorkspace(p.Identity);
        p.Config = ResolveAgentProfileConfig(p.Config, msg, identity);
        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;
        ValidateRuntimeConfiguration(p.Config);

        var spawnSource = !string.IsNullOrWhiteSpace(p.SpawnedFromThreadId)
            ? ThreadSource.SpawnedFromThread(p.SpawnedFromThreadId.Trim())
            : null;

        var thread = await sessionService.CreateThreadAsync(
            identity,
            p.Config,
            historyMode,
            displayName: p.DisplayName,
            ct: ct,
            source: spawnSource);

        await threadBinder.BindThreadRuntimeAsync(thread, p.DynamicTools, p.AdditionalContext, ct);

        var startedWire = await threadProjector.ProjectAsync(thread, true, false, ct);
        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = startedWire },
            AppServerMethods.ThreadStarted,
            new { thread = startedWire },
            ct);

        return null;
    }

    private ThreadConfiguration? ResolveAgentProfileConfig(
        ThreadConfiguration? config,
        AppServerIncomingMessage msg,
        SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(config?.AgentProfileId))
            return config;

        try
        {
            var store = new AgentProfileStore(ResolveAgentProfileWorkspaceCraftPath(identity));
            return store.ResolveThreadStartConfiguration(config, TryGetConfigElement(msg));
        }
        catch (AgentProfileException ex)
        {
            throw AgentProfileRequestHandler.MapError(ex);
        }
    }

    private string? ResolveAgentProfileWorkspaceCraftPath(SessionIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
            return workspaceCraftPath;
        if (!string.IsNullOrWhiteSpace(identity.WorkspacePath))
            return Path.Combine(identity.WorkspacePath, ".craft");
        if (!string.IsNullOrWhiteSpace(hostWorkspacePath))
            return Path.Combine(hostWorkspacePath, ".craft");
        return null;
    }

    private static JsonElement? TryGetConfigElement(AppServerIncomingMessage msg)
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in msg.Params.Value.EnumerateObject())
        {
            if (string.Equals(property.Name, "config", StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private async Task<object?> HandleThreadForkAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadForkParams>(msg);
        threadBinder.ValidateRuntimeInputs(p.DynamicTools, p.AdditionalContext);
        ValidateRuntimeConfiguration(p.Config);

        var identity = p.Identity == null ? null : NormalizeIdentityWorkspace(p.Identity);
        var thread = await sessionService.ForkThreadAsync(
            p.ThreadId,
            new ThreadForkOptions
            {
                Path = p.Path,
                ForkPoint = p.ForkPoint,
                Identity = identity,
                Config = p.Config,
                DisplayName = p.DisplayName,
                Ephemeral = p.Ephemeral ?? false
            },
            ct);

        await threadBinder.BindThreadRuntimeAsync(thread, p.DynamicTools, p.AdditionalContext, ct);

        var includeTurns = p.ExcludeTurns != true;
        var responseWire = await threadProjector.ProjectAsync(thread, includeTurns, true, ct);
        var notificationWire = await threadProjector.ProjectAsync(thread, false, false, ct);

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = responseWire },
            AppServerMethods.ThreadStarted,
            new { thread = notificationWire },
            ct);

        return null;
    }

    private async Task<object?> HandleThreadResumeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadResumeParams>(msg);
        threadBinder.ValidateRuntimeInputs(p.DynamicTools, p.AdditionalContext);

        var thread = await sessionService.ResumeThreadAsync(p.ThreadId, ct);
        await threadBinder.BindThreadRuntimeAsync(thread, p.DynamicTools, p.AdditionalContext, ct);

        var resumedBy = connection.ClientInfo?.Name ?? "appserver";
        var resumedWire = await threadProjector.ProjectAsync(thread, true, false, ct);
        var responseResult = new { thread = resumedWire };
        var notifParams = new { thread = resumedWire, resumedBy };

        if (connection.HasSubscription(p.ThreadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, responseResult, ct);
            SchedulePendingInteractiveRequestReplay(p.ThreadId);
            return null;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            responseResult,
            AppServerMethods.ThreadResumed,
            notifParams,
            ct);
        SchedulePendingInteractiveRequestReplay(p.ThreadId);
        return null;
    }

    private async Task<object?> HandleThreadListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadListParams>(msg);
        var identity = NormalizeIdentityWorkspace(p.Identity);
        var crossOrigins = ResolveCrossChannelOriginsForThreadList(p);
        var threads = await sessionService.FindThreadsAsync(
            identity,
            p.IncludeArchived ?? false,
            crossOrigins,
            ct,
            p.IncludeSubAgents ?? false);

        if (p.IncludeInternal != true)
        {
            threads = threads
                .Where(t => !ThreadVisibility.IsInternal(t))
                .ToList();
        }

        if (!string.IsNullOrEmpty(p.ChannelName))
        {
            threads = threads
                .Where(t => string.Equals(t.OriginChannel, p.ChannelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(p.Query))
        {
            var query = p.Query.Trim();
            threads = threads
                .Where(t => MatchesThreadListQuery(t, query))
                .ToList();
        }

        var totalMatched = threads.Count;
        var isPaged = p.Limit.HasValue || !string.IsNullOrWhiteSpace(p.Cursor);
        var limit = isPaged
            ? NormalizePageLimit(p.Limit, ThreadListDefaultPageLimit, ThreadListMaxPageLimit, "limit")
            : totalMatched;
        var offset = DecodeCursorOffset(p.Cursor, ThreadListCursorKind);
        if (!isPaged && offset != 0)
            throw AppServerErrors.InvalidParams("'cursor' requires pagination.");

        var page = isPaged
            ? threads.Skip(offset).Take(limit).ToList()
            : threads.ToList();
        var nextOffset = offset + page.Count;
        var nextCursor = isPaged && nextOffset < totalMatched
            ? EncodeCursor(ThreadListCursorKind, nextOffset)
            : null;

        var data = new List<ThreadSummary>();
        var catalogByWorkspace = new Dictionary<string, AppCatalogSnapshot?>(StringComparer.Ordinal);
        foreach (var summary in page)
        {
            await threadProjector.EnrichSummaryAsync(summary, catalogByWorkspace, ct);
            data.Add(summary);
        }

        return new ThreadListResult
        {
            Data = data,
            NextCursor = nextCursor,
            TotalMatched = isPaged || !string.IsNullOrWhiteSpace(p.Query) ? totalMatched : null
        };
    }

    private async Task<object?> HandleThreadReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadReadParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var isPaged = p.TurnLimit.HasValue || !string.IsNullOrWhiteSpace(p.Cursor);
        var includeTurns = (p.IncludeTurns ?? false) || isPaged;
        ThreadReadTurnPage? turnPage = null;
        SessionWireThread baseWire;
        if (isPaged)
        {
            var limit = NormalizePageLimit(p.TurnLimit, ThreadListDefaultPageLimit, ThreadReadMaxPageLimit, "turnLimit");
            var offset = DecodeCursorOffset(p.Cursor, ThreadReadCursorKind);
            var totalTurns = thread.Turns.Count;
            var startIndex = Math.Max(0, totalTurns - offset - limit);
            var count = Math.Max(0, totalTurns - offset - startIndex);
            var pageTurns = count == 0
                ? new List<SessionWireTurn>()
                : thread.Turns.Skip(startIndex).Take(count).Select(t => t.ToWire(includeItems: true)).ToList();
            var nextOffset = offset + count;
            var hasMore = startIndex > 0;
            turnPage = new ThreadReadTurnPage
            {
                Limit = limit,
                TotalTurns = totalTurns,
                StartOrdinal = count == 0 ? 0 : startIndex + 1,
                EndOrdinal = count == 0 ? 0 : startIndex + count,
                NextCursor = hasMore ? EncodeCursor(ThreadReadCursorKind, nextOffset) : null,
                HasMore = hasMore
            };
            baseWire = thread.ToWire(includeTurns: false) with { Turns = pageTurns };
        }
        else
        {
            baseWire = thread.ToWire(includeTurns);
        }

        var wire = await threadProjector.WithPlanAsync(
            threadProjector.WithRuntimeSnapshot(
                threadProjector.WithWidgetState(
                    threadProjector.WithContextUsage(threadProjector.FilterToolExecutionItemsForConnection(baseWire), thread.Id),
                    thread.Id),
                thread),
            thread.Id,
            ct);
        return new ThreadReadResult
        {
            Thread = await threadProjector.EnrichAsync(wire, thread, ct),
            TurnPage = turnPage
        };
    }

    private Task<object?> HandleItemWidgetStateSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        // widgetState is part of the Interactive Tool UI bridge; only a client that negotiated
        // interactiveToolUi has an iframe to persist it (tool-result-presentation.md §3, §9).
        if (!connection.SupportsInteractiveToolUi)
            throw AppServerErrors.MethodNotFound(AppServerMethods.ItemWidgetStateSet);
        var p = AppServerParams.Get<ItemWidgetStateSetParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.CallId))
            throw AppServerErrors.InvalidParams("'callId' is required.");

        var json = p.WidgetState?.ToJsonString();
        var cleared = string.IsNullOrEmpty(json);
        if (!cleared && Encoding.UTF8.GetByteCount(json!) > MaxWidgetStateBytes)
            throw AppServerErrors.InvalidParams($"widgetState exceeds the {MaxWidgetStateBytes}-byte limit.");

        sessionService.SetItemWidgetState(p.ThreadId, p.CallId, cleared ? null : json);
        return Task.FromResult<object?>(new ItemWidgetStateSetResult { Cleared = cleared });
    }

    private async Task<object?> HandleThreadGoalGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalGet);

        var p = AppServerParams.Get<ThreadGoalGetParams>(msg);
        var goal = await sessionService.GetThreadGoalAsync(p.ThreadId, ct);
        return new ThreadGoalGetResult { Goal = goal is null ? null : ThreadGoalWire.FromGoal(goal) };
    }

    private async Task<object?> HandleThreadGoalSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalSet);

        if (msg.Params is { ValueKind: JsonValueKind.Object } paramsElement
            && paramsElement.TryGetProperty("mode", out _))
        {
            throw AppServerErrors.InvalidParams("'mode' is not supported by thread/goal/set.");
        }

        var p = AppServerParams.Get<ThreadGoalSetParams>(msg);
        var update = new ThreadGoalUpdate
        {
            Objective = p.Objective,
            Status = ParseThreadGoalStatus(p.Status),
            HasTokenBudget = p.TokenBudget.HasValue,
            TokenBudget = ParseThreadGoalBudget(p.TokenBudget)
        };
        ThreadGoal goal;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            goal = await sessionService.SetThreadGoalAsync(p.ThreadId, update, ct: ct);
        }

        var wireGoal = ThreadGoalWire.FromGoal(goal);
        var result = new ThreadGoalSetResult { Goal = wireGoal };
        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            result,
            AppServerMethods.ThreadGoalUpdated,
            new { threadId = goal.ThreadId, goal = wireGoal, turnId = (string?)null },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadGoalClearAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(AppServerMethods.ThreadGoalClear);

        var p = AppServerParams.Get<ThreadGoalClearParams>(msg);
        ThreadGoalClearResult result;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            result = await sessionService.ClearThreadGoalAsync(p.ThreadId, ct);
        }

        var wireResult = new ThreadGoalClearResultWire { Cleared = result.Cleared };
        if (!result.Cleared)
            return wireResult;

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            wireResult,
            AppServerMethods.ThreadGoalCleared,
            new { threadId = p.ThreadId },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadCompactStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadCompactStartParams>(msg);
        var result = await sessionService.CompactThreadAsync(p.ThreadId, ct);
        return new ThreadCompactStartResponse
        {
            Outcome = result.Outcome,
            Message = result.Message,
            ContextUsage = result.ContextUsage
        };
    }

    private async Task<object?> HandleThreadMemoryConsolidateStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadMemoryConsolidateStartParams>(msg);
        var result = await sessionService.ConsolidateThreadMemoryAsync(p.ThreadId, ct);
        return new ThreadMemoryConsolidateStartResponse
        {
            Outcome = result.Outcome,
            Message = result.Message,
            MemoryWritten = result.MemoryWritten,
            HistoryWritten = result.HistoryWritten
        };
    }

    private async Task<object?> HandleThreadMaintenanceInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadMaintenanceInterruptParams>(msg);
        await sessionService.CancelThreadMaintenanceAsync(p.ThreadId, ct);
        return new { };
    }

    private async Task<object?> HandleThreadRollbackAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadRollbackParams>(msg);
        if (p.NumTurns <= 0)
            throw AppServerErrors.InvalidParams("'numTurns' must be >= 1.");

        var thread = await sessionService.RollbackThreadAsync(p.ThreadId, p.NumTurns, ct);
        return new ThreadRollbackResponse
        {
            Thread = await threadProjector.ProjectAsync(thread, true, true, ct)
        };
    }

    private async Task<object?> HandleThreadSubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadSubscribeParams>(msg);

        var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!connection.TryAddSubscription(p.ThreadId, subCts))
        {
            subCts.Dispose();
            await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
            SchedulePendingInteractiveRequestReplay(p.ThreadId);
            return null;
        }

        var events = sessionService.SubscribeThreadAsync(
            p.ThreadId,
            p.ReplayRecent ?? false,
            subCts.Token);

        var dispatcher = new AppServerEventDispatcher(
            events,
            connection,
            transport,
            sessionService,
            defaultApprovalDecision: defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger,
            enrichThreadWire: threadProjector.EnrichForNotification);
        _ = dispatcher.RunAsync(subCts.Token)
            .ContinueWith(t =>
            {
                connection.TryCancelSubscription(p.ThreadId);
                if (t.IsFaulted)
                    _ = Console.Error.WriteLineAsync(
                        $"[AppServer] Subscription error for thread {p.ThreadId}: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.ExecuteSynchronously);

        await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
        SchedulePendingInteractiveRequestReplay(p.ThreadId);
        return null;
    }

    private Task<object?> HandleThreadUnsubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<ThreadUnsubscribeParams>(msg);
        connection.TryCancelSubscription(p.ThreadId);
        return Task.FromResult<object?>(new { });
    }

    private void SchedulePendingInteractiveRequestReplay(string threadId)
    {
        Task replayTask;
        lock (_pendingInteractiveReplayLock)
        {
            if (_pendingInteractiveReplayTasks.TryGetValue(threadId, out var existingReplay)
                && !existingReplay.IsCompleted)
                return;

            replayTask = Task.Run(() => ReplayPendingInteractiveRequestsAsync(threadId));
            _pendingInteractiveReplayTasks[threadId] = replayTask;
        }

        _ = replayTask.ContinueWith(
            task =>
            {
                lock (_pendingInteractiveReplayLock)
                {
                    if (_pendingInteractiveReplayTasks.TryGetValue(threadId, out var currentReplay)
                        && ReferenceEquals(currentReplay, task))
                    {
                        _pendingInteractiveReplayTasks.Remove(threadId);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReplayPendingInteractiveRequestsAsync(string threadId)
    {
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, CancellationToken.None);
            await ReplayPendingInteractiveRequestsAsync(thread);
        }
        catch (KeyNotFoundException)
        {
            // The thread may have been deleted between subscribe/resume and replay.
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending interactive request replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private async Task ReplayPendingInteractiveRequestsAsync(SessionThread thread)
    {
        var sender = CreateInteractiveRequestSender();
        foreach (var turn in thread.Turns)
        {
            if (turn.Status == TurnStatus.WaitingApproval)
            {
                foreach (var (approvalItem, approvalRequest) in FindPendingApprovalRequests(turn).ToArray())
                {
                    await ReplayApprovalRequestAsync(sender, thread.Id, turn.Id, approvalItem.Id, approvalRequest);
                }
            }
            else if (turn.Status == TurnStatus.WaitingInput
                && TryFindPendingUserInputRequest(turn, out var userInputItem, out var userInputRequest))
            {
                await ReplayUserInputRequestAsync(sender, thread.Id, turn.Id, userInputItem.Id, userInputRequest);
            }
        }
    }

    private AppServerInteractiveRequestSender CreateInteractiveRequestSender() =>
        new(
            connection,
            transport,
            sessionService,
            defaultApprovalDecision);

    private static async Task ReplayApprovalRequestAsync(
        AppServerInteractiveRequestSender sender,
        string threadId,
        string turnId,
        string itemId,
        ApprovalRequestPayload request)
    {
        try
        {
            await sender.SendApprovalRequestAsync(threadId, turnId, itemId, request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending approval replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private static async Task ReplayUserInputRequestAsync(
        AppServerInteractiveRequestSender sender,
        string threadId,
        string turnId,
        string itemId,
        UserInputRequestPayload request)
    {
        try
        {
            await sender.SendUserInputRequestAsync(threadId, turnId, itemId, request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Pending user-input replay failed for thread {threadId}: {ex.Message}");
        }
    }

    private static IEnumerable<(SessionItem Item, ApprovalRequestPayload Request)> FindPendingApprovalRequests(
        SessionTurn turn)
    {
        foreach (var candidate in turn.Items)
        {
            if (candidate.Payload is not ApprovalRequestPayload approvalRequest)
                continue;
            if (HasApprovalResponse(turn, approvalRequest.RequestId))
                continue;

            yield return (candidate, approvalRequest);
        }
    }

    private static bool TryFindPendingUserInputRequest(
        SessionTurn turn,
        out SessionItem item,
        out UserInputRequestPayload request)
    {
        foreach (var candidate in turn.Items.AsEnumerable().Reverse())
        {
            if (candidate.Payload is not UserInputRequestPayload userInputRequest)
                continue;
            if (HasUserInputResponse(turn, userInputRequest.RequestId))
                continue;

            item = candidate;
            request = userInputRequest;
            return true;
        }

        item = null!;
        request = null!;
        return false;
    }

    private static bool HasApprovalResponse(SessionTurn turn, string requestId) =>
        turn.Items.Any(item => item.Payload is ApprovalResponsePayload response
            && string.Equals(response.RequestId, requestId, StringComparison.Ordinal));

    private static bool HasUserInputResponse(SessionTurn turn, string requestId) =>
        turn.Items.Any(item => item.Payload is UserInputResponsePayload response
            && string.Equals(response.RequestId, requestId, StringComparison.Ordinal));

    private async Task<object?> HandleThreadPauseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadPauseParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.PauseThreadAsync(p.ThreadId, ct);

        if (previousStatus == ThreadStatus.Paused)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
            return null;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Paused },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadArchiveParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.ArchiveThreadAsync(p.ThreadId, ct);

        if (previousStatus == ThreadStatus.Archived)
            return new { };

        var socialBindingCleanup = threadProjector.RevokeSocialAppBindingsForArchivedThread(thread);
        if (connection.HasSubscription(p.ThreadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
            await SendArchiveSocialBindingNotificationsAsync(socialBindingCleanup, ct);
            return null;
        }

        await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
        await transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadStatusChanged,
            @params = new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Archived }
        }, ct);
        await SendArchiveSocialBindingNotificationsAsync(socialBindingCleanup, ct);
        return null;
    }

    private async Task SendArchiveSocialBindingNotificationsAsync(
        ThreadArchiveSocialBindingCleanupResult cleanup,
        CancellationToken ct)
    {
        foreach (var change in cleanup.RevokedBindings)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = ThreadAppBindingsChanged,
                @params = new
                {
                    threadId = change.Binding.ThreadId,
                    bindingId = change.Binding.BindingId,
                    appId = change.Binding.AppId,
                    state = change.Binding.State,
                    previousState = change.PreviousState,
                    changeKind = "threadArchived"
                }
            }, ct);
        }

        foreach (var request in cleanup.CancelledRequests)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = ThreadAppBindingsChanged,
                @params = new
                {
                    threadId = request.ThreadId,
                    bindingRequestId = request.BindingRequestId,
                    appId = request.AppId,
                    state = request.State,
                    previousState = AppBindingStates.Pending,
                    changeKind = "threadArchived"
                }
            }, ct);
        }
    }

    private async Task<object?> HandleThreadUnarchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadUnarchiveParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.UnarchiveThreadAsync(p.ThreadId, ct);

        if (previousStatus == ThreadStatus.Active)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new { }, ct);
            return null;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Active },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadDeleteParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        await sessionService.DeleteThreadPermanentlyAsync(p.ThreadId, ct);
        threadProjector.RevokeAppBindingsForDeletedThread(thread);
        return new { };
    }

    private async Task<object?> HandleThreadRenameAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadRenameParams>(msg);
        if (string.IsNullOrWhiteSpace(p.DisplayName))
            throw AppServerErrors.InvalidParams("'displayName' must not be empty.");
        await sessionService.RenameThreadAsync(p.ThreadId, p.DisplayName, ct);
        return new { };
    }

    private async Task<object?> HandleThreadModeSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadModeSetParams>(msg);
        await sessionService.SetThreadModeAsync(p.ThreadId, p.Mode, ct);
        return new { };
    }

    private async Task<object?> HandleThreadConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ThreadConfigUpdateParams>(msg);
        ValidateRuntimeConfiguration(p.Config);
        await sessionService.UpdateThreadConfigurationAsync(p.ThreadId, p.Config, ct);
        return new { };
    }

    private void ValidateRuntimeConfiguration(ThreadConfiguration? config)
    {
        if (config == null)
            return;

        var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
        if (config.Reasoning != null)
        {
            AppServerRuntimeRequestValidator.ValidateReasoningForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.Reasoning);
        }

        if (config.ContextWindow != null)
        {
            AppServerRuntimeRequestValidator.ValidateContextWindowForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.ContextWindow);
        }
    }

    private static IReadOnlyList<string>? ResolveCrossChannelOriginsForThreadList(ThreadListParams p) =>
        p.CrossChannelOrigins;

    private static bool MatchesThreadListQuery(ThreadSummary summary, string query)
    {
        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);

        return Contains(summary.Id, query)
               || Contains(summary.DisplayName, query)
               || Contains(summary.OriginChannel, query)
               || Contains(summary.ChannelContext, query)
               || Contains(summary.Status.ToString(), query);
    }

    private static int NormalizePageLimit(int? limit, int defaultLimit, int maxLimit, string fieldName)
    {
        var value = limit ?? defaultLimit;
        if (value <= 0)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive integer.");
        if (value > maxLimit)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be at most {maxLimit}.");
        return value;
    }

    private static string EncodeCursor(string kind, int offset)
    {
        var raw = $"{kind}:{offset.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursorOffset(string? cursor, string expectedKind)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var prefix = expectedKind + ":";
            if (!raw.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(raw[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0)
            {
                throw AppServerErrors.InvalidParams("'cursor' is invalid.");
            }

            return offset;
        }
        catch (AppServerException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }
    }

    private static ThreadGoalStatus? ParseThreadGoalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim() switch
        {
            "active" => ThreadGoalStatus.Active,
            "paused" => ThreadGoalStatus.Paused,
            "blocked" => ThreadGoalStatus.Blocked,
            "usageLimited" => ThreadGoalStatus.UsageLimited,
            "budgetLimited" => ThreadGoalStatus.BudgetLimited,
            "complete" => ThreadGoalStatus.Complete,
            _ => throw AppServerErrors.InvalidParams("'status' must be active, paused, blocked, usageLimited, budgetLimited, or complete.")
        };
    }

    private static long? ParseThreadGoalBudget(JsonElement? value)
    {
        if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetInt64(out var budget))
            throw AppServerErrors.InvalidParams("'tokenBudget' must be a positive integer or null.");
        if (budget <= 0)
            throw AppServerErrors.InvalidParams("'tokenBudget' must be a positive integer or null.");
        return budget;
    }
}
