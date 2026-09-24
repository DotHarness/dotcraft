namespace DotCraft.Sessions;

/// <summary>
/// Internal runtime lifecycle signals that hosts can aggregate into workspace-level thread runtime snapshots.
/// </summary>
public enum SessionThreadRuntimeSignal
{
    TurnStarted,
    TurnCompleted,
    TurnCompletedAwaitingPlanConfirmation,
    TurnFailed,
    TurnCancelled,
    ApprovalRequested,
    ApprovalResolved,
    UserInputRequested,
    UserInputResolved,
    /// <summary>
    /// Visible history was rolled back. Resets runtime state without reporting a Turn completion.
    /// </summary>
    HistoryRolledBack,
    /// <summary>
    /// A successful context compaction just completed. UI layers use this to
    /// clear any "context almost full" warning indicator.
    /// </summary>
    ContextCompacted,
    /// <summary>
    /// Thread-scoped manual context compaction is active.
    /// </summary>
    MaintenanceCompactingStarted,
    /// <summary>
    /// Thread-scoped maintenance reached a terminal state.
    /// </summary>
    MaintenanceCompleted,
}
