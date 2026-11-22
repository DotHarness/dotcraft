using System.ComponentModel;
using System.Text.Json;
using DotCraft.GeneratedTools.Core;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Provides the tools that move a top-level thread into and out of a managed Git worktree.
/// </summary>
public sealed class WorktreeToolSource : AIFunctionToolSource
{
    private readonly WorktreeToolMethods _methods = new();

    /// <inheritdoc />
    public override string SourceId => "worktree";

    /// <inheritdoc />
    public override int Priority => 26;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (context.ThreadKind != ToolPlanningThreadKind.UserTopLevel || !IsInGitRepository(context.WorkspacePath))
            return [];

        return GeneratedToolFunctions.All(_methods);
    }

    private static bool IsInGitRepository(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return true;
        }

        return false;
    }
}

public static class WorktreeToolNames
{
    public const string EnterWorktree = "EnterWorktree";

    public const string ExitWorktree = "ExitWorktree";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { EnterWorktree, ExitWorktree };
}

/// <summary>Runtime context the worktree tools use to move the running thread.</summary>
public sealed record WorktreeToolRuntimeContext(
    Func<string?, string?, CancellationToken, Task<ThreadWorktreeInfo>> EnterAsync,
    Func<bool, bool, CancellationToken, Task<WorktreeExitResult>> ExitAsync,
    Func<string> CurrentDirectory);

internal sealed class WorktreeToolMethods
{
    private const string Unavailable = "Worktree tools are only available inside a conversation turn.";
    private const string TopLevelOnly = "Worktree tools can only be used by a top-level thread.";

    [GeneratedTool]
    [ToolPresentation("core.worktree", Operation = "enter")]
    [Description("Move this conversation into an isolated Git worktree on a new branch, so its changes stay out of the user's checkout. Use it only when the user or project instructions ask for a worktree; for other branch work, use git. The worktree starts from the current HEAD without uncommitted changes. Your later tool calls run in the worktree.")]
    public async Task<ToolExecutionResult> EnterWorktree(
        ToolInvocationContext invocation,
        [Description("Name for the new branch.")] string? name = null,
        [Description("An existing worktree to enter instead of creating one.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (invocation.TurnServices is { IsTopLevelThread: false })
            return Fail(ToolErrorCodes.ExecutionFailed, TopLevelOnly);
        if (invocation.TurnServices?.Worktree is not { } context)
            return Fail(ToolErrorCodes.ExecutionFailed, Unavailable);
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
            return Fail(ToolErrorCodes.InputInvalid, "Pass either name or path, not both.");

        try
        {
            var worktree = await context.EnterAsync(name, path, cancellationToken);
            return ToolExecutionResult.Succeeded(
                $"Now working in {worktree.Path} on branch {worktree.BranchName}.",
                JsonSerializer.SerializeToElement(new
                {
                    operation = "enter",
                    outcome = "entered",
                    branchName = worktree.BranchName,
                    path = worktree.Path,
                    checkoutPath = worktree.WorkspacePath,
                    baseRef = worktree.BaseRef
                }));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex is ArgumentException ? ToolErrorCodes.InputInvalid : ToolErrorCodes.ExecutionFailed, ex.Message);
        }
    }

    [GeneratedTool]
    [ToolPresentation("core.worktree", Operation = "exit")]
    [Description("Move this conversation back to the local checkout. Use it only when the user asks. `keep` leaves the worktree and its branch on disk; `remove` deletes both. Changes are not moved to the local checkout.")]
    public async Task<ToolExecutionResult> ExitWorktree(
        ToolInvocationContext invocation,
        [Description("keep or remove.")] ExitWorktreeAction action,
        [Description("Remove even when the worktree has uncommitted changes or new commits. Set it only after the user confirms.")] bool discardChanges = false,
        CancellationToken cancellationToken = default)
    {
        if (invocation.TurnServices is { IsTopLevelThread: false })
            return Fail(ToolErrorCodes.ExecutionFailed, TopLevelOnly);
        if (invocation.TurnServices?.Worktree is not { } context)
            return Fail(ToolErrorCodes.ExecutionFailed, Unavailable);

        try
        {
            var result = await context.ExitAsync(action == ExitWorktreeAction.Remove, discardChanges, cancellationToken);
            if (result.RefusedStatus is { } status)
            {
                var lost = new List<string>();
                if (status.HasUncommittedChanges)
                    lost.Add("uncommitted changes");
                if (status.AheadCount > 0)
                    lost.Add($"{status.AheadCount} new commit(s)");
                var refused = $"Not removed: the worktree has {string.Join(" and ", lost)}. Ask the user before calling again with discardChanges: true.";
                return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.ExecutionFailed, refused), refused);
            }

            var outcome = action == ExitWorktreeAction.Remove ? "removed" : "kept";
            var checkoutPath = context.CurrentDirectory();
            return ToolExecutionResult.Succeeded(
                $"Back in {checkoutPath}. The worktree on branch {result.Worktree!.BranchName} was {outcome}.",
                JsonSerializer.SerializeToElement(new
                {
                    operation = "exit",
                    outcome,
                    branchName = result.Worktree!.BranchName,
                    path = result.Worktree.Path,
                    checkoutPath
                }));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex is ArgumentException ? ToolErrorCodes.InputInvalid : ToolErrorCodes.ExecutionFailed, ex.Message);
        }
    }

    private static ToolExecutionResult Fail(string code, string message) =>
        ToolExecutionResult.Failed(new ToolError(code, message), $"Error: {message}");
}

internal enum ExitWorktreeAction
{
    Keep,
    Remove
}
