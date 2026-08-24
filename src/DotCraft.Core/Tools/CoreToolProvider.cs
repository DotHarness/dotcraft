using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.GeneratedTools.Core;
using DotCraft.Lsp;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>
/// Provides core tools: file operations, shell execution, web tools, and agent spawning.
/// These tools are available in all running modes.
/// </summary>
public sealed class CoreToolSource(
    AppConfig config,
    ChatClientRegistry chatClientRegistry,
    SkillsLoader skillsLoader,
    IApprovalService approvalService,
    IBackgroundTerminalService backgroundTerminalService,
    PathBlacklist? pathBlacklist = null,
    LspServerManager? lspServerManager = null,
    TraceCollector? traceCollector = null,
    ISkillMutationApplier? skillMutationApplier = null,
    IContextPageManager? contextPageManager = null,
    string? userDataPath = null,
    IContributionView? contributions = null) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "core-native";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    protected override ToolPolicyHints GetPolicyHints(AIFunction function, ToolPlanningContext context) =>
        GetNativeApprovalDescriptor(function, context) is null
            ? new ToolPolicyHints()
            : new ToolPolicyHints(RequiresApproval: true);

    /// <inheritdoc />
    protected override string GetDescription(AIFunction function, ToolPlanningContext context) =>
        string.Equals(function.Name, nameof(AgentTools.SpawnAgent), StringComparison.Ordinal)
            ? SubAgentModelCatalogSnapshots.AppendToToolDescription(
                base.GetDescription(function, context),
                context.SubAgentModelCatalogSnapshot)
            : base.GetDescription(function, context);

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, JsonElement>? GetAnnotations(
        AIFunction function,
        ToolPlanningContext context)
    {
        var descriptor = GetNativeApprovalDescriptor(function, context);
        return descriptor is null
            ? null
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["dotcraft/nativeApproval"] = JsonSerializer.SerializeToElement(descriptor)
            };
    }

    private object? GetNativeApprovalDescriptor(AIFunction function, ToolPlanningContext context)
    {
        if (string.Equals(function.Name, "SkillManage", StringComparison.Ordinal))
        {
            return new
            {
                kind = "remoteResource",
                targetArgument = "name",
                operationArgument = "action",
                whenOperationIn = new[] { "create", "delete" }
            };
        }

        if (!RequiresApprovalOutsideWorkspace(context))
            return null;

        return function.Name switch
        {
            "ReadFile" => FileApproval("path", "read", context.WorkspacePath, context.WorkspaceRoots, trustedRead: true),
            "WriteFile" => FileApproval("path", "write", context.WorkspacePath, context.WorkspaceRoots),
            "EditFile" => FileApproval("path", "edit", context.WorkspacePath, context.WorkspaceRoots),
            "GrepFiles" => FileApproval("path", "read", context.WorkspacePath, context.WorkspaceRoots, trustedRead: true),
            "FindFiles" => FileApproval("path", "read", context.WorkspacePath, context.WorkspaceRoots, trustedRead: true),
            "LSP" => FileApproval("filePath", "read", context.WorkspacePath, context.WorkspaceRoots, trustedRead: true),
            "Exec" => new
            {
                kind = "shell",
                targetArgument = "workingDir",
                operationArgument = "command",
                workspacePath = context.WorkspacePath,
                workspaceRoots = context.WorkspaceRoots,
                outsideWorkspaceOnly = true
            },
            _ => null
        };
    }

    /// <summary>
    /// Resolves the effective outside-workspace boundary policy for one planning context.
    /// The thread-scoped override wins over the workspace-level default; false means
    /// outside-workspace file/shell operations are rejected without prompting.
    /// </summary>
    private bool RequiresApprovalOutsideWorkspace(ToolPlanningContext context) =>
        context.RequireApprovalOutsideWorkspace ?? config.Tools.File.RequireApprovalOutsideWorkspace;

    private object FileApproval(
        string targetArgument,
        string operation,
        string workspacePath,
        IReadOnlyList<string> workspaceRoots,
        bool trustedRead = false) => new
    {
        kind = "file",
        targetArgument,
        operation,
        workspacePath,
        workspaceRoots,
        outsideWorkspaceOnly = true,
        trustedReadPaths = trustedRead && userDataPath != null
            ? new[] { userDataPath }
            : Array.Empty<string>()
    };

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        // When sandbox mode is enabled, SandboxToolProvider supplies shell/file/agent tools.
        // CoreToolProvider only provides web tools in that case to avoid duplication.
        if (config.Tools.Sandbox.Enabled)
            return [];

        var tools = new List<AIFunction>();
        var requireOutside = RequiresApprovalOutsideWorkspace(context);
        var fileSearchTimeout = TimeSpan.FromSeconds(Math.Max(1, config.Tools.File.SearchTimeoutSeconds));

        var mainRuntime = chatClientRegistry.ResolveMainRuntime(
            config,
            context.EffectiveProviderId,
            context.EffectiveMainModel);
        var subAgentChatClient = chatClientRegistry.GetSubAgentChatClient(
            config,
            mainRuntime.ProviderId,
            mainRuntime.Model);
        var subAgentRuntime = chatClientRegistry.ResolveSubAgentRuntime(
            config,
            mainRuntime.ProviderId,
            mainRuntime.Model);
        var subAgentPreference = ModelPreferenceRules.Find(
            config.SubAgent.ProviderPreferences,
            mainRuntime.ProviderId);
        var subAgentManager = new SubAgentManager(
            subAgentChatClient,
            context.WorkspacePath,
            backgroundTerminalService,
            maxConcurrency: config.SubagentMaxConcurrency,
            shellTimeout: config.Tools.Shell.Timeout,
            requireApprovalOutsideWorkspace: requireOutside,
            reasoningConfig: subAgentPreference?.Reasoning ?? config.Reasoning,
            promptCachingConfig: config.PromptCaching,
            model: subAgentRuntime.Model,
            providerProtocol: subAgentRuntime.Protocol,
            blacklist: pathBlacklist,
            approvalService: approvalService,
            traceCollector: traceCollector,
            ripgrepPath: config.Tools.File.RipgrepPath,
            endpoint: subAgentRuntime.EndPoint,
            maxOutputTokens: subAgentRuntime.MaxOutputTokens,
            config: config,
            workspaceRoots: context.WorkspaceRoots,
            contributions: contributions);
        var subAgentCoordinator = new SubAgentCoordinator(
            context.WorkspacePath,
            [new NativeSubAgentRuntime(subAgentManager), new CliOneshotRuntime()],
            config.SubAgentProfiles,
            approvalService,
            config.SubAgent.DisabledProfiles,
            externalCliSessionStore: null,
            enableExternalCliSessionResume: config.SubAgent.EnableExternalCliSessionResume,
            catalog: SubAgentProfileCatalog.Resolve(contributions, context.ThreadId));
        var agentTools = new AgentTools(
            subAgentManager: subAgentCoordinator,
            subAgentRoles: config.SubAgent.Roles,
            maxSubAgentDepth: config.SubAgent.MaxDepth,
            subAgentPreference: subAgentPreference,
            appConfig: config,
            waitAgentTimeoutOptions: SubAgentWaitAgentTimeoutOptions.FromConfig(config.SubAgent),
            maxConcurrentSubAgents: config.SubAgent.MaxConcurrentSubAgents,
            modelCatalogSnapshot: context.SubAgentModelCatalogSnapshot,
            inheritedModel: context.EffectiveMainModel);
        tools.Add(GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_SendMessage(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_FollowupTask(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_WaitAgent(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_ListAgents(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_CloseAgent(agentTools));

        // File tools
        var fileTools = new FileTools(
            context.WorkspacePath,
            requireOutside,
            config.Tools.File.MaxFileSize,
            approvalService: null,
            pathBlacklist,
            trustedReadPaths: userDataPath == null ? [] : [userDataPath],
            lspServerManager: lspServerManager,
            ripgrepPath: config.Tools.File.RipgrepPath,
            searchTimeout: fileSearchTimeout,
            workspaceRoots: context.WorkspaceRoots);
        tools.Add(GeneratedToolFunctions.FileTools_ReadFile(fileTools));
        tools.Add(GeneratedToolFunctions.FileTools_WriteFile(fileTools));
        tools.Add(GeneratedToolFunctions.FileTools_EditFile(fileTools));
        tools.Add(GeneratedToolFunctions.FileTools_GrepFiles(fileTools));
        tools.Add(GeneratedToolFunctions.FileTools_FindFiles(fileTools));

        // LSP tool
        if (config.Tools.Lsp.Enabled && lspServerManager != null)
        {
            var lspTool = new LspTool(
                context.WorkspacePath,
                lspServerManager,
                requireOutside,
                config.Tools.Lsp.MaxFileSize,
                approvalService: null,
                pathBlacklist,
                context.WorkspaceRoots);
            tools.Add(GeneratedToolFunctions.LspTool_LSP(lspTool));
        }

        // Shell tools
        var shellTools = new ShellTools(
            context.WorkspacePath,
            backgroundTerminalService,
            config.Tools.Shell.Timeout,
            requireOutside,
            config.Tools.Shell.MaxOutputLength,
            approvalService: null,
            blacklist: pathBlacklist,
            workspaceRoots: context.WorkspaceRoots);
        tools.Add(GeneratedToolFunctions.ShellTools_Exec(shellTools));
        tools.Add(GeneratedToolFunctions.ShellTools_WriteStdin(shellTools));

        // Web tools
        var webTools = new WebTools(
            config.Tools.Web.MaxChars,
            config.Tools.Web.Timeout,
            config.Tools.Web.SearchMaxResults,
            config.Tools.Web.SearchProvider);
        tools.Add(GeneratedToolFunctions.WebTools_WebSearch(webTools));
        tools.Add(GeneratedToolFunctions.WebTools_WebFetch(webTools));

        var target = SkillVariantStore.CreateTarget(
            context.EffectiveMainModel,
            context.WorkspacePath,
            config.Tools.Sandbox.Enabled,
            config.Permissions.DefaultApprovalPolicy.ToString(),
            tools.Select(t => t.Name).ToArray());
        var selfLearning = config.Skills.SelfLearning;
        var variantModeEnabled = string.Equals(selfLearning.VariantMode, "enabled", StringComparison.OrdinalIgnoreCase);

        // Effective skill loading is always available; SkillManage remains opt-in.
        var skillViewTool = new SkillViewTool(skillsLoader, variantModeEnabled, target, traceCollector);
        tools.Add(GeneratedToolFunctions.SkillViewTool_SkillView(skillViewTool));

        // Skill self-learning mutation tools are opt-in and hidden from the model unless enabled.
        if (selfLearning.Enabled)
        {
            var mutationApplier = variantModeEnabled
                ? new VariantSkillMutationApplier(
                    skillMutationApplier ?? new WorkspaceFileSkillMutationApplier(skillsLoader),
                    skillsLoader,
                    target)
                : skillMutationApplier ?? new WorkspaceFileSkillMutationApplier(skillsLoader);
            var skillManageTool = new SkillManageTool(
                mutationApplier,
                selfLearning,
                approvalService: null,
                contextPageManager);
            tools.Add(GeneratedToolFunctions.SkillManageTool_SkillManage(skillManageTool));
        }

        return tools;
    }
}
