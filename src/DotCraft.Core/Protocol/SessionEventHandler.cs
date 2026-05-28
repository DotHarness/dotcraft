using DotCraft.Tools;

namespace DotCraft.Protocol;

/// <summary>
/// A callback-based consumer for <see cref="SessionEvent"/> streams from <see cref="ISessionService.SubmitInputAsync"/>.
///
/// Each channel adapter constructs a <see cref="SessionEventHandler"/> with channel-specific callbacks and
/// calls <see cref="ProcessAsync"/> to drive the event loop. This eliminates the duplicated
/// <c>switch (evt.EventType)</c> blocks that would otherwise appear in every adapter.
///
/// Usage:
/// <code>
/// var handler = new SessionEventHandler
/// {
///     OnTextDelta   = text => SendMessageAsync(text),
///     OnToolStarted = (name, icon, args) => ...,
///     OnTurnCompleted = usage => RecordTokenUsageAsync(usage),
///     OnApprovalRequested = req => AskUserAsync(req),
/// };
/// await handler.ProcessAsync(sessionService.SubmitInputAsync(threadId, prompt, ct: ct), ct);
/// </code>
/// </summary>
public sealed class SessionEventHandler
{
    /// <summary>
    /// Called for each text delta from the agent response stream.
    /// The adapter typically buffers this text and sends it to the channel transport.
    /// </summary>
    public required Func<string, Task> OnTextDelta { get; init; }

    /// <summary>
    /// Called when a tool call begins (<see cref="SessionEventType.ItemStarted"/> for <see cref="ItemType.ToolCall"/>).
    /// Parameters: toolName, icon, formatted display string (may be null), callId (correlation ID, may be empty).
    /// The adapter may send a progress notification to the user.
    /// </summary>
    public required Func<string, string, string?, string, Task> OnToolStarted { get; init; }

    /// <summary>
    /// Called when an approval is required before executing a sensitive operation.
    /// The adapter must present the request to the user and return the resulting approval decision.
    /// </summary>
    public required Func<ApprovalRequestPayload, Task<SessionApprovalDecision>> OnApprovalRequested { get; init; }

    /// <summary>
    /// Called when the turn completes successfully.
    /// The parameter is the token usage from the turn (may be null if not available).
    /// </summary>
    public required Func<TokenUsageInfo?, Task> OnTurnCompleted { get; init; }

    /// <summary>
    /// Called for each reasoning delta (optional; if null, reasoning is silently dropped).
    /// </summary>
    public Func<string, Task>? OnReasoningDelta { get; init; }

    /// <summary>
    /// Called when a tool result arrives (<see cref="SessionEventType.ItemCompleted"/> for <see cref="ItemType.ToolResult"/>).
    /// Parameters: callId (correlates with <see cref="OnToolStarted"/>), result text (may be null).
    /// Optional; if null, tool results are not surfaced to the adapter.
    /// </summary>
    public Func<string, string?, Task>? OnToolCompleted { get; init; }

    /// <summary>
    /// Called when the turn fails.
    /// Parameter: error message.
    /// Optional; if null, turn failures are silently handled.
    /// </summary>
    public Func<string, Task>? OnTurnFailed { get; init; }

    /// <summary>
    /// Called when a SubAgent progress snapshot arrives (<see cref="SessionEventType.SubAgentProgress"/>).
    /// The payload contains a complete snapshot of all active SubAgents' progress.
    /// Optional; if null, SubAgent progress events are silently dropped.
    /// </summary>
    public Func<SubAgentProgressPayload, Task>? OnSubAgentProgress { get; init; }

    /// <summary>
    /// Called when a system-level maintenance event arrives (<see cref="SessionEventType.SystemEvent"/>).
    /// The payload carries the event kind (compacting, compacted, consolidating, consolidationSkipped, consolidationFailed, etc.) and an optional message.
    /// Optional; if null, system events are silently dropped.
    /// </summary>
    public Func<SystemEventPayload, Task>? OnSystemEvent { get; init; }

