using System.Diagnostics;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Security;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
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
        var prepared = PrepareRun(coordinator, request, requestedProfileName);
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
        await EnforceResidencyLimitAsync(context, options.MaxConcurrentSubAgents, ct);
        var now = DateTimeOffset.UtcNow;
        var isNativeRuntime = string.Equals(
            runtimeType,
            NativeSubAgentRuntime.RuntimeTypeName,
            StringComparison.OrdinalIgnoreCase);
        if (options.InvocationModelOverride != null)
        {
            if (!isNativeRuntime)
                throw new InvalidOperationException("Subagent model overrides are available only for native runtimes.");
            if (string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Full-history subagents inherit the parent model and reasoning and do not accept overrides.");
        }
        var childConfiguration = ApplyRoleToChildConfiguration(
            context.ParentThread.Configuration,
            roleConfig,
            isNativeRuntime
                && !string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase)
                ? options.SubAgentPreference
                : null,
            isNativeRuntime
                && !string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase)
                ? options.InvocationModelOverride
                : null,
            isNativeRuntime
                ? options.RuntimeConfig
                : null,
            isNativeRuntime,
            string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase),
            depth,
            maxDepth);
        if (options.InvocationModelOverride != null && options.InvocationModelCatalogSnapshot != null)
        {
            var validationOverride = options.InvocationModelOverride.Model == null
                && options.InvocationModelOverride.Effort != null
                ? new SubAgentInvocationModelOverride
                {
                    Model = childConfiguration.Model,
                    Effort = options.InvocationModelOverride.Effort
                }
                : options.InvocationModelOverride;
            SubAgentModelCatalogSnapshots.ValidateInvocationOverride(
                options.InvocationModelCatalogSnapshot,
                validationOverride);
        }
        if (!isNativeRuntime || !string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase))
            childConfiguration.SubAgentModelCatalogSnapshot = null;

        var source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            Purpose = NormalizeOptional(options.Purpose),
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
            ForkTurns = forkTurns,
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

        await using var startup = new SubAgentStartupScope(context.SessionService, context.ParentThread.Id, childThreadId, options.StartupFailed);
        try
        {
            var childThread = await context.SessionService.CreateThreadAsync(
                identity,
                childConfiguration,
                HistoryMode.Server,
                childThreadId,
                nickname,
                ct,
                source);
            startup.Child = childThread;
            if (options.ChildCreated != null)
                await options.ChildCreated(childThread, ct);
            ApplyForkTurns(childThread, context.ParentThread, forkTurns, now);
            var materializedFork = isNativeRuntime
                                   && string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase)
                                   && context.SessionService is INativeSubAgentForkMaterializationService materializationService
                                   && await materializationService.MaterializeNativeSubAgentForkAsync(
                                       context.ParentThread,
                                       childThread,
                                       context.ParentModelHistory,
                                       ct);
            var inheritedToolBindings = isNativeRuntime
                                        && string.Equals(forkTurns, "all", StringComparison.OrdinalIgnoreCase)
                                        && context.SessionService is IThreadForkToolBindingService forkBindingService
                                        && forkBindingService.TryForkThreadToolBindings(context.ParentThread.Id, childThread.Id);
            if ((childThread.Turns.Count > 0 || materializedFork || inheritedToolBindings)
                && context.SessionService is IThreadAgentRefreshService refreshService)
            {
                await refreshService.RefreshThreadAgentAsync(childThread.Id, ct);
            }

            if (context.SessionService is ISubAgentStartupLifecycleService startupLifecycle)
                await startupLifecycle.PersistPreparedSubAgentAsync(context.ParentThread.Id, childThread.Id, ct);

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
            await RunLifecycleHookAsync(
                context.LifecycleHook,
                HookEvent.SubagentStart,
                childThread,
                "running",
                message: null,
                ct);

            ct.ThrowIfCancellationRequested();
            var childCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startup.Cancellation = childCts;
            var initialTrigger = CreateSubAgentTrigger(SubAgentInputTriggerKind, nickname, agentPath.Value);
            var completion = string.Equals(runtimeType, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase)
                ? RunChildTurnAsync(context.SessionService, childThread.Id, prompt, initialTrigger, childCts.Token, startup.Admission)
                : RunExternalChildTurnsAsync(context.SessionService, coordinator, prepared!, childThread.Id, prompt, initialTrigger, childCts.Token, startup.Admission);
            startup.Completion = completion;
            await startup.Admission.Task.ConfigureAwait(false);
            var runningChild = new RunningChild(context.ParentThread.Id, childCts, completion);
            RunningChildren[childThread.Id] = runningChild;
            _ = ObserveChildCompletionAsync(context.SessionService, childThread.Id, runningChild, context.LifecycleHook);

            if (options.ChildStarted != null)
            {
                try
                {
                    await options.ChildStarted(childThread, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Subagent started observer failed for {0}: {1}", childThread.Id, ex);
                }
            }

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
        catch (Exception ex)
        {
            startup.Failure = ex;
            throw;
        }
    }

}
