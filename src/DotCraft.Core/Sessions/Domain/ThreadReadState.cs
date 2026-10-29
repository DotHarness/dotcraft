namespace DotCraft.Sessions;

/// <summary>A thread's durable read receipt; a null <see cref="FirstUnread"/> means the thread is read.</summary>
public sealed record ThreadReadState(ThreadFirstUnread? FirstUnread, string Revision);

/// <summary>Where the unseen part of a thread starts; a null <see cref="TurnId"/> is an explicit unread mark.</summary>
public sealed record ThreadFirstUnread(string? TurnId)
{
    public static ThreadFirstUnread ThreadStart { get; } = new((string?)null);
}

public enum ThreadReadStateOperation
{
    Read,
    Unread
}

public static class ThreadReadReceipts
{
    public static bool IsEligible(SessionThread thread) =>
        !ThreadVisibility.IsHidden(thread) && !IsSubAgent(thread.Source);

    public static bool IsEligible(ThreadSummary summary) =>
        !summary.Ephemeral && !ThreadVisibility.IsInternal(summary) && !IsSubAgent(summary.Source);

    private static bool IsSubAgent(ThreadSource? source) =>
        string.Equals(source?.Kind, ThreadSourceKinds.SubAgent, StringComparison.Ordinal);
}

public sealed class ThreadReadStateUnavailableException(string threadId)
    : Exception($"Thread '{threadId}' has no read receipt.")
{
    public string ThreadId { get; } = threadId;
}

public sealed class ThreadReadStateConflictException(string threadId, ThreadReadState current)
    : Exception($"Thread '{threadId}' read receipt revision does not match.")
{
    public ThreadReadState Current { get; } = current;
}
