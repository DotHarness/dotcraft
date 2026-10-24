using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnvironmentPath { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; init; }
}

public sealed record ThreadWorktreeDirtyHandoffInfo
{
    public bool Requested { get; init; }

    public string Status { get; init; } = WorktreeDirtyHandoffStatuses.Skipped;

    public int CopiedFileCount { get; init; }

    public int DeletedFileCount { get; init; }

    public bool TargetStashed { get; init; }
}

public static class WorktreeHandoffSteps
{
    public const string CreateWorktree = "createWorktree";
    public const string ReuseWorktree = "reuseWorktree";
    public const string StashSourceChanges = "stashSourceChanges";
    public const string StashTargetChanges = "stashTargetChanges";
    public const string DetachWorktreeBranch = "detachWorktreeBranch";
    public const string CheckoutLocalBranch = "checkoutLocalBranch";
    public const string ApplyChanges = "applyChanges";
    public const string MoveThread = "moveThread";
    public const string Rollback = "rollback";
}

public static class WorktreeHandoffStepStatuses
{
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

public static class WorktreeDirtyHandoffStatuses
{
    public const string Skipped = "skipped";
    public const string Succeeded = "succeeded";
}

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

    public string? EnvironmentPath { get; init; }

    public string? OperationId { get; init; }

    public Action<string, string>? Progress { get; init; }

    public Action<string>? SetupOutput { get; init; }
}

public sealed record WorktreeCreateAndForkResult
{
    public SessionThread Thread { get; init; } = new();

    public ThreadWorktreeInfo Worktree { get; init; } = new();

    public WorktreeSetupResult? Setup { get; init; }
}

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

    public string? EnvironmentPath { get; init; }

    public string? OperationId { get; init; }

    public Action<string, string>? Progress { get; init; }

    public Action<string>? SetupOutput { get; init; }

    public ThreadSource? Source { get; init; }
}

public sealed record WorktreeCreateAndStartResult
{
    public SessionThread Thread { get; init; } = new();

    public ThreadWorktreeInfo Worktree { get; init; } = new();

    public WorktreeSetupResult? Setup { get; init; }
}

public static class WorktreeHandoffModes
{
    public const string Local = "local";
    public const string Worktree = "worktree";
}

public sealed record WorktreeHandoffOptions
{
    public string ThreadId { get; init; } = string.Empty;

    public string Mode { get; init; } = WorktreeHandoffModes.Worktree;

    public string? BranchName { get; init; }

    public string? BaseRef { get; init; }

    public string? Path { get; init; }

    public bool CopyDirtyChanges { get; init; } = true;

    /// <summary>Receives each handoff step as it changes state.</summary>
    public Action<string, string>? Progress { get; init; }
}

public sealed record WorktreeHandoffResult
{
    public SessionThread Thread { get; init; } = new();

    public string Mode { get; init; } = WorktreeHandoffModes.Local;

    public ThreadWorktreeInfo? Worktree { get; init; }

    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; init; }
}

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

public sealed record WorktreeRemoveOptions
{
    public string? ThreadId { get; init; }

    public string? WorkspacePath { get; init; }

    public string? BranchName { get; init; }

    public string? Path { get; init; }

    public bool DeleteBranch { get; init; } = true;
}

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

public sealed class WorktreeInUseException(string path)
    : InvalidOperationException($"Another program is using the worktree folder '{path}'.")
{
    public string Path { get; } = path;
}

public sealed record ThreadWorktreeStatus
{
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
