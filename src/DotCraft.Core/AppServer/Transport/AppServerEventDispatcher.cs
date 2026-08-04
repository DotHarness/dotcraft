using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.Logging;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;

namespace DotCraft.AppServer;

/// <summary>
/// Consumes a <see cref="SessionEvent"/> stream from <see cref="ISessionService.SubmitInputAsync"/>
/// or <see cref="ISessionService.SubscribeThreadAsync"/> and fans each event out as a JSON-RPC
/// notification to the connected client.
///
/// The approval flow is handled inline: when an <see cref="SessionEventType.ApprovalRequested"/>
/// event arrives, the dispatcher sends an <c>item/approval/request</c> JSON-RPC request to the
/// client, awaits the response, then calls <see cref="ISessionService.ResolveApprovalAsync"/>.
/// User-input requests are sent on a background path so the event stream can keep draining
/// while the user takes time to answer.
/// </summary>
public sealed class AppServerEventDispatcher
{
    private readonly IAsyncEnumerable<SessionEvent> _events;
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly SessionStreamDebugLogger? _streamDebugLogger;
    private readonly AppServerInteractiveRequestSender _interactiveRequests;
    private readonly IThreadToolSnapshotService? _toolSnapshots;
    private readonly IThreadMcpRuntimeService? _mcpRuntime;
    private bool _transportUnavailable;

    /// <summary>
    /// Called when the first <see cref="SessionEventType.TurnStarted"/> event arrives.
    /// The dispatcher awaits this delegate before sending the <c>turn/started</c> notification,
    /// so the caller can send the <c>turn/start</c> response first and guarantee correct ordering.
    /// </summary>
    private readonly Func<SessionWireTurn, Task>? _onTurnStarted;

    /// <summary>
    /// Optional enricher applied to full thread wires before they are sent as <c>thread/started</c>
    /// (ThreadCreated) and <c>thread/resumed</c> notifications, so server-originated threads (e.g.
    /// created by a managed runtime such as Teams) carry the same App Binding / origin-app attribution
    /// as <c>thread/list</c>. Without it, event-delivered threads fall back to the generic channel icon.
    /// </summary>
    private readonly Func<SessionWireThread, SessionWireThread>? _enrichThreadWire;

    /// <param name="events">Event stream to consume.</param>
    /// <param name="connection">Connection state for opt-out filtering and capability checks.</param>
    /// <param name="transport">Transport for sending notifications and approval requests.</param>
    /// <param name="sessionService">Session service for resolving approvals.</param>
    /// <param name="onTurnStarted">
    /// Optional async callback invoked (and awaited) when the first <c>TurnStarted</c> event
    /// is received. The dispatcher waits for this to complete before sending the notification,
    /// ensuring the <c>turn/start</c> response reaches the client before <c>turn/started</c>.
    /// </param>
    /// <param name="defaultApprovalDecision">
    /// The fallback decision to apply when non-interactive approval resolution cannot be
    /// determined from the thread's approval policy.
    /// Defaults to <see cref="SessionApprovalDecision.Reject"/>.
    /// </param>
    public AppServerEventDispatcher(
        IAsyncEnumerable<SessionEvent> events,
        AppServerConnection connection,
        IAppServerTransport transport,
        ISessionService sessionService,
        Func<SessionWireTurn, Task>? onTurnStarted = null,
        SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.Reject,
        SessionStreamDebugLogger? streamDebugLogger = null,
        Func<SessionWireThread, SessionWireThread>? enrichThreadWire = null)
    {
        _events = events;
        _connection = connection;
        _transport = transport;
        _onTurnStarted = onTurnStarted;
        _streamDebugLogger = streamDebugLogger;
        _enrichThreadWire = enrichThreadWire;
        _toolSnapshots = sessionService as IThreadToolSnapshotService;
        _mcpRuntime = sessionService as IThreadMcpRuntimeService;
        _interactiveRequests = new AppServerInteractiveRequestSender(
            connection,
            transport,
            sessionService,
            defaultApprovalDecision,
            () => _transportUnavailable,
            MarkTransportUnavailable);
    }

