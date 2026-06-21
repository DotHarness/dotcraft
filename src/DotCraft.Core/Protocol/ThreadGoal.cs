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
    Blocked,
    UsageLimited,
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

public enum GoalAccountingMode
{
    ActiveStatusOnly,
    ActiveOnly,
    ActiveOrComplete,
    ActiveOrStopped
}

public sealed record GoalAccountingOutcome(ThreadGoal? Goal, bool Updated)
{
    public static GoalAccountingOutcome Unchanged(ThreadGoal? goal) => new(goal, false);

    public static GoalAccountingOutcome UpdatedGoal(ThreadGoal goal) => new(goal, true);
}

/// <summary>
/// AppServer/model-tool wire shape for a thread goal. Internal state keeps the richer
/// <see cref="ThreadGoal"/> record, including goal id and token breakdowns.
/// </summary>
public sealed record ThreadGoalWire
{
    public required string ThreadId { get; init; }

    public required string Objective { get; init; }

    public required string Status { get; init; }

    public long? TokenBudget { get; init; }

    public required long TokensUsed { get; init; }

    public required long TimeUsedSeconds { get; init; }

    public required long CreatedAt { get; init; }

    public required long UpdatedAt { get; init; }

    public static ThreadGoalWire FromGoal(ThreadGoal goal) => new()
    {
        ThreadId = goal.ThreadId,
        Objective = goal.Objective,
        Status = ToWireStatus(goal.Status),
        TokenBudget = goal.TokenBudget,
        TokensUsed = goal.TokensUsed.TotalTokens,
        TimeUsedSeconds = goal.TimeUsedSeconds,
        CreatedAt = goal.CreatedAt.ToUnixTimeSeconds(),
        UpdatedAt = goal.UpdatedAt.ToUnixTimeSeconds()
    };

    public static string ToWireStatus(ThreadGoalStatus status) => status switch
    {
        ThreadGoalStatus.Active => "active",
        ThreadGoalStatus.Paused => "paused",
        ThreadGoalStatus.Blocked => "blocked",
        ThreadGoalStatus.UsageLimited => "usageLimited",
        ThreadGoalStatus.BudgetLimited => "budgetLimited",
        ThreadGoalStatus.Complete => "complete",
        _ => "active"
    };
}
