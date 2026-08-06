using DotCraft.Agents;

namespace DotCraft.Sessions;

internal static class ThreadConversationIdentity
{
    public static ProviderConversationIdentity Create(
        SessionThread thread,
        SessionTurn? turn,
        string contextWindowId,
        ProviderRequestKind requestKind,
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
        return new ProviderConversationIdentity(
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
