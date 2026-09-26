using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Security;
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
            SessionTurn? finalTurn = null;
            using var triggerScope = triggerInfo == null ? null : TurnTriggerScope.Set(triggerInfo);
            var observeAdmission = dispatchStarted != null && sessionService is ISubAgentInitialInputService;
            var events = observeAdmission
                ? ((ISubAgentInitialInputService)sessionService).SubmitSubAgentInitialInput(childThreadId, [new TextContent(prompt)], ct)
                : sessionService.SubmitInputAsync(childThreadId, [new TextContent(prompt)], ct: ct);
            await using var enumerator = events.GetAsyncEnumerator(observeAdmission ? CancellationToken.None : ct);
            while (await enumerator.MoveNextAsync())
            {
                var ev = enumerator.Current;
                if (ev.EventType == SessionEventType.TurnStarted)
                    dispatchStarted?.TrySetResult();
                if (ev.EventType is SessionEventType.TurnCompleted
                    or SessionEventType.TurnCancelled
                    or SessionEventType.TurnFailed)
                {
                    finalTurn = ev.TurnPayload;
                }
            }
            if (dispatchStarted is { Task.IsCompleted: false })
                throw new InvalidOperationException("Subagent input ended before a Turn was admitted.");

            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = finalTurn?.Status.ToString().ToLowerInvariant() ?? "completed",
                Message = ExtractFinalAgentText(finalTurn)
            };
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

    private static async Task<SubAgentRunResult> RunExternalChildTurnsAsync(
        ISessionService sessionService,
        SubAgentCoordinator? coordinator,
        SubAgentPreparedRun prepared,
        string childThreadId,
        string prompt,
        TurnTriggerInfo? triggerInfo,
        CancellationToken ct,
        TaskCompletionSource? dispatchStarted = null)
    {
        var result = await RunExternalChildTurnOnceAsync(
            sessionService,
            coordinator,
            prepared,
            childThreadId,
            prompt,
            triggerInfo,
            ct,
            dispatchStarted);

        while (string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var queued = await TryTakeNextSubAgentFollowupQueuedInputAsync(sessionService, childThreadId, ct);
            if (queued == null)
                return result;

            prompt = BuildQueuedInputPrompt(queued);
            var child = await sessionService.GetThreadAsync(childThreadId, ct);
            prepared = PrepareExternalChildRun(child, prompt, coordinator, requireExternalResume: false).Run;
            result = await RunExternalChildTurnOnceAsync(
                sessionService,
                coordinator,
                prepared,
                childThreadId,
                prompt,
                CreateSubAgentTrigger(queued.TriggerKind!, queued.TriggerLabel, queued.TriggerRefId),
                ct,
                dispatchStarted: null);
        }

        return result;
    }

    private static async Task<SubAgentRunResult> RunExternalChildTurnOnceAsync(
        ISessionService sessionService,
        SubAgentCoordinator? coordinator,
        SubAgentPreparedRun prepared,
        string childThreadId,
        string prompt,
        TurnTriggerInfo? triggerInfo,
        CancellationToken ct,
        TaskCompletionSource? dispatchStarted = null)
    {
        if (coordinator == null)
        {
            var error = new InvalidOperationException("External subagent profiles require a SubAgentCoordinator.");
            dispatchStarted?.TrySetException(error);
            throw error;
        }
        if (sessionService is not ISubAgentSyntheticTurnService syntheticTurns)
        {
            var error = new InvalidOperationException("Session service does not support external subagent synthetic turns.");
            dispatchStarted?.TrySetException(error);
            throw error;
        }

        SessionTurn? turn = null;
        try
        {
            using var triggerScope = triggerInfo == null ? null : TurnTriggerScope.Set(triggerInfo);
            turn = await syntheticTurns.StartSubAgentSyntheticTurnAsync(
                childThreadId,
                [new TextContent(prompt)],
                prepared.Runtime.RuntimeType,
                prepared.Profile.Name,
                ct);
            dispatchStarted?.TrySetResult();
            var result = await coordinator.ExecutePreparedRunAsync(prepared, cancellationToken: ct);
            var completedTurn = await syntheticTurns.CompleteSubAgentSyntheticTurnAsync(
                childThreadId,
                turn.Id,
                result.Text,
                result.IsError,
                result.TokensUsed,
                CancellationToken.None);
            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = completedTurn.Status.ToString().ToLowerInvariant(),
                Message = result.Text
            };
        }
        catch (OperationCanceledException)
        {
            if (turn == null)
                dispatchStarted?.TrySetCanceled(ct);
            if (turn != null)
            {
                await syntheticTurns.CancelSubAgentSyntheticTurnAsync(
                    childThreadId,
                    turn.Id,
                    "Subagent was cancelled.",
                    CancellationToken.None);
            }

            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "cancelled",
                Message = "Subagent was cancelled."
            };
        }
        catch (Exception ex)
        {
            if (turn == null)
                dispatchStarted?.TrySetException(ex);
            if (turn != null)
            {
                await syntheticTurns.CompleteSubAgentSyntheticTurnAsync(
                    childThreadId,
                    turn.Id,
                    ex.Message,
                    isError: true,
                    tokensUsed: null,
                    CancellationToken.None);
            }

            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "failed",
                Message = ex.Message
            };
        }
    }

    private static async Task<SubAgentControlResult> StartChildTurnAsync(
        ISessionService sessionService,
        SessionThread child,
        string message,
        SubAgentCoordinator? coordinator,
        bool requireExternalResume,
        TurnTriggerInfo? triggerInfo,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook,
        CancellationToken ct)
    {
        var childThreadId = child.Id;
        var running = child.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (running != null)
            throw new InvalidOperationException($"Subagent thread '{childThreadId}' already has a running turn.");

        var childCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext ?? string.Empty;
        var source = child.Source.SubAgent;
        var runtimeType = source?.RuntimeType ?? NativeSubAgentRuntime.RuntimeTypeName;
        var resultCapabilities = ResolveCapabilities(runtimeType, null, coordinator);
        await RunLifecycleHookAsync(
            lifecycleHook,
            HookEvent.SubagentStart,
            child,
            "running",
            message: null,
            ct);

        Task<SubAgentRunResult> completion;
        var dispatchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            completion = RunChildTurnAsync(
                sessionService,
                childThreadId,
                message,
                triggerInfo,
                childCts.Token,
                dispatchStarted);
        }
        else
        {
            var prepared = PrepareExternalChildRun(child, message, coordinator, requireExternalResume);
            resultCapabilities = prepared.Capabilities;
            completion = RunExternalChildTurnsAsync(
                sessionService,
                coordinator,
                prepared.Run,
                childThreadId,
                message,
                triggerInfo,
                childCts.Token,
                dispatchStarted);
        }

        var runningChild = new RunningChild(parentThreadId, childCts, completion);
        RunningChildren[childThreadId] = runningChild;
        _ = ObserveChildCompletionAsync(sessionService, childThreadId, runningChild, lifecycleHook);
        await dispatchStarted.Task.WaitAsync(ct);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = source?.AgentPath,
            TaskName = source?.TaskName,
            Status = "running",
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole,
            ProfileName = source?.ProfileName,
            RuntimeType = runtimeType,
            SupportsSendInput = resultCapabilities.SupportsSendInput,
            SupportsResume = resultCapabilities.SupportsResume,
            SupportsSendMessage = source?.SupportsSendMessage ?? true,
            SupportsFollowupTask = source?.SupportsFollowupTask ?? true,
            SupportsClose = resultCapabilities.SupportsClose
        };
    }

    private static (SubAgentPreparedRun Run, SubAgentCapabilities Capabilities) PrepareExternalChildRun(
        SessionThread child,
        string message,
        SubAgentCoordinator? coordinator,
        bool requireExternalResume)
    {
        var source = child.Source.SubAgent;
        var profileName = NormalizeOptional(source?.ProfileName)
            ?? throw new InvalidOperationException($"Subagent thread '{child.Id}' does not record a profile name.");
        var request = new SubAgentTaskRequest
        {
            Task = message,
            Label = source?.AgentNickname,
            WorkingDirectory = child.WorkspacePath,
            ApprovalContext = ApprovalContextScope.Current
        };
        var prepared = coordinator?.PrepareRun(request, profileName)
            ?? throw new InvalidOperationException("External subagent profiles require a SubAgentCoordinator.");
        var capabilities = ResolveCapabilities(prepared.Runtime.RuntimeType, prepared.Profile, coordinator);
        if (requireExternalResume && !capabilities.SupportsSendInput)
        {
            throw new InvalidOperationException(
                $"Subagent profile '{prepared.Profile.Name}' does not support SendInput. Enable external CLI session resume and use a resumable profile.");
        }

        return (prepared, capabilities);
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

    private static SubAgentPreparedRun? PrepareRun(
        SubAgentCoordinator? coordinator,
        SubAgentTaskRequest request,
        string? profileName)
    {
        var effectiveProfileName = NormalizeOptional(profileName) ?? SubAgentCoordinator.DefaultProfileName;
        if (coordinator != null)
            return coordinator.PrepareRun(request, effectiveProfileName);

        if (!string.Equals(effectiveProfileName, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Subagent profile '{effectiveProfileName}' requires profile management, but it is not available.");

        return null;
    }

    private static SubAgentCapabilities ResolveCapabilities(
        string runtimeType,
        SubAgentProfile? profile,
        SubAgentCoordinator? coordinator)
    {
        var native = string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase);
        var externalResume = !native
            && coordinator?.ExternalCliSessionResumeEnabled == true
            && profile?.SupportsResume == true;
        return new SubAgentCapabilities(
            SupportsSendInput: native || externalResume,
            SupportsResume: native || externalResume,
            SupportsClose: true);
    }
}
