namespace DotCraft.Sessions;

/// <summary>
/// A thread's token usage after one LLM request: <see cref="Total"/> covers every Turn so far and
/// <see cref="Last"/> covers the request.
/// </summary>
public sealed record ThreadTokenUsageUpdate(
    string ThreadId,
    string? ParentThreadId,
    string TurnId,
    TokenUsageInfo Total,
    TokenUsageInfo Last,
    int? ModelContextWindow);
