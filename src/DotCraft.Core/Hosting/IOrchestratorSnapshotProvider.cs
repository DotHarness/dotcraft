namespace DotCraft.Hosting;

/// <summary>
/// Allows orchestrator modules to expose runtime state to host-provided observability surfaces.
/// </summary>
public interface IOrchestratorSnapshotProvider
{
    /// <summary>
    /// Unique module name used to identify the snapshot.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns a serializable snapshot of the current orchestrator state.
    /// </summary>
    object GetSnapshot();

    /// <summary>
    /// Triggers an immediate poll and reconciliation cycle, bypassing the normal interval.
    /// </summary>
    void TriggerRefresh();
}
