using DotCraft.Abstractions;
using DotCraft.Dreams;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for internal Dreams maintenance threads.
/// </summary>
public sealed class DreamsToolProvider(DreamsRunRegistry runRegistry) : IAgentToolProvider
{
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        if (!runRegistry.TryGetFileWorkspace(context.CurrentThreadId, out var workspace))
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
            maxFileSize: context.Config.Tools.File.MaxFileSize,
            approvalService: null,
            blacklist: context.PathBlacklist,
            trustedReadPaths: trustedReadPaths,
            lspServerManager: null,
            ripgrepPath: context.Config.Tools.File.RipgrepPath,
            searchTimeout: TimeSpan.FromSeconds(Math.Max(1, context.Config.Tools.File.SearchTimeoutSeconds)));

        yield return AIFunctionFactory.Create(fileTools.ReadFile);
        yield return AIFunctionFactory.Create(fileTools.WriteFile);
        yield return AIFunctionFactory.Create(fileTools.EditFile);
        yield return AIFunctionFactory.Create(fileTools.GrepFiles);
        yield return AIFunctionFactory.Create(fileTools.FindFiles);
    }
}
