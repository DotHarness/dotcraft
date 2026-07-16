using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools.Sandbox;

/// <summary>Contributes thread-scoped sandbox file, shell, web, skill, and agent tools.</summary>
public sealed class SandboxToolSource(
    AppConfig config,
    ChatClientRegistry chatClientRegistry,
    SkillsLoader skillsLoader,
    IApprovalService approvalService,
    PathBlacklist? pathBlacklist = null,
    TraceCollector? traceCollector = null,
    ISkillMutationApplier? skillMutationApplier = null,
    IContextPageManager? contextPageManager = null)
    : AIFunctionToolSource, IThreadScopedToolSource, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SandboxSessionManager> _managers = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override string SourceId => "sandbox-native";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (!config.Tools.Sandbox.Enabled)
            return [];

        var manager = _managers.GetOrAdd(
            context.ThreadId,
            _ => new SandboxSessionManager(config.Tools.Sandbox, context.WorkspacePath));
        var tools = new List<AIFunction>();
        var shellTools = new SandboxShellTools(
            manager,
            config.Tools.Shell.Timeout,
            config.Tools.Shell.MaxOutputLength);
        tools.Add(GeneratedToolFunctions.SandboxShellTools_Exec(shellTools));

        var fileTools = new SandboxFileTools(manager, config.Tools.File.MaxFileSize);
        tools.Add(GeneratedToolFunctions.SandboxFileTools_ReadFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_WriteFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_EditFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_GrepFiles(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_FindFiles(fileTools));

        if (!context.ProviderCapabilities.Contains("subagent-child"))
        {
            var mainRuntime = chatClientRegistry.ResolveMainRuntime(
                config,
                context.EffectiveProviderId,
                context.EffectiveMainModel);
            var subAgentRuntime = chatClientRegistry.ResolveSubAgentRuntime(
                config,
                mainRuntime.ProviderId,
                mainRuntime.Model);
            var managerRuntime = new SubAgentManager(
                chatClientRegistry.GetSubAgentChatClient(config, mainRuntime.ProviderId, mainRuntime.Model),
                context.WorkspacePath,
                maxConcurrency: config.SubagentMaxConcurrency,
                shellTimeout: config.Tools.Shell.Timeout,
                requireApprovalOutsideWorkspace: config.Tools.File.RequireApprovalOutsideWorkspace,
                reasoningConfig: config.Reasoning,
                promptCachingConfig: config.PromptCaching,
                model: subAgentRuntime.Model,
                providerProtocol: subAgentRuntime.Protocol,
                blacklist: pathBlacklist,
                sandboxManager: manager,
                approvalService: approvalService,
                traceCollector: traceCollector,
                endpoint: subAgentRuntime.EndPoint,
                maxOutputTokens: subAgentRuntime.MaxOutputTokens,
                config: config);
            var coordinator = new SubAgentCoordinator(
                context.WorkspacePath,
                [new NativeSubAgentRuntime(managerRuntime), new CliOneshotRuntime()],
                config.SubAgentProfiles,
                approvalService,
                config.SubAgent.DisabledProfiles,
                externalCliSessionStore: null,
                config.SubAgent.EnableExternalCliSessionResume);
            var agentTools = new AgentTools(
                coordinator,
                config.SubAgent.Roles,
                config.SubAgent.MaxDepth,
                subAgentRuntime.Model,
                SubAgentWaitAgentTimeoutOptions.FromConfig(config.SubAgent));
            tools.Add(GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_SendMessage(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_FollowupTask(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_WaitAgent(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_ListAgents(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_CloseAgent(agentTools));
        }

        var webTools = new WebTools(
            config.Tools.Web.MaxChars,
            config.Tools.Web.Timeout,
            config.Tools.Web.SearchMaxResults,
            config.Tools.Web.SearchProvider);
        tools.Add(GeneratedToolFunctions.WebTools_WebSearch(webTools));
        tools.Add(GeneratedToolFunctions.WebTools_WebFetch(webTools));

        var target = SkillVariantStore.CreateTarget(
            config.Model,
            context.WorkspacePath,
            sandboxEnabled: true,
            config.Permissions.DefaultApprovalPolicy.ToString(),
            tools.Select(tool => tool.Name).ToArray());
        var selfLearning = config.Skills.SelfLearning;
        var variantModeEnabled = string.Equals(
            selfLearning.VariantMode,
            "enabled",
            StringComparison.OrdinalIgnoreCase);
        tools.Add(GeneratedToolFunctions.SkillViewTool_SkillView(
            new SkillViewTool(skillsLoader, variantModeEnabled, target, traceCollector)));

        if (selfLearning.Enabled)
        {
            var baseApplier = skillMutationApplier ?? new WorkspaceFileSkillMutationApplier(skillsLoader);
            var applier = variantModeEnabled
                ? new VariantSkillMutationApplier(baseApplier, skillsLoader, target)
                : baseApplier;
            tools.Add(GeneratedToolFunctions.SkillManageTool_SkillManage(
                new SkillManageTool(applier, selfLearning, approvalService, contextPageManager)));
        }

        return tools;
    }

    /// <summary>Releases the sandbox owned by one thread.</summary>
    public async ValueTask ReleaseThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_managers.TryRemove(threadId, out var manager))
            await manager.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var manager in _managers.Values)
            await manager.DisposeAsync().ConfigureAwait(false);
        _managers.Clear();
    }
}
