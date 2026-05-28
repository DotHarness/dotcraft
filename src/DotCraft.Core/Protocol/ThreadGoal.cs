namespace DotCraft.Protocol;

/// <summary>
/// Current persistent goal attached to a Session Core thread.
/// </summary>
public sealed record ThreadGoal
{
    public required string ThreadId { get; init; }

    public required string GoalId { get; init; }

    public required string Objective { get; init; }

    public required ThreadGoalStatus Status { get; init; }

    public long? TokenBudget { get; init; }

    public required TokenUsageInfo TokensUsed { get; init; }

    public long TimeUsedSeconds { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Lifecycle status for a thread goal.
/// </summary>
public enum ThreadGoalStatus
{
    Active,
    Paused,
    BudgetLimited,
    Complete
}

/// <summary>
/// Patch-like user or system mutation for the current thread goal.
/// </summary>
public sealed record ThreadGoalUpdate
{
    public string? Objective { get; init; }

    public ThreadGoalStatus? Status { get; init; }

    public long? TokenBudget { get; init; }

    public bool HasTokenBudget { get; init; }
}

public enum GoalSetMode
{
    UpsertOrUpdate,
    CreateOnly,
    UpdateOnly,
    ReplaceExisting
}

public sealed record ThreadGoalClearResult(bool Cleared);
