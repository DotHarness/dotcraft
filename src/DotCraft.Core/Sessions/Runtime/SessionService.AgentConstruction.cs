using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Mcp;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private ChatClientAgent GetThreadAgentOrDefault(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Agent != null
            ? runtime.Agent
            : DefaultAgent;

    private bool HasThreadAgent(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime) && runtime.Agent != null;

    private void SetThreadAgent(string threadId, ChatClientAgent agent)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            runtime.Agent = agent;
    }

    private void ClearThreadAgentCaches(string threadId)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            return;

        runtime.Agent = null;
        runtime.ModeManager = null;
        runtime.MarkToolSnapshotDirty();
    }

    private void ClearAllThreadAgentCaches()
    {
        foreach (var runtime in _runtimeRegistry.Values)
        {
            runtime.Agent = null;
            runtime.MarkToolSnapshotDirty();
        }
    }

    private ThreadConfiguration CaptureThreadConfigurationForNewThread(ThreadConfiguration? source)
    {
        var captured = source == null
            ? new ThreadConfiguration()
            : ThreadConfigurationCloner.Clone(source);
        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        ModelPreference preference;

        if (string.IsNullOrWhiteSpace(captured.Model))
        {
            var runtime = agentFactory.RuntimeContext.ChatClientRegistry
                .ResolveMainRuntime(currentConfig, captured.ProviderId);
            captured.ProviderId = runtime.ProviderId;
            captured.Model = runtime.Model;
            preference = ModelProviderResolver.ResolveMainPreference(currentConfig, runtime.ProviderId);
        }
        else
        {
            var runtime = agentFactory.RuntimeContext.ChatClientRegistry
                .ResolveMainRuntime(currentConfig, captured.ProviderId, captured.Model);
            captured.ProviderId = runtime.ProviderId;
            captured.Model = captured.Model.Trim();
            preference = ModelPreferenceRules.Find(currentConfig.ProviderPreferences, runtime.ProviderId)
                         ?? ModelPreferenceRules.CreateDefault(currentConfig, runtime.ProviderId, captured.Model);
            preference.Model = captured.Model;
            preference = ModelPreferenceRules.Normalize(currentConfig, runtime.ProviderId, preference);
        }

        captured.MemoryEnabled ??= currentConfig.Memory.Enabled;
        captured.Reasoning ??= ThreadConfigurationCloner.CloneReasoningConfig(preference.Reasoning);
        captured.Speed ??= preference.Speed;
        captured.ContextWindow ??= new ThreadContextWindowConfig
        {
            Mode = preference.ContextWindow.Mode
        };

        var normalized = ModelPreferenceRules.Normalize(
            currentConfig,
            captured.ProviderId,
            new ModelPreference
            {
                Model = captured.Model,
                Reasoning = ThreadConfigurationCloner.CloneReasoningConfig(captured.Reasoning),
                Speed = captured.Speed.Value,
                ContextWindow = new ModelPreferenceContextWindow
                {
                    Mode = captured.ContextWindow.Mode
                }
            });
        captured.Model = normalized.Model;
        captured.Reasoning = ThreadConfigurationCloner.CloneReasoningConfig(normalized.Reasoning);
        captured.Speed = normalized.Speed;
        captured.ContextWindow = new ThreadContextWindowConfig
        {
            Mode = normalized.ContextWindow.Mode
        };

        return captured;
    }

    private async Task EnsurePerThreadAgentIfMissingAsync(
        string threadId, SessionThread thread, CancellationToken ct)
    {
        var cacheThreadAgent = _forcePerThreadAgents || RequiresPerThreadAgent(thread);
        if (!cacheThreadAgent
            && _runtimeRegistry.TryGetRuntime(threadId, out var existingRuntime)
            && existingRuntime.LatestToolSnapshot != null
            && !existingRuntime.ToolSnapshotDirty)
        {
            return;
        }

        using (await AcquireThreadAgentLockAsync(threadId, ct))
        {
            if (cacheThreadAgent)
            {
                var snapshotMissing = !_runtimeRegistry.TryGetRuntime(threadId, out var cachedRuntime)
                                      || cachedRuntime.LatestToolSnapshot == null
                                      || cachedRuntime.ToolSnapshotDirty;
                if (!HasThreadAgent(threadId) || snapshotMissing)
                    SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, ct));
                return;
            }

            if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
                || runtime.LatestToolSnapshot == null
                || runtime.ToolSnapshotDirty)
            {
                var builtAgent = await BuildAgentForThreadAsync(thread, ct);
                // A native SpawnAgent source establishes durable thread-specific tool text while
                // building. Re-evaluate after the build so it cannot fall back to defaultAgent.
                if (RequiresPerThreadAgent(thread))
                    SetThreadAgent(threadId, builtAgent);
            }
        }
    }

    private bool RequiresPerThreadAgent(SessionThread thread)
    {
        if (agentFactory.RuntimeContext.McpClientManager != null
            || agentFactory.ToolSources.Any(source => source is IThreadScopedToolSource)
            || pluginToolSourceProviders?.Any() == true)
        {
            return true;
        }

        var config = thread.Configuration;
        return config != null
               && (HasAgentShapingConfiguration(config)
                   || config.SubAgentModelCatalogSnapshot != null
                   || ThreadRuntimeDiffersFromCurrentDefault(config.ProviderId, config.Model)
                   || ThreadReasoningDiffersFromCurrentDefault(config.Reasoning));
    }

    private bool ThreadRuntimeDiffersFromCurrentDefault(string? providerId, string? model)
    {
        if (string.IsNullOrWhiteSpace(providerId) && string.IsNullOrWhiteSpace(model))
            return false;

        try
        {
            var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
            var currentDefault = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(currentConfig);
            if (!string.IsNullOrWhiteSpace(providerId)
                && !string.Equals(providerId.Trim(), currentDefault.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(model)
                && !string.Equals(model.Trim(), currentDefault.Model, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private bool ThreadReasoningDiffersFromCurrentDefault(AppConfig.ReasoningConfig? reasoning)
    {
        if (reasoning == null)
            return false;

        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        return reasoning.Enabled != currentConfig.Reasoning.Enabled
               || reasoning.Effort != currentConfig.Reasoning.Effort
               || reasoning.Output != currentConfig.Reasoning.Output;
    }

    private static bool HasAgentShapingConfiguration(ThreadConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.AgentProfileId) || !string.IsNullOrWhiteSpace(config.MemoryScope))
            return true;
        if (config.MemoryEnabled == false)
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentProfileSource))
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentProfileFingerprint))
            return true;
        if (!string.Equals(config.Mode, "agent", StringComparison.OrdinalIgnoreCase))
            return true;
        if (config.McpServers is not null)
            return true;
        if (config.Extensions is { Length: > 0 })
            return true;
        if (config.CustomTools is { Length: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(config.WorkspaceOverride))
            return true;
        if (!string.IsNullOrWhiteSpace(config.Cwd))
            return true;
        if (config.RuntimeWorkspaceRoots is not null)
            return true;
        if (!string.IsNullOrWhiteSpace(config.ExecutionWorkspaceOverride))
            return true;
        if (!string.IsNullOrWhiteSpace(config.ToolProfile))
            return true;
        if (config.UseToolProfileOnly)
            return true;
        if (!string.IsNullOrWhiteSpace(config.AgentInstructions))
            return true;
        if (config.ToolAllowList is { Length: > 0 })
            return true;
        if (config.ToolDenyList is { Length: > 0 })
            return true;
        if (HasPolicy(config.ToolPolicy))
            return true;
        if (HasPolicy(config.McpPolicy))
            return true;
        if (HasPolicy(config.PluginPolicy))
            return true;
        if (HasPolicy(config.SkillsPolicy))
            return true;
        if (config.AgentControlToolAccess.HasValue)
            return true;
        if (config.AllowedAgentControlTools is { Length: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(config.RoleInstructions))
            return true;
        if (!string.IsNullOrWhiteSpace(config.DeveloperInstructions))
            return true;
        if (config.OverrideBasePrompt)
            return true;
        if (config.ApprovalPolicy != ApprovalPolicy.Default)
            return true;
        if (config.ApprovalTimeoutSeconds.HasValue)
            return true;
        if (!string.IsNullOrWhiteSpace(config.AutomationTaskDirectory))
            return true;
        return config.RequireApprovalOutsideWorkspace.HasValue;
    }

    private static bool HasPolicy(ThreadToolPolicy? policy) =>
        policy != null
        && (policy.Allow != null
            || policy.Deny != null
            || !string.IsNullOrWhiteSpace(policy.AgentControl)
            || policy.AllowedAgentControlTools != null);

    private static bool HasPolicy(ThreadMcpPolicy? policy) =>
        policy != null
        && (policy.Servers != null || HasPolicy(policy.Tools));

    private static bool HasPolicy(ThreadNamePolicy? policy) =>
        policy != null && (policy.Allow != null || policy.Deny != null);

    private static bool HasPolicy(ThreadPluginPolicy? policy) =>
        policy != null && (policy.Allow != null || policy.Deny != null);

    private static bool HasPolicy(ThreadSkillsPolicy? policy) =>
        policy != null
        && (policy.Preload != null
            || policy.Allow != null
            || policy.Deny != null
            || policy.AllowManage.HasValue);

    private async Task<ChatClientAgent> BuildAgentForThreadAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = thread.Id;
        try
        {
            return await BuildAgentForThreadCoreAsync(thread, ct);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    private async Task<ChatClientAgent> BuildAgentForThreadCoreAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var threadId = thread.Id;
        var threadRuntimeState = _runtimeRegistry.SetThread(thread);
        var config = thread.Configuration ?? new ThreadConfiguration();
        var mode = config.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
            ? AgentMode.Plan
            : AgentMode.Agent;
        var mm = GetOrCreateModeManager(threadId, mode);
        var baseCtx = agentFactory.RuntimeContext;
        var currentConfig = _appConfigMonitor?.Current ?? baseCtx.Config;
        var agentControlToolAccess = ResolveAgentControlToolAccess(thread);
        var runtime = baseCtx.ChatClientRegistry.ResolveMainRuntime(currentConfig, config.ProviderId, config.Model);
        var effectiveMainModel = runtime.Model;
        var threadChatClient = ResolveThreadChatClient(baseCtx, runtime);
        var externalCliSessionStore = new ThreadExternalCliSessionStore(thread);
        var threadBaseContext = CloneContextWithChatClient(
            baseCtx,
            currentConfig,
            threadChatClient,
            runtime.ProviderId,
            runtime.Protocol,
            effectiveMainModel,
            externalCliSessionStore,
            thread);

        AgentRuntimeContext? scopedContext = null;
        if (!string.IsNullOrEmpty(config.WorkspaceOverride))
        {
            var craftPath = Path.Combine(config.WorkspaceOverride, Path.GetFileName(DataPath));
            Directory.CreateDirectory(craftPath);

            var scopedDreamStore = new DreamStore(craftPath);
            var scopedSkills = new SkillsLoader(
                craftPath,
                baseCtx.SkillsLoader.UserSkillsPath,
                baseCtx.SkillsLoader.SharedSkillsPath);

            scopedContext = new AgentRuntimeContext(baseCtx)
            {
                Config = currentConfig,
                ChatClient = threadChatClient,
                EffectiveProviderId = runtime.ProviderId,
                EffectiveProviderProtocol = runtime.Protocol,
                EffectiveMainModel = effectiveMainModel,
                EffectiveReasoning = ThreadConfigurationCloner.CloneReasoningConfig(config.Reasoning ?? currentConfig.Reasoning),
                EffectiveSpeed = config.Speed ?? InferenceSpeed.Standard,
                WorkspacePath = config.WorkspaceOverride,
                WorkspaceRoots = [config.WorkspaceOverride],
                BotPath = craftPath,
                DreamStore = scopedDreamStore,
                SkillsLoader = scopedSkills,
                // Skill mutations follow the scoped loader, not the workspace-root one.
                SkillMutationApplier = new WorkspaceFileSkillMutationApplier(scopedSkills),
                PathBlacklist = new PathBlacklist([]),
                ExternalCliSessionStore = externalCliSessionStore,
                AutomationTaskDirectory = config.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = config.RequireApprovalOutsideWorkspace,
                CurrentThreadId = thread.Id,
                CurrentThreadSource = thread.Source,
                AgentBuilderTargetId = config.AgentBuilderTargetId,
                AgentBuilderTargetSource = config.AgentBuilderTargetSource,
                CurrentOriginChannel = thread.OriginChannel,
                CurrentChannelContext = thread.ChannelContext,
                AgentControlToolAccess = agentControlToolAccess,
                AllowedAgentControlTools = ResolveAllowedAgentControlTools(config),
                ToolAllowList = ToSet(config.ToolAllowList),
                ToolDenyList = ToSet(config.ToolDenyList),
                RoleInstructions = config.RoleInstructions,
                DeveloperInstructions = config.DeveloperInstructions
            };
        }

        scopedContext = new AgentRuntimeContext(scopedContext ?? threadBaseContext)
        {
            MemoryStore = ResolveMemoryStore(config),
            MemoryEnabled = config.MemoryEnabled ?? true
        };

        var toolContext = CloneContextWithWorkspace(
            scopedContext ?? threadBaseContext,
            ThreadWorkspaceResolver.Resolve(thread));

        var bindingMcpServers = threadRuntimeState.GetBindingMcpServers();
        if (config.McpServers is not null || bindingMcpServers.Count > 0)
        {
            var inheritedMcpServers = config.McpServers is null && toolContext.McpClientManager is not null
                ? await toolContext.McpClientManager.ListConfigsAsync(ct)
                : [];
            var effectiveMcpServers = McpServerComposition.Compose(
                thread.Id,
                config.McpServers,
                inheritedMcpServers,
                bindingMcpServers);
            var threadMcpManager = threadRuntimeState.McpManager
                ?? new McpClientManager(dotCraftPaths, loggerFactory?.CreateLogger<McpClientManager>());
            await threadMcpManager.ConnectAsync(effectiveMcpServers, ct);
            await threadMcpManager.WaitForStartupCompletionAsync(ct);
            threadRuntimeState.McpManager = threadMcpManager;
            toolContext = CloneContextWithMcpManager(toolContext, threadMcpManager);
        }
        else
        {
            if (threadRuntimeState.McpManager is { } obsoleteThreadManager)
            {
                threadRuntimeState.McpManager = null;
                await obsoleteThreadManager.DisposeAsync();
            }
            if (toolContext.McpClientManager != null)
                await toolContext.McpClientManager.WaitForStartupCompletionAsync(ct);
        }

        toolContext.DeferredToolActivationIndex = null;
        var capabilityPolicy = new ThreadCapabilityPolicyEvaluator(config, toolContext);
        toolDispatchPolicyRegistry?.Bind(thread.Id, config, toolContext);
        toolContext.ToolCallPolicy = capabilityPolicy.EvaluateCall;
        toolContext.ToolInvocationPolicy = capabilityPolicy.EvaluateInvocation;

        var snapshotSources = config.UseToolProfileOnly
            ? agentFactory.GetToolSources(thread.Id)
                .Where(static source => source is UserCoordinationToolSource)
                .ToList()
            : agentFactory.GetToolSources(thread.Id).ToList();
        if (!config.UseToolProfileOnly && toolContext.McpClientManager is not null)
            snapshotSources.Add(new McpToolSource(toolContext.McpClientManager, currentConfig));
        if (!config.UseToolProfileOnly && pluginToolSourceProviders is not null)
        {
            snapshotSources.AddRange(
                pluginToolSourceProviders.SelectMany(provider => provider.CreateToolSourcesForThread(thread)));
        }
        if (!config.UseToolProfileOnly && !string.IsNullOrWhiteSpace(config.AgentBuilderTargetId))
        {
            snapshotSources.Add(new AgentProfileBuilderToolSource(
                toolContext.SkillsLoader,
                toolContext.McpClientManager,
                toolContext.BotPath));
        }
        if (!string.IsNullOrEmpty(config.ToolProfile))
        {
            if (toolProfileRegistry == null
                || !toolProfileRegistry.TryGet(config.ToolProfile, out var profileSources)
                || profileSources == null)
            {
                throw new InvalidOperationException($"Tool profile '{config.ToolProfile}' is not registered.");
            }

            snapshotSources.AddRange(profileSources);
        }

        var exposesNativeSpawnAgent = snapshotSources.Any(source =>
            source is CoreToolSource);
        if (exposesNativeSpawnAgent && config.SubAgentModelCatalogSnapshot == null)
        {
            config.SubAgentModelCatalogSnapshot = await SubAgentModelCatalogSnapshots.CreateAsync(
                currentConfig,
                baseCtx.ChatClientRegistry.ProviderRegistry,
                runtime.ProviderId,
                ct).ConfigureAwait(false);
            await PersistThreadIfMaterializedAsync(thread, ct).ConfigureAwait(false);
        }

        var providerCapabilities = new List<string>();
        if (thread.Source.SubAgent is not null)
            providerCapabilities.Add("subagent-child");
        if (!string.IsNullOrWhiteSpace(config.AgentBuilderTargetId))
        {
            providerCapabilities.Add($"agent-builder-target={config.AgentBuilderTargetId}");
            providerCapabilities.Add($"agent-builder-source={config.AgentBuilderTargetSource ?? AgentProfileSources.Workspace}");
        }
        var planningContext = new ToolPlanningContext(
            thread.Id,
            turnId: null,
            toolContext.WorkspacePath,
            toolContext.BotPath,
            config.Mode,
            config.ToolProfile,
            providerCapabilities,
            threadRuntimeState.NextToolSnapshotRevision(),
            ToolPlanningThreadClassifier.Classify(thread),
            config.ProviderId,
            config.Model,
            toolContext.WorkspaceRoots,
            config.SubAgentModelCatalogSnapshot,
            config.RequireApprovalOutsideWorkspace);
        var toolSnapshot = await agentFactory.BuildToolSnapshotAsync(
            snapshotSources,
            planningContext,
            toolContext,
            ct);
        capabilityPolicy.SetRuntimeManagedTools(toolSnapshot);
        RecordWithheldTools(thread.Id, capabilityPolicy, toolSnapshot);
        toolSnapshot = toolSnapshot.WithModelExposure(definition =>
            toolSnapshot.Registrations.TryGetValue(definition.Name, out var registration)
            && capabilityPolicy.AllowsRegistrationExposure(registration)
            && capabilityPolicy.AllowsTool(AgentFactory.ProjectSnapshotDefinition(toolSnapshot, definition)));
        threadRuntimeState.SetLatestToolSnapshot(toolSnapshot);
        EffectiveToolSnapshotChanged?.Invoke(
            this,
            new EffectiveToolSnapshotChangedEventArgs(thread.Id, toolSnapshot.Revision));
        var snapshotTools = AgentFactory.ProjectSnapshotTools(toolSnapshot);

        if (config.UseToolProfileOnly)
        {
            if (snapshotTools.Count == 0)
                throw new InvalidOperationException("UseToolProfileOnly requires a registered ToolProfile with at least one tool.");
            ApplyThreadToolFilters(snapshotTools, capabilityPolicy);
            return agentFactory.CreateAgentWithToolsAndSnapshot(
                snapshotTools,
                toolSnapshot,
                planningContext,
                mm,
                toolContext,
                config.AgentInstructions);
        }

        var toolsWithMcp = snapshotTools;
        ApplyThreadToolFilters(toolsWithMcp, capabilityPolicy);
        return agentFactory.CreateAgentWithToolsAndSnapshot(
            toolsWithMcp,
            toolSnapshot,
            planningContext,
            mm,
            toolContext);
    }

    private static void ApplyThreadToolFilters(List<AITool> tools, ThreadCapabilityPolicyEvaluator policy) =>
        tools.RemoveAll(tool => !policy.AllowsTool(tool));

    private void RecordWithheldTools(
        string threadId,
        ThreadCapabilityPolicyEvaluator policy,
        EffectiveToolSnapshot snapshot)
    {
        if (TraceCollector is not { } trace)
            return;

        trace.RecordToolPolicyWithheld(
            threadId,
            [.. policy.WithheldFromModel(snapshot).Select(static tool => new ToolPolicyWithheldTraceTool(
                tool.Name,
                tool.Namespace,
                tool.Source,
                tool.Reason))]);
    }

    private static bool TryResolveProviderFunctionCall(
        EffectiveToolSnapshot snapshot,
        FunctionCallContent call,
        out ToolName toolName)
    {
        if (ProviderFunctionCallMetadata.TryGetNamespace(call, out var toolNamespace))
            return snapshot.TryResolveProviderNamespacedName(toolNamespace, call.Name, out toolName);

        return snapshot.TryResolveProviderFlatName(call.Name, out toolName);
    }

    private static IChatClient ResolveThreadChatClient(AgentRuntimeContext baseContext, EffectiveModelRuntime runtime)
    {
        if (baseContext.ChatClient is { } chatClient
            && string.Equals(runtime.ProviderId, baseContext.EffectiveProviderId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(runtime.Protocol, baseContext.EffectiveProviderProtocol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(runtime.Model, baseContext.EffectiveMainModel, StringComparison.Ordinal))
        {
            return chatClient;
        }

        return baseContext.ChatClientRegistry.GetChatClient(runtime);
    }

    private static AgentRuntimeContext CloneContextWithChatClient(
        AgentRuntimeContext source,
        AppConfig config,
        IChatClient chatClient,
        string effectiveProviderId,
        string effectiveProviderProtocol,
        string effectiveMainModel,
        IExternalCliSessionStore? externalCliSessionStore = null,
        SessionThread? thread = null)
    {
        var cloned = new AgentRuntimeContext(source)
        {
            Config = config,
            ChatClient = chatClient,
            EffectiveProviderId = effectiveProviderId,
            EffectiveProviderProtocol = effectiveProviderProtocol,
            EffectiveMainModel = effectiveMainModel,
            EffectiveReasoning = ThreadConfigurationCloner.CloneReasoningConfig(thread?.Configuration?.Reasoning ?? config.Reasoning),
            EffectiveSpeed = thread?.Configuration?.Speed ?? InferenceSpeed.Standard,
            ExternalCliSessionStore = externalCliSessionStore ?? source.ExternalCliSessionStore,
            CurrentThreadId = thread?.Id ?? source.CurrentThreadId,
            CurrentThreadSource = thread?.Source ?? source.CurrentThreadSource,
            AgentBuilderTargetId = thread?.Configuration?.AgentBuilderTargetId ?? source.AgentBuilderTargetId,
            AgentBuilderTargetSource = thread?.Configuration?.AgentBuilderTargetSource ?? source.AgentBuilderTargetSource,
            CurrentOriginChannel = thread?.OriginChannel ?? source.CurrentOriginChannel,
            CurrentChannelContext = thread?.ChannelContext ?? source.CurrentChannelContext,
            AgentControlToolAccess = thread == null
                ? source.AgentControlToolAccess
                : ResolveAgentControlToolAccess(thread),
            AllowedAgentControlTools = thread == null ? source.AllowedAgentControlTools : ResolveAllowedAgentControlTools(thread.Configuration),
            ToolAllowList = thread == null ? source.ToolAllowList : ToSet(thread.Configuration?.ToolAllowList),
            ToolDenyList = thread == null ? source.ToolDenyList : ToSet(thread.Configuration?.ToolDenyList),
            RoleInstructions = thread?.Configuration?.RoleInstructions ?? source.RoleInstructions,
            DeveloperInstructions = thread?.Configuration?.DeveloperInstructions ?? source.DeveloperInstructions
        };
        return cloned;
    }

    private static AgentRuntimeContext CloneContextWithWorkspace(
        AgentRuntimeContext source,
        ThreadWorkspaceContext workspace) =>
        new(source)
        {
            EffectiveReasoning = ThreadConfigurationCloner.CloneReasoningConfig(source.EffectiveReasoning),
            WorkspacePath = workspace.Cwd,
            WorkspaceRoots = workspace.RuntimeWorkspaceRoots,
            AgentFileSystem = new HostAgentFileSystem(workspace.Cwd)
        };

    private static AgentRuntimeContext CloneContextWithMcpManager(
        AgentRuntimeContext source,
        McpClientManager mcpClientManager) =>
        new(source)
        {
            EffectiveReasoning = ThreadConfigurationCloner.CloneReasoningConfig(source.EffectiveReasoning),
            McpClientManager = mcpClientManager,
            // Rebuilt per turn by the deferred-loading planner; never carried across an MCP rebind.
            DeferredToolActivationIndex = null
        };

    private static IReadOnlySet<string>? ToSet(IEnumerable<string>? values)
    {
        if (values == null)
            return null;

        var set = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        return set.Count == 0 ? null : set;
    }

    private static IReadOnlySet<string>? ResolveAllowedAgentControlTools(ThreadConfiguration? config)
    {
        if (config == null)
            return null;

        var legacyRequiresAllowList = config.AgentControlToolAccess == AgentControlToolAccess.AllowList;
        var structuredRequiresAllowList =
            ParseAgentControlToolAccess(config.ToolPolicy?.AgentControl) == AgentControlToolAccess.AllowList;
        if (!legacyRequiresAllowList && !structuredRequiresAllowList)
            return null;

        HashSet<string>? result = null;
        if (legacyRequiresAllowList)
            result = ToSetPreserveEmpty(config.AllowedAgentControlTools);

        if (structuredRequiresAllowList)
        {
            var structured = ToSetPreserveEmpty(config.ToolPolicy?.AllowedAgentControlTools);
            result = result == null
                ? structured
                : Intersect(result, structured);
        }

        return result ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private static HashSet<string> ToSetPreserveEmpty(IEnumerable<string>? values)
    {
        if (values == null)
            return new HashSet<string>(StringComparer.Ordinal);

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Intersect(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var result = new HashSet<string>(left, StringComparer.Ordinal);
        result.IntersectWith(right);
        return result;
    }
}
