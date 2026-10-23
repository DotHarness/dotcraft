using System.Runtime.CompilerServices;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private const int HistoryReadPageSize = 128;

    /// <inheritdoc/>
    public async Task<SessionTurn?> ReadThreadTurnAsync(string threadId, string turnId, CancellationToken ct = default) =>
        await ReadTurnAsync(await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false), turnId, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<SessionItem?> ReadThreadItemAsync(
        string threadId,
        string turnId,
        string itemId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        if (ThreadWorkingSet.FindResident(thread, turnId) is { } turn)
            return turn.Items.FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
        return thread.OmittedTurnCount == 0
            ? null
            : await persistence.ReadProjectedItemAsync(thread.Id, turnId, itemId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SessionTurn> ReadThreadTurnsAsync(
        string threadId,
        ThreadHistorySortDirection direction,
        bool includeItems = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        await foreach (var turn in ReadTurnsAsync(thread, direction, includeItems, ct).ConfigureAwait(false))
            yield return turn;
    }

    /// <inheritdoc/>
    public async Task<ThreadHistoryAggregates> ReadThreadAggregatesAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        var resident = thread.Turns.ToArray();
        var totals = ThreadHistoryTotals.Of(resident);
        if (thread.OmittedTurnCount == 0)
            return totals;
        var omitted = await persistence.ReadHistoryAggregatesAsync(
            thread.Id,
            resident.Select(static turn => turn.Id).ToArray(),
            ct).ConfigureAwait(false);
        return ThreadHistoryTotals.Add(omitted, totals);
    }

    private async Task<SessionTurn?> ReadTurnAsync(SessionThread thread, string turnId, CancellationToken ct)
    {
        if (ThreadWorkingSet.FindResident(thread, turnId) is { } resident)
            return resident;
        if (thread.OmittedTurnCount == 0)
            return null;
        return (await persistence.ReadProjectedTurnsAsync(thread.Id, [turnId], ct).ConfigureAwait(false))
            .FirstOrDefault();
    }

    private async Task<List<SessionTurn>> ReadAllTurnsAsync(SessionThread thread, CancellationToken ct)
    {
        var turns = new List<SessionTurn>(thread.TurnCount);
        await foreach (var turn in ReadTurnsAsync(thread, ThreadHistorySortDirection.Ascending, includeItems: true, ct)
                           .ConfigureAwait(false))
        {
            turns.Add(turn);
        }

        return turns;
    }

    private async IAsyncEnumerable<SessionTurn> ReadTurnsAsync(
        SessionThread thread,
        ThreadHistorySortDirection direction,
        bool includeItems,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resident = thread.Turns.ToArray();
        var descending = direction == ThreadHistorySortDirection.Descending;
        if (descending)
        {
            for (var i = resident.Length - 1; i >= 0; i--)
                yield return resident[i];
        }

        if (thread.OmittedTurnCount > 0)
        {
            var residentIds = resident.Select(static turn => turn.Id).ToHashSet(StringComparer.Ordinal);
            if (descending || !includeItems)
            {
                ThreadHistoryCursor? cursor = null;
                do
                {
                    var page = await persistence
                        .ListThreadTurnsAsync(thread.Id, cursor, HistoryReadPageSize, direction, ct)
                        .ConfigureAwait(false);
                    foreach (var turn in page.Data.Where(turn => !residentIds.Contains(turn.Id)))
                    {
                        if (includeItems)
                            turn.Items = await ReadProjectedItemsAsync(thread.Id, turn.Id, ct).ConfigureAwait(false);
                        yield return turn;
                    }

                    cursor = page.NextCursor;
                } while (cursor != null);
            }
            else
            {
                foreach (var turn in await ReadProjectedHistoryAsync(thread.Id, residentIds, ct).ConfigureAwait(false))
                    yield return turn;
            }
        }

        if (!descending)
        {
            foreach (var turn in resident)
                yield return turn;
        }
    }

    private async Task<List<SessionTurn>> ReadProjectedHistoryAsync(
        string threadId,
        IReadOnlySet<string> residentIds,
        CancellationToken ct)
    {
        var turns = new List<SessionTurn>();
        ThreadHistoryCursor? cursor = null;
        do
        {
            var page = await persistence
                .ListThreadTurnsAsync(threadId, cursor, HistoryReadPageSize, ThreadHistorySortDirection.Ascending, ct)
                .ConfigureAwait(false);
            turns.AddRange(page.Data.Where(turn => !residentIds.Contains(turn.Id)));
            cursor = page.NextCursor;
        } while (cursor != null);

        var byId = turns.ToDictionary(static turn => turn.Id, StringComparer.Ordinal);
        cursor = null;
        do
        {
            var page = await persistence
                .ListThreadItemsAsync(threadId, null, cursor, HistoryReadPageSize, ThreadHistorySortDirection.Ascending, ct)
                .ConfigureAwait(false);
            foreach (var entry in page.Data)
            {
                if (byId.TryGetValue(entry.TurnId, out var turn))
                    turn.Items.Add(entry.Item);
            }

            cursor = page.NextCursor;
        } while (cursor != null);

        return turns;
    }

    private async Task<List<SessionItem>> ReadProjectedItemsAsync(string threadId, string turnId, CancellationToken ct)
    {
        var items = new List<SessionItem>();
        ThreadHistoryCursor? cursor = null;
        do
        {
            var page = await persistence
                .ListThreadItemsAsync(threadId, turnId, cursor, HistoryReadPageSize, ThreadHistorySortDirection.Ascending, ct)
                .ConfigureAwait(false);
            items.AddRange(page.Data.Select(static entry => entry.Item));
            cursor = page.NextCursor;
        } while (cursor != null);

        return items;
    }
}
