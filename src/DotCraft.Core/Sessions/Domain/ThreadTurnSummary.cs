namespace DotCraft.Sessions;

/// <summary>A persisted Turn with only summary candidates; ItemCount includes all stored Items before connection filtering.</summary>
public sealed record ThreadTurnSummary(SessionTurn Turn, int ItemCount)
{
    public int ToolExecutionCount { get; init; }
}
