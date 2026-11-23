using DotCraft.Hooks;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static async Task<SubAgentRunResult> RunChildTurnAsync(
        ISessionService sessionService,
        string childThreadId,
        string prompt,
        TurnTriggerInfo? triggerInfo,
        CancellationToken ct,
        TaskCompletionSource? dispatchStarted = null)
    {
        try
        {
            using var triggerScope = triggerInfo == null ? null : TurnTriggerScope.Set(triggerInfo);
            var observeAdmission = dispatchStarted != null && sessionService is ISubAgentInitialInputService;
            var events = observeAdmission
                ? ((ISubAgentInitialInputService)sessionService).SubmitSubAgentInitialInput(childThreadId, [new TextContent(prompt)], ct)
                : sessionService.SubmitInputAsync(childThreadId, [new TextContent(prompt)], ct: ct);
            return await ReadChildTurnResultAsync(
                childThreadId,
                events,
                _ => dispatchStarted?.TrySetResult(),
                observeAdmission ? CancellationToken.None : ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            dispatchStarted?.TrySetCanceled(ct);
            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "cancelled",
                Message = "Subagent was cancelled."
            };
        }
        catch (Exception ex)
        {
            dispatchStarted?.TrySetException(ex);
            throw;
        }
    }

    private static async Task<SubAgentControlResult> StartChildTurnAsync(
        ISessionService sessionService,
        SessionThread child,
        string message,
        TurnTriggerInfo? triggerInfo,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        CancellationToken childLifetime,
        string? parentTurnId,
        CancellationToken ct)
    {
        var childThreadId = child.Id;
        var running = child.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (running != null)
            throw new InvalidOperationException($"Subagent thread '{childThreadId}' already has a running turn.");

        var childCts = CancellationTokenSource.CreateLinkedTokenSource(childLifetime);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext ?? string.Empty;
        var source = child.Source.SubAgent;
        await RunLifecycleHookAsync(
            lifecycleHook,
            HookEvent.SubagentStart,
            child,
            "running",
            message: null,
            ct);

        var dispatchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = RunChildTurnAsync(
            sessionService,
            childThreadId,
            message,
            triggerInfo,
            childCts.Token,
            dispatchStarted);

        var runningChild = new RunningChild(parentThreadId, childCts, completion, parentTurnId, childLifetime);
        TrackRunningChild(sessionService, childThreadId, runningChild);
        _ = ObserveChildCompletionAsync(sessionService, childThreadId, runningChild, lifecycleHook);
        await using (ct.Register(childCts.Cancel))
            await dispatchStarted.Task.WaitAsync(ct);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = source?.AgentPath,
            TaskName = source?.TaskName,
            Status = "running",
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole
        };
    }

    /// <summary>
    /// Tracks a child Turn started from the child's own queue as a running child, so its parent
    /// is notified when it ends. Returns false when the Thread is not a SubAgent child.
    /// </summary>
    internal static bool TryObserveQueuedChildTurn(
        ISessionService sessionService,
        SessionThread child,
        IAsyncEnumerable<SessionEvent> events)
    {
        var source = child.Source.SubAgent;
        if (source == null)
            return false;

        var cancellation = new CancellationTokenSource();
        var runningChild = new RunningChild(
            source.ParentThreadId ?? child.ChannelContext ?? string.Empty,
            cancellation,
            ObserveQueuedChildTurnAsync(sessionService, child.Id, events, cancellation.Token));
        TrackRunningChild(sessionService, child.Id, runningChild);
        _ = ObserveChildCompletionAsync(sessionService, child.Id, runningChild, lifecycleHook: null);
        return true;
    }

    private static async Task<SubAgentRunResult> ObserveQueuedChildTurnAsync(
        ISessionService sessionService,
        string childThreadId,
        IAsyncEnumerable<SessionEvent> events,
        CancellationToken stop)
    {
        await Task.Yield();
        CancellationTokenRegistration? registration = null;
        try
        {
            return await ReadChildTurnResultAsync(childThreadId, events, turnId =>
            {
                if (turnId != null)
                    registration = stop.Register(() => _ = sessionService.CancelTurnAsync(childThreadId, turnId, CancellationToken.None));
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            registration?.Dispose();
        }
    }

    private static string ExtractFinalAgentText(SessionTurn? turn)
    {
        if (turn == null)
            return string.Empty;

        var parts = turn.Items
            .Where(item => item.Type == ItemType.AgentMessage)
            .Select(item => item.AsAgentMessage?.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        var text = string.Join(Environment.NewLine + Environment.NewLine, parts).Trim();
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            turn.Items
                .Where(item => item.Type == ItemType.Error)
                .Select(item => item.AsError?.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message))).Trim();
    }
}
