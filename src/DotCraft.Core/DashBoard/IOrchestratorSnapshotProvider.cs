namespace DotCraft.DashBoard;

/// <summary>
/// Allows orchestrator modules to expose their runtime state to the dashboard API.
/// Implement this interface in any orchestrator that should be queryable via
/// <c>/dashboard/api/orchestrators/{name}/state</c> and <c>/refresh</c>.
/// </summary>
public interface IOrchestratorSnapshotProvider
{
    /// <summary>
    /// Unique channel/module name used as the URL segment for the dashboard endpoints.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns a serializable snapshot of the current orchestrator state.
    /// The returned object is serialized to JSON by the dashboard middleware.
    /// </summary>
    object GetSnapshot();

    /// <summary>
    /// Triggers an immediate poll and reconciliation cycle, bypassing the normal interval.
    /// </summary>
    void TriggerRefresh();
}
