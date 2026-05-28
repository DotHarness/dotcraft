using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Provides sandbox-isolated tools as an alternative to <see cref="CoreToolProvider"/>.
/// When sandbox mode is enabled, this provider supplies shell and file tools that
/// execute inside an OpenSandbox container instead of on the host machine.
/// Web tools and agent tools remain unchanged (they don't need isolation).
/// </summary>
public sealed class SandboxToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 10; // Same priority as CoreToolProvider

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var sandboxConfig = context.Config.Tools.Sandbox;
        if (!sandboxConfig.Enabled)
            return [];

        var tools = new List<AITool>();
        var requireOutside =
            context.RequireApprovalOutsideWorkspace ?? context.Config.Tools.File.RequireApprovalOutsideWorkspace;

        // Create sandbox session manager and register for disposal
        var sandboxManager = new SandboxSessionManager(sandboxConfig, context.WorkspacePath);
        context.DisposableResources.Add(sandboxManager);

        // Override the default HostAgentFileSystem with sandbox-aware implementation
        // so channel tools can transparently access sandbox files.
        context.AgentFileSystem = new SandboxAgentFileSystem(sandboxManager);

        // Sandbox shell tools (replaces ShellTools)
        var shellTools = new SandboxShellTools(
            sandboxManager,
            context.Config.Tools.Shell.Timeout,
            context.Config.Tools.Shell.MaxOutputLength);
        tools.Add(AIFunctionFactory.Create(shellTools.Exec));

        // Sandbox file tools (replaces FileTools)
        var fileTools = new SandboxFileTools(
            sandboxManager,
            context.Config.Tools.File.MaxFileSize);
        tools.Add(AIFunctionFactory.Create(fileTools.ReadFile));
        tools.Add(AIFunctionFactory.Create(fileTools.WriteFile));
        tools.Add(AIFunctionFactory.Create(fileTools.EditFile));
        tools.Add(AIFunctionFactory.Create(fileTools.GrepFiles));
        tools.Add(AIFunctionFactory.Create(fileTools.FindFiles));

        // Agent-control tools are gated by the context policy so sandbox and
        // non-sandbox tool sets expose the same SubAgent permissions.
        if (AgentControlToolPolicy.AllowsAny(context))
        {
            var subAgentChatClient = context.ChatClientRegistry.GetSubAgentChatClient(
                context.Config,
                context.EffectiveProviderId,
                context.EffectiveMainModel);
            var subAgentRuntime = context.ChatClientRegistry.ResolveSubAgentRuntime(
                context.Config,
                context.EffectiveProviderId,
                context.EffectiveMainModel);
            var subAgentManager = new SubAgentManager(
                subAgentChatClient,
                context.WorkspacePath,
                maxConcurrency: context.Config.SubagentMaxConcurrency,
                shellTimeout: context.Config.Tools.Shell.Timeout,
                requireApprovalOutsideWorkspace: requireOutside,
                reasoningConfig: context.EffectiveReasoning,
                promptCachingConfig: context.Config.PromptCaching,
                model: subAgentRuntime.Model,
                providerProtocol: subAgentRuntime.Protocol,
                blacklist: context.PathBlacklist,
                sandboxManager: sandboxManager,
                approvalService: context.ApprovalService,
                traceCollector: context.TraceCollector,
                endpoint: subAgentRuntime.EndPoint,
                maxOutputTokens: subAgentRuntime.MaxOutputTokens,
                config: context.Config);
            var subAgentCoordinator = new SubAgentCoordinator(
                context.WorkspacePath,
                [new NativeSubAgentRuntime(subAgentManager), new CliOneshotRuntime()],
                context.Config.SubAgentProfiles,
                context.ApprovalService,
                context.Config.SubAgent.DisabledProfiles,
                context.ExternalCliSessionStore,
                context.Config.SubAgent.EnableExternalCliSessionResume);
            AgentControlToolRegistrar.AddTools(
                tools,
                context,
                subAgentCoordinator,
                context.Config.SubAgent.Roles,
                context.Config.SubAgent.MaxDepth,
                context.Config.SubAgent.Model);
        }

        // Web tools — no isolation needed, reuse as-is
        var webTools = new WebTools(
            context.Config.Tools.Web.MaxChars,
            context.Config.Tools.Web.Timeout,
            context.Config.Tools.Web.SearchMaxResults,
            context.Config.Tools.Web.SearchProvider);
        tools.Add(AIFunctionFactory.Create(webTools.WebSearch));
        tools.Add(AIFunctionFactory.Create(webTools.WebFetch));

        var target = SkillVariantStore.CreateTarget(
            context.EffectiveMainModel,
            context.WorkspacePath,
            sandboxEnabled: true,
            context.Config.Permissions.DefaultApprovalPolicy.ToString(),
            tools.Select(t => t.Name).ToArray());
        var selfLearning = context.Config.Skills.SelfLearning;
        var variantModeEnabled = string.Equals(selfLearning.VariantMode, "enabled", StringComparison.OrdinalIgnoreCase);
        var skillViewTool = new SkillViewTool(context.SkillsLoader, variantModeEnabled, target);
        tools.Add(AIFunctionFactory.Create(skillViewTool.SkillView));

        if (selfLearning.Enabled)
        {
            var mutationApplier = variantModeEnabled
                ? new VariantSkillMutationApplier(context.SkillMutationApplier, context.SkillsLoader, target)
                : context.SkillMutationApplier;
            var skillManageTool = new SkillManageTool(
                mutationApplier,
                selfLearning,
                context.ApprovalService,
                context.ContextPageManager);
            tools.Add(AIFunctionFactory.Create(skillManageTool.SkillManage));
        }

        return tools;
    }
}
