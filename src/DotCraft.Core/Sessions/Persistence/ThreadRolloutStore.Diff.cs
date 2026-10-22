using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed partial class ThreadRolloutStore
{
    private static List<ThreadRolloutRecord> BuildRecords(SessionThread? previous, SessionThread current)
    {
        var records = new List<ThreadRolloutRecord>();

        if (previous == null || ThreadBaselineChanged(previous, current))
            records.Add(CreateThreadOpenedRecord(current));

        var previousTurns = previous?.Turns.ToDictionary(t => t.Id, StringComparer.Ordinal) ?? [];
        foreach (var turn in current.Turns)
        {
            if (!previousTurns.TryGetValue(turn.Id, out var previousTurn))
            {
                records.Add(CreateTurnStartedRecord(turn));
                foreach (var item in turn.Items)
                    records.Add(RolloutChangeRecords.ItemAppended(turn.Id, item));

                if (turn.Status != TurnStatus.Running)
                    records.Add(CreateTurnCompletedRecord(turn));
                continue;
            }

            if (TurnsEquivalent(previousTurn, turn))
                continue;

            if (TryBuildIncrementalTurnRecords(previousTurn, turn, records))
                continue;

            records.Add(CreateTurnStartedRecord(turn));
            foreach (var item in turn.Items)
                records.Add(RolloutChangeRecords.ItemAppended(turn.Id, item));

            if (turn.Status != TurnStatus.Running)
                records.Add(CreateTurnCompletedRecord(turn));
        }

        static bool TryBuildIncrementalTurnRecords(
            SessionTurn previousTurn,
            SessionTurn currentTurn,
            List<ThreadRolloutRecord> records)
        {
            if (!TurnIdentityEquivalent(previousTurn, currentTurn))
                return false;
            if (currentTurn.Items.Count < previousTurn.Items.Count)
                return false;

            var statusChanged = previousTurn.Status != currentTurn.Status;
            if (statusChanged && previousTurn.Status != TurnStatus.Running)
                return false;

            for (var i = 0; i < previousTurn.Items.Count; i++)
            {
                if (!string.Equals(
                        previousTurn.Items[i].Id,
                        currentTurn.Items[i].Id,
                        StringComparison.Ordinal))
                    return false;
            }

            if (!statusChanged
                && IsTerminal(currentTurn.Status)
                && !TurnCompletionMetadataEquivalent(previousTurn, currentTurn))
                return false;

            for (var i = 0; i < currentTurn.Items.Count; i++)
            {
                if (i >= previousTurn.Items.Count
                    || !JsonEquals(previousTurn.Items[i], currentTurn.Items[i]))
                {
                    records.Add(RolloutChangeRecords.ItemAppended(currentTurn.Id, currentTurn.Items[i]));
                }
            }

            if (statusChanged)
                records.Add(CreateTurnCompletedRecord(currentTurn));

            return true;
        }

        static bool TurnIdentityEquivalent(SessionTurn previousTurn, SessionTurn currentTurn)
        {
            return string.Equals(previousTurn.Id, currentTurn.Id, StringComparison.Ordinal)
                && string.Equals(previousTurn.ThreadId, currentTurn.ThreadId, StringComparison.Ordinal)
                && previousTurn.StartedAt == currentTurn.StartedAt
                && string.Equals(previousTurn.OriginChannel, currentTurn.OriginChannel, StringComparison.Ordinal)
                && JsonEquals(previousTurn.Initiator, currentTurn.Initiator)
                && JsonEquals(previousTurn.Input, currentTurn.Input);
        }

        static bool IsTerminal(TurnStatus status) =>
            status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled;

        static bool TurnCompletionMetadataEquivalent(SessionTurn previousTurn, SessionTurn currentTurn) =>
            previousTurn.Status == currentTurn.Status
            && previousTurn.CompletedAt == currentTurn.CompletedAt
            && JsonEquals(previousTurn.TokenUsage, currentTurn.TokenUsage)
            && JsonEquals(previousTurn.Error, currentTurn.Error);

        if (!string.Equals(previous?.DisplayName, current.DisplayName, StringComparison.Ordinal))
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = RolloutKinds.ThreadNameUpdated,
                Timestamp = current.LastActiveAt,
                ThreadNameUpdated = new ThreadNameUpdatedPayload
                {
                    ThreadId = current.Id,
                    DisplayName = current.DisplayName
                }
            });
        }

        var previousQueue = previous?.QueuedInputs ?? [];
        var currentQueue = current.QueuedInputs;
        foreach (var queued in currentQueue.Where(q => previousQueue.All(p => !string.Equals(p.Id, q.Id, StringComparison.Ordinal))))
            records.Add(RolloutChangeRecords.QueuedInputAdded(current.Id, queued));

        foreach (var removed in previousQueue.Where(p => currentQueue.All(q => !string.Equals(q.Id, p.Id, StringComparison.Ordinal))))
            records.Add(RolloutChangeRecords.QueuedInputRemoved(current.Id, removed.Id, current.LastActiveAt));

        foreach (var currentQueued in currentQueue)
        {
            var previousQueued = previousQueue.FirstOrDefault(q => string.Equals(q.Id, currentQueued.Id, StringComparison.Ordinal));
            if (previousQueued != null && !string.Equals(previousQueued.Status, currentQueued.Status, StringComparison.Ordinal))
                records.Add(RolloutChangeRecords.QueuedInputUpdated(current.Id, currentQueued, current.LastActiveAt));
        }

        var previousQueueIds = previousQueue.Select(q => q.Id).ToList();
        var currentQueueIds = currentQueue.Select(q => q.Id).ToList();
        if (previousQueueIds.Count == currentQueueIds.Count
            && !previousQueueIds.SequenceEqual(currentQueueIds, StringComparer.Ordinal)
            && previousQueueIds.ToHashSet(StringComparer.Ordinal).SetEquals(currentQueueIds))
        {
            records.Add(RolloutChangeRecords.QueuedInputReordered(current.Id, currentQueueIds, current.LastActiveAt));
        }

        if (previous == null || previous.Status != current.Status || previous.LastActiveAt != current.LastActiveAt)
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = RolloutKinds.ThreadStatusChanged,
                Timestamp = current.LastActiveAt,
                ThreadStatusChanged = new RolloutThreadStatusChangedPayload
                {
                    ThreadId = current.Id,
                    Status = current.Status,
                    LastActiveAt = current.LastActiveAt
                }
            });
        }

        return records;
    }

    private static bool ThreadBaselineChanged(SessionThread previous, SessionThread current)
    {
        if (!string.Equals(previous.UserId, current.UserId, StringComparison.Ordinal))
            return true;
        if (!string.Equals(previous.OriginChannel, current.OriginChannel, StringComparison.Ordinal))
            return true;
        if (!string.Equals(previous.ChannelContext, current.ChannelContext, StringComparison.Ordinal))
            return true;
        if (previous.CreatedAt != current.CreatedAt)
            return true;
        if (previous.HistoryMode != current.HistoryMode)
            return true;
        if (!string.Equals(previous.ForkedFromId, current.ForkedFromId, StringComparison.Ordinal))
            return true;
        if (previous.Ephemeral != current.Ephemeral)
            return true;
        if (!JsonEquals(previous.Worktree, current.Worktree))
            return true;
        if (!JsonEquals(previous.GitInfo, current.GitInfo))
            return true;
        if (!JsonEquals(previous.Source, current.Source))
            return true;
        if (!JsonEquals(previous.Configuration, current.Configuration))
            return true;
        if (previous.ProviderHistorySchemaVersion != current.ProviderHistorySchemaVersion)
            return true;
        return !JsonEquals(previous.Metadata, current.Metadata);
    }

    private static bool TurnsEquivalent(SessionTurn previous, SessionTurn current)
    {
        return JsonEquals(previous, current);
    }

    private static bool JsonEquals<T>(T? left, T? right)
    {
        return JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);
    }

    private static ThreadRolloutRecord CreateThreadOpenedRecord(SessionThread thread)
    {
        return new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ThreadOpened,
            Timestamp = thread.LastActiveAt,
            ThreadOpened = new ThreadOpenedPayload
            {
                ThreadId = thread.Id,
                UserId = thread.UserId,
                OriginChannel = thread.OriginChannel,
                ChannelContext = thread.ChannelContext,
                Source = PersistedThreadSourceCodec.Encode(thread.Source),
                ForkedFromId = thread.ForkedFromId,
                Ephemeral = thread.Ephemeral,
                Worktree = thread.Worktree,
                GitInfo = thread.GitInfo,
                CreatedAt = thread.CreatedAt,
                LastActiveAt = thread.LastActiveAt,
                Metadata = new Dictionary<string, string>(thread.Metadata),
                HistoryMode = thread.HistoryMode,
                Configuration = thread.Configuration,
                ProviderHistorySchemaVersion = thread.ProviderHistorySchemaVersion,
                TurnSequenceHighWatermark = thread.TurnSequenceHighWatermark
            }
        };
    }

    private static ThreadRolloutRecord CreateTurnStartedRecord(SessionTurn turn)
    {
        return new ThreadRolloutRecord
        {
            Kind = RolloutKinds.TurnStarted,
            Timestamp = turn.StartedAt,
            TurnStarted = new TurnStartedPayload
            {
                Turn = new SessionTurn
                {
                    Id = turn.Id,
                    ThreadId = turn.ThreadId,
                    Status = turn.Status == TurnStatus.Completed || turn.Status == TurnStatus.Failed || turn.Status == TurnStatus.Cancelled
                        ? TurnStatus.Running
                        : turn.Status,
                    StartedAt = turn.StartedAt,
                    CompletedAt = null,
                    TokenUsage = null,
                    Error = null,
                    OriginChannel = turn.OriginChannel,
                    Initiator = turn.Initiator,
                    Items = []
                }
            }
        };
    }

    private static ThreadRolloutRecord CreateTurnCompletedRecord(SessionTurn turn)
    {
        return new ThreadRolloutRecord
        {
            Kind = RolloutKinds.TurnCompleted,
            Timestamp = turn.CompletedAt ?? turn.StartedAt,
            TurnCompleted = new TurnCompletedPayload
            {
                TurnId = turn.Id,
                Status = turn.Status,
                CompletedAt = turn.CompletedAt,
                TokenUsage = turn.TokenUsage,
                Error = turn.Error,
                OriginChannel = turn.OriginChannel,
                Initiator = turn.Initiator
            }
        };
    }
}
