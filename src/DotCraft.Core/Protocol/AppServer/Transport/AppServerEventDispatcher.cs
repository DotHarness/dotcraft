using System.Net.WebSockets;
using DotCraft.Logging;
using Contract = DotCraft.Protocol.Contracts.AppServer;

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

        return wire with { McpApp = new McpAppViewHintWire { Available = true } };
    }

    private async ValueTask<object> BuildItemCompletedParamsAsync(
        SessionEvent evt,
        CancellationToken cancellationToken) => new ItemCompletedNotification
    {
        ThreadId = evt.ThreadId,
        TurnId = evt.TurnId,
        Item = await ToLiveItemWireAsync(evt, cancellationToken).ConfigureAwait(false)
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
            SessionEventType.TurnFailed => new TurnFailedNotification
            {
                Turn = turn,
                Error = evt.TurnFailedPayload?.Error
            },
            SessionEventType.TurnCancelled => new TurnCancelledNotification
            {
                Turn = turn,
                Reason = evt.TurnCancelledPayload?.Reason
            },
            _ => new TurnCompletedNotification { Turn = turn }
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
            wire.Items[index] = itemWire with { McpApp = new McpAppViewHintWire { Available = true } };
        }
        return wire;
    }

    private object? BuildParams(SessionEvent evt) => evt.EventType switch
    {
        // Thread notifications (spec Section 6.1)
        SessionEventType.ThreadCreated => new ThreadStartedNotification
        {
            Thread = EnrichThreadWire(evt.ThreadPayload?.ToWire())
        },
        SessionEventType.ThreadResumed => new ThreadResumedNotification
        {
            Thread = EnrichThreadWire(evt.ThreadPayload?.ToWire()),
            ResumedBy = evt.ResumedPayload?.ResumedBy
        },
        SessionEventType.ThreadStatusChanged => new ThreadStatusChangedNotification
        {
            ThreadId = evt.ThreadId,
            PreviousStatus = evt.StatusChangedPayload?.PreviousStatus,
            NewStatus = evt.StatusChangedPayload?.NewStatus
        },
        SessionEventType.ThreadQueueUpdated when evt.ThreadQueueUpdatedPayload is { } queue => new ThreadQueueUpdatedNotification
        {
            ThreadId = queue.ThreadId,
            QueuedInputs = queue.QueuedInputs
        },

        // Turn notifications (spec Section 6.2)
        SessionEventType.TurnStarted => new TurnStartedNotification
        {
            Turn = ToWireTurnForConnection(evt.TurnPayload)
        },
        SessionEventType.TurnCompleted => new TurnCompletedNotification
        {
            Turn = ToWireTurnForConnection(evt.TurnPayload)
        },
        SessionEventType.TurnFailed => new TurnFailedNotification
        {
            Turn = ToWireTurnForConnection(evt.TurnPayload),
            Error = evt.TurnFailedPayload?.Error
        },
        SessionEventType.TurnCancelled => new TurnCancelledNotification
        {
            Turn = ToWireTurnForConnection(evt.TurnPayload),
            Reason = evt.TurnCancelledPayload?.Reason
        },

        // Item notifications (spec Section 6.3)
        SessionEventType.ItemStarted => new ItemStartedNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = evt.ItemPayload?.ToWire()
        },
        // Fix 1: Include deltaKind so clients can distinguish agentMessage from reasoningContent
        // without inspecting surrounding state (spec Section 2.3).
        SessionEventType.ItemDelta when evt.DeltaPayload is { } delta => new ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = delta.DeltaKind,
            Delta = delta.TextDelta
        },
        SessionEventType.ItemDelta when evt.CommandExecutionDeltaPayload is { } commandDelta => new ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            Delta = commandDelta.TextDelta
        },
        SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning => new ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = reasoning.DeltaKind,
            Delta = reasoning.TextDelta
        },
        SessionEventType.ItemDelta when evt.ToolCallArgumentsDeltaPayload is { } toolCallDelta => new ItemDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            DeltaKind = toolCallDelta.DeltaKind,
            ToolName = toolCallDelta.ToolName,
            CallId = toolCallDelta.CallId,
            Delta = toolCallDelta.Delta
        },
        SessionEventType.ItemCompleted => new ItemCompletedNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = evt.ItemPayload?.ToWire()
        },

        // Approval resolved notification (spec Section 6.4)
        SessionEventType.ApprovalResolved => new ApprovalResolvedNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = evt.ItemPayload?.ToWire()
        },

        // Model question resolved notification.
        SessionEventType.UserInputResolved => new UserInputResolvedNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Item = evt.ItemPayload?.ToWire()
        },

        // SubAgent progress notification (spec Section 6.5)
        SessionEventType.SubAgentProgress when evt.SubAgentProgressPayload is { } progress => new SubAgentProgressNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Entries = progress.Entries
        },

        // Usage delta notification (spec Section 6.6)
        SessionEventType.UsageDelta when evt.UsageDeltaPayload is { } usage => new UsageDeltaNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheWriteInputTokens = usage.CacheWriteInputTokens,
            FreshInputTokens = usage.FreshInputTokens,
            ReasoningOutputTokens = usage.ReasoningOutputTokens,
            LlmCallDelta = usage.LlmCallDelta,
            TotalInputTokens = usage.TotalInputTokens,
            TotalOutputTokens = usage.TotalOutputTokens,
            ContextInputTokens = usage.ContextInputTokens,
            TurnInputTokens = usage.TurnInputTokens,
            TurnOutputTokens = usage.TurnOutputTokens,
            TurnLlmCalls = usage.TurnLlmCalls,
            ContextUsage = usage.ContextUsage
        },

        // System event notification (spec Section 6.7)
        SessionEventType.SystemEvent when evt.SystemEventPayload is { } sysEvt => new SystemEventNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            Kind = sysEvt.Kind,
            MessageKey = sysEvt.MessageKey,
            Params = sysEvt.Params,
            FallbackText = sysEvt.FallbackText,
            Message = sysEvt.Message,
            PercentLeft = sysEvt.PercentLeft,
            TokenCount = sysEvt.TokenCount,
            ContextUsage = sysEvt.ContextUsage
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

    private bool ShouldSuppressTerminalMirror(SessionEvent evt) =>
        evt.CommandExecutionDeltaPayload?.MirrorsTerminalOutput == true
        && _connection.SupportsBackgroundTerminals
        && _connection.ShouldSendNotification(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TerminalOutputDelta);

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
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadStarted,
            AppServerContractMapper.ToContract(Require<ThreadStartedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadResumed => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadResumed,
            AppServerContractMapper.ToContract(Require<ThreadResumedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadUpdated => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadUpdated,
            AppServerContractMapper.ToContract(Require<ThreadUpdatedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadDeleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadDeleted,
            AppServerContractMapper.ToContract(Require<ThreadDeletedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnStarted,
            AppServerContractMapper.ToContract(Require<TurnStartedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCompleted => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnCompleted,
            AppServerContractMapper.ToContract(Require<TurnCompletedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnFailed => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnFailed,
            AppServerContractMapper.ToContract(Require<TurnFailedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCancelled => _transport.NotifyAsync(
            Contract.AppServerRpc.TurnCancelled,
            AppServerContractMapper.ToContract(Require<TurnCancelledNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ItemStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemStarted,
            AppServerContractMapper.ToContract(Require<ItemStartedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ItemCompleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemCompleted,
            AppServerContractMapper.ToContract(Require<ItemCompletedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.AgentMessageDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.AgentMessageDelta,
            AppServerContractMapper.ToContract(Require<ItemDeltaNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ReasoningDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ReasoningDelta,
            AppServerContractMapper.ToContract(Require<ItemDeltaNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.CommandOutputDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.CommandOutputDelta,
            AppServerContractMapper.ToContract(Require<ItemDeltaNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ToolArgumentsDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ToolArgumentsDelta,
            AppServerContractMapper.ToContract(Require<ItemDeltaNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ApprovalResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.ApprovalResolved,
            AppServerContractMapper.ToContract(Require<ApprovalResolvedNotification>(method, parameters)),
            ct),
        DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.UserInputResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.UserInputResolved,
            AppServerContractMapper.ToContract(Require<UserInputResolvedNotification>(method, parameters)),
            ct),
        _ => _transport.NotifyContractAsync(method, parameters, ct)
    };

    private static T Require<T>(string method, object? parameters) where T : class =>
        parameters as T
        ?? throw new InvalidOperationException($"Notification '{method}' received an unrelated payload type.");

    private void MarkTransportUnavailable() => _transportUnavailable = true;

    internal static bool IsTransportUnavailableException(Exception ex) =>
        ex is IOException
            or WebSocketException
            or ObjectDisposedException
            or InvalidOperationException;
}
