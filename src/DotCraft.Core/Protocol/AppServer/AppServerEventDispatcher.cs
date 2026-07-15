using System.Net.WebSockets;
using DotCraft.Logging;

namespace DotCraft.Protocol.AppServer;

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
                    && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.TurnCompleted:
                LogTurnCompletedSnapshot(evt);
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
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
    {
        var item = evt.ItemPayload;
        var wire = item?.ToWire();
        if (evt.EventType != SessionEventType.ItemCompleted
            || evt.IsReplay
            || evt.TurnId is not { Length: > 0 } turnId
            || item is null
            || wire is null
            || !_connection.SupportsMcpApps)
            return wire;

        var eligibility = await McpAppLiveEligibilityResolver.ResolveAsync(
            evt.ThreadId,
            turnId,
            item,
            _toolSnapshots,
            _mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        if (eligibility is null
            || !_connection.TryRegisterMcpAppItem(evt.ThreadId, turnId, item.Id))
            return wire;

        return wire with { McpApp = new McpAppViewHintWire { Available = true } };
    }

    private async ValueTask<object> BuildItemCompletedParamsAsync(
        SessionEvent evt,
        CancellationToken cancellationToken) => new
    {
        threadId = evt.ThreadId,
        turnId = evt.TurnId,
        item = await ToLiveItemWireAsync(evt, cancellationToken).ConfigureAwait(false)
    };

    private object? BuildParams(SessionEvent evt) => evt.EventType switch
    {
        // Thread notifications (spec Section 6.1)
        SessionEventType.ThreadCreated => new
        {
            thread = EnrichThreadWire(evt.ThreadPayload?.ToWire())
        },
        SessionEventType.ThreadResumed => new
        {
            thread = EnrichThreadWire(evt.ThreadPayload?.ToWire()),
            resumedBy = evt.ResumedPayload?.ResumedBy
        },
        SessionEventType.ThreadStatusChanged => new
        {
            threadId = evt.ThreadId,
            previousStatus = evt.StatusChangedPayload?.PreviousStatus,
            newStatus = evt.StatusChangedPayload?.NewStatus
        },
        SessionEventType.ThreadQueueUpdated when evt.ThreadQueueUpdatedPayload is { } queue => new
        {
            threadId = queue.ThreadId,
            queuedInputs = queue.QueuedInputs
        },

        // Turn notifications (spec Section 6.2)
        SessionEventType.TurnStarted => new
        {
            turn = ToWireTurnForConnection(evt.TurnPayload)
        },
        SessionEventType.TurnCompleted => new
        {
            turn = ToWireTurnForConnection(evt.TurnPayload)
        },
        SessionEventType.TurnFailed => new
        {
            turn = ToWireTurnForConnection(evt.TurnPayload),
            error = evt.TurnFailedPayload?.Error
        },
        SessionEventType.TurnCancelled => new
        {
            turn = ToWireTurnForConnection(evt.TurnPayload),
            reason = evt.TurnCancelledPayload?.Reason
        },

        // Item notifications (spec Section 6.3)
        SessionEventType.ItemStarted => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },
        // Fix 1: Include deltaKind so clients can distinguish agentMessage from reasoningContent
        // without inspecting surrounding state (spec Section 2.3).
        SessionEventType.ItemDelta when evt.DeltaPayload is { } delta => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            deltaKind = delta.DeltaKind,
            delta = delta.TextDelta
        },
        SessionEventType.ItemDelta when evt.CommandExecutionDeltaPayload is { } commandDelta => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            delta = commandDelta.TextDelta
        },
        SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            deltaKind = reasoning.DeltaKind,
            delta = reasoning.TextDelta
        },
        SessionEventType.ItemDelta when evt.ToolCallArgumentsDeltaPayload is { } toolCallDelta => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            deltaKind = toolCallDelta.DeltaKind,
            toolName = toolCallDelta.ToolName,
            callId = toolCallDelta.CallId,
            delta = toolCallDelta.Delta
        },
        SessionEventType.ItemCompleted => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },

        // Approval resolved notification (spec Section 6.4)
        SessionEventType.ApprovalResolved => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },

        // Model question resolved notification.
        SessionEventType.UserInputResolved => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },

        // SubAgent progress notification (spec Section 6.5)
        SessionEventType.SubAgentProgress when evt.SubAgentProgressPayload is { } progress => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            entries = progress.Entries
        },

        // Usage delta notification (spec Section 6.6)
        SessionEventType.UsageDelta when evt.UsageDeltaPayload is { } usage => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            inputTokens = usage.InputTokens,
            outputTokens = usage.OutputTokens,
            cachedInputTokens = usage.CachedInputTokens,
            cacheWriteInputTokens = usage.CacheWriteInputTokens,
            freshInputTokens = usage.FreshInputTokens,
            reasoningOutputTokens = usage.ReasoningOutputTokens,
            llmCallDelta = usage.LlmCallDelta,
            totalInputTokens = usage.TotalInputTokens,
            totalOutputTokens = usage.TotalOutputTokens,
            contextInputTokens = usage.ContextInputTokens,
            turnInputTokens = usage.TurnInputTokens,
            turnOutputTokens = usage.TurnOutputTokens,
            turnLlmCalls = usage.TurnLlmCalls,
            contextUsage = usage.ContextUsage
        },

        // System event notification (spec Section 6.7)
        SessionEventType.SystemEvent when evt.SystemEventPayload is { } sysEvt => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            kind = sysEvt.Kind,
            messageKey = sysEvt.MessageKey,
            @params = sysEvt.Params,
            fallbackText = sysEvt.FallbackText,
            message = sysEvt.Message,
            percentLeft = sysEvt.PercentLeft,
            tokenCount = sysEvt.TokenCount,
            contextUsage = sysEvt.ContextUsage
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

        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };

        try
        {
            await _transport.WriteMessageAsync(notification, ct);
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

    private void MarkTransportUnavailable() => _transportUnavailable = true;

    internal static bool IsTransportUnavailableException(Exception ex) =>
        ex is IOException
            or WebSocketException
            or ObjectDisposedException
            or InvalidOperationException;
}
