using System.Text.Json.Nodes;
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

        try
        {
            await RunLifecycleHookAsync(
                lifecycleHook,
                HookEvent.SubagentStop,
                sessionService,
                childThreadId,
                result.Status,
                result.Message,
                CancellationToken.None).ConfigureAwait(false);

            await AddSubAgentCompletionNotificationAsync(
                sessionService,
                childThreadId,
                result,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Completion cleanup and graph wakeups must still run if persistence is unavailable.
        }
        finally
        {
            RunningChildren.TryRemove(KeyValuePair.Create(childThreadId, runningChild));
            runningChild.Cancellation.Dispose();
        }
    }

    private static async Task AddSubAgentCompletionNotificationAsync(
        ISessionService sessionService,
        string childThreadId,
        SubAgentRunResult result,
        CancellationToken ct)
    {
        var status = NormalizeCompletionNotificationStatus(result.Status);
        if (status == null)
            return;

        var child = await sessionService.GetThreadAsync(childThreadId, ct).ConfigureAwait(false);
        var source = child.Source.SubAgent;
        if (source == null
            || string.IsNullOrWhiteSpace(source.RootThreadId)
            || !AgentPath.TryParse(source.AgentPath, out var childPath)
            || string.IsNullOrWhiteSpace(childPath.ParentValue))
        {
            return;
        }

        var terminalTurnId = child.Turns.LastOrDefault()?.Id;
        var communication = new SubAgentCommunication
        {
            Id = NewMailboxEntryId(),
            RootThreadId = source.RootThreadId,
            AuthorAgentPath = childPath.Value,
            RecipientAgentPath = childPath.ParentValue!,
            MessageType = SubAgentCommunicationMessageType.FinalAnswer,
            Payload = BuildSubAgentCompletionPayload(childPath.Value, status, result.Message),
            ParentTurnId = terminalTurnId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await sessionService.AddSubAgentMailboxEntryAsync(
            SubAgentMailboxEntry.FromCommunication(communication),
            ct).ConfigureAwait(false);
    }

    private static string? NormalizeCompletionNotificationStatus(string? status)
    {
        var normalized = NormalizeOptional(status)?.ToLowerInvariant();
        return normalized switch
        {
            "completed" => "completed",
            "failed" => "failed",
            "cancelled" => "cancelled",
            _ => null
        };
    }

    private static string BuildSubAgentCompletionPayload(
        string agentPath,
        string status,
        string? message)
    {
        var payload = new JsonObject
        {
            ["agentPath"] = agentPath,
            ["status"] = new JsonObject
            {
                [status] = message ?? string.Empty
            }
        };
        return payload.ToJsonString();
    }
}
