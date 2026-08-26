using System.Globalization;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Logging;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;
using ThreadSource = DotCraft.Sessions.ThreadSource;

namespace DotCraft.AppServer;

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
    SessionApprovalDecision defaultApprovalDecision,
    ILogger<ThreadRequestHandler>? logger) : IAppServerDomainHandler
{
    private const int ThreadListDefaultPageLimit = 50;
    private const int ThreadListMaxPageLimit = 100;
    private const int ThreadTurnsDefaultPageLimit = 20;
    private const int ThreadTurnsMaxPageLimit = 100;
    private const int ThreadItemsDefaultPageLimit = 100;
    private const int ThreadItemsMaxPageLimit = 500;
    private const int MaxWidgetStateBytes = 8 * 1024;
    private const string ThreadListCursorKind = "thread-list";
    private readonly object _pendingInteractiveReplayLock = new();
    private readonly Dictionary<string, Task> _pendingInteractiveReplayTasks = new(StringComparer.Ordinal);

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.ThreadStart, HandleThreadStartAsync);
        table.Map(Contract.AppServerRpc.ThreadFork, HandleThreadForkAsync);
        table.Map(Contract.AppServerRpc.ThreadResume, HandleThreadResumeAsync);
        table.Map(Contract.AppServerRpc.ThreadList, HandleThreadListAsync);
        table.Map(Contract.AppServerRpc.ThreadRead, HandleThreadReadAsync);
        table.Map(Contract.AppServerRpc.ThreadTurnsList, HandleThreadTurnsListAsync);
        table.Map(Contract.AppServerRpc.ThreadItemsList, HandleThreadItemsListAsync);
        table.Map(Contract.AppServerRpc.ThreadGoalGet, HandleThreadGoalGetAsync);
        table.Map(Contract.AppServerRpc.ThreadGoalSet, HandleThreadGoalSetAsync);
        table.Map(Contract.AppServerRpc.ThreadGoalClear, HandleThreadGoalClearAsync);
        table.Map(Contract.AppServerRpc.ItemWidgetStateSet, HandleItemWidgetStateSetAsync);
        table.Map(Contract.AppServerRpc.ThreadCompactStart, HandleThreadCompactStartAsync);
        table.Map(Contract.AppServerRpc.ThreadMemoryConsolidateStart, HandleThreadMemoryConsolidateStartAsync);
        table.Map(Contract.AppServerRpc.ThreadMaintenanceInterrupt, HandleThreadMaintenanceInterruptAsync);
        table.Map(Contract.AppServerRpc.ThreadRollback, HandleThreadRollbackAsync);
        table.Map(Contract.AppServerRpc.ThreadSubscribe, HandleThreadSubscribeAsync);
        table.Map(Contract.AppServerRpc.ThreadUnsubscribe, HandleThreadUnsubscribeAsync);
        table.Map(Contract.AppServerRpc.ThreadPause, HandleThreadPauseAsync);
        table.Map(Contract.AppServerRpc.ThreadArchive, HandleThreadArchiveAsync);
        table.Map(Contract.AppServerRpc.ThreadUnarchive, HandleThreadUnarchiveAsync);
        table.Map(Contract.AppServerRpc.ThreadDelete, HandleThreadDeleteAsync);
        table.Map(Contract.AppServerRpc.ThreadRename, HandleThreadRenameAsync);
        table.Map(Contract.AppServerRpc.ThreadModeSet, HandleThreadModeSetAsync);
        table.Map(Contract.AppServerRpc.ThreadConfigUpdate, HandleThreadConfigUpdateAsync);
    }

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath))
            return identity with { WorkspacePath = hostWorkspacePath };
        return identity;
    }

    private async Task<AppServerTypedResult<Contract.ThreadStartResult>> HandleThreadStartAsync(
        AppServerTypedRequest<Contract.ThreadStartParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var p = request.Params;
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(p.DynamicTools);
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(p.AdditionalContext);
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);

        var identity = NormalizeIdentityWorkspace(WorktreeContractMapper.ToDomain(p.Identity)!);
        var config = p.Config is null ? null : ThreadConfigurationContractMapper.FromContract(p.Config);
        config = ResolveAgentProfileConfig(config, msg, identity);
        config = ThreadWorkspaceResolver.Apply(
            identity.WorkspacePath,
            config,
            p.Cwd,
            p.RuntimeWorkspaceRoots);
        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;
        ValidateRuntimeConfiguration(config);

        var spawnSource = !string.IsNullOrWhiteSpace(p.SpawnedFromThreadId)
            ? ThreadSource.SpawnedFromThread(p.SpawnedFromThreadId.Trim())
            : null;

        var thread = await sessionService.CreateThreadAsync(
            identity,
            config,
            historyMode,
            displayName: p.DisplayName,
            ct: ct,
            source: spawnSource);

        await threadBinder.BindThreadRuntimeAsync(thread, dynamicTools, additionalContext, ct);

        var instructionSources = await sessionService.GetInstructionSourcesAsync(thread.Id, ct);
        var startedWire = await threadProjector.ProjectAsync(thread, false, false, ct);
        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new Contract.ThreadStartResult
            {
                Thread = AppServerContractMapper.ToContract(startedWire),
                InstructionSources = instructionSources
            },
            Contract.AppServerRpc.ThreadStarted,
            new Contract.ThreadNotification { Thread = AppServerContractMapper.ToContract(startedWire) },
            ct);

        return AppServerTypedResult<Contract.ThreadStartResult>.Written;
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
            var store = new AgentProfileStore(ResolveAgentProfileWorkspaceDataPath());
            var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
            var resolved = store.ResolveThreadStartConfiguration(config, currentConfig, TryGetConfigElement(msg));
            AppServerRuntimeRequestValidator.NormalizeCompleteModelConfiguration(currentConfig, resolved);
            return resolved;
        }
        catch (AgentProfileException ex)
        {
            if (ex.Kind == AgentProfileErrorKind.ValidationFailed)
            {
                logger?.LogWarning(
                    "Agent Profile rejected thread start. ProfileId={ProfileId} RequestedSource={RequestedSource} DiagnosticCodes={DiagnosticCodes}",
                    config?.AgentProfileId,
                    config?.AgentProfileSource,
                    string.Join(",", ex.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal)));
            }
            throw AgentProfileRequestHandler.MapError(ex);
        }
    }

    private string? ResolveAgentProfileWorkspaceDataPath() =>
        string.IsNullOrWhiteSpace(workspaceCraftPath) ? null : workspaceCraftPath;

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

    private async Task<AppServerTypedResult<Contract.ThreadForkResult>> HandleThreadForkAsync(
        AppServerTypedRequest<Contract.ThreadForkParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var p = request.Params;
        RejectRemovedFields(p.ExtensionData, "excludeTurns");
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(ValueOrDefault(p.DynamicTools));
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(ValueOrDefault(p.AdditionalContext));
        var config = ValueOrDefault(p.Config) is { } contractConfig
            ? ThreadConfigurationContractMapper.FromContract(contractConfig)
            : null;
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);
        ValidateRuntimeConfiguration(config);

        var identity = WorktreeContractMapper.ToDomain(ValueOrDefault(p.Identity));
        if (identity is not null)
            identity = NormalizeIdentityWorkspace(identity);
        var thread = await sessionService.ForkThreadAsync(
            threadId,
            new ThreadForkOptions
            {
                Path = ValueOrDefault(p.Path),
                ForkPoint = WorktreeContractMapper.ToDomain(ValueOrDefault(p.ForkPoint)),
                Identity = identity,
                Config = config,
                Cwd = ValueOrDefault(p.Cwd),
                RuntimeWorkspaceRoots = ValueOrDefault(p.RuntimeWorkspaceRoots),
                DisplayName = ValueOrDefault(p.DisplayName),
                Ephemeral = ValueOrDefault(p.Ephemeral) ?? false
            },
            ct);

        await threadBinder.BindThreadRuntimeAsync(thread, dynamicTools, additionalContext, ct);

        var instructionSources = await sessionService.GetInstructionSourcesAsync(thread.Id, ct);
        var responseWire = await threadProjector.ProjectAsync(thread, false, false, ct);
        var notificationWire = responseWire;

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new Contract.ThreadForkResult
            {
                Thread = Protocol.Optional<Contract.SessionThread>.FromValue(
                    AppServerContractMapper.ToContract(responseWire)),
                InstructionSources = instructionSources
            },
            Contract.AppServerRpc.ThreadStarted,
            new Contract.ThreadNotification
            {
                Thread = AppServerContractMapper.ToContract(notificationWire)
            },
            ct);

        return AppServerTypedResult<Contract.ThreadForkResult>.Written;
    }

    private async Task<AppServerTypedResult<Contract.ThreadResumeResult>> HandleThreadResumeAsync(
        AppServerTypedRequest<Contract.ThreadResumeParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var p = request.Params;
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(p.DynamicTools);
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(p.AdditionalContext);
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);

        var thread = await sessionService.ResumeThreadAsync(p.ThreadId, ct);
        if (p.Cwd != null || p.RuntimeWorkspaceRoots != null)
        {
            thread = await sessionService.UpdateThreadWorkspaceAsync(
                p.ThreadId,
                p.Cwd,
                p.RuntimeWorkspaceRoots,
                ct);
        }
        await threadBinder.BindThreadRuntimeAsync(thread, dynamicTools, additionalContext, ct);

        var resumedBy = connection.ClientInfo?.Name ?? "appserver";
        var instructionSources = await sessionService.GetInstructionSourcesAsync(thread.Id, ct);
        var resumedWire = await threadProjector.ProjectAsync(thread, false, false, ct);
        var responseResult = new Contract.ThreadResumeResult
        {
            Thread = AppServerContractMapper.ToContract(resumedWire),
            InstructionSources = instructionSources
        };
        var notifParams = new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(resumedWire),
            ResumedBy = resumedBy
        };

        if (connection.HasSubscription(p.ThreadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, responseResult, ct);
            SchedulePendingInteractiveRequestReplay(p.ThreadId);
            return AppServerTypedResult<Contract.ThreadResumeResult>.Written;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            responseResult,
            Contract.AppServerRpc.ThreadResumed,
            notifParams,
            ct);
        SchedulePendingInteractiveRequestReplay(p.ThreadId);
        return AppServerTypedResult<Contract.ThreadResumeResult>.Written;
    }

    private async Task<AppServerTypedResult<Contract.ThreadListResult>> HandleThreadListAsync(
        AppServerTypedRequest<Contract.ThreadListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var identity = NormalizeIdentityWorkspace(WorktreeContractMapper.ToDomain(p.Identity)!);
        var scope = ResolveThreadDiscoveryScope(p.Scope);
        var crossOrigins = scope == ThreadDiscoveryScope.Workspace
            ? null
            : ResolveCrossChannelOriginsForThreadList(p);
        var threads = await sessionService.FindThreadsAsync(
            identity,
            p.IncludeArchived ?? false,
            crossOrigins,
            ct,
            p.IncludeSubAgents ?? false,
            scope);

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

        var data = new List<Contract.ThreadSummary>();
        var catalogByWorkspace = new Dictionary<string, AppCatalogSnapshot?>(StringComparer.Ordinal);
        foreach (var summary in page)
        {
            await threadProjector.EnrichSummaryAsync(summary, catalogByWorkspace, ct);
            data.Add(ThreadContractMapper.ToContract(summary));
        }

        var result = new Contract.ThreadListResult
        {
            Data = data,
            NextCursor = nextCursor,
            TotalMatched = isPaged || !string.IsNullOrWhiteSpace(p.Query) ? totalMatched : null
        };
        return AppServerTypedResult<Contract.ThreadListResult>.FromResult(result);
    }

    private async Task<AppServerTypedResult<Contract.ThreadReadResult>> HandleThreadReadAsync(
        AppServerTypedRequest<Contract.ThreadReadParams> request,
        CancellationToken ct)
    {
        RejectRemovedFields(request.Params.ExtensionData, "includeTurns", "turnLimit", "cursor");
        var snapshot = await sessionService.ReadThreadSnapshotAsync(request.Params.ThreadId, ct);
        var thread = snapshot.Thread;
        var baseWire = thread.ToWire(includeTurns: false) with
        {
            Runtime = snapshot.PersistedRuntime.ToWireRuntimeState()
        };
        var wire = await threadProjector.WithPlanAsync(
            threadProjector.WithWidgetState(
                threadProjector.WithContextUsage(baseWire, thread.Id),
                thread.Id),
            thread.Id,
            ct);
        var result = new Contract.ThreadReadResult
        {
            Thread = AppServerContractMapper.ToContract(
                await threadProjector.EnrichAsync(wire, thread, ct))
        };
        return AppServerTypedResult<Contract.ThreadReadResult>.FromResult(result);
    }

    private async Task<AppServerTypedResult<Contract.ThreadTurnsListResult>> HandleThreadTurnsListAsync(
        AppServerTypedRequest<Contract.ThreadTurnsListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var direction = ParseHistoryDirection(p.SortDirection);
        var directionName = HistoryDirectionName(direction);
        var ordinal = ThreadHistoryCursorCodec.Decode(
            p.Cursor, p.ThreadId, "turns", null, directionName);
        ThreadHistoryPage<SessionTurn> page;
        try
        {
            page = await sessionService.ListThreadTurnsAsync(
                p.ThreadId,
                ordinal.HasValue ? new ThreadHistoryCursor(ordinal.Value) : null,
                NormalizePageLimit(p.Limit, ThreadTurnsDefaultPageLimit, ThreadTurnsMaxPageLimit, "limit"),
                direction,
                ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }
        return AppServerTypedResult<Contract.ThreadTurnsListResult>.FromResult(new()
        {
            Data = page.Data.Select(turn => AppServerContractMapper.ToContract(turn.ToWire(includeItems: false))).ToArray(),
            NextCursor = page.NextCursor is { } next
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, "turns", null, directionName, next.ExclusiveRolloutOrdinal)
                : null
        });
    }

    private async Task<AppServerTypedResult<Contract.ThreadItemsListResult>> HandleThreadItemsListAsync(
        AppServerTypedRequest<Contract.ThreadItemsListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var turnId = string.IsNullOrWhiteSpace(p.TurnId) ? null : p.TurnId;
        var scope = turnId is null ? "items" : "turn-items";
        var direction = ParseHistoryDirection(p.SortDirection);
        var directionName = HistoryDirectionName(direction);
        var ordinal = ThreadHistoryCursorCodec.Decode(
            p.Cursor, p.ThreadId, scope, turnId, directionName);
        ThreadHistoryPage<ThreadHistoryItem> page;
        try
        {
            page = await sessionService.ListThreadItemsAsync(
                p.ThreadId,
                turnId,
                ordinal.HasValue ? new ThreadHistoryCursor(ordinal.Value) : null,
                NormalizePageLimit(p.Limit, ThreadItemsDefaultPageLimit, ThreadItemsMaxPageLimit, "limit"),
                direction,
                ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }
        var snapshot = await sessionService.ReadThreadSnapshotAsync(p.ThreadId, ct);
        var projected = await threadProjector.ProjectHistoryItemsAsync(snapshot.Thread, page.Data, ct);
        return AppServerTypedResult<Contract.ThreadItemsListResult>.FromResult(new()
        {
            Data = projected.Select(entry => new Contract.ThreadItemListEntry
            {
                TurnId = entry.TurnId,
                Item = AppServerContractMapper.ToContract(entry.Item)
            }).ToArray(),
            NextCursor = page.NextCursor is { } next
                ? ThreadHistoryCursorCodec.Encode(p.ThreadId, scope, turnId, directionName, next.ExclusiveRolloutOrdinal)
                : null
        });
    }

    private Task<AppServerTypedResult<Contract.ItemWidgetStateSetResult>> HandleItemWidgetStateSetAsync(
        AppServerTypedRequest<Contract.ItemWidgetStateSetParams> request,
        CancellationToken ct)
    {
        _ = ct;
        // widgetState is part of the Interactive Tool UI bridge; only a client that negotiated
        // interactiveToolUi has an iframe to persist it (tools-architecture.md §13).
        if (!connection.SupportsInteractiveToolUi)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ItemWidgetStateSet);
        var p = request.Params;
        var threadId = p.ThreadId.IsSet ? p.ThreadId.Value : null;
        var callId = p.CallId.IsSet ? p.CallId.Value : null;
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(callId))
            throw AppServerErrors.InvalidParams("'callId' is required.");

        var json = p.WidgetState.IsSet && p.WidgetState.Value is { } widgetState
            ? widgetState.GetRawText()
            : null;
        var cleared = string.IsNullOrEmpty(json);
        if (!cleared && Encoding.UTF8.GetByteCount(json!) > MaxWidgetStateBytes)
            throw AppServerErrors.InvalidParams($"widgetState exceeds the {MaxWidgetStateBytes}-byte limit.");

        sessionService.SetItemWidgetState(threadId, callId, cleared ? null : json);
        return Task.FromResult(AppServerTypedResult<Contract.ItemWidgetStateSetResult>.FromResult(new()
        {
            Cleared = cleared
        }));
    }

    private async Task<AppServerTypedResult<Contract.ThreadGoalGetResult>> HandleThreadGoalGetAsync(
        AppServerTypedRequest<Contract.ThreadGoalGetParams> request,
        CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ThreadGoalGet);

        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var goal = await sessionService.GetThreadGoalAsync(threadId, ct);
        return AppServerTypedResult<Contract.ThreadGoalGetResult>.FromResult(new()
        {
            Goal = Protocol.Optional<Contract.ThreadGoal?>.FromValue(
                goal is null ? null : ThreadContractMapper.ToContract(ThreadGoalSnapshot.FromGoal(goal)))
        });
    }

    private async Task<AppServerTypedResult<Contract.ThreadGoalSetResult>> HandleThreadGoalSetAsync(
        AppServerTypedRequest<Contract.ThreadGoalSetParams> request,
        CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ThreadGoalSet);

        var msg = request.Message;
        if (msg.Params is { ValueKind: JsonValueKind.Object } paramsElement
            && paramsElement.TryGetProperty("mode", out _))
        {
            throw AppServerErrors.InvalidParams("'mode' is not supported by thread/goal/set.");
        }

        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var update = new ThreadGoalUpdate
        {
            Objective = ValueOrDefault(p.Objective),
            Status = ParseThreadGoalStatus(ValueOrDefault(p.Status)),
            HasTokenBudget = p.TokenBudget.IsSet,
            TokenBudget = ParseThreadGoalBudget(p.TokenBudget)
        };
        ThreadGoal goal;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            goal = await sessionService.SetThreadGoalAsync(threadId, update, ct: ct);
        }

        var wireGoal = ThreadContractMapper.ToContract(ThreadGoalSnapshot.FromGoal(goal));
        var result = new Contract.ThreadGoalSetResult { Goal = wireGoal };
        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            result,
            Contract.AppServerRpc.ThreadGoalUpdated,
            new Contract.ThreadGoalUpdatedNotification
            {
                ThreadId = goal.ThreadId,
                Goal = Protocol.Optional<Contract.ThreadGoal?>.FromValue(wireGoal)
            },
            ct);
        return AppServerTypedResult<Contract.ThreadGoalSetResult>.Written;
    }

    private async Task<AppServerTypedResult<Contract.ThreadGoalClearResult>> HandleThreadGoalClearAsync(
        AppServerTypedRequest<Contract.ThreadGoalClearParams> request,
        CancellationToken ct)
    {
        if (!threadProjector.GoalsCapabilityEnabled())
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ThreadGoalClear);

        var msg = request.Message;
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        ThreadGoalClearResult result;
        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            result = await sessionService.ClearThreadGoalAsync(threadId, ct);
        }

        var wireResult = new Contract.ThreadGoalClearResult { Cleared = result.Cleared };
        if (!result.Cleared)
            return AppServerTypedResult<Contract.ThreadGoalClearResult>.FromResult(wireResult);

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            wireResult,
            Contract.AppServerRpc.ThreadGoalCleared,
            new Contract.ThreadGoalClearedNotification { ThreadId = threadId },
            ct);
        return AppServerTypedResult<Contract.ThreadGoalClearResult>.Written;
    }

    private async Task<AppServerTypedResult<Contract.ThreadCompactStartResponse>> HandleThreadCompactStartAsync(
        AppServerTypedRequest<Contract.ThreadCompactStartParams> request,
        CancellationToken ct)
    {
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var result = await sessionService.CompactThreadAsync(threadId, ct);
        return AppServerTypedResult<Contract.ThreadCompactStartResponse>.FromResult(new()
        {
            Outcome = result.Outcome,
            Message = OmitIfNull(result.Message),
            ContextUsage = result.ContextUsage is null
                ? default
                : Protocol.Optional<Contract.ContextUsageSnapshot?>.FromValue(
                    ThreadContractMapper.ToContract(result.ContextUsage))
        });
    }

    private async Task<AppServerTypedResult<Contract.ThreadMemoryConsolidateStartResponse>> HandleThreadMemoryConsolidateStartAsync(
        AppServerTypedRequest<Contract.ThreadMemoryConsolidateStartParams> request,
        CancellationToken ct)
    {
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var result = await sessionService.ConsolidateThreadMemoryAsync(threadId, ct);
        return AppServerTypedResult<Contract.ThreadMemoryConsolidateStartResponse>.FromResult(new()
        {
            Outcome = result.Outcome,
            Message = OmitIfNull(result.Message),
            MemoryWritten = result.MemoryWritten,
            HistoryWritten = result.HistoryWritten
        });
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadMaintenanceInterruptAsync(
        AppServerTypedRequest<Contract.ThreadMaintenanceInterruptParams> request,
        CancellationToken ct)
    {
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        await sessionService.CancelThreadMaintenanceAsync(threadId, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<AppServerTypedResult<Contract.ThreadRollbackResponse>> HandleThreadRollbackAsync(
        AppServerTypedRequest<Contract.ThreadRollbackParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var numTurns = Require(p.NumTurns, "'numTurns' is required.");
        if (numTurns <= 0)
            throw AppServerErrors.InvalidParams("'numTurns' must be >= 1.");

        var thread = await sessionService.RollbackThreadAsync(threadId, numTurns, ct);
        connection.RevokeMcpAppThreadEligibility(threadId);
        return AppServerTypedResult<Contract.ThreadRollbackResponse>.FromResult(new()
        {
            Thread = Protocol.Optional<Contract.SessionThread>.FromValue(
                AppServerContractMapper.ToContract(
                    await threadProjector.ProjectAsync(thread, false, true, ct)))
        });
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadSubscribeAsync(
        AppServerTypedRequest<Contract.ThreadSubscribeParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");

        var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!connection.TryAddSubscription(threadId, subCts))
        {
            subCts.Dispose();
            await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
            SchedulePendingInteractiveRequestReplay(threadId);
            return AppServerTypedResult<Protocol.RpcEmpty>.Written;
        }

        var events = sessionService.SubscribeThreadAsync(
            threadId,
            ValueOrDefault(p.ReplayRecent) ?? false,
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
                connection.TryCancelSubscription(threadId);
                if (t.IsFaulted)
                    logger?.LogError(
                        t.Exception?.GetBaseException(),
                        "AppServer subscription failed for thread {ThreadId}",
                        threadId);
            }, TaskContinuationOptions.ExecuteSynchronously);

        await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
        SchedulePendingInteractiveRequestReplay(threadId);
        return AppServerTypedResult<Protocol.RpcEmpty>.Written;
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadUnsubscribeAsync(
        AppServerTypedRequest<Contract.ThreadUnsubscribeParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        connection.TryCancelSubscription(threadId);
        return Task.FromResult(
            AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new()));
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
            logger?.LogError(ex, "Pending interactive request replay failed for thread {ThreadId}", threadId);
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

    private async Task ReplayApprovalRequestAsync(
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
            logger?.LogError(ex, "Pending approval replay failed for thread {ThreadId}", threadId);
        }
    }

    private async Task ReplayUserInputRequestAsync(
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
            logger?.LogError(ex, "Pending user-input replay failed for thread {ThreadId}", threadId);
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

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadPauseAsync(
        AppServerTypedRequest<Contract.ThreadPauseParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var previousStatus = thread.Status;

        await sessionService.PauseThreadAsync(threadId, ct);

        if (previousStatus == ThreadStatus.Paused)
            return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());

        if (connection.HasSubscription(threadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
            return AppServerTypedResult<Protocol.RpcEmpty>.Written;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new Protocol.RpcEmpty(),
            Contract.AppServerRpc.ThreadStatusChanged,
            new Contract.ThreadStatusChangedNotification
            {
                ThreadId = threadId,
                PreviousStatus = WireString(previousStatus),
                NewStatus = WireString(ThreadStatus.Paused)
            },
            ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.Written;
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadArchiveAsync(
        AppServerTypedRequest<Contract.ThreadArchiveParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var previousStatus = thread.Status;

        await sessionService.ArchiveThreadAsync(threadId, ct);

        if (previousStatus == ThreadStatus.Archived)
            return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());

        var socialBindingCleanup = threadProjector.RevokeSocialAppBindingsForArchivedThread(thread);
        if (connection.HasSubscription(threadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
            await SendArchiveSocialBindingNotificationsAsync(socialBindingCleanup, ct);
            return AppServerTypedResult<Protocol.RpcEmpty>.Written;
        }

        await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
        await transport.NotifyContractAsync(
            Contract.AppServerRpc.ThreadStatusChanged,
            new Contract.ThreadStatusChangedNotification
            {
                ThreadId = threadId,
                PreviousStatus = WireString(previousStatus),
                NewStatus = WireString(ThreadStatus.Archived)
            },
            ct);
        await SendArchiveSocialBindingNotificationsAsync(socialBindingCleanup, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.Written;
    }

    private async Task SendArchiveSocialBindingNotificationsAsync(
        IReadOnlyList<AppBindingSnapshot> revokedBindings,
        CancellationToken ct)
    {
        foreach (var binding in revokedBindings)
        {
            await transport.NotifyContractAsync(
                Contract.AppServerRpc.ThreadAppBindingsChanged,
                new Contract.ThreadAppBindingsChangedNotification
                {
                    ThreadId = binding.ThreadId,
                    BindingId = binding.BindingId,
                    AppId = binding.AppId,
                    State = binding.State,
                    PreviousState = AppBindingStates.Active,
                    ChangeKind = "threadArchived"
                },
                ct);
        }

    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadUnarchiveAsync(
        AppServerTypedRequest<Contract.ThreadUnarchiveParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var previousStatus = thread.Status;

        await sessionService.UnarchiveThreadAsync(threadId, ct);

        if (previousStatus == ThreadStatus.Active)
            return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());

        if (connection.HasSubscription(threadId))
        {
            await responseWriter.WriteResponseAsync(msg.Id, new Protocol.RpcEmpty(), ct);
            return AppServerTypedResult<Protocol.RpcEmpty>.Written;
        }

        await responseWriter.SendNotificationAfterResponseAsync(
            msg.Id,
            new Protocol.RpcEmpty(),
            Contract.AppServerRpc.ThreadStatusChanged,
            new Contract.ThreadStatusChangedNotification
            {
                ThreadId = threadId,
                PreviousStatus = WireString(previousStatus),
                NewStatus = WireString(ThreadStatus.Active)
            },
            ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.Written;
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadDeleteAsync(
        AppServerTypedRequest<Contract.ThreadDeleteParams> request,
        CancellationToken ct)
    {
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        await sessionService.DeleteThreadPermanentlyAsync(threadId, ct);
        threadProjector.RevokeAppBindingsForDeletedThread(thread);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadRenameAsync(
        AppServerTypedRequest<Contract.ThreadRenameParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var displayName = Require(p.DisplayName, "'displayName' is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw AppServerErrors.InvalidParams("'displayName' must not be empty.");
        await sessionService.RenameThreadAsync(threadId, displayName, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadModeSetAsync(
        AppServerTypedRequest<Contract.ThreadModeSetParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        await sessionService.SetThreadModeAsync(
            Require(p.ThreadId, "'threadId' is required."),
            Require(p.Mode, "'mode' is required."),
            ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleThreadConfigUpdateAsync(
        AppServerTypedRequest<Contract.ThreadConfigUpdateParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var contractConfig = Require(p.Config, "'config' is required.");
        var config = ThreadConfigurationContractMapper.FromContract(contractConfig);
        ValidateRuntimeConfiguration(config);
        await sessionService.UpdateThreadConfigurationAsync(threadId, config, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
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

        if (config.ApprovalTimeoutSeconds is < 1 or > 86400)
            throw AppServerErrors.InvalidParams("'config.approvalTimeoutSeconds' must be between 1 and 86400.");
    }

    private static IReadOnlyList<string>? ResolveCrossChannelOriginsForThreadList(Contract.ThreadListParams p) =>
        p.CrossChannelOrigins;

    private static ThreadDiscoveryScope ResolveThreadDiscoveryScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)
            || string.Equals(scope, "identity", StringComparison.OrdinalIgnoreCase))
        {
            return ThreadDiscoveryScope.Identity;
        }

        if (string.Equals(scope, "workspace", StringComparison.OrdinalIgnoreCase))
            return ThreadDiscoveryScope.Workspace;

        throw new ArgumentException(
            "scope must be 'identity' or 'workspace'.",
            nameof(scope));
    }

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

    private static void RejectRemovedFields(
        IDictionary<string, JsonElement>? extensionData,
        params string[] removedFields)
    {
        if (extensionData is null)
            return;
        foreach (var field in removedFields)
        {
            if (extensionData.ContainsKey(field))
                throw AppServerErrors.InvalidParams($"'{field}' is no longer supported.");
        }
    }

    private static ThreadHistorySortDirection ParseHistoryDirection(string? value) => value switch
    {
        null or "descending" => ThreadHistorySortDirection.Descending,
        "ascending" => ThreadHistorySortDirection.Ascending,
        _ => throw AppServerErrors.InvalidParams("'sortDirection' must be 'ascending' or 'descending'.")
    };

    private static string HistoryDirectionName(ThreadHistorySortDirection direction) =>
        direction == ThreadHistorySortDirection.Ascending ? "ascending" : "descending";

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

    private static long? ParseThreadGoalBudget(
        Protocol.Optional<long?> value)
    {
        if (!value.IsSet || value.Value is null)
            return null;
        var budget = value.Value.Value;
        if (budget <= 0)
            throw AppServerErrors.InvalidParams("'tokenBudget' must be a positive integer or null.");
        return budget;
    }

    private static T Require<T>(Protocol.Optional<T> value, string message)
    {
        if (!value.IsSet || value.Value is null)
            throw AppServerErrors.InvalidParams(message);
        return value.Value;
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize wire enum {typeof(T).Name}.");
}
