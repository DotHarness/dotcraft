using System.Text.Json.Nodes;

namespace DotCraft.Sessions;

internal sealed record RolloutAppendResult(string Path, RolloutWriteReceipt Receipt, IReadOnlyList<ThreadRolloutRecord> Records);

internal sealed record TurnCompactionCommit(
    string Trigger,
    string Mode,
    long TokensBefore,
    long TokensAfter,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ModelHistoryMessage> ReplacementHistory);

internal sealed class ThreadRolloutRecord
{
    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public ThreadOpenedPayload? ThreadOpened { get; init; }

    public TurnStartedPayload? TurnStarted { get; init; }

    public ItemAppendedPayload? ItemAppended { get; init; }

    public TurnCompletedPayload? TurnCompleted { get; init; }

    public RolloutThreadStatusChangedPayload? ThreadStatusChanged { get; init; }

    public ThreadNameUpdatedPayload? ThreadNameUpdated { get; init; }

    public ThreadRolledBackPayload? ThreadRolledBack { get; init; }

    public ContextCompactedPayload? ContextCompacted { get; init; }

    public ModelHistoryMessagesAppendedPayload? ModelHistoryMessagesAppended { get; init; }

    public WorldStatePayload? WorldState { get; init; }

    public ProviderHistoryItemsAppendedPayload? ProviderHistoryItemsAppended { get; init; }

    public ProviderHistoryReplacedPayload? ProviderHistoryReplaced { get; init; }

    public ProviderHistoryAttemptAbortedPayload? ProviderHistoryAttemptAborted { get; init; }

    public TurnStateReplacedPayload? TurnStateReplaced { get; init; }

    public QueuedInputAddedPayload? QueuedInputAdded { get; init; }

    public QueuedInputRemovedPayload? QueuedInputRemoved { get; init; }

    public QueuedInputUpdatedPayload? QueuedInputUpdated { get; init; }

    public QueuedInputReorderedPayload? QueuedInputReordered { get; init; }
}

internal sealed class ThreadOpenedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string? UserId { get; init; }

    public string OriginChannel { get; init; } = string.Empty;

    public string? ChannelContext { get; init; }

    public PersistedThreadSource? Source { get; init; }

    public string? ForkedFromId { get; init; }

    public bool Ephemeral { get; init; }

    public ThreadWorktreeInfo? Worktree { get; init; }

    public ThreadGitInfo? GitInfo { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }

    public int ProviderHistorySchemaVersion { get; init; }

    public int TurnSequenceHighWatermark { get; init; }
}

internal sealed class TurnStartedPayload
{
    public SessionTurn Turn { get; init; } = new();
}

internal sealed class ItemAppendedPayload
{
    public string TurnId { get; init; } = string.Empty;

    public SessionItem? Item { get; init; }
}

internal sealed class TurnCompletedPayload
{
    public string TurnId { get; init; } = string.Empty;

    public TurnStatus Status { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public TokenUsageInfo? TokenUsage { get; init; }

    public string? Error { get; init; }

    public string? OriginChannel { get; init; }

    public TurnInitiatorContext? Initiator { get; init; }
}

internal sealed class RolloutThreadStatusChangedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public ThreadStatus Status { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class ThreadNameUpdatedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}

internal sealed class ThreadRolledBackPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public int NumTurns { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class ContextCompactedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string CoveredThroughTurnId { get; init; } = string.Empty;

    public string CheckpointId { get; init; } = string.Empty;

    public string Trigger { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public long TokensBefore { get; init; }

    public long TokensAfter { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public List<ModelHistoryMessage> ReplacementHistory { get; init; } = [];
}

internal sealed record ThreadCompactionCheckpoint(
    string ThreadId,
    string CoveredThroughTurnId,
    string CheckpointId,
    string Trigger,
    string Mode,
    long TokensBefore,
    long TokensAfter,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ModelHistoryMessage> ReplacementHistory);

internal sealed class ModelHistoryMessagesAppendedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public List<ModelHistoryMessage> Messages { get; init; } = [];
}

internal sealed class WorldStatePayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public bool Full { get; init; }

    public JsonObject State { get; init; } = [];
}

internal sealed class TurnStateReplacedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public SessionTurn Turn { get; init; } = new();

    public ThreadStatus ThreadStatus { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public string? DisplayName { get; init; }
}

internal sealed class QueuedInputAddedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public QueuedTurnInput QueuedInput { get; init; } = new();
}

internal sealed class QueuedInputRemovedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string QueuedInputId { get; init; } = string.Empty;

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class QueuedInputUpdatedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public QueuedTurnInput QueuedInput { get; init; } = new();

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class QueuedInputReorderedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public List<string> OrderedQueuedInputIds { get; init; } = [];

    public DateTimeOffset LastActiveAt { get; init; }
}
