using DotCraft.Context.Compaction;
using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol;

internal sealed record ContextUsageCheckpoint(
    string ThreadId,
    ContextUsagePersistenceSnapshot Usage,
    ContextUsageAnchor? Anchor)
{
    public static ContextUsageCheckpoint Capture(ThreadStore store, string threadId) => new(
        threadId,
        store.LoadContextUsageSnapshot(threadId)
            ?? throw new InvalidOperationException($"Thread '{threadId}' has no context usage."),
        store.LoadContextUsageAnchor(threadId));

    public Task RestoreAsync(ThreadStore store) => Anchor is { } anchor
        ? store.SaveContextUsageAnchorAsync(ThreadId, Usage.Tokens, anchor, Usage.Source, Usage.IsEstimate)
        : store.SaveContextUsageTokensAsync(ThreadId, Usage.Tokens, Usage.Source, Usage.IsEstimate);
}
