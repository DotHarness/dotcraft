using System.Collections.Concurrent;
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

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? WorkingDirectory { get; set; }

    public IReadOnlyList<SubAgentRoleConfig>? RoleConfigs { get; set; }

    public string? SubAgentModel { get; set; }

    public int MaxDepth { get; set; } = 1;
}

public sealed class SubAgentControlResult
{
    public string ChildThreadId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? RuntimeType { get; set; }

    public bool SupportsSendInput { get; set; }

    public bool SupportsResume { get; set; }

    public bool SupportsClose { get; set; } = true;
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

    private sealed record RunningChild(
        string ParentThreadId,
        CancellationTokenSource Cancellation,
        Task<SubAgentRunResult> Completion);

    private static readonly ConcurrentDictionary<string, RunningChild> RunningChildren = new(StringComparer.Ordinal);

    public static async Task<SubAgentControlResult> SpawnAgentAsync(
        SubAgentSessionContext context,
        SubAgentSpawnOptions options,
        bool waitForCompletion,
        SubAgentCoordinator? coordinator,
        CancellationToken ct)
    {
        var prompt = NormalizeRequired(options.AgentPrompt, nameof(options.AgentPrompt));
        var childThreadId = SessionIdGenerator.NewThreadId();
        var nickname = NormalizeNickname(options.AgentNickname, prompt);
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
            prepared = prepared with
            {
                Request = prepared.Request with
                {
                    Task = BuildExternalRolePrompt(prompt, roleConfig)
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
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
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

        await context.SessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = context.ParentThread.Id,
            ChildThreadId = childThread.Id,
            ParentTurnId = context.ParentTurnId,
            Depth = depth,
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
            SupportsClose = capabilities.SupportsClose,
            Status = ThreadSpawnEdgeStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        var childCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var completion = string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase)
            ? RunChildTurnAsync(context.SessionService, childThread.Id, prompt, childCts.Token)
            : RunExternalChildTurnAsync(context.SessionService, coordinator, prepared!, childThread.Id, prompt, childCts.Token);
        RunningChildren[childThread.Id] = new RunningChild(context.ParentThread.Id, childCts, completion);
        _ = completion.ContinueWith(
            task =>
            {
                RunningChildren.TryRemove(childThread.Id, out var running);
                running?.Cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (!waitForCompletion)
        {
            return new SubAgentControlResult
            {
                ChildThreadId = childThread.Id,
                Status = "running",
                AgentNickname = nickname,
                AgentRole = role,
                ProfileName = profileName,
                RuntimeType = runtimeType,
                SupportsSendInput = capabilities.SupportsSendInput,
                SupportsResume = capabilities.SupportsResume,
                SupportsClose = capabilities.SupportsClose
            };
        }

        var result = await completion.WaitAsync(ct);
        return new SubAgentControlResult
        {
            ChildThreadId = childThread.Id,
            Status = result.Status,
            Message = result.Message,
            AgentNickname = nickname,
            AgentRole = role,
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendInput = capabilities.SupportsSendInput,
            SupportsResume = capabilities.SupportsResume,
            SupportsClose = capabilities.SupportsClose
        };
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
            completion = RunChildTurnAsync(sessionService, childThreadId, normalizedMessage, childCts.Token);
        }
        else
        {
            var profileName = NormalizeOptional(source?.ProfileName)
                ?? throw new InvalidOperationException($"Subagent thread '{childThreadId}' does not record a profile name.");
            var request = new SubAgentTaskRequest
            {
                Task = normalizedMessage,
                Label = source?.AgentNickname,
                WorkingDirectory = child.WorkspacePath,
                ApprovalContext = ApprovalContextScope.Current
            };
            var prepared = coordinator?.PrepareRun(request, profileName)
                ?? throw new InvalidOperationException("SendInput for external subagent profiles requires a SubAgentCoordinator.");
            var capabilities = ResolveCapabilities(prepared.Runtime.RuntimeType, prepared.Profile, coordinator);
            if (!capabilities.SupportsSendInput)
            {
                throw new InvalidOperationException(
                    $"Subagent profile '{prepared.Profile.Name}' does not support SendInput. Enable external CLI session resume and use a resumable profile.");
            }

            resultCapabilities = capabilities;
            completion = RunExternalChildTurnAsync(sessionService, coordinator, prepared, childThreadId, normalizedMessage, childCts.Token);
        }

        RunningChildren[childThreadId] = new RunningChild(parentThreadId, childCts, completion);
        _ = completion.ContinueWith(
            task =>
            {
                RunningChildren.TryRemove(childThreadId, out var active);
                active?.Cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            Status = "running",
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole,
            ProfileName = source?.ProfileName,
            RuntimeType = runtimeType,
            SupportsSendInput = resultCapabilities.SupportsSendInput,
            SupportsResume = resultCapabilities.SupportsResume,
            SupportsClose = resultCapabilities.SupportsClose
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
            Status = result.Status,
            Message = result.Message,
            AgentNickname = source?.AgentNickname,
            AgentRole = source?.AgentRole,
            ProfileName = source?.ProfileName,
            RuntimeType = source?.RuntimeType,
            SupportsSendInput = source?.SupportsSendInput ?? true,
            SupportsResume = source?.SupportsResume ?? true,
            SupportsClose = source?.SupportsClose ?? true
        };
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

        return new SubAgentControlResult
        {
            ChildThreadId = childThreadId,
            Status = ThreadSpawnEdgeStatus.Closed,
            AgentNickname = child.Source.SubAgent?.AgentNickname,
            AgentRole = child.Source.SubAgent?.AgentRole,
            ProfileName = child.Source.SubAgent?.ProfileName,
            RuntimeType = child.Source.SubAgent?.RuntimeType,
            SupportsSendInput = child.Source.SubAgent?.SupportsSendInput ?? true,
            SupportsResume = child.Source.SubAgent?.SupportsResume ?? true,
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

    private static async Task<SubAgentRunResult> RunExternalChildTurnAsync(
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
