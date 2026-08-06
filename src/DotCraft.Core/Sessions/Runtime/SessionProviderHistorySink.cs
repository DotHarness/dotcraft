using DotCraft.Agents;

namespace DotCraft.Sessions;

/// <summary>
/// Persists provider-owned opaque JSON without interpreting its wire representation.
/// </summary>
internal sealed class SessionProviderHistorySink(SessionPersistenceService persistence) : IProviderHistorySink
{
    public async ValueTask AppendAsync(
        ProviderHistoryIdentity identity,
        ProviderHistoryEntrySource source,
        string? attemptId,
        IReadOnlyList<ProviderHistoryItem> items,
        CancellationToken cancellationToken)
    {
        await persistence.AppendProviderHistoryItemsAsync(new ProviderHistoryItemsAppendedPayload
        {
            SchemaVersion = identity.SchemaVersion,
            ThreadId = identity.ThreadId,
            TurnId = identity.TurnId ?? string.Empty,
            Protocol = identity.Protocol,
            GenerationId = identity.GenerationId,
            ContextWindowId = identity.ContextWindowId,
            Source = source == ProviderHistoryEntrySource.LocalInput
                ? ProviderHistorySources.LocalInput
                : ProviderHistorySources.ProviderOutput,
            AttemptId = attemptId,
            Entries = items.Select(ToEntry).ToList()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReplaceAsync(
        ProviderHistoryIdentity identity,
        string? coveredThroughTurnId,
        string reason,
        IReadOnlyList<ProviderHistoryItem> items,
        CancellationToken cancellationToken)
    {
        await persistence.ReplaceProviderHistoryAsync(new ProviderHistoryReplacedPayload
        {
            SchemaVersion = identity.SchemaVersion,
            ThreadId = identity.ThreadId,
            Protocol = identity.Protocol,
            GenerationId = identity.GenerationId,
            ContextWindowId = identity.ContextWindowId,
            CoveredThroughTurnId = coveredThroughTurnId,
            Reason = reason,
            Entries = items.Select(ToEntry).ToList()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AbortAttemptAsync(
        ProviderHistoryIdentity identity,
        string attemptId,
        CancellationToken cancellationToken)
    {
        await persistence.AbortProviderHistoryAttemptAsync(new ProviderHistoryAttemptAbortedPayload
        {
            SchemaVersion = identity.SchemaVersion,
            ThreadId = identity.ThreadId,
            TurnId = identity.TurnId ?? string.Empty,
            Protocol = identity.Protocol,
            GenerationId = identity.GenerationId,
            AttemptId = attemptId
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ProviderHistoryEntry ToEntry(ProviderHistoryItem item) => new()
    {
        EntryId = item.EntryId,
        Item = item.Payload.Clone()
    };
}
