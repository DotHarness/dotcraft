using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for internal Dreams maintenance threads.
/// </summary>
public sealed class DreamsToolSource(
    DreamsRunRegistry runRegistry,
    AppConfig config,
    PathBlacklist? pathBlacklist = null) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "dreams";

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (!runRegistry.TryGetFileWorkspace(context.ThreadId, out var workspace))
            yield break;

        var trustedReadPaths = new List<string>
        {
            workspace.InputPath,
            context.WorkspacePath
        };
        if (!string.IsNullOrWhiteSpace(workspace.ActiveStorePath))
            trustedReadPaths.Add(workspace.ActiveStorePath);

        var fileTools = new FileTools(
            workspace.OutputStorePath,
            requireApprovalOutsideWorkspace: false,
            maxFileSize: config.Tools.File.MaxFileSize,
            approvalService: null,
            blacklist: pathBlacklist,
            trustedReadPaths: trustedReadPaths,
            lspServerManager: null,
            ripgrepPath: config.Tools.File.RipgrepPath,
            searchTimeout: TimeSpan.FromSeconds(Math.Max(1, config.Tools.File.SearchTimeoutSeconds)));

        foreach (var function in GeneratedToolFunctions.All(fileTools))
            yield return function;
    }
}
