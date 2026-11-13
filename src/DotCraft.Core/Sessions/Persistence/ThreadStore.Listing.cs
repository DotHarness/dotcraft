using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    public async Task<ThreadListPage> ListThreadsAsync(
        ThreadListQuery query,
        IReadOnlyList<ThreadSummary>? liveSummaries = null,
        CancellationToken ct = default)
    {
        if (query.Cursor is { } cursor)
            ArgumentException.ThrowIfNullOrWhiteSpace(cursor.ThreadId);
        if (query.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "The page limit must be positive.");
        if (!Enum.IsDefined(query.Scope))
            throw new ArgumentOutOfRangeException(nameof(query), "Unknown thread discovery scope.");

        var live = liveSummaries ?? [];
        try
        {
            var excluded = await ReconcileIndexAsync(ct);
            ct.ThrowIfCancellationRequested();
            return _metadataStore.ListThreads(query, live, excluded);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException
                                       or InvalidDataException or JsonException or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning($"Thread metadata listing failed; using rollout summaries: {ex.Message}");
            var summaries = await LoadFilesystemIndexAsync(_rolloutStore.EnumerateRolloutPaths().ToList(), ct);
            return ThreadListFiltering.Apply(summaries, live, query);
        }
    }
}
