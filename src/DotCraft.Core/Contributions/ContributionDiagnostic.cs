namespace DotCraft.Contributions;

/// <summary>Stable diagnostic codes the contribution registry logs. The registry never fails closed, so these are reporting only.</summary>
public static class ContributionDiagnosticCodes
{
    /// <summary>Two or more contributions replace the same Tier-B target; the losers are inactive.</summary>
    public const string ReplaceConflict = "contribution.replace_conflict";

    /// <summary>A contribution threw while being disposed.</summary>
    public const string TeardownFailed = "contribution.teardown_failed";

    /// <summary>A change-event subscriber threw.</summary>
    public const string ObserverFailed = "contribution.observer_failed";
}
