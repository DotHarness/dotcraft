using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ThreadSource = DotCraft.Sessions.ThreadSource;
namespace DotCraft.Sessions;

internal enum ThreadConversationRequestKind
{
    Turn,
    Compaction,
    Memory
}

internal sealed record ThreadConversationIdentity(
    string CurrentThreadId,
    string RootThreadId,
    string? ParentThreadId,
    string? ForkedFromThreadId,
    string? TurnId,
    string ContextWindowId,
    ThreadConversationRequestKind RequestKind,
    long TurnStartedAtUnixMs,
    string ThreadSource,
    string? SubagentKind)
{
    public static ThreadConversationIdentity Create(
        SessionThread thread,
        SessionTurn? turn,
        string contextWindowId,
        ThreadConversationRequestKind requestKind,
        DateTimeOffset? fallbackStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (string.IsNullOrWhiteSpace(contextWindowId))
            throw new ArgumentException("Value must be non-empty.", nameof(contextWindowId));

        var subAgent = thread.Source.SubAgent;
        var rootThreadId = string.IsNullOrWhiteSpace(subAgent?.RootThreadId)
            ? thread.Id
            : subAgent.RootThreadId;
        var startedAt = turn?.StartedAt ?? fallbackStartedAt ?? DateTimeOffset.UtcNow;
        return new ThreadConversationIdentity(
            CurrentThreadId: thread.Id,
            RootThreadId: rootThreadId,
            ParentThreadId: subAgent?.ParentThreadId ?? thread.Source.SpawnedFromThreadId,
            ForkedFromThreadId: thread.ForkedFromId,
            TurnId: turn?.Id,
            ContextWindowId: contextWindowId.Trim(),
            RequestKind: requestKind,
            TurnStartedAtUnixMs: startedAt.ToUnixTimeMilliseconds(),
            ThreadSource: thread.Source.Kind,
            SubagentKind: ResolveSubagentKind(subAgent));
    }

    private static string? ResolveSubagentKind(SubAgentThreadSource? subAgent) =>
        subAgent == null ? null : "thread_spawn";
}
