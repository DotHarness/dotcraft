using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private static SessionTurn ValidateExportThread(
        SessionThread thread,
        string normalizedWorkspace)
    {
        if (thread.Ephemeral || thread.HistoryMode != HistoryMode.Server)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "Only durable server-managed Threads can be exported for recovery.");
        }
        if (!PathsEqual(thread.WorkspacePath, normalizedWorkspace))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.WorkspaceMismatch,
                "Thread workspace does not match the recovery workspace.");
        }
        if (thread.Status == ThreadStatus.Archived)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageInvalid,
                "An archived Thread must be unarchived before recovery export.");
        }

        var newest = thread.Turns
            .OrderBy(static turn => turn.StartedAt)
            .ThenBy(static turn => turn.Id, StringComparer.Ordinal)
            .LastOrDefault();
        if (newest == null || !IsTerminal(newest.Status))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageInvalid,
                $"Thread '{thread.Id}' does not end at a terminal Turn.");
        }
        return newest;
    }

    private static ThreadRecoverySnapshot CreateRecoverySnapshot(
        SessionThread thread,
        SessionTurn terminalTurn,
        List<ModelHistoryMessage> modelHistory,
        ProviderHistorySnapshot providerHistory) =>
        new()
        {
            FormatVersion = ThreadRecoveryFormatVersion,
            Thread = new ThreadRecoveryHeaderSnapshot
            {
                ThreadId = thread.Id,
                WorkspacePath = NormalizeWorkspace(thread.WorkspacePath),
                UserId = thread.UserId,
                OriginChannel = thread.OriginChannel,
                ChannelContext = thread.ChannelContext,
                Source = PersistedThreadSourceCodec.Encode(thread.Source),
                Worktree = thread.Worktree,
                Metadata = new Dictionary<string, string>(thread.Metadata),
                Configuration = thread.Configuration
            },
            TerminalTurn = new ThreadRecoveryTerminalTurnSnapshot
            {
                TurnId = terminalTurn.Id,
                Status = terminalTurn.Status
            },
            TurnSequenceHighWatermark = Math.Max(
                thread.TurnSequenceHighWatermark,
                SessionIdGenerator.LastTurnSequence(thread.Turns)),
            ModelHistory = modelHistory,
            ProviderHistory = new ThreadRecoveryProviderSnapshot
            {
                SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                GenerationId = providerHistory.GenerationId,
                ContextWindowId = providerHistory.ContextWindowId,
                IsNativeCompacted = providerHistory.IsNativeCompacted,
                Entries = providerHistory.Entries.Select(ProviderHistoryEntryCloner.Clone).ToList()
            }
        };

    private static void ValidateRecoverySnapshot(
        ThreadRecoverySnapshot snapshot,
        string expectedThreadId,
        string normalizedWorkspace)
    {
        if (snapshot.FormatVersion != ThreadRecoveryFormatVersion)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                $"Thread recovery package version {snapshot.FormatVersion} is not supported.");
        }
        if (snapshot.Thread == null
            || !string.Equals(snapshot.Thread.ThreadId, expectedThreadId, StringComparison.Ordinal))
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery snapshot Thread ID is invalid.");
        }
        if (!PathsEqual(snapshot.Thread.WorkspacePath, normalizedWorkspace))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.WorkspaceMismatch,
                "Recovery snapshot belongs to a different workspace.");
        }
        if (snapshot.Thread.Source == null)
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "Recovery snapshot is not an executable server-managed Session.");
        }
        _ = PersistedThreadSourceCodec.Decode(snapshot.Thread.Source);

        if (snapshot.TerminalTurn == null
            || string.IsNullOrWhiteSpace(snapshot.TerminalTurn.TurnId)
            || !IsTerminal(snapshot.TerminalTurn.Status))
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery terminal Turn boundary is invalid.");
        }
        if (snapshot.TurnSequenceHighWatermark < 0
            || snapshot.TurnSequenceHighWatermark
               < SessionIdGenerator.LastTurnSequence([snapshot.TerminalTurn.TurnId]))
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery Turn sequence high-watermark is invalid.");
        }
        if (snapshot.ProviderHistory == null
            || snapshot.ProviderHistory.SchemaVersion != ProviderHistorySchema.CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(snapshot.ProviderHistory.GenerationId)
            || string.IsNullOrWhiteSpace(snapshot.ProviderHistory.ContextWindowId))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageIncompatible,
                "Recovery provider-history schema is not supported.");
        }
        if (snapshot.ModelHistory == null)
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery model Session is missing.");
        var codec = new ModelHistoryCodec();
        foreach (var message in snapshot.ModelHistory)
            _ = codec.Decode(message);

        ValidateProviderSnapshot(snapshot);
    }

    private static void ValidateProviderSnapshot(ThreadRecoverySnapshot snapshot)
    {
        var payload = CreateProviderReplacement(snapshot);
        var record = new ThreadRolloutRecord
        {
            Kind = "provider_history_replaced",
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryReplaced = payload
        };
        ProviderHistorySnapshot replayed;
        try
        {
            replayed = ProviderHistoryReplayer.Replay(
                snapshot.Thread.ThreadId,
                snapshot.ProviderHistory.ContextWindowId,
                new HashSet<string>(StringComparer.Ordinal) { snapshot.TerminalTurn.TurnId },
                [record]);
        }
        catch (InvalidDataException ex)
        {
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery provider history is invalid.", ex);
        }

        if (!ProviderSnapshotsEqual(snapshot.ProviderHistory, replayed, snapshot.TerminalTurn.TurnId))
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery provider history is not replayable.");
    }

    private async Task<(SessionThread Thread, string RolloutPath)> MaterializeRecoveryRolloutAsync(
        ThreadRecoverySnapshot snapshot,
        string validationDirectory,
        CancellationToken ct)
    {
        var validationCraftPath = Path.Combine(validationDirectory, "craft");
        var rolloutStore = new ThreadRolloutStore(validationCraftPath);
        var restoredAt = DateTimeOffset.UtcNow;
        var thread = CreateRestoredThread(snapshot, restoredAt);
        await rolloutStore.SaveThreadAsync(thread, previous: null, ct);
        await rolloutStore.AppendCompactionCheckpointAsync(
            thread.Id,
            snapshot.TerminalTurn.TurnId,
            "recovery",
            "snapshot",
            0,
            0,
            snapshot.ModelHistory,
            restoredAt,
            ct);
        await rolloutStore.AppendProviderHistoryReplacementAsync(CreateProviderReplacement(snapshot), ct);
        await rolloutStore.CloseThreadAsync(thread.Id, ct);

        var rolloutPath = rolloutStore.ResolveExistingPath(thread.Id)
            ?? throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery rollout was not materialized.");
        return (thread, rolloutPath);
    }

    private static SessionThread CreateRestoredThread(
        ThreadRecoverySnapshot snapshot,
        DateTimeOffset restoredAt)
    {
        var header = snapshot.Thread;
        var terminal = snapshot.TerminalTurn;
        var turn = new SessionTurn
        {
            Id = terminal.TurnId,
            ThreadId = header.ThreadId,
            Status = terminal.Status,
            StartedAt = restoredAt,
            CompletedAt = restoredAt
        };
        return new SessionThread
        {
            Id = header.ThreadId,
            WorkspacePath = header.WorkspacePath,
            UserId = header.UserId,
            OriginChannel = header.OriginChannel,
            ChannelContext = header.ChannelContext,
            Source = PersistedThreadSourceCodec.Decode(header.Source!),
            Worktree = header.Worktree,
            Status = ThreadStatus.Active,
            CreatedAt = restoredAt,
            LastActiveAt = restoredAt,
            Metadata = new Dictionary<string, string>(header.Metadata),
            Configuration = header.Configuration,
            Turns = [turn],
            TurnSequenceHighWatermark = snapshot.TurnSequenceHighWatermark,
            ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion
        };
    }

    private static ProviderHistoryReplacedPayload CreateProviderReplacement(ThreadRecoverySnapshot snapshot) =>
        new()
        {
            SchemaVersion = snapshot.ProviderHistory.SchemaVersion,
            ThreadId = snapshot.Thread.ThreadId,
            Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
            GenerationId = snapshot.ProviderHistory.GenerationId,
            ContextWindowId = snapshot.ProviderHistory.ContextWindowId,
            CoveredThroughTurnId = snapshot.TerminalTurn.TurnId,
            Reason = snapshot.ProviderHistory.IsNativeCompacted
                ? ProviderHistoryReasons.RecoveryNativeCompaction
                : ProviderHistoryReasons.Recovery,
            Entries = snapshot.ProviderHistory.Entries.Select(ProviderHistoryEntryCloner.Clone).ToList()
        };

    private static bool ProviderSnapshotsEqual(
        ThreadRecoveryProviderSnapshot expected,
        ProviderHistorySnapshot actual,
        string terminalTurnId)
    {
        if (!string.Equals(expected.GenerationId, actual.GenerationId, StringComparison.Ordinal)
            || !string.Equals(expected.ContextWindowId, actual.ContextWindowId, StringComparison.Ordinal)
            || !string.Equals(actual.CoveredThroughTurnId, terminalTurnId, StringComparison.Ordinal)
            || expected.IsNativeCompacted != actual.IsNativeCompacted
            || expected.Entries.Count != actual.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Entries.Count; index++)
        {
            if (!string.Equals(expected.Entries[index].EntryId, actual.Entries[index].EntryId, StringComparison.Ordinal)
                || !JsonElement.DeepEquals(expected.Entries[index].Item, actual.Entries[index].Item))
            {
                return false;
            }
        }
        return true;
    }
}
