using System.Text;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    public static async Task<SubAgentControlResult> SendMessageAsync(
        SubAgentSessionContext context,
        string target,
        string message,
        CancellationToken ct)
    {
        var normalizedMessage = NormalizeRequired(message, nameof(message));
        var resolved = await ResolveAgentTargetAsync(
            context.SessionService,
            context.ParentThread,
            context.RootThreadId,
            target,
            requireOpen: true,
            ct);
        var senderPath = GetCurrentAgentPath(context.ParentThread);
        if (string.Equals(senderPath.Value, resolved.Path.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("SendMessage target cannot be the current agent.");

        var communication = new SubAgentCommunication
        {
            Id = NewMailboxEntryId(),
            RootThreadId = context.RootThreadId,
            AuthorAgentPath = senderPath.Value,
            RecipientAgentPath = resolved.Path.Value,
            MessageType = SubAgentCommunicationMessageType.Message,
            Payload = normalizedMessage,
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
            AgentRole = resolved.Edge?.AgentRole,
            ProfileName = resolved.Edge?.ProfileName,
            RuntimeType = resolved.Edge?.RuntimeType,
            SupportsSendMessage = resolved.Edge?.SupportsSendMessage ?? true,
            SupportsFollowupTask = resolved.Edge?.SupportsFollowupTask ?? true,
            SupportsClose = resolved.Edge?.SupportsClose ?? true
        };
    }

    public static async Task<SubAgentControlResult> FollowupTaskAsync(
        SubAgentSessionContext context,
        string target,
        string message,
        SubAgentCoordinator? coordinator,
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
            MessageType = SubAgentCommunicationMessageType.NewTask,
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
            if (!IsNativeRuntime(resolved.Thread))
                turnPrompt = BuildExternalThreadContextPrompt(resolved.Thread, turnPrompt);

            var activeTurn = GetActiveTurn(resolved.Thread);
            if (activeTurn != null)
            {
                if (deliveryMode == SubAgentFollowupDeliveryMode.Steer && !IsNativeRuntime(resolved.Thread))
                    throw new InvalidOperationException(
                        "FollowupTask deliveryMode 'steer' is supported only for running native SubAgents. Use deliveryMode 'queue' for external SubAgents.");

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
                    coordinator,
                    requireExternalResume: false,
                    CreateSubAgentTrigger(SubAgentFollowupTriggerKind, BuildFollowupTriggerLabel(resolved), resolved.Path.Value),
                    context.LifecycleHook,
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
        result.SupportsSendMessage = resolved.Edge?.SupportsSendMessage ?? true;
        result.SupportsFollowupTask = resolved.Edge?.SupportsFollowupTask ?? true;
        return result;
    }

    public static async Task<SubAgentControlResult> SendInputAsync(
        ISessionService sessionService,
        string childThreadId,
        string message,
        SubAgentCoordinator? coordinator,
        CancellationToken ct)
    {
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook = null;
        if (sessionService is SessionService service)
            lifecycleHook = service.RunSubAgentLifecycleHookAsync;

        return await SendInputAsync(sessionService, childThreadId, message, coordinator, lifecycleHook, ct);
    }

    internal static async Task<SubAgentControlResult> SendInputAsync(
        ISessionService sessionService,
        string childThreadId,
        string message,
        SubAgentCoordinator? coordinator,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        CancellationToken ct)
    {
        var normalizedMessage = NormalizeRequired(message, nameof(message));
        var child = await sessionService.GetThreadAsync(childThreadId, ct);
        return await StartChildTurnAsync(
            sessionService,
            child,
            normalizedMessage,
            coordinator,
            requireExternalResume: true,
            CreateSubAgentTrigger(SubAgentInputTriggerKind, BuildChildTriggerLabel(child), child.Source.SubAgent?.AgentPath),
            lifecycleHook,
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
            AgentRole = resolved.Edge?.AgentRole,
            ProfileName = resolved.Edge?.ProfileName,
            RuntimeType = resolved.Edge?.RuntimeType ?? resolved.Thread.Source.SubAgent?.RuntimeType,
            SupportsSendMessage = resolved.Edge?.SupportsSendMessage ?? true,
            SupportsFollowupTask = resolved.Edge?.SupportsFollowupTask ?? true,
            SupportsClose = resolved.Edge?.SupportsClose ?? true
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

    private static async Task<QueuedTurnInput?> TryTakeNextSubAgentFollowupQueuedInputAsync(
        ISessionService sessionService,
        string childThreadId,
        CancellationToken ct)
    {
        var child = await sessionService.GetThreadAsync(childThreadId, ct);
        if (HasActiveTurn(child))
            return null;

        var queued = child.QueuedInputs.FirstOrDefault(input => string.Equals(input.Status, "queued", StringComparison.Ordinal));
        if (queued == null || !string.Equals(queued.TriggerKind, SubAgentFollowupTriggerKind, StringComparison.Ordinal))
            return null;

        await sessionService.RemoveQueuedTurnInputAsync(childThreadId, queued.Id, ct);
        return queued;
    }

    private static string BuildQueuedInputPrompt(QueuedTurnInput queued)
    {
        var text = string.Concat(
            queued.MaterializedInputParts
                .Select(part => part.ToAIContent())
                .OfType<TextContent>()
                .Select(content => content.Text));
        return string.IsNullOrWhiteSpace(text) ? queued.DisplayText : text;
    }

    /// <summary>
    /// Bounds the number of open (resident) SubAgents in the root thread's subtree.
    /// When spawning would exceed the limit, the oldest idle (non-running) SubAgent is
    /// closed to make room. If every resident SubAgent is still running, the spawn
    /// fails instead of evicting active work. Runs before a new child thread/edge is
    /// created.
    /// </summary>

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

    private static string? ExtractLastTaskMessage(SessionThread thread) =>
        thread.Turns
            .OrderByDescending(turn => turn.StartedAt)
            .Select(turn => turn.Input?.AsUserMessage?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

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
