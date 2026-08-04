namespace DotCraft.Sessions;

/// <summary>
/// Location inside a source thread where a fork should stop copying history.
/// When omitted, a fork copies all source turns.
/// </summary>
public sealed record ThreadForkPoint
{
    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public string Position { get; init; } = ThreadForkPositions.After;
}

public static class ThreadForkPositions
{
    public const string Before = "before";
    public const string After = "after";
}

/// <summary>
/// Options used to fork a thread into a new sibling thread.
/// </summary>
public sealed record ThreadForkOptions
{
    public string? Path { get; init; }

    public ThreadForkPoint? ForkPoint { get; init; }

    public SessionIdentity? Identity { get; init; }

    public ThreadConfiguration? Config { get; init; }

    public string? Cwd { get; init; }

    public IReadOnlyList<string>? RuntimeWorkspaceRoots { get; init; }

    public string? DisplayName { get; init; }

    public bool Ephemeral { get; init; }

    public ThreadWorktreeInfo? Worktree { get; init; }
}