    /// <summary>
    /// Processes the <see cref="SessionEvent"/> stream, calling the appropriate callback for each event.
    /// The <paramref name="resolveApproval"/> delegate is called after <see cref="OnApprovalRequested"/>
    /// returns, to inform Session Core of the user's decision.
    /// </summary>
    /// <param name="events">The event stream returned by <see cref="ISessionService.SubmitInputAsync"/>.</param>
    /// <param name="resolveApproval">
    /// Delegate called with <c>(threadId, turnId, requestId, decision)</c> to resolve a pending approval.
    /// Typically: <c>(thId, tid, rid, decision) => sessionService.ResolveApprovalAsync(thId, tid, rid, decision, ct)</c>
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ProcessAsync(
        IAsyncEnumerable<SessionEvent> events,
        Func<string, string, string, SessionApprovalDecision, Task> resolveApproval,
        CancellationToken ct = default)
    {
        string? activeThreadId = null;
        string? activeTurnId = null;

        await foreach (var evt in events.WithCancellation(ct))
        {
            if (evt.ThreadId != null)
                activeThreadId = evt.ThreadId;
            if (evt.TurnId != null)
                activeTurnId = evt.TurnId;

            switch (evt.EventType)
            {
                case SessionEventType.ItemDelta when evt.DeltaPayload is { } delta:
                    if (!string.IsNullOrEmpty(delta.TextDelta))
                        await OnTextDelta(delta.TextDelta);
                    break;

                case SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning:
                    if (OnReasoningDelta != null && !string.IsNullOrEmpty(reasoning.TextDelta))
                        await OnReasoningDelta(reasoning.TextDelta);
                    break;

                case SessionEventType.ItemStarted when evt.ItemPayload?.Type == ItemType.ToolCall:
                {
                    var tp = evt.ItemPayload!.Payload as ToolCallPayload;
                    var toolName = tp?.ToolName ?? string.Empty;
                    var icon = ToolRegistry.GetToolIcon(toolName);
                    var formatted = tp?.Arguments != null
                        ? ToolRegistry.FormatToolCall(toolName, tp.Arguments)
                        : null;
                    await OnToolStarted(toolName, icon, formatted, tp?.CallId ?? string.Empty);
                    break;
                }

                case SessionEventType.ItemCompleted when evt.ItemPayload?.Type == ItemType.ToolResult:
                {
                    if (OnToolCompleted != null)
                    {
                        var rp = evt.ItemPayload!.Payload as ToolResultPayload;
                        await OnToolCompleted(rp?.CallId ?? string.Empty, rp?.Result);
                    }
                    break;
                }

                case SessionEventType.ApprovalRequested:
                {
                    var item = evt.ItemPayload;
                    if (item?.Payload is ApprovalRequestPayload req && activeThreadId != null && activeTurnId != null)
                    {
                        var decision = await OnApprovalRequested(req);
                        await resolveApproval(activeThreadId, activeTurnId, req.RequestId, decision);
                    }
                    break;
                }

                case SessionEventType.TurnCompleted:
                    await OnTurnCompleted(evt.TurnPayload?.TokenUsage);
                    break;

                case SessionEventType.TurnFailed:
                {
                    if (OnTurnFailed != null)
                        await OnTurnFailed(evt.TurnPayload?.Error ?? "Turn failed");
                    break;
                }

                case SessionEventType.SubAgentProgress:
                {
                    if (OnSubAgentProgress != null && evt.SubAgentProgressPayload is { } progress)
                        await OnSubAgentProgress(progress);
                    break;
                }

                case SessionEventType.SystemEvent:
                {
                    if (OnSystemEvent != null && evt.SystemEventPayload is { } sysEvt)
                        await OnSystemEvent(sysEvt);
                    break;
                }
            }
        }
    }
}
