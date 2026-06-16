using System.Collections.Concurrent;

namespace DotCraft.Tools;

/// <summary>
/// One conversational-builder thread's working draft (see specs/agents/agent-profiles.md §12A.1).
/// </summary>
public sealed record ProfileBuilderDraftEntry(string TargetId, string TargetSource, string Markdown);

/// <summary>
/// Process-wide, thread-keyed store of conversational Agent Builder working drafts.
///
/// The draft is the profile-builder agent's authoritative in-session document: the builder tools
/// (<see cref="AgentProfileBuilderToolProvider"/>) mutate it and the system-prompt provider injects it.
/// It is shared as a static registry — consistent with the other cross-cutting tool registries
/// (<c>ToolRegistry</c>, <c>ChatContextRegistry</c>) — because tool providers are constructed directly
/// rather than through DI, so a static store is the simplest shared surface between the tools (which run
/// inside a turn) and the prompt provider (a DI singleton). Presence of an entry marks a thread as a
/// builder thread; the draft is transient until the client creates or saves the profile (§12A.4).
/// </summary>
public static class ProfileBuilderDraftStore
{
    private static readonly ConcurrentDictionary<string, ProfileBuilderDraftEntry> Drafts = new(StringComparer.Ordinal);

    /// <summary>Seeds a thread's draft if it has none yet (idempotent). Returns the resulting entry.</summary>
    public static ProfileBuilderDraftEntry Seed(string threadId, string targetId, string targetSource, string markdown) =>
        Drafts.GetOrAdd(threadId, _ => new ProfileBuilderDraftEntry(targetId, targetSource, markdown ?? string.Empty));

    /// <summary>Returns the thread's draft entry, or null when the thread is not a builder thread.</summary>
    public static ProfileBuilderDraftEntry? TryGet(string threadId) =>
        Drafts.TryGetValue(threadId, out var entry) ? entry : null;

    /// <summary>Replaces the Markdown of an existing builder thread's draft. No-op when the thread has no entry.</summary>
    public static ProfileBuilderDraftEntry? Update(string threadId, string markdown)
    {
        if (!Drafts.TryGetValue(threadId, out var existing))
            return null;
        var updated = existing with { Markdown = markdown ?? string.Empty };
        Drafts[threadId] = updated;
        return updated;
    }

    /// <summary>Drops a thread's draft (call when the builder thread is closed/discarded).</summary>
    public static void Remove(string threadId) => Drafts.TryRemove(threadId, out _);
}
