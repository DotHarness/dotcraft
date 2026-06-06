using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides core tools: file operations, shell execution, web tools, and agent spawning.
/// These tools are available in all running modes.
/// </summary>
public sealed class CoreToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 10; // Core tools have highest priority (lowest number)

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        // When sandbox mode is enabled, SandboxToolProvider supplies shell/file/agent tools.
        // CoreToolProvider only provides web tools in that case to avoid duplication.
        if (context.Config.Tools.Sandbox.Enabled)
            return [];

        var tools = new List<AITool>();
        var requireOutside =
            context.RequireApprovalOutsideWorkspace ?? context.Config.Tools.File.RequireApprovalOutsideWorkspace;
        var fileSearchTimeout = TimeSpan.FromSeconds(Math.Max(1, context.Config.Tools.File.SearchTimeoutSeconds));

        // Agent-control tools are gated by the context policy so session-backed
        // SubAgent child threads cannot recursively spawn/control children.
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
                approvalService: context.ApprovalService,
                traceCollector: context.TraceCollector,
                ripgrepPath: context.Config.Tools.File.RipgrepPath,
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
                context.Config.SubAgent.Model,
                SubAgentWaitAgentTimeoutOptions.FromConfig(context.Config.SubAgent));
        }

        // File tools
        var userDotCraftPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
        var fileTools = new FileTools(
            context.WorkspacePath,
            requireOutside,
            context.Config.Tools.File.MaxFileSize,
            context.ApprovalService,
            context.PathBlacklist,
            trustedReadPaths: [userDotCraftPath],
            lspServerManager: context.LspServerManager,
            ripgrepPath: context.Config.Tools.File.RipgrepPath,
            searchTimeout: fileSearchTimeout);
        tools.Add(AIFunctionFactory.Create(fileTools.ReadFile));
        tools.Add(AIFunctionFactory.Create(fileTools.WriteFile));
        tools.Add(AIFunctionFactory.Create(fileTools.EditFile));
        tools.Add(AIFunctionFactory.Create(fileTools.GrepFiles));
        tools.Add(AIFunctionFactory.Create(fileTools.FindFiles));

        // LSP tool
        if (context.Config.Tools.Lsp.Enabled && context.LspServerManager != null)
        {
            var lspTool = new LspTool(
                context.WorkspacePath,
                context.LspServerManager,
                requireOutside,
                context.Config.Tools.Lsp.MaxFileSize,
                context.ApprovalService,
                context.PathBlacklist);
            tools.Add(AIFunctionFactory.Create(lspTool.LSP));
        }

        // Shell tools
        var shellTools = new ShellTools(
            context.WorkspacePath,
            context.Config.Tools.Shell.Timeout,
            requireOutside,
            context.Config.Tools.Shell.MaxOutputLength,
            approvalService: context.ApprovalService,
            blacklist: context.PathBlacklist,
            backgroundTerminals: context.BackgroundTerminalService);
        tools.Add(AIFunctionFactory.Create(shellTools.Exec));
        tools.Add(AIFunctionFactory.Create(shellTools.WriteStdin));

        // Web tools
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
            context.Config.Tools.Sandbox.Enabled,
            context.Config.Permissions.DefaultApprovalPolicy.ToString(),
            tools.Select(t => t.Name).ToArray());
        var selfLearning = context.Config.Skills.SelfLearning;
        var variantModeEnabled = string.Equals(selfLearning.VariantMode, "enabled", StringComparison.OrdinalIgnoreCase);

        // Effective skill loading is always available; SkillManage remains opt-in.
        var skillViewTool = new SkillViewTool(context.SkillsLoader, variantModeEnabled, target, context.TraceCollector);
        tools.Add(AIFunctionFactory.Create(skillViewTool.SkillView));

        // Skill self-learning mutation tools are opt-in and hidden from the model unless enabled.
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
