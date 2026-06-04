using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

public sealed class SubAgentSessionContext
{
    public required ISessionService SessionService { get; init; }

    public required SessionThread ParentThread { get; init; }

    public required string ParentTurnId { get; init; }

    public required string RootThreadId { get; init; }

    public int Depth { get; init; }
}

public static class SubAgentSessionScope
{
    private static readonly AsyncLocal<SubAgentSessionContext?> CurrentContext = new();

    public static SubAgentSessionContext? Current => CurrentContext.Value;

    public static IDisposable Set(SubAgentSessionContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(() => CurrentContext.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class SubAgentSpawnOptions
{
    public string AgentPrompt { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? WorkingDirectory { get; set; }

    public IReadOnlyList<SubAgentRoleConfig>? RoleConfigs { get; set; }

    public string? SubAgentModel { get; set; }

    public int MaxDepth { get; set; } = 1;

    public string? ForkTurns { get; set; }
}

public sealed class SubAgentControlResult
{
    [JsonIgnore]
    public string ChildThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? RuntimeType { get; set; }

    [JsonIgnore]
    public bool SupportsSendInput { get; set; }

    [JsonIgnore]
    public bool SupportsResume { get; set; }

    public bool SupportsSendMessage { get; set; }

    public bool SupportsFollowupTask { get; set; }

    public bool SupportsClose { get; set; } = true;
}

public sealed class SubAgentWaitResult
{
    public string Status { get; set; } = string.Empty;

    public bool TimedOut { get; set; }
}

public sealed class SubAgentListResult
{
    public IReadOnlyList<SubAgentListItem> Data { get; set; } = [];
}

public sealed class SubAgentListItem
{
    public string AgentPath { get; set; } = DotCraft.Protocol.AgentPath.Root;

    public string Status { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? LastTaskMessage { get; set; }
}

public sealed class SubAgentRunResult
{
    public string ThreadId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public static class SubAgentSessionControl
{
    private static readonly TimeSpan CloseAgentCancellationWait = TimeSpan.FromSeconds(5);
    private const int DefaultWaitAgentTimeoutMs = 30_000;
    private const string SubAgentFollowupTriggerKind = "subagentFollowupTask";

    private sealed record RunningChild(
        string ParentThreadId,
        CancellationTokenSource Cancellation,
        Task<SubAgentRunResult> Completion);

    private static readonly ConcurrentDictionary<string, RunningChild> RunningChildren = new(StringComparer.Ordinal);
    private static readonly object ChangeSignalLock = new();
    private static TaskCompletionSource ChangeSignal = CreateChangeSignal();

    private sealed record ResolvedAgentTarget(
        string ThreadId,
        AgentPath Path,
        SessionThread Thread,
        ThreadSpawnEdge? Edge);

    public static async Task<SubAgentControlResult> SpawnAgentAsync(
        SubAgentSessionContext context,
        SubAgentSpawnOptions options,
        bool waitForCompletion,
        SubAgentCoordinator? coordinator,
        CancellationToken ct)
    {
        var prompt = NormalizeRequired(options.AgentPrompt, nameof(options.AgentPrompt));
        var taskName = AgentPath.ValidateTaskName(NormalizeRequired(options.TaskName, nameof(options.TaskName)), nameof(options.TaskName));
        var parentPath = GetCurrentAgentPath(context.ParentThread);
        var agentPath = parentPath.Join(taskName);
        await ThrowIfDuplicateSiblingPathAsync(context.SessionService, context.ParentThread.Id, taskName, agentPath, ct);
        var forkTurns = NormalizeForkTurns(options.ForkTurns);
        var childThreadId = SessionIdGenerator.NewThreadId();
        var nickname = NormalizeNickname(options.AgentNickname, taskName);
        var roleRegistry = new SubAgentRoleRegistry(options.RoleConfigs);
        if (!roleRegistry.TryGet(options.AgentRole, out var roleConfig))
        {
            var unknownRole = NormalizeOptional(options.AgentRole) ?? SubAgentRoleNames.Default;
            throw new InvalidOperationException($"Unknown subagent role '{unknownRole}'.");
        }

        var role = roleConfig.Name;
        var requestedProfileName = NormalizeOptional(options.ProfileName);
        var requestedWorkingDirectory = NormalizeOptional(options.WorkingDirectory);
        var request = new SubAgentTaskRequest
        {
            Task = prompt,
            Label = nickname,
            WorkingDirectory = requestedWorkingDirectory,
            ApprovalContext = ApprovalContextScope.Current
        };
        var prepared = PrepareRun(coordinator, request, requestedProfileName, context.ParentThread.WorkspacePath);
        var profileName = prepared?.Profile.Name ?? SubAgentCoordinator.DefaultProfileName;
        var runtimeType = prepared?.Runtime.RuntimeType ?? NativeSubAgentRuntime.RuntimeTypeName;
        if (prepared != null
            && !string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            var forkContext = BuildExternalForkContext(context.ParentThread, forkTurns);
            prepared = prepared with
            {
                Request = prepared.Request with
                {
                    Task = BuildExternalRuntimePrompt(prompt, roleConfig, forkContext)
                }
            };
        }

        var workspace = prepared?.LaunchContext.WorkingDirectory
            ?? requestedWorkingDirectory
            ?? context.ParentThread.WorkspacePath;
        var capabilities = ResolveCapabilities(runtimeType, prepared?.Profile, coordinator);
        var depth = context.Depth + 1;
        var maxDepth = Math.Max(1, options.MaxDepth);
        if (depth > maxDepth)
            throw new InvalidOperationException($"Subagent depth limit reached. Maximum depth is {maxDepth}.");
        var now = DateTimeOffset.UtcNow;
        var childConfiguration = ApplyRoleToChildConfiguration(
            context.ParentThread.Configuration,
            roleConfig,
            string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase)
                ? options.SubAgentModel
                : null,
            depth,
            maxDepth);

        var source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            ParentThreadId = context.ParentThread.Id,
            ParentTurnId = context.ParentTurnId,
            RootThreadId = context.RootThreadId,
            Depth = depth,
            AgentPath = agentPath.Value,
            TaskName = taskName,
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = capabilities.SupportsClose
        });

        var identity = new SessionIdentity
        {
            WorkspacePath = workspace,
            UserId = context.ParentThread.UserId,
            ChannelName = SubAgentThreadOrigin.ChannelName,
            ChannelContext = context.ParentThread.Id
        };

        var childThread = await context.SessionService.CreateThreadAsync(
            identity,
            childConfiguration,
            HistoryMode.Server,
            childThreadId,
            nickname,
            ct,
            source);
        ApplyForkTurns(childThread, context.ParentThread, forkTurns, now);
        if (childThread.Turns.Count > 0 && context.SessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(childThread.Id, ct);

        await context.SessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = context.ParentThread.Id,
            ChildThreadId = childThread.Id,
            ParentTurnId = context.ParentTurnId,
            Depth = depth,
            AgentPath = agentPath.Value,
            TaskName = taskName,
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = capabilities.SupportsClose,
            Status = ThreadSpawnEdgeStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);
        NotifyAgentChange();

        var childCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var completion = string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase)
            ? RunChildTurnAsync(context.SessionService, childThread.Id, prompt, childCts.Token)
            : RunExternalChildTurnsAsync(context.SessionService, coordinator, prepared!, childThread.Id, prompt, childCts.Token);
        RunningChildren[childThread.Id] = new RunningChild(context.ParentThread.Id, childCts, completion);
        _ = ObserveChildCompletionAsync(context.SessionService, childThread.Id, completion);

        if (!waitForCompletion)
        {
            return new SubAgentControlResult
            {
                ChildThreadId = childThread.Id,
                AgentPath = agentPath.Value,
                TaskName = taskName,
                Status = "running",
                AgentNickname = nickname,
                AgentRole = role,
                ProfileName = profileName,
                RuntimeType = runtimeType,
                SupportsSendInput = capabilities.SupportsSendInput,
                SupportsResume = capabilities.SupportsResume,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = capabilities.SupportsClose
            };
        }

        var result = await completion.WaitAsync(ct);
        return new SubAgentControlResult
        {
            ChildThreadId = childThread.Id,
            AgentPath = agentPath.Value,
            TaskName = taskName,
            Status = result.Status,
            Message = result.Message,
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = capabilities.SupportsClose
        };
    }

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

        await context.SessionService.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = NewMailboxEntryId(),
            RootThreadId = context.RootThreadId,
            SenderAgentPath = senderPath.Value,
            TargetAgentPath = resolved.Path.Value,
            Message = normalizedMessage,
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        NotifyAgentChange();

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
        if (resolved.Path.IsRoot)
            throw new InvalidOperationException("FollowupTask target cannot be '/root'.");

        var senderPath = GetCurrentAgentPath(context.ParentThread);
        if (string.Equals(senderPath.Value, resolved.Path.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("FollowupTask target cannot be the current agent.");

        var pending = await context.SessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            resolved.Path.Value,
            ct);
        var turnPrompt = BuildFollowupPrompt(pending, normalizedMessage);
        if (!string.Equals(
                resolved.Thread.Source.SubAgent?.RuntimeType,
                NativeSubAgentRuntime.RuntimeTypeName,
                StringComparison.OrdinalIgnoreCase))
        {
            turnPrompt = BuildExternalThreadContextPrompt(resolved.Thread, turnPrompt);
        }

        SubAgentControlResult result;
        if (HasActiveTurn(resolved.Thread))
        {
            result = await QueueFollowupTaskAsync(
                context.SessionService,
                resolved,
                turnPrompt,
                normalizedMessage,
                ct);
        }
        else
        {
            result = await StartChildTurnAsync(
                context.SessionService,
                resolved.Thread,
                turnPrompt,
                coordinator,
                requireExternalResume: false,
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

        NotifyAgentChange();
        result.AgentPath = resolved.Path.Value;
        result.TaskName = resolved.Edge?.TaskName;
        result.SupportsSendMessage = resolved.Edge?.SupportsSendMessage ?? true;
        result.SupportsFollowupTask = resolved.Edge?.SupportsFollowupTask ?? true;
        return result;
    }

    public static async Task<SubAgentListResult> ListAgentsAsync(
        SubAgentSessionContext context,
        string? pathPrefix,
        CancellationToken ct)
    {
        var prefix = string.IsNullOrWhiteSpace(pathPrefix)
            ? (AgentPath?)null
            : GetCurrentAgentPath(context.ParentThread).Resolve(pathPrefix);
        var root = await context.SessionService.GetThreadAsync(context.RootThreadId, ct);
        var items = new List<SubAgentListItem>
        {
            new()
            {
                AgentPath = AgentPath.Root,
                Status = ResolveThreadStatus(root, null),
                DisplayName = root.DisplayName,
                LastTaskMessage = ExtractLastTaskMessage(root)
            }
        };

        await AddAgentListChildrenAsync(context.SessionService, context.RootThreadId, items, includeClosed: false, ct);
        var filtered = items
            .Where(item => prefix == null || AgentPath.Parse(item.AgentPath).IsSameOrDescendantOf(prefix.Value))
            .OrderBy(item => item.AgentPath == AgentPath.Root ? 0 : 1)
            .ThenBy(item => item.AgentPath, StringComparer.Ordinal)
            .ToArray();

        return new SubAgentListResult { Data = filtered };
    }

    public static async Task<SubAgentWaitResult> WaitAgentAsync(
        SubAgentSessionContext context,
        int? timeoutMs,
        CancellationToken ct)
    {
        var effectiveTimeoutMs = timeoutMs ?? DefaultWaitAgentTimeoutMs;
        if (effectiveTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "timeoutMs must be greater than zero.");

        var waitTask = CaptureChangeSignal();
        var currentPath = GetCurrentAgentPath(context.ParentThread);
        var pending = await context.SessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            currentPath.Value,
            ct);
        if (pending.Count > 0)
            return new SubAgentWaitResult { Status = "changed", TimedOut = false };

        try
        {
            await waitTask.WaitAsync(TimeSpan.FromMilliseconds(effectiveTimeoutMs), ct);
            return new SubAgentWaitResult { Status = "changed", TimedOut = false };
        }
        catch (TimeoutException)
        {
            return new SubAgentWaitResult { Status = "timeout", TimedOut = true };
        }
    }

    public static async Task<SubAgentControlResult> SendInputAsync(
        ISessionService sessionService,
        string childThreadId,
        string message,
        SubAgentCoordinator? coordinator,
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
            ct);
    }

    private static async Task<SubAgentControlResult> StartChildTurnAsync(
        ISessionService sessionService,
        SessionThread child,
        string message,
        SubAgentCoordinator? coordinator,
        bool requireExternalResume,
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
        Task<SubAgentRunResult> completion;
        if (string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            completion = RunChildTurnAsync(sessionService, childThreadId, message, childCts.Token);
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
                childCts.Token);
        }

        RunningChildren[childThreadId] = new RunningChild(parentThreadId, childCts, completion);
        _ = ObserveChildCompletionAsync(sessionService, childThreadId, completion);

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

    private static async Task<SubAgentControlResult> QueueFollowupTaskAsync(
        ISessionService sessionService,
        ResolvedAgentTarget resolved,
        string turnPrompt,
        string displayText,
        CancellationToken ct)
    {
        var materializedPart = new SessionWireInputPart { Type = "text", Text = turnPrompt };
        var nativePart = new SessionWireInputPart { Type = "text", Text = displayText };
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = SubAgentFollowupTriggerKind,
            Label = BuildFollowupTriggerLabel(resolved),
            RefId = resolved.Path.Value
        });

        await sessionService.EnqueueTurnInputAsync(
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

        return new SubAgentControlResult
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
    }

    public static async Task<SubAgentControlResult> WaitAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        int? timeoutSeconds,
        CancellationToken ct)
    {
        SubAgentRunResult result;
        if (RunningChildren.TryGetValue(childThreadId, out var running))
        {
            try
            {
                var waitTask = running.Completion;
                if (timeoutSeconds is > 0)
                    waitTask = waitTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds.Value), ct);
                result = await waitTask.WaitAsync(ct);
            }
            catch (TimeoutException)
            {
                result = new SubAgentRunResult
                {
                    ThreadId = childThreadId,
                    Status = "timeout",
                    Message = "Timed out waiting for subagent; it may still be running."
                };
            }
        }
        else
        {
            var loadedThread = await sessionService.GetThreadAsync(childThreadId, ct);
            var lastTurn = loadedThread.Turns.LastOrDefault();
            result = new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = lastTurn?.Status.ToString().ToLowerInvariant() ?? "idle",
                Message = ExtractFinalAgentText(lastTurn)
            };
        }

        var thread = await sessionService.GetThreadAsync(childThreadId, ct);
        var source = thread.Source.SubAgent;
        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = source?.AgentPath,
            TaskName = source?.TaskName,
            Status = result.Status,
            Message = result.Message,
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole,
            ProfileName = source?.ProfileName,
            RuntimeType = source?.RuntimeType,
            SupportsSendInput = source?.SupportsSendInput ?? true,
            SupportsResume = source?.SupportsResume ?? true,
            SupportsSendMessage = source?.SupportsSendMessage ?? true,
            SupportsFollowupTask = source?.SupportsFollowupTask ?? true,
            SupportsClose = source?.SupportsClose ?? true
        };
    }

    public static async Task<SubAgentControlResult> CloseAgentAsync(
        SubAgentSessionContext context,
        string target,
        CancellationToken ct)
    {
        var resolved = await ResolveAgentTargetAsync(
            context.SessionService,
            context.ParentThread,
            context.RootThreadId,
            target,
            requireOpen: false,
            ct);
        if (resolved.Path.IsRoot)
            throw new InvalidOperationException("CloseAgent target cannot be '/root'.");

        var senderPath = GetCurrentAgentPath(context.ParentThread);
        if (string.Equals(senderPath.Value, resolved.Path.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("CloseAgent target cannot be the current agent.");

        var result = await CloseAgentAsync(context.SessionService, resolved.ThreadId, ct);
        result.AgentPath = resolved.Path.Value;
        result.TaskName = resolved.Edge?.TaskName;
        return result;
    }

    public static async Task<SubAgentControlResult> CloseAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        CancellationToken ct)
    {
        var child = await sessionService.GetThreadAsync(childThreadId, ct);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext;
        if (RunningChildren.TryRemove(childThreadId, out var running))
        {
            try
            {
                await running.Cancellation.CancelAsync();
                await running.Completion.WaitAsync(CloseAgentCancellationWait, ct);
            }
            catch (TimeoutException)
            {
                // Best-effort: fall through and explicitly cancel any active turn snapshot below.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // The running task may surface cancellation directly instead of returning a cancelled result.
            }
            finally
            {
                running.Cancellation.Dispose();
            }

            child = await sessionService.GetThreadAsync(childThreadId, ct);
        }

        var activeTurn = child.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (activeTurn != null)
        {
            if (sessionService is ISubAgentSyntheticTurnService syntheticTurns
                && !string.Equals(
                    child.Source.SubAgent?.RuntimeType,
                    NativeSubAgentRuntime.RuntimeTypeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await syntheticTurns.CancelSubAgentSyntheticTurnAsync(
                    childThreadId,
                    activeTurn.Id,
                    "Subagent was cancelled.",
                    CancellationToken.None);
            }
            else
            {
                await sessionService.CancelTurnAsync(childThreadId, activeTurn.Id, ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(parentThreadId))
            await sessionService.SetThreadSpawnEdgeStatusAsync(parentThreadId!, childThreadId, ThreadSpawnEdgeStatus.Closed, ct);
        NotifyAgentChange();

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            AgentPath = child.Source.SubAgent?.AgentPath,
            TaskName = child.Source.SubAgent?.TaskName,
            Status = ThreadSpawnEdgeStatus.Closed,
            AgentNickname = child.Source.SubAgent?.AgentNickname,
            AgentRole = child.Source.SubAgent?.AgentRole,
            ProfileName = child.Source.SubAgent?.ProfileName,
            RuntimeType = child.Source.SubAgent?.RuntimeType,
            SupportsSendInput = child.Source.SubAgent?.SupportsSendInput ?? true,
            SupportsResume = child.Source.SubAgent?.SupportsResume ?? true,
            SupportsSendMessage = child.Source.SubAgent?.SupportsSendMessage ?? true,
            SupportsFollowupTask = child.Source.SubAgent?.SupportsFollowupTask ?? true,
            SupportsClose = child.Source.SubAgent?.SupportsClose ?? true
        };
    }

    public static async Task<SubAgentControlResult> ResumeAgentAsync(
        ISessionService sessionService,
        string childThreadId,
        CancellationToken ct)
    {
        var child = await sessionService.ResumeThreadAsync(childThreadId, ct);
        var parentThreadId = child.Source.SubAgent?.ParentThreadId ?? child.ChannelContext;
        if (!string.IsNullOrWhiteSpace(parentThreadId))
            await sessionService.SetThreadSpawnEdgeStatusAsync(parentThreadId!, childThreadId, ThreadSpawnEdgeStatus.Open, ct);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            Status = ThreadSpawnEdgeStatus.Open,
            AgentNickname = child.Source.SubAgent?.AgentNickname,
            AgentRole = child.Source.SubAgent?.AgentRole,
            ProfileName = child.Source.SubAgent?.ProfileName,
            RuntimeType = child.Source.SubAgent?.RuntimeType,
            SupportsSendInput = child.Source.SubAgent?.SupportsSendInput ?? true,
            SupportsResume = child.Source.SubAgent?.SupportsResume ?? true,
            SupportsClose = child.Source.SubAgent?.SupportsClose ?? true
        };
    }

    private static async Task<SubAgentRunResult> RunChildTurnAsync(
        ISessionService sessionService,
        string childThreadId,
        string prompt,
        CancellationToken ct)
    {
        try
        {
            SessionTurn? finalTurn = null;
            await foreach (var ev in sessionService.SubmitInputAsync(
                               childThreadId,
                               [new TextContent(prompt)],
                               ct: ct).WithCancellation(ct))
            {
                if (ev.EventType is SessionEventType.TurnCompleted
                    or SessionEventType.TurnCancelled
                    or SessionEventType.TurnFailed)
                {
                    finalTurn = ev.TurnPayload;
                }
            }

            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = finalTurn?.Status.ToString().ToLowerInvariant() ?? "completed",
                Message = ExtractFinalAgentText(finalTurn)
            };
        }
        catch (OperationCanceledException)
        {
            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = "cancelled",
                Message = "Subagent was cancelled."
            };
        }
    }

    private static async Task<SubAgentRunResult> RunExternalChildTurnsAsync(
        ISessionService sessionService,
        SubAgentCoordinator? coordinator,
        SubAgentPreparedRun prepared,
        string childThreadId,
        string prompt,
        CancellationToken ct)
    {
        var result = await RunExternalChildTurnOnceAsync(sessionService, coordinator, prepared, childThreadId, prompt, ct);

        while (string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var queued = await TryTakeNextSubAgentFollowupQueuedInputAsync(sessionService, childThreadId, ct);
            if (queued == null)
                return result;

            prompt = BuildQueuedInputPrompt(queued);
            var child = await sessionService.GetThreadAsync(childThreadId, ct);
            prepared = PrepareExternalChildRun(child, prompt, coordinator, requireExternalResume: false).Run;
            result = await RunExternalChildTurnOnceAsync(sessionService, coordinator, prepared, childThreadId, prompt, ct);
        }

        return result;
    }

    private static async Task<SubAgentRunResult> RunExternalChildTurnOnceAsync(
        ISessionService sessionService,
        SubAgentCoordinator? coordinator,
        SubAgentPreparedRun prepared,
        string childThreadId,
        string prompt,
        CancellationToken ct)
    {
        if (coordinator == null)
            throw new InvalidOperationException("External subagent profiles require a SubAgentCoordinator.");
        if (sessionService is not ISubAgentSyntheticTurnService syntheticTurns)
            throw new InvalidOperationException("Session service does not support external subagent synthetic turns.");

        SessionTurn? turn = null;
        try
        {
            turn = await syntheticTurns.StartSubAgentSyntheticTurnAsync(
                childThreadId,
                [new TextContent(prompt)],
                prepared.Runtime.RuntimeType,
                prepared.Profile.Name,
                ct);
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

    private static async Task ThrowIfDuplicateSiblingPathAsync(
        ISessionService sessionService,
        string parentThreadId,
        string taskName,
        AgentPath agentPath,
        CancellationToken ct)
    {
        var siblings = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed: true, ct);
        if (siblings.Any(edge =>
                string.Equals(edge.TaskName, taskName, StringComparison.Ordinal)
                || string.Equals(edge.AgentPath, agentPath.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Subagent taskName '{taskName}' already exists under this parent.");
        }
    }

    private static AgentPath GetCurrentAgentPath(SessionThread thread)
    {
        var source = thread.Source.SubAgent;
        if (source == null)
            return AgentPath.RootPath;
        if (string.IsNullOrWhiteSpace(source.AgentPath))
            throw new InvalidOperationException($"Subagent thread '{thread.Id}' has no agentPath and cannot use path controls.");

        return AgentPath.Parse(source.AgentPath);
    }

    private static async Task<ResolvedAgentTarget> ResolveAgentTargetAsync(
        ISessionService sessionService,
        SessionThread currentThread,
        string rootThreadId,
        string target,
        bool requireOpen,
        CancellationToken ct)
    {
        var currentPath = GetCurrentAgentPath(currentThread);
        var targetPath = currentPath.Resolve(target);
        if (targetPath.IsRoot)
        {
            var root = await sessionService.GetThreadAsync(rootThreadId, ct);
            return new ResolvedAgentTarget(root.Id, targetPath, root, null);
        }

        var queue = new Queue<string>();
        queue.Enqueue(rootThreadId);
        while (queue.Count > 0)
        {
            var parentThreadId = queue.Dequeue();
            var edges = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed: true, ct);
            foreach (var edge in edges
                         .Where(edge => !string.IsNullOrWhiteSpace(edge.AgentPath))
                         .OrderBy(edge => edge.AgentPath, StringComparer.Ordinal))
            {
                var edgePath = AgentPath.Parse(edge.AgentPath!);
                if (string.Equals(edgePath.Value, targetPath.Value, StringComparison.Ordinal))
                {
                    if (requireOpen && string.Equals(edge.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Subagent '{targetPath.Value}' is closed.");

                    var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
                    return new ResolvedAgentTarget(child.Id, edgePath, child, edge);
                }

                if (targetPath.IsSameOrDescendantOf(edgePath))
                    queue.Enqueue(edge.ChildThreadId);
            }
        }

        throw new KeyNotFoundException($"Subagent path '{targetPath.Value}' was not found.");
    }

    private static async Task AddAgentListChildrenAsync(
        ISessionService sessionService,
        string parentThreadId,
        List<SubAgentListItem> items,
        bool includeClosed,
        CancellationToken ct)
    {
        var edges = await sessionService.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);
        foreach (var edge in edges
                     .Where(edge => !string.IsNullOrWhiteSpace(edge.AgentPath))
                     .OrderBy(edge => edge.AgentPath, StringComparer.Ordinal))
        {
            var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
            items.Add(new SubAgentListItem
            {
                AgentPath = edge.AgentPath!,
                Status = ResolveThreadStatus(child, edge),
                DisplayName = child.DisplayName ?? edge.AgentNickname ?? edge.TaskName,
                LastTaskMessage = ExtractLastTaskMessage(child)
            });
            await AddAgentListChildrenAsync(sessionService, edge.ChildThreadId, items, includeClosed, ct);
        }
    }

    private static string ResolveThreadStatus(SessionThread thread, ThreadSpawnEdge? edge)
    {
        if (string.Equals(edge?.Status, ThreadSpawnEdgeStatus.Closed, StringComparison.Ordinal))
            return ThreadSpawnEdgeStatus.Closed;

        var active = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (active != null)
            return ToAgentStatus(active.Status);

        var latest = thread.Turns.LastOrDefault();
        return latest == null ? "idle" : ToAgentStatus(latest.Status);
    }

    private static string ToAgentStatus(TurnStatus status) => status.ToString().ToLowerInvariant();

    private static bool HasActiveTurn(SessionThread thread) =>
        thread.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);

    private static string BuildFollowupTriggerLabel(ResolvedAgentTarget resolved) =>
        NormalizeOptional(resolved.Thread.DisplayName)
        ?? NormalizeOptional(resolved.Edge?.AgentNickname)
        ?? NormalizeOptional(resolved.Edge?.TaskName)
        ?? resolved.Path.TaskName;

    private static string? ExtractLastTaskMessage(SessionThread thread) =>
        thread.Turns
            .OrderByDescending(turn => turn.StartedAt)
            .Select(turn => turn.Input?.AsUserMessage?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    private static string BuildFollowupPrompt(IReadOnlyList<SubAgentMailboxEntry> pending, string message)
    {
        if (pending.Count == 0)
            return message;

        var sb = new StringBuilder();
        sb.AppendLine("## Mailbox Messages");
        sb.AppendLine();
        foreach (var entry in pending)
        {
            sb.Append("From ");
            sb.Append(entry.SenderAgentPath);
            sb.AppendLine(":");
            sb.AppendLine(entry.Message.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Task");
        sb.AppendLine();
        sb.Append(message);
        return sb.ToString();
    }

    private static void ApplyForkTurns(
        SessionThread childThread,
        SessionThread parentThread,
        string forkTurns,
        DateTimeOffset now)
    {
        var selected = SelectForkTurns(parentThread, forkTurns, childThread.Id, now);
        if (selected.Count > 0)
            childThread.Turns.AddRange(selected);
    }

    private static string BuildExternalForkContext(SessionThread parentThread, string forkTurns)
    {
        var selected = SelectForkTurns(parentThread, forkTurns, parentThread.Id, DateTimeOffset.UtcNow);
        return RenderTurnsAsPromptContext(selected);
    }

    private static string BuildExternalThreadContextPrompt(SessionThread childThread, string message)
    {
        var context = RenderTurnsAsPromptContext(childThread.Turns.Where(turn => !IsActiveTurn(turn)).ToArray());
        if (string.IsNullOrWhiteSpace(context))
            return message;

        return
$$"""
## Existing Thread Context

{{context}}

## Task

{{message}}
""";
    }

    private static List<SessionTurn> SelectForkTurns(
        SessionThread source,
        string forkTurns,
        string targetThreadId,
        DateTimeOffset now)
    {
        if (string.Equals(forkTurns, "none", StringComparison.OrdinalIgnoreCase))
            return [];

        var stable = source.Turns.Where(turn => !IsActiveTurn(turn)).ToList();
        if (int.TryParse(forkTurns, out var count))
            stable = stable.TakeLast(count).ToList();

        var selected = DeepCloneTurns(stable);
        var active = source.Turns.LastOrDefault(IsActiveTurn);
        if (active?.Input != null)
        {
            var activeInput = DeepCloneTurns([new SessionTurn
            {
                Id = active.Id,
                ThreadId = active.ThreadId,
                Status = TurnStatus.Completed,
                Input = active.Input,
                Items = [active.Input],
                StartedAt = active.StartedAt,
                CompletedAt = now,
                OriginChannel = active.OriginChannel,
                Initiator = active.Initiator
            }]).Single();
            selected.Add(activeInput);
        }

        RetargetTurns(selected, targetThreadId);
        return selected;
    }

    private static List<SessionTurn> DeepCloneTurns(IReadOnlyList<SessionTurn> turns)
    {
        var json = JsonSerializer.Serialize(turns, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<List<SessionTurn>>(json, SessionJsonOptions.Default) ?? [];
    }

    private static void RetargetTurns(List<SessionTurn> turns, string threadId)
    {
        foreach (var turn in turns)
        {
            turn.ThreadId = threadId;
            foreach (var item in turn.Items)
                item.TurnId = turn.Id;
            turn.Input = turn.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage) ?? turn.Input;
        }
    }

    private static bool IsActiveTurn(SessionTurn turn) =>
        turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput;

    private static string RenderTurnsAsPromptContext(IReadOnlyList<SessionTurn> turns)
    {
        var sb = new StringBuilder();
        foreach (var turn in turns)
        {
            var user = turn.Input?.AsUserMessage?.Text;
            if (!string.IsNullOrWhiteSpace(user))
            {
                sb.AppendLine("User:");
                sb.AppendLine(user.Trim());
                sb.AppendLine();
            }

            var agentText = ExtractFinalAgentText(turn);
            if (!string.IsNullOrWhiteSpace(agentText))
            {
                sb.AppendLine("Agent:");
                sb.AppendLine(agentText.Trim());
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeForkTurns(string? forkTurns)
    {
        var normalized = NormalizeOptional(forkTurns) ?? "all";
        if (string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase))
            return "all";
        if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (int.TryParse(normalized, out var count) && count > 0)
            return count.ToString();

        throw new ArgumentException("'forkTurns' must be 'all', 'none', or a positive integer string.", nameof(forkTurns));
    }

    private static string BuildExternalRuntimePrompt(
        string prompt,
        SubAgentRoleConfig role,
        string forkContext)
    {
        var rolePrompt = BuildExternalRolePrompt(prompt, role);
        if (string.IsNullOrWhiteSpace(forkContext))
            return rolePrompt;

        return
$$"""
## Parent Context

{{forkContext}}

{{rolePrompt}}
""";
    }

    private static async Task ObserveChildCompletionAsync(
        ISessionService sessionService,
        string childThreadId,
        Task<SubAgentRunResult> completion)
    {
        SubAgentRunResult result;
        try
        {
            result = await completion.ConfigureAwait(false);
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
            RunningChildren.TryRemove(childThreadId, out var active);
            active?.Cancellation.Dispose();
            NotifyAgentChange();
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

        await sessionService.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = NewMailboxEntryId(),
            RootThreadId = source.RootThreadId,
            SenderAgentPath = childPath.Value,
            TargetAgentPath = childPath.ParentValue!,
            Message = BuildSubAgentCompletionNotification(childPath.Value, status, result.Message),
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
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

    private static string BuildSubAgentCompletionNotification(
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
        return
            SubAgentMailboxDelivery.NotificationStartTag
            + payload.ToJsonString()
            + SubAgentMailboxDelivery.NotificationEndTag;
    }

    private static string NewMailboxEntryId() => $"mailbox_{Guid.NewGuid():N}";

    private static Task CaptureChangeSignal()
    {
        lock (ChangeSignalLock)
            return ChangeSignal.Task;
    }

    private static void NotifyAgentChange()
    {
        TaskCompletionSource signal;
        lock (ChangeSignalLock)
        {
            signal = ChangeSignal;
            ChangeSignal = CreateChangeSignal();
        }

        signal.TrySetResult();
    }

    private static TaskCompletionSource CreateChangeSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        string? profileName,
        string parentWorkspace)
    {
        var effectiveProfileName = NormalizeOptional(profileName) ?? SubAgentCoordinator.DefaultProfileName;
        if (coordinator != null)
            return coordinator.PrepareRun(request, effectiveProfileName);

        if (!string.Equals(effectiveProfileName, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Subagent profile '{effectiveProfileName}' requires profile management, but it is not available.");

        _ = parentWorkspace;
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

    private static ThreadConfiguration ApplyRoleToChildConfiguration(
        ThreadConfiguration? parentConfiguration,
        SubAgentRoleConfig role,
        string? nativeSubAgentModel,
        int childDepth,
        int maxDepth)
    {
        var child = CloneConfiguration(parentConfiguration);
        if (!string.IsNullOrWhiteSpace(role.Mode))
            child.Mode = role.Mode.Trim();
        if (!string.IsNullOrWhiteSpace(role.Model))
            child.Model = role.Model.Trim();
        else if (!string.IsNullOrWhiteSpace(nativeSubAgentModel))
            child.Model = nativeSubAgentModel.Trim();

        child.ToolAllowList = MergeAllowLists(parentConfiguration?.ToolAllowList, role.ToolAllowList);
        child.ToolDenyList = MergeDenyLists(parentConfiguration?.ToolDenyList, role.ToolDenyList);
        child.PromptProfile = NormalizeOptional(role.PromptProfile) ?? SubAgentPromptProfiles.Light;
        child.RoleInstructions = NormalizeOptional(role.Instructions);
        child.OverrideBasePrompt = role.OverrideBasePrompt;
        if (role.OverrideBasePrompt && !string.IsNullOrWhiteSpace(role.Instructions))
            child.AgentInstructions = role.Instructions;

        ApplyAgentControlPolicy(child, role, childDepth, maxDepth);
        return child;
    }

    private static void ApplyAgentControlPolicy(
        ThreadConfiguration child,
        SubAgentRoleConfig role,
        int childDepth,
        int maxDepth)
    {
        var requestedAccess = role.AgentControlToolAccess;
        var requestedAllowed = role.AllowedAgentControlTools
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (requestedAccess == AgentControlToolAccess.Full)
            requestedAllowed = AgentControlToolPolicy.AllToolNames.ToHashSet(StringComparer.Ordinal);

        if (childDepth >= maxDepth)
            requestedAllowed.Remove(nameof(AgentTools.SpawnAgent));

        if (requestedAccess == AgentControlToolAccess.Disabled || requestedAllowed.Count == 0)
        {
            child.AgentControlToolAccess = AgentControlToolAccess.Disabled;
            child.AllowedAgentControlTools = null;
            return;
        }

        child.AgentControlToolAccess = requestedAccess == AgentControlToolAccess.AllowList || childDepth >= maxDepth
            ? AgentControlToolAccess.AllowList
            : AgentControlToolAccess.Full;
        child.AllowedAgentControlTools = child.AgentControlToolAccess == AgentControlToolAccess.AllowList
            ? requestedAllowed.ToArray()
            : null;
    }

    private static ThreadConfiguration CloneConfiguration(ThreadConfiguration? source)
    {
        if (source == null)
            return new ThreadConfiguration();

        return new ThreadConfiguration
        {
            McpServers = source.McpServers?.ToArray(),
            Mode = source.Mode,
            Extensions = source.Extensions?.ToArray(),
            CustomTools = source.CustomTools?.ToArray(),
            ProviderId = source.ProviderId,
            Model = source.Model,
            Reasoning = CloneReasoningConfig(source.Reasoning),
            WorkspaceOverride = source.WorkspaceOverride,
            ToolProfile = source.ToolProfile,
            UseToolProfileOnly = source.UseToolProfileOnly,
            AgentInstructions = source.AgentInstructions,
            ToolAllowList = source.ToolAllowList?.ToArray(),
            ToolDenyList = source.ToolDenyList?.ToArray(),
            AgentControlToolAccess = source.AgentControlToolAccess,
            AllowedAgentControlTools = source.AllowedAgentControlTools?.ToArray(),
            PromptProfile = source.PromptProfile,
            RoleInstructions = source.RoleInstructions,
            OverrideBasePrompt = source.OverrideBasePrompt,
            ApprovalPolicy = source.ApprovalPolicy,
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace
        };
    }

    private static AppConfig.ReasoningConfig? CloneReasoningConfig(AppConfig.ReasoningConfig? source) =>
        source == null
            ? null
            : new AppConfig.ReasoningConfig
            {
                Enabled = source.Enabled,
                Effort = source.Effort,
                Output = source.Output
            };

    private static string[]? MergeAllowLists(string[]? parent, IReadOnlyList<string> role)
    {
        var parentSet = parent?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);
        var roleSet = role.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);
        if (parentSet is not { Count: > 0 })
            return roleSet.Count == 0 ? null : roleSet.ToArray();
        if (roleSet.Count == 0)
            return parentSet.ToArray();

        parentSet.IntersectWith(roleSet);
        return parentSet.Count == 0 ? [] : parentSet.ToArray();
    }

    private static string[]? MergeDenyLists(string[]? parent, IReadOnlyList<string> role)
    {
        var deny = parent?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in role.Where(v => !string.IsNullOrWhiteSpace(v)))
            deny.Add(item);
        return deny.Count == 0 ? null : deny.ToArray();
    }

    private static string BuildExternalRolePrompt(string prompt, SubAgentRoleConfig role)
    {
        if (string.IsNullOrWhiteSpace(role.Instructions))
            return prompt;

        return
$$"""
## SubAgent Role: {{role.Name}}

{{role.Instructions.Trim()}}

## Task

{{prompt}}
""";
    }

    private static string NormalizeRequired(string value, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized == null)
            throw new ArgumentException($"{name} is required.", name);
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeNickname(string? nickname, string prompt)
    {
        var normalized = NormalizeOptional(nickname);
        if (normalized != null)
            return normalized.Length <= 48 ? normalized : normalized[..48];

        var firstLine = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? "Subagent";
        return firstLine.Length <= 48 ? firstLine : firstLine[..48];
    }

    private sealed record SubAgentCapabilities(
        bool SupportsSendInput,
        bool SupportsResume,
        bool SupportsClose);
}
