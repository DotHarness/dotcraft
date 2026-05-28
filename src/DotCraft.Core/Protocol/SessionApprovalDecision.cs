namespace DotCraft.Protocol;

/// <summary>
/// Rich approval decision captured by Session Core and the wire protocol.
/// </summary>
public enum SessionApprovalDecision
{
    AcceptOnce,
    AcceptForSession,
    /// <summary>
    /// Approve and persist permanently. The server writes the approval to
    /// <see cref="DotCraft.Security.ApprovalStore"/> so future sessions skip the prompt.
    /// Also acts as a session-scoped approval for the remainder of the current thread.
    /// </summary>
    AcceptAlways,
    Reject,
    CancelTurn
}

/// <summary>
/// Helpers for working with <see cref="SessionApprovalDecision"/>.
/// </summary>
public static class SessionApprovalDecisionExtensions
{
    /// <summary>
    /// Returns true when the decision allows the requested operation to continue.
    /// </summary>
    public static bool IsApproved(this SessionApprovalDecision decision) =>
        decision is SessionApprovalDecision.AcceptOnce
            or SessionApprovalDecision.AcceptForSession
            or SessionApprovalDecision.AcceptAlways;

    /// <summary>
    /// Returns true when the decision should be cached for the rest of the thread.
    /// </summary>
    public static bool AppliesToSession(this SessionApprovalDecision decision) =>
        decision is SessionApprovalDecision.AcceptForSession or SessionApprovalDecision.AcceptAlways;

    /// <summary>
    /// Returns true when the decision should be persisted permanently via
    /// <see cref="DotCraft.Security.ApprovalStore"/>.
    /// </summary>
    public static bool IsPersistent(this SessionApprovalDecision decision) =>
        decision == SessionApprovalDecision.AcceptAlways;
}
