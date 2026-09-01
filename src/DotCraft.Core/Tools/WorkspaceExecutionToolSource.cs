using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Lsp;
using DotCraft.Security;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provider-free source for the workspace execution tools that may be hosted by a
/// Remote Tool Host. It intentionally has no Agent, model, Session Core, Memory,
/// or AppServer dependencies.
/// </summary>
public sealed class WorkspaceExecutionToolSource(
    AppConfig config,
    IBackgroundTerminalService backgroundTerminalService,
    PathBlacklist? pathBlacklist = null,
    LspServerManager? lspServerManager = null,
    string? userDataPath = null,
    IApprovalService? approvalService = null) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "core-native";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (config.Tools.Sandbox.Enabled)
            return [];

        var tools = new List<AIFunction>();
        var requireOutside = context.RequireApprovalOutsideWorkspace
            ?? config.Tools.File.RequireApprovalOutsideWorkspace;
        var fileSearchTimeout = TimeSpan.FromSeconds(
            Math.Max(1, config.Tools.File.SearchTimeoutSeconds));

        var fileTools = new FileTools(
            context.WorkspacePath,
            requireOutside,
            config.Tools.File.MaxFileSize,
            approvalService,
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

        if (config.Tools.Lsp.Enabled && lspServerManager != null)
        {
            var lspTool = new LspTool(
                context.WorkspacePath,
                lspServerManager,
                requireOutside,
                config.Tools.Lsp.MaxFileSize,
                approvalService,
                pathBlacklist,
                context.WorkspaceRoots);
            tools.Add(GeneratedToolFunctions.LspTool_LSP(lspTool));
        }

        var shellTools = new ShellTools(
            context.WorkspacePath,
            backgroundTerminalService,
            config.Tools.Shell.Timeout,
            requireOutside,
            config.Tools.Shell.MaxOutputLength,
            approvalService,
            pathBlacklist,
            context.WorkspaceRoots);
        tools.Add(GeneratedToolFunctions.ShellTools_Exec(shellTools));
        tools.Add(GeneratedToolFunctions.ShellTools_WriteStdin(shellTools));

        return tools;
    }
}
