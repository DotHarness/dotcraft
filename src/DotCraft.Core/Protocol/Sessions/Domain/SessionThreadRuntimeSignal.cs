namespace DotCraft.Protocol;

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
    /// A successful context compaction just completed. UI layers use this to
    /// clear any "context almost full" warning indicator.
    /// </summary>
    ContextCompacted,
    /// <summary>
    /// A successful long-term memory consolidation just completed.
    /// Hosts may use this to trigger memory-derived follow-up maintenance.
    /// </summary>
    MemoryConsolidated,
    /// <summary>
    /// Thread-scoped manual context compaction is active.
    /// </summary>
    MaintenanceCompactingStarted,
    /// <summary>
    /// Thread-scoped memory consolidation is active.
    /// </summary>
    MaintenanceConsolidatingStarted,
    /// <summary>
    /// Thread-scoped maintenance reached a terminal state.
    /// </summary>
    MaintenanceCompleted,
}
