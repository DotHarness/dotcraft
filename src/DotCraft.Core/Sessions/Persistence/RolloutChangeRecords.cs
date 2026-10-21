namespace DotCraft.Sessions;

/// <summary>Builds the rollout records that describe one Item or queue change.</summary>
internal static class RolloutChangeRecords
{
    public static ThreadRolloutRecord ItemAppended(string turnId, SessionItem item) => new()
    {
        Kind = RolloutKinds.ItemAppended,
        Timestamp = item.CompletedAt ?? item.CreatedAt,
        ItemAppended = new ItemAppendedPayload { TurnId = turnId, Item = item }
    };

    public static ThreadRolloutRecord QueuedInputAdded(string threadId, QueuedTurnInput queued) => new()
    {
        Kind = RolloutKinds.QueuedInputAdded,
        Timestamp = queued.CreatedAt,
        QueuedInputAdded = new QueuedInputAddedPayload { ThreadId = threadId, QueuedInput = queued }
    };

    public static ThreadRolloutRecord QueuedInputRemoved(string threadId, string queuedInputId, DateTimeOffset lastActiveAt) => new()
    {
        Kind = RolloutKinds.QueuedInputRemoved,
        Timestamp = lastActiveAt,
        QueuedInputRemoved = new QueuedInputRemovedPayload
        {
            ThreadId = threadId,
            QueuedInputId = queuedInputId,
            LastActiveAt = lastActiveAt
        }
    };

    public static ThreadRolloutRecord QueuedInputUpdated(string threadId, QueuedTurnInput queued, DateTimeOffset lastActiveAt) => new()
    {
        Kind = RolloutKinds.QueuedInputUpdated,
        Timestamp = lastActiveAt,
        QueuedInputUpdated = new QueuedInputUpdatedPayload
        {
            ThreadId = threadId,
            QueuedInput = queued,
            LastActiveAt = lastActiveAt
        }
    };

    public static ThreadRolloutRecord QueuedInputReordered(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        DateTimeOffset lastActiveAt) => new()
    {
        Kind = RolloutKinds.QueuedInputReordered,
        Timestamp = lastActiveAt,
        QueuedInputReordered = new QueuedInputReorderedPayload
        {
            ThreadId = threadId,
            OrderedQueuedInputIds = orderedQueuedInputIds.ToList(),
            LastActiveAt = lastActiveAt
        }
    };
}
