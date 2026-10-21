namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>Appends the records of one queue change without saving the whole thread.</summary>
    private async Task PersistThreadChangesAsync(
        SessionThread thread,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct,
        bool deleteUnreferencedAttachments = true)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (thread.Ephemeral)
        {
            await PersistThreadWithMaterializationAsync(thread, ct);
            return;
        }

        await persistence.AppendThreadChangesAsync(thread, records, deleteUnreferencedAttachments, ct);
        SetMaterializedIfCurrent(thread, materialized: true);
    }

    /// <summary>Appends the Items of a running Turn that changed since the Turn last persisted them.</summary>
    private async Task PersistTurnItemsAsync(
        SessionThread thread,
        SessionTurn turn,
        bool materialize,
        CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (thread.Ephemeral
            || !_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            || !runtime.Materialized
            || !runtime.TryGetTurn(turn.Id, out var turnRuntime))
        {
            if (materialize)
                await PersistThreadWithMaterializationAsync(thread, ct);
            else
                await PersistThreadIfMaterializedAsync(thread, ct);
            return;
        }

        var tracker = turnRuntime.PersistedItems;
        await tracker.WriteGate.WaitAsync(ct);
        try
        {
            var current = PersistedTurnItems.Capture(turn.Items.ToArray());
            var changed = tracker.FindChanged(current);
            if (changed is null)
            {
                await persistence.SaveThreadAsync(thread, ct);
                tracker.Reset(current);
                return;
            }

            if (changed.Count == 0)
                return;

            await persistence.AppendThreadChangesAsync(
                thread,
                changed.Select(item => RolloutChangeRecords.ItemAppended(turn.Id, item.Item)).ToList(),
                deleteUnreferencedAttachments: true,
                ct);
            tracker.MarkPersisted(changed);
        }
        finally
        {
            tracker.WriteGate.Release();
        }
    }

    private IReadOnlyList<(PersistedTurnItems Tracker, IReadOnlyList<PersistedTurnItems.PersistedItem> Items)> CaptureRunningTurnItems(
        SessionThread thread)
    {
        if (!_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
            return [];

        return thread.Turns
            .Where(static turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
            .Select(turn => runtime.TryGetTurn(turn.Id, out var turnRuntime)
                ? (turnRuntime.PersistedItems, PersistedTurnItems.Capture(turn.Items.ToArray()))
                : default)
            .Where(static entry => entry.Item1 is not null)
            .ToList();
    }

    private static void MarkTurnItemsPersisted(
        IReadOnlyList<(PersistedTurnItems Tracker, IReadOnlyList<PersistedTurnItems.PersistedItem> Items)> captured)
    {
        foreach (var (tracker, items) in captured)
            tracker.Reset(items);
    }
}
