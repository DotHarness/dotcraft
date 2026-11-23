using System.Text;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private const string ContinuedChildMessage =
        "You will be notified when the subagent finishes.";

    /// <summary>
    /// The model's <c>SendMessage</c>: delivers the message to any other open Agent in the tree and wakes it when it is idle.
    /// </summary>
    public static async Task<SubAgentControlResult> SendAgentMessageAsync(
        SubAgentSessionContext context,
        string target,
        string message,
        CancellationToken ct)
    {
        var normalizedMessage = NormalizeRequired(message, nameof(message));
        if (CacheSharingFork.PurposeOf(context.ParentThread) == ThreadForkPurpose.SideChat)
        {
            if (!string.Equals(NormalizeRequired(target, nameof(target)), SessionService.SideChatMainTarget, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"In a side chat, SendMessage can only reach the main thread; use target \"{SessionService.SideChatMainTarget}\".");
            if (context.SessionService is not SessionService sessionService)
                throw new InvalidOperationException("SendMessage to the main thread is unavailable in this host.");
            return await sessionService.DeliverSideChatMessageAsync(context.ParentThread, normalizedMessage, ct);
        }

        var callerPath = GetCurrentAgentPath(context.ParentThread);
        var targetPath = callerPath.Resolve(NormalizeRequired(target, nameof(target)));
        if (targetPath == callerPath)
            throw new InvalidOperationException("SendMessage target cannot be the current agent.");

        var resolved = await ResolveAgentTargetAsync(
            context.SessionService,
            context.ParentThread,
            context.RootThreadId,
            targetPath.Value,
            requireOpen: true,
            ct);

        var service = context.SessionService as SessionService;
        var descendant = targetPath.IsSameOrDescendantOf(callerPath);
        SubAgentControlResult result;
        if (descendant && GetActiveTurn(resolved.Thread) != null)
        {
            try
            {
                result = await FollowupTaskAsync(context, targetPath.Value, normalizedMessage, ct, SubAgentFollowupDeliveryMode.Steer);
            }
            catch (InvalidOperationException)
            {
                result = await FollowupTaskAsync(context, targetPath.Value, normalizedMessage, ct);
            }
        }
        else if (descendant)
        {
            result = await FollowupTaskAsync(context, targetPath.Value, normalizedMessage, ct);
        }
        else
        {
            result = await WriteMailboxMessageAsync(context, resolved, callerPath, normalizedMessage, ct);
            if (service != null)
                await service.WakeForAgentMessagesAsync(resolved.ThreadId, ct);
        }

        if (descendant)
            result.Message ??= ContinuedChildMessage;
        return result;
    }

    private static async Task<SubAgentControlResult> WriteMailboxMessageAsync(
        SubAgentSessionContext context,
        ResolvedAgentTarget resolved,
        AgentPath senderPath,
        string message,
        CancellationToken ct)
    {
        var communication = new SubAgentCommunication
        {
            Id = NewMailboxEntryId(),
            RootThreadId = context.RootThreadId,
            AuthorAgentPath = senderPath.Value,
            RecipientAgentPath = resolved.Path.Value,
            Payload = message,
            ParentTurnId = context.ParentTurnId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.SessionService.AddSubAgentMailboxEntryAsync(
            SubAgentMailboxEntry.FromCommunication(communication),
            ct);
        return new SubAgentControlResult
        {
            ChildThreadId = resolved.ThreadId,
            AgentPath = resolved.Path.Value,
            TaskName = resolved.Edge?.TaskName,
            Status = "sent",
            AgentNickname = resolved.Edge?.AgentNickname ?? resolved.Thread.DisplayName,
            AgentRole = resolved.Edge?.AgentRole
        };
    }

    public static async Task<SubAgentControlResult> FollowupTaskAsync(
        SubAgentSessionContext context,
        string target,
        string message,
        CancellationToken ct,
        SubAgentFollowupDeliveryMode deliveryMode = SubAgentFollowupDeliveryMode.Queue)
    {
        var normalizedMessage = NormalizeRequired(message, nameof(message));
        var resolved = await ResolveAgentTargetAsync(
            context.SessionService,
            context.ParentThread,
            context.RootThreadId,
            target,
            requireOpen: true,
            ct);
        if (resolved.Path.IsRoot)
            throw new InvalidOperationException("FollowupTask target cannot be '/root'.");

        var senderPath = GetCurrentAgentPath(context.ParentThread);
        if (string.Equals(senderPath.Value, resolved.Path.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("FollowupTask target cannot be the current agent.");

        var followupCommunication = new SubAgentCommunication
        {
            Id = NewCommunicationId(),
            RootThreadId = context.RootThreadId,
            AuthorAgentPath = senderPath.Value,
            RecipientAgentPath = resolved.Path.Value,
            Payload = normalizedMessage,
            ParentTurnId = context.ParentTurnId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        SubAgentControlResult result;
        using (await SubAgentCommunicationRuntime.For(context.SessionService).AcquireInboxAsync(
            context.RootThreadId,
            resolved.Path.Value,
            ct))
        {
            var pending = await context.SessionService.ListPendingSubAgentMailboxAsync(
                context.RootThreadId,
                resolved.Path.Value,
                ct);
            var turnPrompt = BuildFollowupPrompt(pending, followupCommunication);

            var activeTurn = GetActiveTurn(resolved.Thread);
            if (activeTurn != null)
            {
                var queued = await QueueFollowupTaskAsync(
                    context.SessionService,
                    resolved,
                    turnPrompt,
                    normalizedMessage,
                    ct);
                result = queued.Result;
                if (deliveryMode == SubAgentFollowupDeliveryMode.Steer)
                {
                    result = await SteerQueuedFollowupTaskAsync(
                        context.SessionService,
                        resolved,
                        activeTurn.Id,
                        queued.QueuedInput,
                        result,
                        ct);
                }
            }
            else
            {
                result = await StartChildTurnAsync(
                    context.SessionService,
                    resolved.Thread,
                    turnPrompt,
                    CreateSubAgentTrigger(SubAgentFollowupTriggerKind, BuildFollowupTriggerLabel(resolved), resolved.Path.Value),
                    context.LifecycleHook,
                    context.ParentTurnCancellation ?? ct,
                    NormalizeOptional(context.ParentTurnId),
                    ct);
            }

            if (pending.Count > 0)
            {
                await context.SessionService.MarkSubAgentMailboxDeliveredAsync(
                    context.RootThreadId,
                    pending.Select(entry => entry.Id).ToArray(),
                    DateTimeOffset.UtcNow,
                    ct);
            }
        }
        result.AgentPath = resolved.Path.Value;
        result.TaskName = resolved.Edge?.TaskName;
        return result;
    }

    public static async Task<SubAgentControlResult> SendInputAsync(
        ISessionService sessionService,
        string childThreadId,
        string message,
        CancellationToken ct)
    {
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook = null;
        if (sessionService is SessionService service)
            lifecycleHook = service.RunSubAgentLifecycleHookAsync;

        return await SendInputAsync(sessionService, childThreadId, message, lifecycleHook, ct);
    }

    internal static async Task<SubAgentControlResult> SendInputAsync(
        ISessionService sessionService,
        string childThreadId,
        string message,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        CancellationToken ct)
    {
        var normalizedMessage = NormalizeRequired(message, nameof(message));
        var child = await sessionService.GetThreadAsync(childThreadId, ct);
        return await StartChildTurnAsync(
            sessionService,
            child,
            normalizedMessage,
            CreateSubAgentTrigger(SubAgentInputTriggerKind, BuildChildTriggerLabel(child), child.Source.SubAgent?.AgentPath),
            lifecycleHook,
            ct,
            parentTurnId: null,
            ct);
    }

    private static async Task<QueuedFollowupTaskResult> QueueFollowupTaskAsync(
        ISessionService sessionService,
        ResolvedAgentTarget resolved,
        string turnPrompt,
        string displayText,
        CancellationToken ct)
    {
        var materializedPart = new SessionInputPart { Type = "text", Text = turnPrompt };
        var nativePart = new SessionInputPart { Type = "text", Text = displayText };
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = SubAgentFollowupTriggerKind,
            Label = BuildFollowupTriggerLabel(resolved),
            RefId = resolved.Path.Value
        });

        var queued = await sessionService.EnqueueTurnInputAsync(
            resolved.ThreadId,
            [new TextContent(turnPrompt)],
            sender: null,
            ct: ct,
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [nativePart],
                MaterializedInputParts = [materializedPart],
                DisplayText = displayText
            });

        var result = new SubAgentControlResult
        {
            ChildThreadId = resolved.ThreadId,
            AgentPath = resolved.Path.Value,
            TaskName = resolved.Edge?.TaskName ?? resolved.Path.TaskName,
            Status = "queued",
            AgentNickname = resolved.Edge?.AgentNickname ?? resolved.Thread.DisplayName,
            AgentRole = resolved.Edge?.AgentRole
        };
        return new QueuedFollowupTaskResult(result, queued);
    }

    private static async Task<SubAgentControlResult> SteerQueuedFollowupTaskAsync(
        ISessionService sessionService,
        ResolvedAgentTarget resolved,
        string activeTurnId,
        QueuedTurnInput queued,
        SubAgentControlResult result,
        CancellationToken ct)
    {
        try
        {
            await sessionService.UpdateQueuedTurnInputAsync(
                resolved.ThreadId,
                queued.Id,
                activeTurnId,
                "guidancePending",
                ct);
            result.Status = "guidancePending";
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            try
            {
                await sessionService.RemoveQueuedTurnInputAsync(resolved.ThreadId, queued.Id, CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup; surface the steering failure.
            }

            throw new InvalidOperationException(
                "FollowupTask deliveryMode 'steer' could not promote the task into current-turn guidance because the target active turn changed or ended. Retry with deliveryMode 'queue' or refresh the target status.",
                ex);
        }
    }

    private static string BuildFollowupTriggerLabel(ResolvedAgentTarget resolved) =>
        NormalizeOptional(resolved.Thread.DisplayName)
        ?? NormalizeOptional(resolved.Edge?.AgentNickname)
        ?? NormalizeOptional(resolved.Edge?.TaskName)
        ?? resolved.Path.TaskName;

    private static string BuildChildTriggerLabel(SessionThread child) =>
        NormalizeOptional(child.DisplayName)
        ?? NormalizeOptional(child.Source.SubAgent?.AgentNickname)
        ?? NormalizeOptional(child.Source.SubAgent?.TaskName)
        ?? NormalizeOptional(child.Source.SubAgent?.AgentPath)
        ?? child.Id;

    private static TurnTriggerInfo CreateSubAgentTrigger(string kind, string? label, string? refId) => new()
    {
        Kind = kind,
        Label = NormalizeOptional(label),
        RefId = NormalizeOptional(refId)
    };

    private static async Task<string?> ExtractLastTaskMessageAsync(
        ISessionService sessionService,
        SessionThread thread,
        CancellationToken ct)
    {
        var resident = thread.Turns
            .OrderByDescending(turn => turn.StartedAt)
            .Select(turn => turn.Input?.AsUserMessage?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (resident != null || thread.OmittedTurnCount == 0)
            return resident;

        await foreach (var turn in sessionService.ReadThreadTurnsAsync(
                           thread.Id, ThreadHistorySortDirection.Descending, includeItems: false, ct))
        {
            if (turn.Input?.AsUserMessage?.Text is { } text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string BuildFollowupPrompt(
        IReadOnlyList<SubAgentMailboxEntry> pending,
        SubAgentCommunication followup)
    {
        var sb = new StringBuilder();
        foreach (var entry in pending)
        {
            var rendered = entry.ToCommunication().RenderForModel();
            if (string.IsNullOrWhiteSpace(rendered))
                continue;

            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append(rendered);
        }

        if (sb.Length > 0)
            sb.AppendLine().AppendLine();
        sb.Append(followup.RenderForModel());
        return sb.ToString();
    }

    private static string NewMailboxEntryId() => $"mailbox_{Guid.NewGuid():N}";

    private static string NewCommunicationId() => $"communication_{Guid.NewGuid():N}";
}
