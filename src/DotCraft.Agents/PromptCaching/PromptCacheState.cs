namespace DotCraft.Agents;

internal sealed record PromptCacheMaintenanceScope(
    int SnapshotMessageCount,
    PromptCacheMaintenanceWriteMode CacheWriteMode = PromptCacheMaintenanceWriteMode.WriteThrough);

internal enum PromptCacheMaintenanceWriteMode
{
    WriteThrough,
    ReadOnlyPrefix
}

internal sealed record PromptCacheStateOverride(
    string CacheStateKey,
    string? TraceSessionKey,
    PromptCacheMaintenanceScope? MaintenanceScope);

/// <summary>
/// Narrows remembered breakpoints to an internal state key so a maintenance fork and the main
/// conversation do not overwrite each other's remembered breakpoints.
/// </summary>
internal static class PromptCacheStateScope
{
    private static readonly AsyncLocal<PromptCacheStateOverride?> Override = new();

    internal static PromptCacheStateOverride? Current => Override.Value;

    internal static IDisposable Use(
        string cacheStateKey,
        string? traceSessionKey = null,
        PromptCacheMaintenanceScope? maintenanceScope = null)
    {
        var previous = Override.Value;
        Override.Value = new PromptCacheStateOverride(
            cacheStateKey,
            string.IsNullOrWhiteSpace(traceSessionKey) ? null : traceSessionKey,
            maintenanceScope);
        return new Restore(previous);
    }

    private sealed class Restore(PromptCacheStateOverride? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
