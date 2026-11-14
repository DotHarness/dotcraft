using DotCraft.Sessions.Wire;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal sealed class ThreadObservationState
{
    private readonly ThreadObservationItems _items = new();
    private readonly Dictionary<string, ThreadPendingRequest> _pending = new(StringComparer.Ordinal);
    private SessionTurn? _turn;
    private string? _diff;
    private ContextUsageSnapshot? _contextUsage;
    private IReadOnlyList<QueuedTurnInput> _queuedInputs = [];
    private DotCraft.Memory.StructuredPlan? _plan;
    public bool PlanPrepared { get; private set; }
    public bool HasContextUsage => _contextUsage is not null;

    public void SeedContextUsageIfMissing(ContextUsageSnapshot? usage) => _contextUsage ??= usage;

    public void UpdatePlan(DotCraft.Memory.StructuredPlan? plan)
    {
        _plan = CopyPlan(plan);
        PlanPrepared = true;
    }

    public void SeedPlanIfMissing(DotCraft.Memory.StructuredPlan? plan)
    {
        if (!PlanPrepared)
            UpdatePlan(plan);
    }

    private static DotCraft.Memory.StructuredPlan? CopyPlan(DotCraft.Memory.StructuredPlan? plan) =>
        plan is null ? null : new()
        {
            Title = plan.Title, Overview = plan.Overview, Content = plan.Content,
            CreatedAt = plan.CreatedAt, UpdatedAt = plan.UpdatedAt,
            Todos = plan.Todos.Select(todo => new DotCraft.Memory.PlanTodo
            {
                Id = todo.Id, Content = todo.Content, Priority = todo.Priority, Status = todo.Status
            }).ToList()
        };

    public void Initialize(SessionThread thread)
    {
        Clear();
        _queuedInputs = CopyQueue(thread.QueuedInputs);
        var turn = thread.Turns.LastOrDefault();
        if (turn is null)
            return;
        _turn = ThreadObservationItems.CopyTurnMetadata(turn);
        MergeItems(turn, initialize: true);
        UpdateWaiting();
    }

    public void Clear()
    {
        _turn = null;
        _items.Clear();
        _pending.Clear();
        _diff = null;
        _contextUsage = null;
        _queuedInputs = [];
        _plan = null;
        PlanPrepared = false;
    }

    public void Apply(SessionEvent evt)
    {
        if (evt.ThreadQueueUpdatedPayload is { } queue)
            _queuedInputs = CopyQueue(queue.QueuedInputs);
        var turnId = evt.TurnId ?? evt.TurnPayload?.Id ?? evt.ItemPayload?.TurnId;
        if (evt.SystemEventPayload?.ContextUsage is { } systemUsage
            && (turnId is null || turnId == _turn?.Id))
            _contextUsage = systemUsage;

        if (evt.EventType == SessionEventType.TurnStarted && evt.TurnPayload is { } started)
        {
            if (_turn is not null && started.Id != _turn.Id && started.StartedAt < _turn.StartedAt)
                return;
            if (_turn?.Id != started.Id)
            {
                var contextUsage = _contextUsage;
                var queuedInputs = _queuedInputs;
                var plan = _plan;
                var planPrepared = PlanPrepared;
                Clear();
                _contextUsage = contextUsage;
                _queuedInputs = queuedInputs;
                _plan = plan;
                PlanPrepared = planPrepared;
                _turn = ThreadObservationItems.CopyTurnMetadata(started);
            }
            else if (IsTerminal(_turn.Status))
            {
                return;
            }
            MergeItems(started);
            return;
        }

        if (_turn is null || turnId != _turn.Id)
            return;

        switch (evt.EventType)
        {
            case SessionEventType.TurnCompleted:
            case SessionEventType.TurnFailed:
            case SessionEventType.TurnCancelled:
                if (evt.TurnPayload is { } terminal)
                {
                    var accumulatedUsage = _turn.TokenUsage;
                    _turn = ThreadObservationItems.CopyTurnMetadata(terminal);
                    _turn.TokenUsage ??= accumulatedUsage;
                    MergeItems(terminal);
                }
                break;
            case SessionEventType.ItemStarted:
            case SessionEventType.ItemCompleted:
            case SessionEventType.ApprovalRequested:
            case SessionEventType.ApprovalResolved:
            case SessionEventType.UserInputRequested:
            case SessionEventType.UserInputResolved:
                if (evt.ItemPayload is { } item)
                {
                    _items.Upsert(item);
                    ApplyRequest(item);
                    UpdateWaiting();
                }
                break;
            case SessionEventType.ItemDelta:
                if (evt.ItemId is { } itemId)
                    _items.ApplyDelta(itemId, evt.Payload);
                break;
            case SessionEventType.TurnDiffUpdated:
                _diff = evt.TurnDiffUpdatedPayload?.Diff;
                break;
            case SessionEventType.UsageDelta:
                if (evt.UsageDeltaPayload is { } usage)
                {
                    _contextUsage = usage.ContextUsage ?? _contextUsage;
                    var previous = _turn.TokenUsage ?? new TokenUsageInfo();
                    var input = usage.TurnInputTokens ?? previous.InputTokens + usage.InputTokens;
                    var output = usage.TurnOutputTokens ?? usage.TotalOutputTokens
                        ?? previous.OutputTokens + usage.OutputTokens;
                    _turn.TokenUsage = new TokenUsageInfo
                    {
                        InputTokens = input,
                        OutputTokens = output,
                        TotalTokens = input + output,
                        CachedInputTokens = previous.CachedInputTokens + usage.CachedInputTokens,
                        CacheWriteInputTokens = previous.CacheWriteInputTokens + usage.CacheWriteInputTokens,
                        ReasoningOutputTokens = previous.ReasoningOutputTokens + usage.ReasoningOutputTokens,
                        LlmCallCount = usage.TurnLlmCalls ?? previous.LlmCallCount + usage.LlmCallDelta
                    };
                }
                break;
        }
    }

    public ThreadObservationSnapshot Capture(
        SessionThread header,
        string? maintenanceKind,
        ThreadObservationView view,
        IReadOnlyList<ToolName> summaryTools,
        ThreadObservationItemSelection? selection = null)
    {
        SessionTurn? turn = null;
        var hasMoreItems = false;
        if (_turn is not null)
        {
            turn = ThreadObservationItems.CopyTurnMetadata(_turn);
            turn.Items = _items.Capture(view, summaryTools, out hasMoreItems, selection);
            if (view != ThreadObservationView.Metadata && _turn.Input is { } input)
                turn.Input = turn.Items.FirstOrDefault(item => item.Id == input.Id);
        }

        var runtime = GetRuntimeSnapshot(header, maintenanceKind);
        var selected = turn?.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var activeItems = selection is null ? _items.CaptureActive() : [];
        selected.UnionWith(activeItems.Select(item => item.Id));
        var toolExecutionCount = _items.Items.Count(item => item.Type == ItemType.ToolExecution);
        return new ThreadObservationSnapshot
        {
            ThreadId = header.Id,
            Mode = header.Configuration?.Mode,
            Configuration = header.Configuration is null ? null : ThreadConfigurationCloner.Clone(header.Configuration),
            Runtime = runtime,
            Turn = turn,
            ActiveItems = activeItems,
            ItemCount = _items.Items.Count,
            ToolExecutionCount = toolExecutionCount,
            HasMoreItems = hasMoreItems,
            PlanTurnId = runtime.WaitingOnPlanConfirmation ? _turn?.Id : null,
            ToolArguments = _items.CaptureArguments(selected),
            ToolProgress = _items.CaptureProgress(selected),
            Diff = view == ThreadObservationView.Metadata ? null : _diff,
            ContextUsage = _contextUsage,
            QueuedInputs = CopyQueue(_queuedInputs),
            Plan = CopyPlan(_plan),
            PendingRequests = _pending.Values.ToArray()
        };
    }

    public ThreadSummaryRuntime GetRuntimeSnapshot(SessionThread header, string? maintenanceKind = null) =>
        ThreadSummaryRuntime.FromThread(new SessionThread
        {
            Id = header.Id,
            Configuration = header.Configuration,
            Turns = _turn is null ? [] : [_turn]
        }, maintenanceKind);

    private void MergeItems(SessionTurn source, bool initialize = false)
    {
        foreach (var item in source.Items)
        {
            Store(item);
            ApplyRequest(item);
        }
        if (source.Input is { } input && !source.Items.Any(item => item.Id == input.Id))
            Store(input);
        _turn!.Input = _items.Items.FirstOrDefault(item => item.Id == source.Input?.Id)
            ?? _items.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage);
        _turn.Items = _items.Items;
        if (IsTerminal(_turn.Status))
            _pending.Clear();

        void Store(SessionItem item)
        {
            if (initialize)
                _items.Seed(item);
            else
                _items.Upsert(item);
        }
    }

    private void ApplyRequest(SessionItem item)
    {
        ThreadPendingRequest? request = item.Payload switch
        {
            ApprovalRequestPayload approval => new()
            {
                RequestId = approval.RequestId, ItemId = item.Id, Type = ItemType.ApprovalRequest, IsBlocking = true
            },
            UserInputRequestPayload input => new()
            {
                RequestId = input.RequestId, ItemId = item.Id, Type = ItemType.UserInputRequest, IsBlocking = input.IsBlocking
            },
            _ => null
        };
        if (request is not null && !IsTerminal(_turn!.Status))
            _pending[request.RequestId] = request;
        else if (item.Payload is ApprovalResponsePayload approval)
            _pending.Remove(approval.RequestId);
        else if (item.Payload is UserInputResponsePayload input)
            _pending.Remove(input.RequestId);
    }

    private void UpdateWaiting()
    {
        if (_turn is null || IsTerminal(_turn.Status))
            return;
        _turn.Status = _pending.Values.Any(request => request.Type == ItemType.ApprovalRequest)
            ? TurnStatus.WaitingApproval
            : _pending.Values.Any(request => request.IsBlocking) ? TurnStatus.WaitingInput : TurnStatus.Running;
    }

    private static IReadOnlyList<QueuedTurnInput> CopyQueue(IReadOnlyList<QueuedTurnInput> inputs) =>
        System.Text.Json.JsonSerializer.Deserialize<List<QueuedTurnInput>>(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(inputs, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? [];

    private static bool IsTerminal(TurnStatus status) =>
        status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled;
}
