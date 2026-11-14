namespace DotCraft.Sessions;

public sealed record ThreadHistoryBootstrap(ThreadHistoryCursor? TurnsCursor, ThreadHistoryCursor? ItemsCursor);

public enum ThreadTimelineEntryKind
{
    Item,
    TurnStarted,
    TurnCompleted
}

/// <summary>The canonical position and current payload of one surviving history identity.</summary>
public sealed record ThreadTimelineEntry(
    ThreadTimelineEntryKind Kind,
    long Position,
    string TurnId,
    SessionTurn? Turn,
    SessionItem? Item = null);

/// <summary>Adapters encode this complete source key without interpreting private cursor formats.</summary>
public sealed record ThreadTimelineCursor(
    long Position,
    ThreadTimelineEntryKind Kind,
    string TurnId,
    string ItemId,
    long SnapshotOrdinal);

public sealed record ThreadTimelinePage(IReadOnlyList<ThreadTimelineEntry> Data, ThreadTimelineCursor? NextCursor);
