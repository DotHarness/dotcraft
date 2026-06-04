using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

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

    public string Head { get; init; } = string.Empty;

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

public sealed class WorktreeHandoffConflictException(
    string message,
    IReadOnlyList<string> conflictPaths) : InvalidOperationException(message)
{
    public IReadOnlyList<string> ConflictPaths { get; } = conflictPaths;
}

public sealed record ThreadWorktreeStatus
{
    public string ThreadId { get; init; } = string.Empty;

    public ThreadWorktreeInfo Worktree { get; init; } = new();

    public string Path { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string? Head { get; init; }

    public bool Exists { get; init; }

    public bool IsGitWorktree { get; init; }

    public bool HasUncommittedChanges { get; init; }
}
