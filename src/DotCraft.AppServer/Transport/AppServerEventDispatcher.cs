using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.Logging;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;

namespace DotCraft.AppServer;

/// <summary>Interactive requests run independently so a pending answer does not block event delivery.</summary>
public sealed class AppServerEventDispatcher
{
    private readonly IAsyncEnumerable<SessionEvent> _events;
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly SessionStreamDebugLogger? _streamDebugLogger;
    private readonly AppServerInteractiveRequestSender _interactiveRequests;
    private bool _transportUnavailable;


    /// <summary>
    /// Optional enricher applied to full thread wires before they are sent as <c>thread/started</c>
    /// (ThreadCreated) and <c>thread/resumed</c> notifications, so server-originated threads (e.g.
    /// created by a managed runtime) carry the same App Binding / origin-app attribution
    /// as <c>thread/list</c>. Without it, event-delivered threads fall back to the generic channel icon.
    /// </summary>
    private readonly Func<SessionWireThread, SessionWireThread>? _enrichThreadWire;

    /// <param name="events">Event stream to consume.</param>
    /// <param name="connection">Connection state for opt-out filtering and capability checks.</param>
    /// <param name="transport">Transport for sending notifications and approval requests.</param>
    /// <param name="sessionService">Session service for resolving approvals.</param>
    public AppServerEventDispatcher(
        IAsyncEnumerable<SessionEvent> events,
        AppServerConnection connection,
        IAppServerTransport transport,
        ISessionService sessionService,
        SessionStreamDebugLogger? streamDebugLogger = null,
        Func<SessionWireThread, SessionWireThread>? enrichThreadWire = null)
    {
        _events = events;
        _connection = connection;
        _transport = transport;
        _streamDebugLogger = streamDebugLogger;
        _enrichThreadWire = enrichThreadWire;
        _interactiveRequests = new AppServerInteractiveRequestSender(
            connection,
            transport,
            sessionService,
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
        _interactiveRequests.ObserveResolution(evt);

        var method = evt.ToWireMethodName();

        if (evt.ItemPayload?.Type == ItemType.ToolExecution && !_connection.SupportsToolExecutionLifecycle)
            return;
        if (evt.ToolExecutionProgressPayload is not null && !_connection.SupportsToolExecutionLifecycle)
            return;
        if (evt.TerminalInteractionPayload is not null && !_connection.SupportsCommandExecutionStreaming)
            return;

        if (evt.RuntimeSnapshot is { } runtime && CanSendToClient(Contract.AppServerRpc.ThreadStatusChanged.Name))
            await SendNotificationAsync(Contract.AppServerRpc.ThreadStatusChanged.Name, new Contract.ThreadStatusChangedNotification
            {
                ThreadId = evt.ThreadId,
                Runtime = AppServerContractMapper.ToContract(runtime.ToWireRuntimeState())
            }, ct);

        switch (evt.EventType)
        {
            case SessionEventType.TurnStarted:
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.ApprovalRequested:
                if (evt.TurnId != null && evt.ItemPayload?.Payload is ApprovalRequestPayload approval)
                    _ = _interactiveRequests.SendApprovalRequestAsync(evt.ThreadId, evt.TurnId, evt.ItemId ?? string.Empty, approval);
                break;

            case SessionEventType.UserInputRequested:
                if (evt.TurnId != null && evt.ItemPayload?.Payload is UserInputRequestPayload userInput)
                    _ = _interactiveRequests.SendUserInputRequestAsync(evt.ThreadId, evt.TurnId, evt.ItemId ?? string.Empty, userInput);
                break;

            case SessionEventType.ItemDelta:
                LogOutboundDelta(evt, method);
                // Fix 4: Suppress delta notifications when client declared streamingSupport = false.
                // Also skip empty deltas — the LLM emits empty string chunks at stream boundaries.
                if (_connection.IsClientReady
                    && !_transportUnavailable
                    && (_connection.SupportsStreaming || evt.ToolExecutionProgressPayload is not null || evt.TerminalInteractionPayload is not null)
                    && !IsEmptyDelta(evt)
                    && !ShouldSuppressTerminalMirror(evt)
                    && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.TurnCompleted:
                LogTurnCompletedSnapshot(evt);
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, BuildTerminalTurnParams(evt), ct);
                break;

            case SessionEventType.TurnFailed:
            case SessionEventType.TurnCancelled:
                if (CanSendToClient(method))
                    await SendNotificationAsync(method, BuildTerminalTurnParams(evt), ct);
                break;

            case SessionEventType.ThreadStatusChanged:
                if (evt.StatusChangedPayload is { } status)
                {
                    var lifecycle = ThreadLifecycleNotification.For(evt.ThreadId, status.NewStatus);
                    if (CanSendToClient(lifecycle.Method))
                        await SendNotificationAsync(lifecycle.Method, lifecycle.Params, ct);
                }
                break;

            default:
                if (CanSendToClient(method))
                {
                    var parameters = evt.EventType == SessionEventType.ItemCompleted
                        ? BuildItemCompletedParams(evt)
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

    private static object BuildItemCompletedParams(SessionEvent evt) => new Contract.ItemNotification
    {
        ThreadId = evt.ThreadId,
        TurnId = evt.TurnId,
        Item = AppServerContractMapper.ToContract(
            evt.ItemPayload?.ToWire()
            ?? throw new InvalidOperationException("item/completed requires an item payload."))
    };

    private object BuildTerminalTurnParams(SessionEvent evt)
    {
        var turn = ProjectTerminalTurn(evt.TurnPayload, _connection.SupportsToolExecutionLifecycle);
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

    internal static SessionWireTurn? ProjectTerminalTurn(SessionTurn? turn, bool supportsToolExecutionLifecycle)
    {
        if (turn is null)
            return null;

        var wire = turn.ToWire(includeItems: true);
        if (!supportsToolExecutionLifecycle)
            wire.Items?.RemoveAll(item => item.Type == ItemType.ToolExecution);
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
        SessionEventType.ThreadQueueUpdated when evt.ThreadQueueUpdatedPayload is { } queue => new Contract.ThreadQueueUpdatedNotification
        {
            ThreadId = queue.ThreadId,
            QueuedInputs = Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
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
        SessionEventType.ItemDelta when evt.TerminalInteractionPayload is { } interaction => new Contract.TerminalInteractionNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId ?? throw new InvalidOperationException("terminal interaction requires a turn id."),
            ItemId = evt.ItemId ?? throw new InvalidOperationException("terminal interaction requires an item id."),
            SessionId = interaction.SessionId,
            Stdin = interaction.Stdin
        },
        SessionEventType.ItemDelta when evt.ToolExecutionProgressPayload is { } toolProgress => new Contract.ToolExecutionProgressNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId ?? throw new InvalidOperationException("toolExecution progress requires a turn id."),
            ItemId = evt.ItemId ?? throw new InvalidOperationException("toolExecution progress requires an item id."),
            CallId = toolProgress.CallId,
            ToolName = toolProgress.ToolName,
            Progress = JsonSerializer.SerializeToElement(toolProgress.Progress)
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
            Entries = Protocol.Optional<IReadOnlyList<Contract.SubAgentProgressEntry>>.FromValue(
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
            RequestTiming = usage.RequestTiming is { } timing
                ? Protocol.Optional<Contract.UsageRequestTiming?>.FromValue(new Contract.UsageRequestTiming
                {
                    FirstOutputLatencyMs = timing.FirstOutputLatencyMs,
                    GenerationMs = timing.GenerationMs
                })
                : default,
            ContextUsage = usage.ContextUsage is null
                ? default
                : Protocol.Optional<Contract.ContextUsageSnapshot?>.FromValue(
                    ThreadContractMapper.ToContract(usage.ContextUsage))
        },

        SessionEventType.TurnDiffUpdated when evt.TurnDiffUpdatedPayload is { } turnDiff => new Contract.TurnDiffUpdatedNotification
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId ?? throw new InvalidOperationException("turn/diff/updated requires a turn id."),
            Diff = turnDiff.Diff
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
                : Protocol.Optional<IReadOnlyDictionary<string, JsonElement>?>.FromValue(
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
                : Protocol.Optional<Contract.ContextUsageSnapshot?>.FromValue(
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

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

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
        && _connection.ShouldSendNotification(Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta);

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

        var connectionKind = _connection.IsChannelAdapter ? "externalChannel" : _connection.Surface.Id;

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


    private async Task SendNotificationAsync(string method, object? @params, CancellationToken ct)
    {
        if (_transportUnavailable)
            return;

        try
        {
            if (method is Protocol.AppServer.AppServerMethodNames.ThreadStarted
                or Protocol.AppServer.AppServerMethodNames.ThreadResumed
                or Protocol.AppServer.AppServerMethodNames.TurnStarted
                or Protocol.AppServer.AppServerMethodNames.TurnCompleted
                or Protocol.AppServer.AppServerMethodNames.TurnFailed
                or Protocol.AppServer.AppServerMethodNames.TurnCancelled
                or Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged)
                await _connection.SendThreadNotificationAsync(_transport, method, @params!, ct);
            else
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
        Protocol.AppServer.AppServerMethodNames.ThreadUpdated => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadUpdated,
            Require<Contract.ThreadNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ThreadDeleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ThreadDeleted,
            Require<Contract.ThreadDeletedNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ItemStarted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemStarted,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ItemCompleted => _transport.NotifyAsync(
            Contract.AppServerRpc.ItemCompleted,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.AgentMessageDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.AgentMessageDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ReasoningDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ReasoningDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.CommandOutputDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.CommandOutputDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta => _transport.NotifyAsync(
            Contract.AppServerRpc.ToolArgumentsDelta,
            Require<Contract.ItemDeltaNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.ApprovalResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.ApprovalResolved,
            Require<Contract.ItemNotification>(method, parameters),
            ct),
        Protocol.AppServer.AppServerMethodNames.UserInputResolved => _transport.NotifyAsync(
            Contract.AppServerRpc.UserInputResolved,
            Require<Contract.ItemNotification>(method, parameters),
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
