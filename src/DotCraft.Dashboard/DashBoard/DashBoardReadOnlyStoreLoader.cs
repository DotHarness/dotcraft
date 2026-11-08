using DotCraft.Persistence;
using DotCraft.Tracing;

namespace DotCraft.DashBoard;

/// <summary>
/// Loads Dashboard trace stores without creating or mutating workspace state.
/// </summary>
public static class DashBoardReadOnlyStoreLoader
{
    /// <summary>
    /// Opens workspace diagnostics and user-level usage in SQLite read-only mode.
    /// </summary>
    public static DashBoardReadOnlyStores Load(string craftPath, string userDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftPath);

        var stateDbPath = Path.Combine(craftPath, "state.db");
        if (!File.Exists(stateDbPath))
            throw new FileNotFoundException("DotCraft workspace state database was not found.", stateDbPath);

        var stateRuntime = new WorkspaceStateDatabase(craftPath, readOnly: true);
        var traceStore = new TraceStore(stateRuntime, maxEventsPerSession: 5000);
        var tokenUsageStore = File.Exists(Path.Combine(userDataPath, "usage.db"))
            ? new UsageStatisticsStore(new UsageStatisticsDatabase(userDataPath, readOnly: true), stateRuntime.WorkspacePath, collectionEnabled: false)
            : null;
        return new DashBoardReadOnlyStores(traceStore, tokenUsageStore);
    }
}

/// <summary>
/// Stores loaded for a standalone read-only Dashboard process.
/// </summary>
public sealed record DashBoardReadOnlyStores(
    TraceStore TraceStore,
    UsageStatisticsStore? UsageStatisticsStore);