    /// <summary>
    /// Starts iterating the event stream and dispatches events as JSON-RPC notifications.
    /// Runs until the stream completes or the cancellation token is signalled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        await foreach (var evt in _events.WithCancellation(ct))
        {
            await DispatchEventAsync(evt, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Event dispatch
    // -------------------------------------------------------------------------

    private async Task DispatchEventAsync(SessionEvent evt, CancellationToken ct)
    {
        // Fix 7: Do not send any server-initiated notifications until the client has
        // sent the `initialized` notification signalling readiness. The TurnStarted
        // case is exempt because it also signals the turn/start response callback.
        var method = evt.ToWireMethodName();

        if (evt.ItemPayload?.Type == ItemType.ToolExecution && !_connection.SupportsToolExecutionLifecycle)
            return;

        switch (evt.EventType)
        {
            case SessionEventType.TurnStarted:
                // Give the turn/start handler a chance to send its response first,
                // guaranteeing the response arrives before the notification.
                if (_onTurnStarted != null && evt.TurnPayload is { } turn)
                    await InvokeOnTurnStartedAsync(turn.ToWire(includeItems: false) with { Items = [] }, ct);

                // Only send the turn/started notification when client is ready
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.ApprovalRequested:
                await HandleApprovalRequestedAsync(evt, ct);
                break;

            case SessionEventType.UserInputRequested:
                await HandleUserInputRequestedAsync(evt, ct);
                break;

            case SessionEventType.ItemDelta:
                LogOutboundDelta(evt, method);
                // Fix 4: Suppress delta notifications when client declared streamingSupport = false.
                // Also skip empty deltas — the LLM emits empty string chunks at stream boundaries.
                if (_connection.IsClientReady
                    && !_transportUnavailable
                    && _connection.SupportsStreaming
                    && !IsEmptyDelta(evt)
                    && !ShouldSuppressTerminalMirror(evt)
                    && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.TurnCompleted:
                LogTurnCompletedSnapshot(evt);
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, await BuildTerminalTurnParamsAsync(evt, ct), ct);
                break;

            case SessionEventType.TurnFailed:
            case SessionEventType.TurnCancelled:
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, await BuildTerminalTurnParamsAsync(evt, ct), ct);
                break;

            default:
                if (CanSendToClient(method))
                {
                    var parameters = evt.EventType == SessionEventType.ItemCompleted
                        ? await BuildItemCompletedParamsAsync(evt, ct).ConfigureAwait(false)
                        : BuildParams(evt);
                    await SendNotificationAsync(method, parameters, ct);
                }
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Notification params builder
    //
    // Each notification method has a specific params shape per the wire spec.
    // This method maps SessionEvent → the correct params object.
    // -------------------------------------------------------------------------

    private SessionWireThread? EnrichThreadWire(SessionWireThread? wire) =>
        wire is null || _enrichThreadWire is null ? wire : _enrichThreadWire(wire);

    private async ValueTask<SessionWireItem?> ToLiveItemWireAsync(SessionEvent evt, CancellationToken cancellationToken)
        => await ProjectCompletedItemAsync(
            evt,
            _connection.SupportsMcpApps,
            _toolSnapshots,
            _mcpRuntime,
            cancellationToken).ConfigureAwait(false);

    internal static async ValueTask<SessionWireItem?> ProjectCompletedItemAsync(
        SessionEvent evt,
        bool supportsMcpApps,
        IThreadToolSnapshotService? toolSnapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        CancellationToken cancellationToken)
    {
        var item = evt.ItemPayload;
        var wire = item?.ToWire();
        if (evt.EventType != SessionEventType.ItemCompleted
            || evt.TurnId is not { Length: > 0 } turnId
            || item is null
            || wire is null
            || !supportsMcpApps)
            return wire;

        var eligibility = await McpAppEligibilityResolver.ResolveAsync(
            evt.ThreadId,
            turnId,
            item,
            toolSnapshots,
            mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        if (eligibility is null)
            return wire;

        return wire with { McpApp = new McpAppViewHintSnapshot { Available = true } };
    }

    private async ValueTask<object> BuildItemCompletedParamsAsync(
        SessionEvent evt,
        CancellationToken cancellationToken) => new Contract.ItemNotification
    {
        ThreadId = evt.ThreadId,
        TurnId = evt.TurnId,
        Item = AppServerContractMapper.ToContract(
            await ToLiveItemWireAsync(evt, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("item/completed requires an item payload."))
    };

    private async ValueTask<object> BuildTerminalTurnParamsAsync(
        SessionEvent evt,
        CancellationToken cancellationToken)
    {
        var turn = await ProjectTerminalTurnAsync(
            evt.TurnPayload,
            _connection.SupportsToolExecutionLifecycle,
            _connection.SupportsMcpApps,
            _toolSnapshots,
            _mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        return evt.EventType switch
        {
            SessionEventType.TurnFailed => new Contract.TurnNotification
            {
                Turn = AppServerContractMapper.ToContract(
                    turn ?? throw new InvalidOperationException("turn/failed requires a turn payload.")),
                Error = evt.TurnFailedPayload?.Error
            },
            SessionEventType.TurnCancelled => new Contract.TurnNotification
            {
                Turn = AppServerContractMapper.ToContract(
                    turn ?? throw new InvalidOperationException("turn/cancelled requires a turn payload.")),
                Reason = evt.TurnCancelledPayload?.Reason
            },
            _ => new Contract.TurnNotification
            {
                Turn = AppServerContractMapper.ToContract(
                    turn ?? throw new InvalidOperationException("turn/completed requires a turn payload."))
            }
        };
    }

    internal static async ValueTask<SessionWireTurn?> ProjectTerminalTurnAsync(
        SessionTurn? turn,
        bool supportsToolExecutionLifecycle,
        bool supportsMcpApps,
        IThreadToolSnapshotService? toolSnapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        CancellationToken cancellationToken)
    {
        if (turn is null)
            return null;

        var wire = turn.ToWire(includeItems: true);
        if (!supportsToolExecutionLifecycle)
            wire.Items?.RemoveAll(item => item.Type == ItemType.ToolExecution);
        if (!supportsMcpApps || wire.Items is null)
            return wire;

        var context = await McpAppEligibilityResolver.CreateContextAsync(
            turn.ThreadId,
            toolSnapshots,
            mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
            return wire;

        var sourceItems = turn.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        for (var index = 0; index < wire.Items.Count; index++)
        {
            var itemWire = wire.Items[index];
            if (!sourceItems.TryGetValue(itemWire.Id, out var item)
                || McpAppEligibilityResolver.Resolve(turn.Id, item, context) is null)
                continue;
            wire.Items[index] = itemWire with { McpApp = new McpAppViewHintSnapshot { Available = true } };
        }
        return wire;
    }

    private object? BuildParams(SessionEvent evt) => evt.EventType switch
    {
        // Thread notifications (spec Section 6.1)
        SessionEventType.ThreadCreated => new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(
                EnrichThreadWire(evt.ThreadPayload?.ToWire())
                ?? throw new InvalidOperationException("thread/started requires a thread payload."))
        },
        SessionEventType.ThreadResumed => new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(
                EnrichThreadWire(evt.ThreadPayload?.ToWire())
                ?? throw new InvalidOperationException("thread/resumed requires a thread payload.")),
            ResumedBy = evt.ResumedPayload?.ResumedBy
        },
        SessionEventType.ThreadStatusChanged => new Contract.ThreadStatusChangedNotification
        {
            ThreadId = evt.ThreadId,
            PreviousStatus = evt.StatusChangedPayload?.PreviousStatus is { } previousStatus
                ? WireString(previousStatus)
                : default,
            NewStatus = evt.StatusChangedPayload?.NewStatus is { } newStatus
                ? WireString(newStatus)
                : default
        },
        SessionEventType.ThreadQueueUpdated when evt.ThreadQueueUpdatedPayload is { } queue => new Contract.ThreadQueueUpdatedNotification
        {
            ThreadId = queue.ThreadId,
            QueuedInputs = DotCraft.Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queue.QueuedInputs))
        },

        // Turn notifications (spec Section 6.2)
        SessionEventType.TurnStarted => new Contract.TurnNotification
        {
            Turn = AppServerContractMapper.ToContract(
                ToWireTurnForConnection(evt.TurnPayload)
                ?? throw new InvalidOperationException("turn/started requires a turn payload."))
        },
        SessionEventType.TurnCompleted => new Contract.TurnNotification
        {
            Turn = AppServerContractMapper.ToContract(
                ToWireTurnForConnection(evt.TurnPayload)
                ?? throw new InvalidOperationException("turn/completed requires a turn payload."))
        },
        SessionEventType.TurnFailed => new Contract.TurnNotification
        {
            Turn = AppServerContractMapper.ToContract(
                ToWireTurnForConnection(evt.TurnPayload)
                ?? throw new InvalidOperationException("turn/failed requires a turn payload.")),
            Error = evt.TurnFailedPayload?.Error
        },
        SessionEventType.TurnCancelled => new Contract.TurnNotification
        {
            Turn = AppServerContractMapper.ToContract(
                ToWireTurnForConnection(evt.TurnPayload)
                ?? throw new InvalidOperationException("turn/cancelled requires a turn payload.")),
            Reason = evt.TurnCancelledPayload?.Reason
        },

        // Item notifications (spec Section 6.3)
        SessionEventType.ItemStarted => new Contract.ItemNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = AppServerContractMapper.ToContract(
                evt.ItemPayload?.ToWire()
                ?? throw new InvalidOperationException("item/started requires an item payload."))
        },
        // Fix 1: Include deltaKind so clients can distinguish agentMessage from reasoningContent
        // without inspecting surrounding state (spec Section 2.3).
        SessionEventType.ItemDelta when evt.DeltaPayload is { } delta => new Contract.ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = delta.DeltaKind,
            Delta = delta.TextDelta
        },
        SessionEventType.ItemDelta when evt.CommandExecutionDeltaPayload is { } commandDelta => new Contract.ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            Delta = commandDelta.TextDelta
        },
        SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning => new Contract.ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = reasoning.DeltaKind,
            Delta = reasoning.TextDelta
        },
        SessionEventType.ItemDelta when evt.ToolCallArgumentsDeltaPayload is { } toolCallDelta => new Contract.ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = toolCallDelta.DeltaKind,
            ToolName = toolCallDelta.ToolName,
            CallId = toolCallDelta.CallId,
            Delta = toolCallDelta.Delta
        },
        SessionEventType.ItemCompleted => new Contract.ItemNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = AppServerContractMapper.ToContract(
                evt.ItemPayload?.ToWire()
                ?? throw new InvalidOperationException("item/completed requires an item payload."))
        },

        // Approval resolved notification (spec Section 6.4)
        SessionEventType.ApprovalResolved => new Contract.ItemNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = AppServerContractMapper.ToContract(
                evt.ItemPayload?.ToWire()
                ?? throw new InvalidOperationException("item/approval/resolved requires an item payload."))
        },

        // Model question resolved notification.
        SessionEventType.UserInputResolved => new Contract.ItemNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = AppServerContractMapper.ToContract(
                evt.ItemPayload?.ToWire()
                ?? throw new InvalidOperationException("item/tool/requestUserInput/resolved requires an item payload."))
        },

        // SubAgent progress notification (spec Section 6.5)
        SessionEventType.SubAgentProgress when evt.SubAgentProgressPayload is { } progress => new Contract.SubAgentProgressNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = OmitIfNull(evt.TurnId),
            Entries = DotCraft.Protocol.Optional<IReadOnlyList<Contract.SubAgentProgressEntry>>.FromValue(
                progress.Entries.Select(ToContract).ToArray())
        },

        // Usage delta notification (spec Section 6.6)
        SessionEventType.UsageDelta when evt.UsageDeltaPayload is { } usage => new Contract.UsageDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = OmitIfNull(evt.TurnId),
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheWriteInputTokens = usage.CacheWriteInputTokens,
            FreshInputTokens = usage.FreshInputTokens,
            ReasoningOutputTokens = usage.ReasoningOutputTokens,
            LlmCallDelta = usage.LlmCallDelta,
            TotalInputTokens = OmitIfNull(usage.TotalInputTokens),
            TotalOutputTokens = OmitIfNull(usage.TotalOutputTokens),
            ContextInputTokens = OmitIfNull(usage.ContextInputTokens),
            TurnInputTokens = OmitIfNull(usage.TurnInputTokens),
            TurnOutputTokens = OmitIfNull(usage.TurnOutputTokens),
            TurnLlmCalls = OmitIfNull(usage.TurnLlmCalls),
            ContextUsage = usage.ContextUsage is null
                ? default
                : DotCraft.Protocol.Optional<Contract.ContextUsageSnapshot?>.FromValue(
                    ThreadContractMapper.ToContract(usage.ContextUsage))
        },

        // System event notification (spec Section 6.7)
        SessionEventType.SystemEvent when evt.SystemEventPayload is { } sysEvt => new Contract.SystemEventNotification
        {
            ThreadId = OmitIfNull(evt.ThreadId),
            TurnId = OmitIfNull(evt.TurnId),
            Kind = sysEvt.Kind,
            MessageKey = OmitIfNull(sysEvt.MessageKey),
            Params = sysEvt.Params is null
                ? default
                : DotCraft.Protocol.Optional<IReadOnlyDictionary<string, JsonElement>?>.FromValue(
                    sysEvt.Params.ToDictionary(
                        static pair => pair.Key,
                        static pair => JsonSerializer.SerializeToElement(
                            pair.Value,
                            pair.Value?.GetType() ?? typeof(object),
                            SessionWireJsonOptions.Default),
                        StringComparer.Ordinal)),
            FallbackText = OmitIfNull(sysEvt.FallbackText),
            Message = OmitIfNull(sysEvt.Message),
            PercentLeft = OmitIfNull(sysEvt.PercentLeft),
            TokenCount = OmitIfNull(sysEvt.TokenCount),
            ContextUsage = sysEvt.ContextUsage is null
                ? default
                : DotCraft.Protocol.Optional<Contract.ContextUsageSnapshot?>.FromValue(
                    ThreadContractMapper.ToContract(sysEvt.ContextUsage))
        },

        _ => null
    };

    private SessionWireTurn? ToWireTurnForConnection(SessionTurn? turn)
    {
        if (turn is null)
            return null;

        var wire = turn.ToWire(includeItems: true);
        if (_connection.SupportsToolExecutionLifecycle || wire.Items is null)
            return wire;

        wire.Items.RemoveAll(item => item.Type == ItemType.ToolExecution);
        return wire;
    }

    private static Contract.SubAgentProgressEntry ToContract(SubAgentProgressEntry value) => new()
    {
        Label = value.Label,
        CurrentTool = OmitIfNull(value.CurrentTool),
        CurrentToolDisplay = OmitIfNull(value.CurrentToolDisplay),
        InputTokens = value.InputTokens,
        OutputTokens = value.OutputTokens,
        CachedInputTokens = value.CachedInputTokens,
        CacheWriteInputTokens = value.CacheWriteInputTokens,
        FreshInputTokens = value.FreshInputTokens,
        ReasoningOutputTokens = value.ReasoningOutputTokens,
        IsCompleted = value.IsCompleted
    };

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);

    // -------------------------------------------------------------------------
    // Approval flow (spec Section 7)
    // -------------------------------------------------------------------------

    private async Task HandleApprovalRequestedAsync(SessionEvent evt, CancellationToken ct)
    {
        var item = evt.ItemPayload;
        if (item?.Payload is not ApprovalRequestPayload req)
            return;

        if (evt.TurnId == null)
            return;

        await _interactiveRequests.SendApprovalRequestAsync(
            evt.ThreadId,
            evt.TurnId,
            evt.ItemId ?? string.Empty,
            req,
            ct);
    }

    // -------------------------------------------------------------------------
    // Model-initiated user input request flow
    // -------------------------------------------------------------------------

    private Task HandleUserInputRequestedAsync(SessionEvent evt, CancellationToken ct)
    {
        var item = evt.ItemPayload;
        if (item?.Payload is not UserInputRequestPayload req)
            return Task.CompletedTask;

        if (evt.TurnId == null)
            return Task.CompletedTask;

        _ = _interactiveRequests.SendUserInputRequestAsync(
            evt.ThreadId,
            evt.TurnId,
            evt.ItemId ?? string.Empty,
            req,
            ct);

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Transport helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when an ItemDelta event carries an empty text chunk.
    /// The LLM streaming API emits empty strings at stream open/close boundaries;
    /// these carry no information and should not be forwarded to the client.
    /// </summary>
    private static bool IsEmptyDelta(SessionEvent evt) =>
        evt.EventType == SessionEventType.ItemDelta &&
        (evt.DeltaPayload is { } d ? string.IsNullOrEmpty(d.TextDelta)
            : evt.CommandExecutionDeltaPayload is { } c ? string.IsNullOrEmpty(c.TextDelta)
            : evt.ReasoningDeltaPayload is { } r ? string.IsNullOrEmpty(r.TextDelta)
            : evt.ToolCallArgumentsDeltaPayload is { } t && string.IsNullOrEmpty(t.Delta));

    private bool ShouldSuppressTerminalMirror(SessionEvent evt) =>
        evt.CommandExecutionDeltaPayload?.MirrorsTerminalOutput == true
        && _connection.SupportsBackgroundTerminals
        && _connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta);

    private void LogOutboundDelta(SessionEvent evt, string method)
    {
        if (_streamDebugLogger == null || !_streamDebugLogger.ShouldCapture(evt.ThreadId, evt.TurnId))
            return;

        string? deltaKind = null;
        string deltaText = string.Empty;
        if (evt.DeltaPayload is { } agentDelta)
        {
            deltaKind = agentDelta.DeltaKind;
            deltaText = agentDelta.TextDelta;
        }
        else if (evt.ReasoningDeltaPayload is { } reasoningDelta)
        {
            deltaKind = reasoningDelta.DeltaKind;
            deltaText = reasoningDelta.TextDelta;
        }
        else if (evt.CommandExecutionDeltaPayload is { } commandDelta)
        {
            deltaKind = "commandExecution";
            deltaText = commandDelta.TextDelta;
        }
        else if (evt.ToolCallArgumentsDeltaPayload is { } toolCallDelta)
        {
            deltaKind = toolCallDelta.DeltaKind;
            deltaText = toolCallDelta.Delta;
        }

        var connectionKind = _connection.IsChannelAdapter
            ? "externalChannel"
            : _connection.HasAcpExtensions
                ? "acp"
                : "cli";

        _streamDebugLogger.Log(
            "appserver_outbound_delta",
            evt.ThreadId,
            evt.TurnId,
            new
            {
                method,
                itemId = evt.ItemId,
                deltaKind,
                deltaChars = deltaText.Length,
                deltaText = _streamDebugLogger.IncludeFullText ? deltaText : null,
                connectionKind,
                channelAdapterName = _connection.ChannelAdapterName
            });
    }

    private void LogTurnCompletedSnapshot(SessionEvent evt)
    {
        if (_streamDebugLogger == null
            || evt.TurnPayload == null
            || !_streamDebugLogger.ShouldCapture(evt.ThreadId, evt.TurnId))
            return;

        var agentMessageTexts = new List<string>();
        foreach (var item in evt.TurnPayload.Items)
        {
            if (item.Type != ItemType.AgentMessage)
                continue;
            if (item.Payload is AgentMessagePayload { Text: { } text } && text.Length > 0)
                agentMessageTexts.Add(text);
        }

        var snapshotConcatText = string.Concat(agentMessageTexts);
        _streamDebugLogger.Log(
            "appserver_turn_completed_snapshot",
            evt.ThreadId,
            evt.TurnId,
            new
            {
                agentMessageTexts = _streamDebugLogger.IncludeFullText ? agentMessageTexts : null,
                agentMessageCount = agentMessageTexts.Count,
                snapshotConcatChars = snapshotConcatText.Length,
                snapshotConcatText = _streamDebugLogger.IncludeFullText ? snapshotConcatText : null
            });
    }

    private bool CanSendToClient(string method) =>
        !_transportUnavailable
        && _connection.IsClientReady
        && _connection.ShouldSendNotification(method);

    private async Task InvokeOnTurnStartedAsync(SessionWireTurn turn, CancellationToken ct)
    {
        try
        {
            await _onTurnStarted!(turn);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MarkTransportUnavailable();
        }
        catch (Exception ex) when (IsTransportUnavailableException(ex))
        {
            MarkTransportUnavailable();
        }
    }

    private async Task SendNotificationAsync(string method, object? @params, CancellationToken ct)
    {
        if (_transportUnavailable)
            return;

        try
        {
            await SendMappedNotificationAsync(method, @params, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MarkTransportUnavailable();
        }
        catch (Exception ex) when (IsTransportUnavailableException(ex))
        {
            MarkTransportUnavailable();
        }
    }

    private Task SendMappedNotificationAsync(string method, object? parameters, CancellationToken ct) => method switch
    {
        DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadStarted,
            Require<Contract.ThreadNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResumed => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadResumed,
            Require<Contract.ThreadNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUpdated => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadUpdated,
            Require<Contract.ThreadNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadDeleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadDeleted,
            Require<Contract.ThreadDeletedNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnStarted,
            Require<Contract.TurnNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnCompleted,
            Require<Contract.TurnNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnFailed,
            Require<Contract.TurnNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnCancelled,
            Require<Contract.TurnNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ItemStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemStarted,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ItemCompleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemCompleted,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.AgentMessageDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ReasoningDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.CommandOutputDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.CommandOutputDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ToolArgumentsDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.ApprovalResolved,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        DotCraft.Protocol.AppServer.AppServerMethodNames.UserInputResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.UserInputResolved,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        _ => _transport.NotifyContractAsync(method, parameters, ct)
    };

    private static T Require<T>(string method, object? parameters) where T : class =>
        parameters as T
        ?? throw new InvalidOperationException($"Notification '{method}' received an unrelated payload type.");

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize wire enum {typeof(T).Name}.");

    private void MarkTransportUnavailable() => _transportUnavailable = true;

    internal static bool IsTransportUnavailableException(Exception ex) =>
        ex is IOException
            or WebSocketException
            or ObjectDisposedException
            or InvalidOperationException;
}
