using System.ComponentModel;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for internal Dreams threads: reads the workspace, the run's baseline, and AGENTS.md,
/// writes only memory files in the run's candidate store, and reaches transcripts through the evidence tools.
/// </summary>
public sealed class DreamsToolSource(
    DreamsRunRegistry runRegistry,
    AppConfig config,
    PathBlacklist? pathBlacklist = null) : AIFunctionToolSource
{
    private static readonly HashSet<string> ReadTools =
        [nameof(FileTools.ReadFile), nameof(FileTools.GrepFiles), nameof(FileTools.FindFiles)];

    /// <inheritdoc />
    public override string SourceId => "dreams";

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (!runRegistry.TryGet(context.ThreadId, out var run))
            yield break;

        var input = run.Input;
        List<string> trustedReadPaths = [input.BaselineDirectory, input.WorkspacePath];
        trustedReadPaths.AddRange(new AgentInstructionsLoader().Load(input.WorkspacePath, config).Sources);
        var fileTools = new FileTools(
            input.CandidateDirectory,
            requireApprovalOutsideWorkspace: false,
            maxFileSize: config.Tools.File.MaxFileSize,
            blacklist: pathBlacklist,
            trustedReadPaths: trustedReadPaths,
            ripgrepPath: config.Tools.File.RipgrepPath,
            searchTimeout: TimeSpan.FromSeconds(Math.Max(1, config.Tools.File.SearchTimeoutSeconds)));

        foreach (var function in GeneratedToolFunctions.All(fileTools).Where(function => ReadTools.Contains(function.Name)))
            yield return function;
        foreach (var function in GeneratedToolFunctions.All(new DreamsCandidateTools(fileTools, input.CandidateDirectory)))
            yield return function;
        foreach (var function in GeneratedToolFunctions.All(new DreamsEvidenceTools(run)))
            yield return function;
    }
}

internal sealed class DreamsCandidateTools(FileTools fileTools, string candidateDirectory)
{
    [GeneratedTool]
    [ToolPresentation("core.file-write", Operation = "write")]
    [Description("Create or overwrite a memory file or MEMORY.md in the candidate store. Use EditFile for targeted changes.")]
    public async Task<ToolExecutionResult> WriteFile(
        [Description("A top-level <name>.md file in the candidate store, as a name or a full path.")] string path,
        [Description("The content to write.")] string content) =>
        TryResolve(path, out var fullPath, out var refusal)
            ? await fileTools.WriteFile(fullPath, content).ConfigureAwait(false)
            : refusal;

    [GeneratedTool]
    [ToolPresentation("core.file-write", Operation = "edit")]
    [Description("Replace a text snippet in a memory file or MEMORY.md in the candidate store. Copy oldText from the file.")]
    public async Task<ToolExecutionResult> EditFile(
        [Description("A top-level <name>.md file in the candidate store, as a name or a full path.")] string path,
        [Description("The text to replace. Must identify one location unless replaceAll is true.")] string oldText = "",
        [Description("The replacement text. Use an empty string to delete the matched text.")] string newText = "",
        [Description("Replace every exact occurrence of oldText. Defaults to false.")] bool replaceAll = false) =>
        TryResolve(path, out var fullPath, out var refusal)
            ? await fileTools.EditFile(fullPath, oldText, newText, replaceAll).ConfigureAwait(false)
            : refusal;

    [GeneratedTool]
    [Description("Delete a memory file from the candidate store. Remove its pointer from MEMORY.md as well.")]
    public ToolExecutionResult DeleteMemoryFile(
        [Description("The memory file's name, such as package-manager.md.")] string name)
    {
        if (!TryResolve(name, out var fullPath, out var refusal))
            return refusal;
        if (DreamsStoreFiles.IsIndex(Path.GetFileName(fullPath)))
            return Refuse("MEMORY.md is the index; edit it instead of deleting it.");
        if (!File.Exists(fullPath))
            return Refuse($"No memory file named {Path.GetFileName(fullPath)} exists in the candidate store.");

        File.Delete(fullPath);
        return ToolExecutionResult.Succeeded($"Deleted {Path.GetFileName(fullPath)}.");
    }

    private bool TryResolve(string path, out string fullPath, out ToolExecutionResult refusal)
    {
        fullPath = string.Empty;
        refusal = null!;
        try
        {
            fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path, candidateDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            refusal = Refuse($"Invalid path: {path}");
            return false;
        }

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullPath) ?? string.Empty),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateDirectory)),
                StringComparison.OrdinalIgnoreCase)
            && DreamsStoreFiles.IsMemoryFileName(Path.GetFileName(fullPath)))
        {
            return true;
        }

        refusal = Refuse($"Dreams can only write top-level <name>.md files in the candidate store {candidateDirectory}; {path} is not one.");
        return false;
    }

    private static ToolExecutionResult Refuse(string message) =>
        ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.AccessDenied, message), message);
}

internal sealed class DreamsEvidenceTools(DreamsRunSession run)
{
    [GeneratedTool]
    [Description("Search the transcripts of this run's manifest threads for a literal, case-insensitive term. Returns matching messages with their thread, turn, and date. Use narrow terms; each run has a small search budget.")]
    public Task<string> SearchTranscripts(
        [Description("The text to find.")] string query,
        [Description("Manifest thread ids to search. Omit to search all of them.")] string[]? threadIds = null,
        [Description("Maximum matches to return, up to 50. Defaults to 20.")] int maxMatches = 20,
        CancellationToken cancellationToken = default) =>
        run.SearchAsync(query, threadIds, maxMatches, cancellationToken);

    [GeneratedTool]
    [Description("Read a bounded excerpt of one manifest thread's transcript, one numbered message per line. Continue with a later offset; each run has a small read budget.")]
    public Task<string> ReadTranscript(
        [Description("A thread id from the manifest.")] string threadId,
        [Description("The first message to read, starting at 1.")] int offset = 1,
        [Description("Messages to read, up to 300. Defaults to 120.")] int limit = 120,
        CancellationToken cancellationToken = default) =>
        run.ReadAsync(threadId, offset, limit, cancellationToken);
}
