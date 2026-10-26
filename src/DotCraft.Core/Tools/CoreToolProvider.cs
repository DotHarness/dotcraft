using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.GeneratedTools.Core;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
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
    string? userDataPath = null,
    IContributionView? contributions = null,
    IProviderHttpTransport? providerHttpTransport = null) : AIFunctionToolSource
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
        if (!RequiresApprovalOutsideWorkspace(context))
            return null;

        return FileToolAccess.TryGet(function.Name, out var access)
            ? FileApproval(access.PathArgument, access.Operation, context.WorkspacePath, context.WorkspaceRoots,
                context, trustedRead: access.Operation == "read")
            : null;
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
        ToolPlanningContext context,
        bool trustedRead = false) => new
    {
        kind = "file",
        targetArgument,
        operation,
        workspacePath,
        workspaceRoots,
        outsideWorkspaceOnly = true,
        trustedReadPaths = trustedRead
            ? (string[])[.. userDataPath == null ? [] : new[] { userDataPath }, .. context.MemoryStores?.ReadOnlyDirectories ?? []]
            : Array.Empty<string>(),
        trustedPaths = (string[])[.. context.MemoryStores?.WritableDirectories ?? []]
    };

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        var tools = new List<AIFunction>();
        var requireOutside = RequiresApprovalOutsideWorkspace(context);
        var fileSearchTimeout = TimeSpan.FromSeconds(Math.Max(1, config.Tools.File.SearchTimeoutSeconds));
        var shellPolicy = ShellPolicySource.ForWorkspace(config.Tools.Shell.Policy.ToRules(), context.DataPath);

        var mainRuntime = chatClientRegistry.ResolveMainRuntime(
            config,
            context.EffectiveProviderId,
            context.EffectiveMainModel);
        var subAgentPreference = ModelPreferenceRules.Find(
            config.SubAgent.ProviderPreferences,
            mainRuntime.ProviderId);
        var subAgentCoordinator = new SubAgentCoordinator(
            context.WorkspacePath,
            [new CliOneshotRuntime()],
            config.SubAgentProfiles,
            approvalService,
            config.SubAgent.DisabledProfiles,
            externalCliSessionStore: null,
            enableExternalCliSessionResume: config.SubAgent.EnableExternalCliSessionResume,
            catalog: SubAgentProfileCatalog.Resolve(contributions, context.ThreadId));
        var agentTools = new AgentTools(
            subAgentCoordinator: subAgentCoordinator,
            subAgentRoles: config.SubAgent.Roles,
            maxSubAgentDepth: config.SubAgent.MaxDepth,
            subAgentPreference: subAgentPreference,
            appConfig: config,
            maxConcurrentSubAgents: config.SubAgent.MaxConcurrentSubAgents,
            modelCatalogSnapshot: context.SubAgentModelCatalogSnapshot,
            inheritedModel: context.EffectiveMainModel);
        tools.AddRange(GeneratedToolFunctions.All(agentTools));

        var boardTools = new AgentMessageBoardTools();
        tools.AddRange(GeneratedToolFunctions.All(boardTools));
        tools.AddRange(GeneratedToolFunctions.All(new BackgroundTaskTools()));

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
            workspaceRoots: context.WorkspaceRoots,
            memoryStores: context.MemoryStores,
            memoryWriteGates: contributions is null ? null : () => contributions.Resolve<IMemoryWriteGate>(context.ThreadId),
            location: context.Location);
        tools.AddRange(GeneratedToolFunctions.All(fileTools));

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
            approvalService,
            workspaceRoots: context.WorkspaceRoots,
            policy: shellPolicy,
            location: context.Location);
        tools.AddRange(GeneratedToolFunctions.All(shellTools));

        var webTools = new WebTools(config, mainRuntime, providerHttpTransport, chatClientRegistry);
        var hostedWebSearch = WebSearchToolSource.IsSupportedRuntime(mainRuntime);
        tools.AddRange(GeneratedToolFunctions.All(webTools)
            .Where(tool => !hostedWebSearch || tool.Name != nameof(WebTools.WebSearch)));

        var skillViewTool = new SkillViewTool(skillsLoader, traceCollector);
        tools.Add(GeneratedToolFunctions.SkillViewTool_SkillView(skillViewTool));

        return tools;
    }
}
