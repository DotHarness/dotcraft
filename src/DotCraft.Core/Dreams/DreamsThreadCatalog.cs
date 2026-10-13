using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Workspaces;

namespace DotCraft.Dreams;

/// <summary>
/// Finds the eligible Threads of a store: workspace Threads with server-managed history that resolve the store
/// and are neither internal nor SubAgent Threads, active or archived.
/// </summary>
internal sealed class DreamsThreadCatalog(ThreadStore threadStore, DotCraftPaths paths)
{
    private const int TurnPageSize = 20;

    /// <summary>The eligible Threads that completed a Turn after <paramref name="since"/>, most recent first.</summary>
    public async Task<IReadOnlyList<DreamsThreadInput>> FindAsync(
        DreamsStore store,
        DateTimeOffset? since,
        IReadOnlyCollection<string>? threadIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var threads = new List<DreamsThreadInput>();
        var summaries = await threadStore.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        foreach (var summary in summaries.OrderByDescending(static summary => summary.LastActiveAt))
        {
            if (threads.Count >= limit || since is { } cutoff && summary.LastActiveAt <= cutoff)
                break;
            if (summary.Ephemeral
                || ThreadVisibility.IsInternal(summary)
                || IsSubAgent(summary.Source, summary.OriginChannel)
                || !SamePath(summary.WorkspacePath, paths.WorkspacePath)
                || threadIds is { Count: > 0 } && !threadIds.Contains(summary.Id))
            {
                continue;
            }

            var thread = await ReadHeaderAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (thread == null
                || thread.HistoryMode != HistoryMode.Server
                || ThreadVisibility.IsInternal(thread)
                || thread.Configuration?.MemoryEnabled == false
                || !UsesStore(thread, store)
                || !await CompletedTurnSinceAsync(thread.Id, since, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            threads.Add(new DreamsThreadInput(
                thread.Id,
                thread.DisplayName,
                thread.OriginChannel,
                thread.Status,
                thread.CreatedAt,
                summary.LastActiveAt));
        }

        return threads;
    }

    private async Task<SessionThread?> ReadHeaderAsync(string threadId, CancellationToken cancellationToken)
    {
        try
        {
            return (await threadStore.ReadThreadSnapshotAsync(threadId, cancellationToken).ConfigureAwait(false)).Thread;
        }
        catch (Exception ex) when (ex is NotSupportedException or ThreadHistoryUnavailableException or IOException)
        {
            return null;
        }
    }

    private async Task<bool> CompletedTurnSinceAsync(string threadId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        ThreadHistoryCursor? cursor = null;
        do
        {
            var page = await threadStore.ListThreadTurnsAsync(
                    threadId,
                    cursor,
                    TurnPageSize,
                    ThreadHistorySortDirection.Descending,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var turn in page.Data)
            {
                if (turn.Status == TurnStatus.Completed && (since == null || turn.CompletedAt > since))
                    return true;
                if (since is { } cutoff && (turn.CompletedAt ?? turn.StartedAt) <= cutoff)
                    return false;
            }

            cursor = page.NextCursor;
        }
        while (cursor != null);

        return false;
    }

    private bool UsesStore(SessionThread thread, DreamsStore store)
    {
        try
        {
            return SamePath(MemoryScopes.StoreDirectory(thread.Configuration, paths.Data.RootPath), store.Directory);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSubAgent(ThreadSource source, string? originChannel) =>
        string.Equals(source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(originChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
