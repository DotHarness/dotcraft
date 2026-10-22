using System.ComponentModel;
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

/// <summary>Async-local scope for the worktree tools. Session Core sets it while executing a top-level turn.</summary>
public static class WorktreeToolRuntimeScope
{
    private static readonly AsyncLocal<WorktreeToolRuntimeContext?> CurrentContext = new();

    public static WorktreeToolRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(WorktreeToolRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(WorktreeToolRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}

internal sealed class WorktreeToolMethods
{
    private const string Unavailable = "Error: Worktree tools are only available inside a conversation turn.";

    [GeneratedTool]
    [Description("Move this conversation into an isolated Git worktree on a new branch, so its changes stay out of the user's checkout. Use it only when the user or project instructions ask for a worktree; for other branch work, use git. The worktree starts from the current HEAD without uncommitted changes. Your later tool calls run in the worktree.")]
    public async Task<string> EnterWorktree(
        [Description("Name for the new branch.")] string? name = null,
        [Description("An existing worktree to enter instead of creating one.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (WorktreeToolRuntimeScope.Current is not { } context)
            return Unavailable;
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
            return "Error: Pass either name or path, not both.";

        try
        {
            var worktree = await context.EnterAsync(name, path, cancellationToken);
            return $"Now working in {worktree.Path} on branch {worktree.BranchName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Error: {ex.Message}";
        }
    }

    [GeneratedTool]
    [Description("Move this conversation back to the local checkout. Use it only when the user asks. `keep` leaves the worktree and its branch on disk; `remove` deletes both. Changes are not moved to the local checkout.")]
    public async Task<string> ExitWorktree(
        [Description("keep or remove.")] ExitWorktreeAction action,
        [Description("Remove even when the worktree has uncommitted changes or new commits. Set it only after the user confirms.")] bool discardChanges = false,
        CancellationToken cancellationToken = default)
    {
        if (WorktreeToolRuntimeScope.Current is not { } context)
            return Unavailable;

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
                return $"Not removed: the worktree has {string.Join(" and ", lost)}. Ask the user before calling again with discardChanges: true.";
            }

            var outcome = action == ExitWorktreeAction.Remove ? "removed" : "kept";
            return $"Back in {context.CurrentDirectory()}. The worktree on branch {result.Worktree!.BranchName} was {outcome}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Error: {ex.Message}";
        }
    }
}

internal enum ExitWorktreeAction
{
    Keep,
    Remove
}
