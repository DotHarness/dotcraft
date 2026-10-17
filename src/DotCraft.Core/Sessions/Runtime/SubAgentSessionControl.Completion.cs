using System.Diagnostics;
using DotCraft.Hooks;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static async Task RunLifecycleHookAsync(
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        HookEvent evt,
        SessionThread child,
        string? status,
        string? message,
        CancellationToken ct)
    {
        if (lifecycleHook == null)
            return;

        try
        {
            await lifecycleHook(
                new SubAgentLifecycleHookRequest
                {
                    Event = evt,
                    ChildThread = child,
                    Status = status,
                    Message = message
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Lifecycle hooks are observational for SubAgents and must not break cleanup.
        }
    }

    private static async Task RunLifecycleHookAsync(
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        HookEvent evt,
        ISessionService sessionService,
        string childThreadId,
        string? status,
        string? message,
        CancellationToken ct)
    {
        if (lifecycleHook == null)
            return;

        try
        {
            var child = await sessionService.GetThreadAsync(childThreadId, ct).ConfigureAwait(false);
            await RunLifecycleHookAsync(lifecycleHook, evt, child, status, message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Best-effort: lifecycle hooks should not prevent subagent cleanup.
        }
    }

    private static async Task ObserveChildCompletionAsync(
        ISessionService sessionService,
        string childThreadId,
        RunningChild runningChild,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook)
    {
        SubAgentRunResult result;
        try
        {
            result = await runningChild.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "cancelled",
                Message = "Subagent was cancelled."
            };
        }
        catch (Exception ex)
        {
            result = new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "failed",
                Message = ex.Message
            };
        }

        await RunLifecycleHookAsync(
            lifecycleHook,
            HookEvent.SubagentStop,
            sessionService,
            childThreadId,
            result.Status,
            result.Message,
            CancellationToken.None).ConfigureAwait(false);

        var notify = runningChild.Latch.TryClaimNotification();
        RunningChildren.TryRemove(KeyValuePair.Create(childThreadId, runningChild));
        runningChild.Cancellation.Dispose();
        if (!notify)
            return;
        try
        {
            await NotifyParentOfCompletionAsync(sessionService, childThreadId, runningChild, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Subagent completion notification failed for {0}: {1}", childThreadId, ex);
        }
    }

    private const string CancelledMessage = "Subagent was cancelled.";

    private static async Task NotifyParentOfCompletionAsync(
        ISessionService sessionService,
        string childThreadId,
        RunningChild runningChild,
        SubAgentRunResult result)
    {
        var kindStatus = NormalizeOptional(result.Status)?.ToLowerInvariant();
        var status = kindStatus switch
        {
            "completed" => BackgroundTaskStatus.Completed,
            "failed" => BackgroundTaskStatus.Failed,
            "cancelled" => BackgroundTaskStatus.Stopped,
            _ => null
        };
        if (status == null || sessionService is not IBackgroundTaskNotifications notifications)
            return;

        var child = await sessionService.GetThreadAsync(childThreadId, CancellationToken.None).ConfigureAwait(false);
        var source = child.Source.SubAgent;
        var parentThreadId = NormalizeOptional(source?.ParentThreadId) ?? NormalizeOptional(runningChild.ParentThreadId);
        var path = NormalizeOptional(source?.AgentPath);
        if (source == null || source.SilentCompletion || parentThreadId == null || path == null)
            return;

        var label = NormalizeOptional(source.AgentNickname) ?? NormalizeOptional(child.DisplayName) ?? path;
        var cascaded = status == BackgroundTaskStatus.Stopped && runningChild.ParentTurnLifetime.IsCancellationRequested;
        var message = NormalizeOptional(result.Message);
        var stoppedByUser = BackgroundTaskLatch.IsUserStop(status, runningChild.Latch);
        await notifications.NotifyBackgroundTaskEndedAsync(new BackgroundTaskEnding
        {
            TaskId = path,
            Kind = BackgroundTaskKinds.Agent,
            OwnerThreadId = parentThreadId,
            StartTurnId = runningChild.ParentTurnId,
            Label = label,
            Status = status,
            KindStatus = kindStatus!,
            Summary = status switch
            {
                BackgroundTaskStatus.Completed => $"Subagent {label} finished",
                BackgroundTaskStatus.Failed => $"Subagent {label} failed",
                _ => stoppedByUser ? $"Subagent {label} was stopped by the user" : $"Subagent {label} was stopped"
            },
            Result = message == CancelledMessage ? null : message,
            StoppedByUser = stoppedByUser,
            MayWake = !cascaded
        }, CancellationToken.None).ConfigureAwait(false);
    }
}
