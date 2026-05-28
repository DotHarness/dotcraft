using DotCraft.State;
using DotCraft.Tracing;

namespace DotCraft.DashBoard;

/// <summary>
/// Loads Dashboard trace stores without creating or mutating workspace state.
/// </summary>
public static class DashBoardReadOnlyStoreLoader
{
    /// <summary>
    /// Loads trace and usage stores from an existing workspace <c>.craft</c> directory.
    /// Uses <c>state.db</c> in SQLite read-only mode when present, otherwise falls back
    /// to legacy trace JSONL files under <c>.craft/tracing</c>.
    /// </summary>
    public static DashBoardReadOnlyStores Load(string craftPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftPath);

        var tracingPath = Path.Combine(craftPath, "tracing");
        var stateDbPath = Path.Combine(craftPath, "state.db");
        if (File.Exists(stateDbPath))
        {
            var stateRuntime = new StateRuntime(craftPath, readOnly: true);
            var traceStore = new TraceStore(
                tracingPath,
                maxEventsPerSession: 5000,
                synchronousPersist: false,
                stateRuntime: stateRuntime);
            traceStore.LoadFromDisk();

            var tokenUsageStore = new TokenUsageStore(tracingPath, stateRuntime);
            tokenUsageStore.LoadFromDisk();

            return new DashBoardReadOnlyStores(traceStore, tokenUsageStore, UsesStateDb: true);
        }

        var legacyTraceStore = new TraceStore(tracingPath);
        legacyTraceStore.LoadFromDisk();
        var legacyTokenUsageStore = new TokenUsageStore(tracingPath);
        legacyTokenUsageStore.LoadFromDisk();
        return new DashBoardReadOnlyStores(legacyTraceStore, legacyTokenUsageStore, UsesStateDb: false);
    }
}

/// <summary>
/// Stores loaded for a standalone read-only Dashboard process.
/// </summary>
public sealed record DashBoardReadOnlyStores(
    TraceStore TraceStore,
    TokenUsageStore TokenUsageStore,
    bool UsesStateDb);
