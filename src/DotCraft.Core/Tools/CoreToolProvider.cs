using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.GeneratedTools.Core;
using DotCraft.Lsp;
using DotCraft.Security;
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
    PathBlacklist? pathBlacklist = null,
    LspServerManager? lspServerManager = null,
    IBackgroundTerminalService? backgroundTerminalService = null,
    TraceCollector? traceCollector = null,
    ISkillMutationApplier? skillMutationApplier = null,
    IContextPageManager? contextPageManager = null) : AIFunctionToolSource
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

        if (!config.Tools.File.RequireApprovalOutsideWorkspace)
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

    private static object FileApproval(
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
        trustedReadPaths = trustedRead
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft") }
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
        var requireOutside = config.Tools.File.RequireApprovalOutsideWorkspace;
        var fileSearchTimeout = TimeSpan.FromSeconds(Math.Max(1, config.Tools.File.SearchTimeoutSeconds));

        // Agent-control tools are gated by the context policy so session-backed
        // SubAgent child threads cannot recursively spawn/control children.
        if (!context.ProviderCapabilities.Contains("subagent-child"))
        {
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
            var subAgentManager = new SubAgentManager(
                subAgentChatClient,
                context.WorkspacePath,
                maxConcurrency: config.SubagentMaxConcurrency,
                shellTimeout: config.Tools.Shell.Timeout,
                requireApprovalOutsideWorkspace: requireOutside,
                reasoningConfig: config.Reasoning,
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
                workspaceRoots: context.WorkspaceRoots);
            var subAgentCoordinator = new SubAgentCoordinator(
                context.WorkspacePath,
                [new NativeSubAgentRuntime(subAgentManager), new CliOneshotRuntime()],
                config.SubAgentProfiles,
                approvalService,
                config.SubAgent.DisabledProfiles,
                externalCliSessionStore: null,
                config.SubAgent.EnableExternalCliSessionResume);
            var agentTools = new AgentTools(
                subAgentCoordinator,
                config.SubAgent.Roles,
                config.SubAgent.MaxDepth,
                subAgentRuntime.Model,
                SubAgentWaitAgentTimeoutOptions.FromConfig(config.SubAgent),
                config.SubAgent.MaxConcurrentSubAgents);
            tools.Add(GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_SendMessage(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_FollowupTask(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_WaitAgent(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_ListAgents(agentTools));
            tools.Add(GeneratedToolFunctions.AgentTools_CloseAgent(agentTools));
        }

        // File tools
        var userDotCraftPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
        var fileTools = new FileTools(
            context.WorkspacePath,
            requireOutside,
            config.Tools.File.MaxFileSize,
            approvalService: null,
            pathBlacklist,
            trustedReadPaths: [userDotCraftPath],
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
            config.Tools.Shell.Timeout,
            requireOutside,
            config.Tools.Shell.MaxOutputLength,
            approvalService: null,
            blacklist: pathBlacklist,
            backgroundTerminals: backgroundTerminalService,
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
