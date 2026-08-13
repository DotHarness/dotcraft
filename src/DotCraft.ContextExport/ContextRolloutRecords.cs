using DotCraft.Sessions;

namespace DotCraft.ContextExport;

internal sealed class ContextRolloutRecord
{
    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public ContextItemAppendedPayload? ItemAppended { get; init; }

    public ContextTurnStateReplacedPayload? TurnStateReplaced { get; init; }

    public ContextThreadRolledBackPayload? ThreadRolledBack { get; init; }

    public ContextCompactedPayload? ContextCompacted { get; init; }
}

internal sealed class ContextItemAppendedPayload
{
    public string TurnId { get; init; } = string.Empty;

    public SessionItem Item { get; init; } = new();
}

internal sealed class ContextTurnStateReplacedPayload
{
    public SessionTurn Turn { get; init; } = new();
}

internal sealed class ContextThreadRolledBackPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public int NumTurns { get; init; }
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
}
