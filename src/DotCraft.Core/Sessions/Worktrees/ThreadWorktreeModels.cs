using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

/// <summary>
/// Persistent metadata for a DotCraft-managed Git worktree bound to a thread.
/// </summary>
public sealed record ThreadWorktreeInfo
{
    public string Id { get; init; } = string.Empty;

    public string SourceThreadId { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string SourceWorkspacePath { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string BaseRef { get; init; } = string.Empty;

    public string BaseHead { get; init; } = string.Empty;

    public string Head { get; init; } = string.Empty;

    public string? OwnerKind { get; init; }

    public string? OwnerId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; init; }
}

/// <summary>
/// Summary of dirty working tree files copied into a newly created worktree.
/// </summary>
public sealed record ThreadWorktreeDirtyHandoffInfo
{
    public bool Requested { get; init; }

    public string Status { get; init; } = WorktreeDirtyHandoffStatuses.Skipped;

    public int CopiedFileCount { get; init; }

    public int DeletedFileCount { get; init; }
}

public static class WorktreeDirtyHandoffStatuses
{
    public const string Skipped = "skipped";
    public const string Succeeded = "succeeded";
}

/// <summary>
/// Options for creating a Git worktree and then forking a source thread into it.
/// </summary>
public sealed record WorktreeCreateAndForkOptions
{
    public string SourceThreadId { get; init; } = string.Empty;

    public ThreadForkPoint? ForkPoint { get; init; }

    public SessionIdentity? Identity { get; init; }

    public ThreadConfiguration? Config { get; init; }

    public string? DisplayName { get; init; }

    public string? BranchName { get; init; }

    public string? BaseRef { get; init; }

    public string? Path { get; init; }

    public bool CopyDirtyChanges { get; init; } = true;
}

public sealed record WorktreeCreateAndForkResult
{
    public SessionThread Thread { get; init; } = new();

    public ThreadWorktreeInfo Worktree { get; init; } = new();
}

/// <summary>
/// Options for creating a Git worktree and starting a new thread inside it.
/// </summary>
public sealed record WorktreeCreateAndStartOptions
{
    public SessionIdentity Identity { get; init; } = new();

    public ThreadConfiguration? Config { get; init; }

    public HistoryMode HistoryMode { get; init; } = HistoryMode.Server;

    public string? DisplayName { get; init; }

    public string? BranchName { get; init; }

    public string? BaseRef { get; init; }

    public string? Path { get; init; }

    public bool CopyDirtyChanges { get; init; } = true;

    public ThreadSource? Source { get; init; }
}

public sealed record WorktreeCreateAndStartResult
{
    public SessionThread Thread { get; init; } = new();

    public ThreadWorktreeInfo Worktree { get; init; } = new();
}

public static class WorktreeHandoffModes
{
    public const string Local = "local";
    public const string Worktree = "worktree";
}

/// <summary>
/// Options for moving an existing thread between its local workspace and a managed worktree.
/// </summary>
public sealed record WorktreeHandoffOptions
{
    public string ThreadId { get; init; } = string.Empty;

    public string Mode { get; init; } = WorktreeHandoffModes.Worktree;

    public string? BranchName { get; init; }

    public string? BaseRef { get; init; }

    public string? Path { get; init; }

    public bool CopyDirtyChanges { get; init; } = true;
}

public sealed record WorktreeHandoffResult
{
    public SessionThread Thread { get; init; } = new();

    public string Mode { get; init; } = WorktreeHandoffModes.Local;

    public ThreadWorktreeInfo? Worktree { get; init; }

    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; init; }
}

/// <summary>
/// Options for ensuring a named DotCraft-managed worktree is bound to an existing thread.
/// </summary>
public sealed record WorktreeEnsureOptions
{
    public string ThreadId { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string? BaseRef { get; init; }

    public string? Path { get; init; }

    public string? OwnerKind { get; init; }

    public string? OwnerId { get; init; }
}

public sealed record WorktreeEnsureResult
{
    public SessionThread Thread { get; init; } = new();

    public ThreadWorktreeInfo Worktree { get; init; } = new();

    public bool Reused { get; init; }
}

/// <summary>
/// Options for setting a thread's execution workspace without moving its state workspace.
/// </summary>
public sealed record ThreadExecutionWorkspaceOptions
{
    public string ThreadId { get; init; } = string.Empty;

    public string? ExecutionWorkspaceOverride { get; init; }

    public bool ClearWorktree { get; init; } = true;
}

/// <summary>
/// Options for force-removing a managed worktree and its branch.
/// </summary>
public sealed record WorktreeRemoveOptions
{
    public string? ThreadId { get; init; }

    public string? WorkspacePath { get; init; }

    public string? BranchName { get; init; }

    public string? Path { get; init; }

    public bool DeleteBranch { get; init; } = true;
}

/// <summary>Outcome of leaving a worktree: either the thread left it, or removal was refused to keep work.</summary>
public sealed record WorktreeExitResult
{
    public ThreadWorktreeInfo? Worktree { get; init; }

    public ThreadWorktreeStatus? RefusedStatus { get; init; }

    public static WorktreeExitResult Exited(ThreadWorktreeInfo worktree) => new() { Worktree = worktree };

    public static WorktreeExitResult Refused(ThreadWorktreeStatus status) => new() { RefusedStatus = status };
}

public sealed class WorktreeHandoffConflictException(
    string message,
    IReadOnlyList<string> conflictPaths) : InvalidOperationException(message)
{
    public IReadOnlyList<string> ConflictPaths { get; } = conflictPaths;
}

public sealed record ThreadWorktreeStatus
{
    /// <summary>The bound thread, or <c>null</c> for a worktree no thread is bound to.</summary>
    public string? ThreadId { get; init; }

    public ThreadWorktreeInfo Worktree { get; init; } = new();

    public string Path { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string? Head { get; init; }

    public bool Exists { get; init; }

    public bool IsGitWorktree { get; init; }

    public bool HasUncommittedChanges { get; init; }

    public bool HasCommitsAheadOfBase { get; init; }

    public int AheadCount { get; init; }
}
