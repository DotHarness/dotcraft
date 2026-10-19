using System.Text.Json;

namespace DotCraft.Sessions;

internal static class ProviderHistoryReplayer
{
    public static ProviderHistorySnapshot Replay(
        string expectedThreadId,
        string currentContextWindowId,
        IReadOnlySet<string> survivingTurnIds,
        IReadOnlyList<ThreadRolloutRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentContextWindowId);
        foreach (var record in records)
            ValidateRecord(record, expectedThreadId);

        var replacementIndex = -1;
        ProviderHistoryReplacedPayload? replacement = null;
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var candidate = records[i].ProviderHistoryReplaced;
            if (!IsValidReplacement(candidate, expectedThreadId, survivingTurnIds))
                continue;

            replacementIndex = i;
            replacement = candidate;
            break;
        }

        var generationId = replacement?.GenerationId ?? string.Empty;
        var protocol = replacement?.Protocol ?? ProviderHistorySchema.OpenAIResponsesProtocol;
        var backendId = replacement?.BackendId;
        var providerId = replacement?.ProviderId;
        var contextWindowId = replacement?.ContextWindowId ?? currentContextWindowId;
        var coveredThroughTurnId = replacement?.CoveredThroughTurnId;
        var entries = new List<(ProviderHistoryEntry Entry, string? AttemptId)>();
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        if (replacement != null)
        {
            foreach (var entry in replacement.Entries)
                AddEntry(entry, attemptId: null);
        }

        var abortedAttempts = new HashSet<string>(StringComparer.Ordinal);
        for (var i = replacementIndex + 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.ProviderHistoryAttemptAborted is { } aborted
                && IsCommonValid(aborted.SchemaVersion, aborted.ThreadId, aborted.Protocol, expectedThreadId,
                    aborted.BackendId, aborted.ProviderId)
                && MatchesIdentity(aborted.Protocol, aborted.BackendId, aborted.ProviderId)
                && survivingTurnIds.Contains(aborted.TurnId)
                && (string.IsNullOrEmpty(generationId)
                    || string.Equals(generationId, aborted.GenerationId, StringComparison.Ordinal)))
            {
                abortedAttempts.Add(aborted.AttemptId);
                entries.RemoveAll(pair => string.Equals(pair.AttemptId, aborted.AttemptId, StringComparison.Ordinal));
                entryIds = entries.Select(pair => pair.Entry.EntryId).ToHashSet(StringComparer.Ordinal);
                continue;
            }

            if (record.ProviderHistoryItemsAppended is not { } appended
                || !IsValidAppend(appended, expectedThreadId, survivingTurnIds))
            {
                continue;
            }

            if (string.IsNullOrEmpty(generationId))
            {
                generationId = appended.GenerationId;
                contextWindowId = appended.ContextWindowId;
                protocol = appended.Protocol;
                backendId = appended.BackendId;
                providerId = appended.ProviderId;
            }
            if (!string.Equals(generationId, appended.GenerationId, StringComparison.Ordinal)
                || !MatchesIdentity(appended.Protocol, appended.BackendId, appended.ProviderId)
                || !string.Equals(contextWindowId, appended.ContextWindowId, StringComparison.Ordinal)
                || (appended.AttemptId != null && abortedAttempts.Contains(appended.AttemptId)))
            {
                continue;
            }

            foreach (var entry in appended.Entries)
                AddEntry(entry, appended.AttemptId);
            coveredThroughTurnId = appended.TurnId;
        }

        if (string.IsNullOrEmpty(generationId))
            generationId = currentContextWindowId;

        return new ProviderHistorySnapshot(
            generationId,
            contextWindowId,
            entries.Select(pair => CloneEntry(pair.Entry)).ToList(),
            coveredThroughTurnId,
            ProviderHistoryReasons.IsNativeCompacted(replacement?.Reason))
        {
            Protocol = protocol,
            BackendId = backendId,
            ProviderId = providerId
        };

        bool MatchesIdentity(string candidateProtocol, string? candidateBackend, string? candidateProvider) =>
            string.Equals(protocol, candidateProtocol, StringComparison.Ordinal)
            && string.Equals(backendId, candidateBackend, StringComparison.Ordinal)
            && string.Equals(providerId, candidateProvider, StringComparison.OrdinalIgnoreCase);

        void AddEntry(ProviderHistoryEntry entry, string? attemptId)
        {
            if (!IsValidEntry(entry) || !entryIds.Add(entry.EntryId))
                return;
            entries.Add((CloneEntry(entry), attemptId));
        }
    }

    internal static bool IsValidReplacement(
        ProviderHistoryReplacedPayload? value,
        string expectedThreadId,
        IReadOnlySet<string> survivingTurnIds) =>
        value != null
        && IsCommonValid(value.SchemaVersion, value.ThreadId, value.Protocol, expectedThreadId,
            value.BackendId, value.ProviderId)
        && !string.IsNullOrWhiteSpace(value.GenerationId)
        && !string.IsNullOrWhiteSpace(value.ContextWindowId)
        && (string.IsNullOrWhiteSpace(value.CoveredThroughTurnId)
            || survivingTurnIds.Contains(value.CoveredThroughTurnId));

    private static bool IsValidAppend(
        ProviderHistoryItemsAppendedPayload value,
        string expectedThreadId,
        IReadOnlySet<string> survivingTurnIds) =>
        IsCommonValid(value.SchemaVersion, value.ThreadId, value.Protocol, expectedThreadId,
            value.BackendId, value.ProviderId)
        && survivingTurnIds.Contains(value.TurnId)
        && !string.IsNullOrWhiteSpace(value.GenerationId)
        && !string.IsNullOrWhiteSpace(value.ContextWindowId)
        && value.Source is ProviderHistorySources.LocalInput or ProviderHistorySources.ProviderOutput;

    private static bool IsCommonValid(
        int schemaVersion,
        string threadId,
        string protocol,
        string expectedThreadId,
        string? backendId = null,
        string? providerId = null) =>
        schemaVersion == ProviderHistorySchema.CurrentSchemaVersion
        && string.Equals(threadId, expectedThreadId, StringComparison.Ordinal)
        && (backendId is null && providerId is null
            ? string.Equals(protocol, ProviderHistorySchema.OpenAIResponsesProtocol, StringComparison.Ordinal)
            : !string.IsNullOrWhiteSpace(backendId) && !string.IsNullOrWhiteSpace(providerId)
              && !string.IsNullOrWhiteSpace(protocol));

    private static bool IsValidEntry(ProviderHistoryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.EntryId) && entry.Item.ValueKind == JsonValueKind.Object;

    private static void ValidateRecord(ThreadRolloutRecord record, string expectedThreadId)
    {
        switch (record.Kind)
        {
            case RolloutKinds.ProviderHistoryItemsAppended:
                if (record.ProviderHistoryItemsAppended is not { } appended
                    || !IsCommonValid(
                        appended.SchemaVersion,
                        appended.ThreadId,
                        appended.Protocol,
                        expectedThreadId, appended.BackendId, appended.ProviderId)
                    || string.IsNullOrWhiteSpace(appended.TurnId)
                    || string.IsNullOrWhiteSpace(appended.GenerationId)
                    || string.IsNullOrWhiteSpace(appended.ContextWindowId)
                    || appended.Source is not (
                        ProviderHistorySources.LocalInput or
                        ProviderHistorySources.ProviderOutput)
                    || appended.Entries.Any(entry => !IsValidEntry(entry)))
                {
                    ThrowCorrupt(expectedThreadId);
                }
                break;

            case RolloutKinds.ProviderHistoryReplaced:
                if (record.ProviderHistoryReplaced is not { } replaced
                    || !IsCommonValid(
                        replaced.SchemaVersion,
                        replaced.ThreadId,
                        replaced.Protocol,
                        expectedThreadId, replaced.BackendId, replaced.ProviderId)
                    || string.IsNullOrWhiteSpace(replaced.GenerationId)
                    || string.IsNullOrWhiteSpace(replaced.ContextWindowId)
                    || replaced.Entries.Any(entry => !IsValidEntry(entry)))
                {
                    ThrowCorrupt(expectedThreadId);
                }
                break;

            case RolloutKinds.ProviderHistoryAttemptAborted:
                if (record.ProviderHistoryAttemptAborted is not { } aborted
                    || !IsCommonValid(
                        aborted.SchemaVersion,
                        aborted.ThreadId,
                        aborted.Protocol,
                        expectedThreadId, aborted.BackendId, aborted.ProviderId)
                    || string.IsNullOrWhiteSpace(aborted.TurnId)
                    || string.IsNullOrWhiteSpace(aborted.GenerationId)
                    || string.IsNullOrWhiteSpace(aborted.AttemptId))
                {
                    ThrowCorrupt(expectedThreadId);
                }
                break;
        }
    }

    private static void ThrowCorrupt(string threadId) =>
        throw new InvalidDataException(
            $"responses_provider_history_corrupt: Thread '{threadId}' contains invalid provider history.");

    internal static ProviderHistoryEntry CloneEntry(ProviderHistoryEntry entry) =>
        new()
        {
            EntryId = entry.EntryId,
            Item = entry.Item.Clone()
        };
}
