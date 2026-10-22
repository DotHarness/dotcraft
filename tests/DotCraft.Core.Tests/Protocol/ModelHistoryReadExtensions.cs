using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class ModelHistoryReadExtensions
{
    public static async Task<List<ChatMessage>> LoadModelHistoryAsync(
        this ThreadStore store,
        string threadId,
        CancellationToken cancellationToken = default) =>
        await store.LoadThreadAsync(threadId, cancellationToken) is { } thread
            ? await store.LoadModelHistoryAsync(thread, excludedTurnId: null, cancellationToken)
            : [];

    public static async Task<List<ChatMessage>> LoadModelHistoryAsync(
        this SessionPersistenceService persistence,
        string threadId,
        CancellationToken cancellationToken = default) =>
        await persistence.LoadThreadAsync(threadId, cancellationToken) is { } thread
            ? await persistence.LoadModelHistoryAsync(thread, excludedTurnId: null, cancellationToken)
            : [];
}
