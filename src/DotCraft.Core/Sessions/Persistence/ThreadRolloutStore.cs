using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed class ThreadRolloutStore
{
    private readonly string _activeDir;
    private readonly string _archivedDir;
    private readonly IRolloutWriter _writer;
    private readonly Func<string, CancellationToken, Task>? _beforeDeleteAsync;

    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public ThreadRolloutStore(string botPath)
        : this(botPath, beforeDeleteAsync: null)
    {
    }

    internal ThreadRolloutStore(
        string botPath,
        Func<string, CancellationToken, Task>? beforeDeleteAsync)
    {
        _activeDir = Path.Combine(botPath, "threads", "active");
        _archivedDir = Path.Combine(botPath, "threads", "archived");
        _writer = new OrderedRolloutWriter(JsonOptions);
        _beforeDeleteAsync = beforeDeleteAsync;
        Directory.CreateDirectory(_activeDir);
        Directory.CreateDirectory(_archivedDir);
    }

    public string GetExpectedPath(string threadId, bool archived)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var fileName = BuildFileName(threadId);
        return Path.Combine(archived ? _archivedDir : _activeDir, fileName);
    }

    public string? ResolveExistingPath(string threadId)
    {
        foreach (var archived in new[] { false, true })
        {
            var path = GetExpectedPath(threadId, archived);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        foreach (var archived in new[] { false, true })
        {
            var path = GetExpectedPath(threadId, archived);
            if (!File.Exists(path))
                continue;

            var thread = await LoadThreadFromPathAsync(path, ct);
            if (thread != null && string.Equals(thread.Id, threadId, StringComparison.Ordinal))
                return thread;
        }

        return null;
    }

    public async Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryNormalizeAllowedPath(path, out var normalizedPath))
            throw new ArgumentException("Thread path must resolve directly under .craft/threads/active or .craft/threads/archived.", nameof(path));
        if (!File.Exists(normalizedPath))
            return null;
        return await ReplayAsync(File.ReadLinesAsync(normalizedPath, ct), ct);
    }

    public IEnumerable<SessionThread> LoadAllThreads()
    {
        foreach (var dir in new[] { _activeDir, _archivedDir })
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                SessionThread? thread = null;
                try
                {
                    thread = Replay(File.ReadLines(path));
                }
                catch
                {
                    // Corrupt rollout files are already ignored during discovery.
                }

                if (thread != null)
                    yield return thread;
            }
        }
    }

    public IEnumerable<string> EnumerateRolloutPaths()
    {
        foreach (var dir in new[] { _activeDir, _archivedDir })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
                yield return Path.GetFullPath(path);
        }
    }

    public async Task<RolloutAppendResult> SaveThreadAsync(SessionThread thread, SessionThread? previous, CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath != null && !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await _writer.CloseAsync(thread.Id, ct);
            existingPath = MoveToTargetPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
        }

        var records = BuildRecords(previous, thread);
        if (records.Count == 0 && !File.Exists(targetPath))
            records.Add(CreateThreadOpenedRecord(thread));

        if (records.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var receipt = await AppendRecordsAsync(thread.Id, targetPath, records, ct);
            return new RolloutAppendResult(targetPath, receipt);
        }

        return new RolloutAppendResult(
            targetPath,
            new RolloutWriteReceipt(File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0, 0, new Dictionary<string, long>()));
    }

    public async Task<RolloutAppendResult> AppendRollbackAsync(
        SessionThread thread,
        int numTurns,
        CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath != null && !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await _writer.CloseAsync(thread.Id, ct);
            existingPath = MoveToTargetPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
        }

        if (existingPath == null && !File.Exists(targetPath))
            throw new KeyNotFoundException($"Thread '{thread.Id}' not found.");

        var record = new ThreadRolloutRecord
        {
            Kind = "thread_rolled_back",
            Timestamp = thread.LastActiveAt,
            ThreadRolledBack = new ThreadRolledBackPayload
            {
                ThreadId = thread.Id,
                NumTurns = numTurns,
                LastActiveAt = thread.LastActiveAt
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var receipt = await AppendRecordsAsync(thread.Id, targetPath, [record], ct);
        return new RolloutAppendResult(targetPath, receipt);
    }

    public async Task<RolloutAppendResult> AppendTurnStateAsync(
        SessionThread thread,
        SessionTurn turn,
        CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath == null)
            return await SaveThreadAsync(thread, previous: null, ct);
        if (!string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await _writer.CloseAsync(thread.Id, ct);
            existingPath = MoveToTargetPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
        }
        var record = new ThreadRolloutRecord
        {
            Kind = "turn_state_replaced",
            Timestamp = thread.LastActiveAt,
            TurnStateReplaced = new TurnStateReplacedPayload
            {
                ThreadId = thread.Id,
                Turn = turn,
                ThreadStatus = thread.Status,
                LastActiveAt = thread.LastActiveAt,
                DisplayName = thread.DisplayName
            }
        };

        var receipt = await AppendRecordsAsync(thread.Id, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<RolloutAppendResult> AppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        IReadOnlyList<ModelHistoryMessage> replacementHistory,
        DateTimeOffset createdAt,
        CancellationToken ct = default)
    {
        var existingPath = ResolveExistingPath(threadId);
        if (existingPath == null)
            throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        var record = new ThreadRolloutRecord
        {
            Kind = "context_compacted",
            Timestamp = createdAt,
            ContextCompacted = new ContextCompactedPayload
            {
                ThreadId = threadId,
                CoveredThroughTurnId = coveredThroughTurnId,
                CheckpointId = $"compact_{createdAt:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}",
                Trigger = trigger,
                Mode = mode,
                TokensBefore = tokensBefore,
                TokensAfter = tokensAfter,
                CreatedAt = createdAt,
                ReplacementHistory = [.. replacementHistory]
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        var receipt = await AppendRecordsAsync(threadId, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<IReadOnlyList<ThreadCompactionCheckpoint>> LoadCompactionCheckpointsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return [];

        var checkpoints = new List<ThreadCompactionCheckpoint>();
        await foreach (var line in File.ReadLinesAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (record is not { Kind: "context_compacted", ContextCompacted: { } checkpoint } ||
                !string.Equals(checkpoint.ThreadId, threadId, StringComparison.Ordinal))
            {
                continue;
            }

            checkpoints.Add(new ThreadCompactionCheckpoint(
                checkpoint.ThreadId,
                checkpoint.CoveredThroughTurnId,
                checkpoint.CheckpointId,
                checkpoint.Trigger,
                checkpoint.Mode,
                checkpoint.TokensBefore,
                checkpoint.TokensAfter,
                checkpoint.CreatedAt,
                checkpoint.ReplacementHistory));
        }

        return checkpoints;
    }

    public async Task<RolloutAppendResult> AppendModelHistoryAsync(
        string threadId,
        string turnId,
        IReadOnlyList<ModelHistoryMessage> messages,
        CancellationToken ct = default)
    {
        var existingPath = ResolveExistingPath(threadId)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        if (messages.Count == 0)
            return new RolloutAppendResult(
                existingPath,
                new RolloutWriteReceipt(new FileInfo(existingPath).Length, 0, new Dictionary<string, long>()));

        var record = new ThreadRolloutRecord
        {
            Kind = "model_history_messages_appended",
            Timestamp = DateTimeOffset.UtcNow,
            ModelHistoryMessagesAppended = new ModelHistoryMessagesAppendedPayload
            {
                ThreadId = threadId,
                TurnId = turnId,
                Messages = [.. messages]
            }
        };

        var receipt = await AppendRecordsAsync(threadId, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<RolloutAppendResult> AppendProviderHistoryItemsAsync(
        ProviderHistoryItemsAppendedPayload payload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var existingPath = ResolveExistingPath(payload.ThreadId)
            ?? throw new KeyNotFoundException($"Thread '{payload.ThreadId}' not found.");
        if (payload.Entries.Count == 0)
        {
            return new RolloutAppendResult(
                existingPath,
                new RolloutWriteReceipt(new FileInfo(existingPath).Length, 0, new Dictionary<string, long>()));
        }

        var record = new ThreadRolloutRecord
        {
            Kind = "provider_history_items_appended",
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryItemsAppended = payload
        };
        var receipt = await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<RolloutAppendResult> AppendProviderHistoryReplacementAsync(
        ProviderHistoryReplacedPayload payload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var existingPath = ResolveExistingPath(payload.ThreadId)
            ?? throw new KeyNotFoundException($"Thread '{payload.ThreadId}' not found.");
        var record = new ThreadRolloutRecord
        {
            Kind = "provider_history_replaced",
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryReplaced = payload
        };
        var receipt = await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<RolloutAppendResult> AppendProviderHistoryAttemptAbortedAsync(
        ProviderHistoryAttemptAbortedPayload payload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var existingPath = ResolveExistingPath(payload.ThreadId)
            ?? throw new KeyNotFoundException($"Thread '{payload.ThreadId}' not found.");
        var record = new ThreadRolloutRecord
        {
            Kind = "provider_history_attempt_aborted",
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryAttemptAborted = payload
        };
        var receipt = await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<IReadOnlyList<ThreadRolloutRecord>> LoadProviderHistoryRecordsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return [];

        var records = new List<ThreadRolloutRecord>();
        await foreach (var line in File.ReadLinesAsync(path, ct).WithCancellation(ct))
        {
            if (!line.Contains("provider_history_", StringComparison.Ordinal))
                continue;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Thread '{threadId}' contains malformed provider history.",
                    ex);
            }

            if (record?.Kind is "provider_history_items_appended"
                or "provider_history_replaced"
                or "provider_history_attempt_aborted")
            {
                records.Add(record);
            }
        }

        return records;
    }

    public async Task<RolloutAppendResult> AppendTurnCommitAsync(
        SessionThread thread,
        SessionTurn turn,
        IReadOnlyList<ModelHistoryMessage> modelHistory,
        TurnCompactionCommit? compaction,
        CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath == null)
        {
            var opened = await SaveThreadAsync(thread, previous: null, ct);
            existingPath = opened.Path;
        }
        if (!string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await _writer.CloseAsync(thread.Id, ct);
            existingPath = MoveToTargetPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
        }
        var records = new List<ThreadRolloutRecord>
        {
            new()
            {
                Kind = "turn_state_replaced",
                Timestamp = thread.LastActiveAt,
                TurnStateReplaced = new TurnStateReplacedPayload
                {
                    ThreadId = thread.Id,
                    Turn = turn,
                    ThreadStatus = thread.Status,
                    LastActiveAt = thread.LastActiveAt,
                    DisplayName = thread.DisplayName
                }
            }
        };

        if (compaction != null)
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "context_compacted",
                Timestamp = compaction.CreatedAt,
                ContextCompacted = new ContextCompactedPayload
                {
                    ThreadId = thread.Id,
                    CoveredThroughTurnId = turn.Id,
                    CheckpointId = $"compact_{compaction.CreatedAt:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}",
                    Trigger = compaction.Trigger,
                    Mode = compaction.Mode,
                    TokensBefore = compaction.TokensBefore,
                    TokensAfter = compaction.TokensAfter,
                    CreatedAt = compaction.CreatedAt,
                    ReplacementHistory = [.. compaction.ReplacementHistory]
                }
            });
        }

        if (modelHistory.Count > 0)
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "model_history_messages_appended",
                Timestamp = DateTimeOffset.UtcNow,
                ModelHistoryMessagesAppended = new ModelHistoryMessagesAppendedPayload
                {
                    ThreadId = thread.Id,
                    TurnId = turn.Id,
                    Messages = [.. modelHistory]
                }
            });
        }

        var receipt = await AppendRecordsAsync(thread.Id, existingPath, records, ct);
        return new RolloutAppendResult(existingPath, receipt);
    }

    public async Task<ModelHistoryReplayResult> ReplayModelHistoryAsync(
        string threadId,
        SessionThread thread,
        string? excludedTurnId = null,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return new ModelHistoryReplayResult([], HasModelHistoryRecords: false);

        return await new RolloutReplayer().ReplayModelHistoryAsync(
            path,
            thread.Turns,
            excludedTurnId,
            ct,
            threadId);
    }

    public async Task DeleteThreadAsync(string threadId, CancellationToken ct = default)
    {
        await _writer.CloseAsync(threadId, ct);
        if (_beforeDeleteAsync != null)
            await _beforeDeleteAsync(threadId, ct);
        foreach (var archived in new[] { false, true })
        {
            var path = GetExpectedPath(threadId, archived);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public Task CloseThreadAsync(string threadId, CancellationToken ct = default) =>
        _writer.CloseAsync(threadId, ct);

    private string MoveToTargetPath(string threadId, bool archived, string existingPath)
    {
        var targetPath = GetExpectedPath(threadId, archived);
        if (string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
            return targetPath;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        if (File.Exists(existingPath))
            File.Move(existingPath, targetPath);
        return targetPath;
    }

    public bool IsArchivedPath(string path)
        => TryNormalizeAllowedPath(path, out var normalizedPath)
        && string.Equals(Path.GetDirectoryName(normalizedPath), Path.GetFullPath(_archivedDir), StringComparison.OrdinalIgnoreCase);

    public bool TryNormalizeAllowedPath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;

        var parent = Path.GetDirectoryName(fullPath);
        if (parent == null)
            return false;

        var active = Path.GetFullPath(_activeDir);
        var archived = Path.GetFullPath(_archivedDir);
        if (!string.Equals(parent, active, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parent, archived, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    internal static string BuildFileName(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var safeName = string.Concat(threadId.Split(Path.GetInvalidFileNameChars()));
        if (string.Equals(safeName, threadId, StringComparison.Ordinal))
            return $"{safeName}.jsonl";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(threadId))).ToLowerInvariant();
        return $"thread-{hash}.jsonl";
    }

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
                    records.Add(CreateItemAppendedRecord(turn.Id, item));

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
                records.Add(CreateItemAppendedRecord(turn.Id, item));

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

            if (!statusChanged && !TurnCompletionMetadataEquivalent(previousTurn, currentTurn))
                return false;

            for (var i = 0; i < currentTurn.Items.Count; i++)
            {
                if (i >= previousTurn.Items.Count
                    || !JsonEquals(previousTurn.Items[i], currentTurn.Items[i]))
                {
                    records.Add(CreateItemAppendedRecord(currentTurn.Id, currentTurn.Items[i]));
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

        static bool TurnCompletionMetadataEquivalent(SessionTurn previousTurn, SessionTurn currentTurn) =>
            previousTurn.Status == currentTurn.Status
            && previousTurn.CompletedAt == currentTurn.CompletedAt
            && JsonEquals(previousTurn.TokenUsage, currentTurn.TokenUsage)
            && JsonEquals(previousTurn.Error, currentTurn.Error);

        if (!string.Equals(previous?.DisplayName, current.DisplayName, StringComparison.Ordinal))
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "thread_name_updated",
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
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "queued_input_added",
                Timestamp = queued.CreatedAt,
                QueuedInputAdded = new QueuedInputAddedPayload
                {
                    ThreadId = current.Id,
                    QueuedInput = queued
                }
            });
        }

        foreach (var removed in previousQueue.Where(p => currentQueue.All(q => !string.Equals(q.Id, p.Id, StringComparison.Ordinal))))
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "queued_input_removed",
                Timestamp = current.LastActiveAt,
                QueuedInputRemoved = new QueuedInputRemovedPayload
                {
                    ThreadId = current.Id,
                    QueuedInputId = removed.Id,
                    LastActiveAt = current.LastActiveAt
                }
            });
        }

        foreach (var currentQueued in currentQueue)
        {
            var previousQueued = previousQueue.FirstOrDefault(q => string.Equals(q.Id, currentQueued.Id, StringComparison.Ordinal));
            if (previousQueued == null || string.Equals(previousQueued.Status, currentQueued.Status, StringComparison.Ordinal))
                continue;

            records.Add(new ThreadRolloutRecord
            {
                Kind = "queued_input_updated",
                Timestamp = current.LastActiveAt,
                QueuedInputUpdated = new QueuedInputUpdatedPayload
                {
                    ThreadId = current.Id,
                    QueuedInput = currentQueued,
                    LastActiveAt = current.LastActiveAt
                }
            });
        }

        var previousQueueIds = previousQueue.Select(q => q.Id).ToList();
        var currentQueueIds = currentQueue.Select(q => q.Id).ToList();
        if (previousQueueIds.Count == currentQueueIds.Count
            && !previousQueueIds.SequenceEqual(currentQueueIds, StringComparer.Ordinal)
            && previousQueueIds.ToHashSet(StringComparer.Ordinal).SetEquals(currentQueueIds))
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "queued_input_reordered",
                Timestamp = current.LastActiveAt,
                QueuedInputReordered = new QueuedInputReorderedPayload
                {
                    ThreadId = current.Id,
                    OrderedQueuedInputIds = currentQueueIds,
                    LastActiveAt = current.LastActiveAt
                }
            });
        }

        if (previous == null || previous.Status != current.Status || previous.LastActiveAt != current.LastActiveAt)
        {
            records.Add(new ThreadRolloutRecord
            {
                Kind = "thread_status_changed",
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

    public Task ShutdownAsync(CancellationToken ct = default) => _writer.ShutdownAllAsync(ct);

    private async Task<RolloutWriteReceipt> AppendRecordsAsync(
        string threadId,
        string path,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct)
    {
        await _writer.AddBatchAsync(threadId, path, records, ct);
        return await _writer.FlushAsync(threadId, ct);
    }

    private static bool ThreadBaselineChanged(SessionThread previous, SessionThread current)
    {
        if (!string.Equals(previous.WorkspacePath, current.WorkspacePath, StringComparison.Ordinal))
            return true;
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
            Kind = "thread_opened",
            Timestamp = thread.LastActiveAt,
            ThreadOpened = new ThreadOpenedPayload
            {
                ThreadId = thread.Id,
                WorkspacePath = thread.WorkspacePath,
                UserId = thread.UserId,
                OriginChannel = thread.OriginChannel,
                ChannelContext = thread.ChannelContext,
                Source = PersistedThreadSourceCodec.Encode(thread.Source),
                ForkedFromId = thread.ForkedFromId,
                Ephemeral = thread.Ephemeral,
                Worktree = thread.Worktree,
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
            Kind = "turn_started",
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

    private static ThreadRolloutRecord CreateItemAppendedRecord(string turnId, SessionItem item)
    {
        return new ThreadRolloutRecord
        {
            Kind = "item_appended",
            Timestamp = item.CompletedAt ?? item.CreatedAt,
            ItemAppended = new ItemAppendedPayload
            {
                TurnId = turnId,
                Item = item
            }
        };
    }

    private static ThreadRolloutRecord CreateTurnCompletedRecord(SessionTurn turn)
    {
        return new ThreadRolloutRecord
        {
            Kind = "turn_completed",
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

    private static SessionThread? Replay(IEnumerable<string> lines)
    {
        var replay = new ThreadReplay();

        foreach (var line in lines)
            replay.Apply(line);

        return replay.Build();
    }

    private static async Task<SessionThread?> ReplayAsync(IAsyncEnumerable<string> lines, CancellationToken ct)
    {
        var replay = new ThreadReplay();
        await foreach (var line in lines.WithCancellation(ct))
            replay.Apply(line);

        return replay.Build();
    }

    private sealed class ThreadReplay
    {
        private readonly Dictionary<string, SessionTurn> _turns = new(StringComparer.Ordinal);
        private SessionThread? _thread;
        private int _turnSequenceHighWatermark;
        private bool _hasCanonicalHeader;

        public void Apply(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                if (!_hasCanonicalHeader)
                    throw new InvalidDataException("The canonical thread header is unreadable.", ex);
                System.Diagnostics.Trace.TraceWarning("Skipped a malformed rollout record after the canonical thread header.");
                return;
            }

            if (record == null)
            {
                if (!_hasCanonicalHeader)
                    throw new InvalidDataException("The canonical thread header is empty.");
                System.Diagnostics.Trace.TraceWarning("Skipped an empty rollout record after the canonical thread header.");
                return;
            }

            if (record.Kind == "thread_opened" && record.ThreadOpened == null)
                throw new InvalidDataException("A canonical thread baseline record is incomplete.");

            if (!_hasCanonicalHeader
                && (record.Kind != "thread_opened" || record.ThreadOpened == null))
            {
                throw new InvalidDataException("The rollout does not begin with a canonical thread header.");
            }

            switch (record.Kind)
            {
                case "thread_opened" when record.ThreadOpened != null:
                    _thread ??= new SessionThread();
                    _thread.Id = record.ThreadOpened.ThreadId;
                    _thread.WorkspacePath = record.ThreadOpened.WorkspacePath;
                    _thread.UserId = record.ThreadOpened.UserId;
                    _thread.OriginChannel = record.ThreadOpened.OriginChannel;
                    _thread.ChannelContext = record.ThreadOpened.ChannelContext;
                    _thread.Source = PersistedThreadSourceCodec.Decode(
                        record.ThreadOpened.Source
                        ?? throw new InvalidDataException("The canonical thread header has no source."));
                    _thread.ForkedFromId = record.ThreadOpened.ForkedFromId;
                    _thread.Ephemeral = record.ThreadOpened.Ephemeral;
                    _thread.Worktree = record.ThreadOpened.Worktree;
                    _thread.CreatedAt = record.ThreadOpened.CreatedAt;
                    _thread.LastActiveAt = record.ThreadOpened.LastActiveAt;
                    _thread.Metadata = new Dictionary<string, string>(record.ThreadOpened.Metadata);
                    _thread.HistoryMode = record.ThreadOpened.HistoryMode;
                    _thread.Configuration = record.ThreadOpened.Configuration;
                    _thread.ProviderHistorySchemaVersion = record.ThreadOpened.ProviderHistorySchemaVersion;
                    _turnSequenceHighWatermark = Math.Max(
                        _turnSequenceHighWatermark,
                        record.ThreadOpened.TurnSequenceHighWatermark);
                    _hasCanonicalHeader = true;
                    break;

                case "thread_name_updated" when _thread != null && record.ThreadNameUpdated != null:
                    _thread.DisplayName = record.ThreadNameUpdated.DisplayName;
                    break;

                case "thread_status_changed" when _thread != null && record.ThreadStatusChanged != null:
                    _thread.Status = record.ThreadStatusChanged.Status;
                    _thread.LastActiveAt = record.ThreadStatusChanged.LastActiveAt;
                    break;

                case "turn_state_replaced" when _thread != null && record.TurnStateReplaced != null:
                    var replacement = record.TurnStateReplaced;
                    var replacementTurn = replacement.Turn;
                    _turnSequenceHighWatermark = Math.Max(
                        _turnSequenceHighWatermark,
                        SessionIdGenerator.LastTurnSequence([replacementTurn.Id]));
                    replacementTurn.Input ??= replacementTurn.Items.FirstOrDefault(static item =>
                        item.Type == ItemType.UserMessage);
                    _turns[replacementTurn.Id] = replacementTurn;
                    _thread.Status = replacement.ThreadStatus;
                    _thread.LastActiveAt = replacement.LastActiveAt;
                    _thread.DisplayName = replacement.DisplayName;
                    break;

                case "turn_started" when _thread != null && record.TurnStarted != null:
                    var started = record.TurnStarted.Turn;
                    _turnSequenceHighWatermark = Math.Max(
                        _turnSequenceHighWatermark,
                        SessionIdGenerator.LastTurnSequence([started.Id]));
                    started.Items = [];
                    started.Input = null;
                    _turns[started.Id] = started;
                    break;

                case "item_appended" when record.ItemAppended != null:
                    if (!_turns.TryGetValue(record.ItemAppended.TurnId, out var turn))
                    {
                        turn = new SessionTurn
                        {
                            Id = record.ItemAppended.TurnId,
                            ThreadId = _thread?.Id ?? string.Empty,
                            Status = TurnStatus.Running,
                            StartedAt = record.Timestamp
                        };
                        _turns[turn.Id] = turn;
                    }

                    var existingIdx = turn.Items.FindIndex(i => string.Equals(i.Id, record.ItemAppended.Item.Id, StringComparison.Ordinal));
                    if (existingIdx >= 0)
                        turn.Items[existingIdx] = record.ItemAppended.Item;
                    else
                        turn.Items.Add(record.ItemAppended.Item);

                    if (record.ItemAppended.Item.Type == ItemType.UserMessage && turn.Input == null)
                        turn.Input = record.ItemAppended.Item;
                    break;

                case "turn_completed" when record.TurnCompleted != null && _turns.TryGetValue(record.TurnCompleted.TurnId, out var completedTurn):
                    completedTurn.Status = record.TurnCompleted.Status;
                    completedTurn.CompletedAt = record.TurnCompleted.CompletedAt;
                    completedTurn.TokenUsage = record.TurnCompleted.TokenUsage;
                    completedTurn.Error = record.TurnCompleted.Error;
                    completedTurn.OriginChannel = record.TurnCompleted.OriginChannel;
                    completedTurn.Initiator = record.TurnCompleted.Initiator;
                    break;

                case "thread_rolled_back" when _thread != null && record.ThreadRolledBack != null:
                    ApplyRollback(_turns, record.ThreadRolledBack.NumTurns);
                    _thread.LastActiveAt = record.ThreadRolledBack.LastActiveAt;
                    break;

                case "queued_input_added" when _thread != null && record.QueuedInputAdded != null:
                    if (_thread.QueuedInputs.All(q => !string.Equals(q.Id, record.QueuedInputAdded.QueuedInput.Id, StringComparison.Ordinal)))
                        _thread.QueuedInputs.Add(record.QueuedInputAdded.QueuedInput);
                    break;

                case "queued_input_removed" when _thread != null && record.QueuedInputRemoved != null:
                    _thread.QueuedInputs.RemoveAll(q => string.Equals(q.Id, record.QueuedInputRemoved.QueuedInputId, StringComparison.Ordinal));
                    _thread.LastActiveAt = record.QueuedInputRemoved.LastActiveAt;
                    break;

                case "queued_input_updated" when _thread != null && record.QueuedInputUpdated != null:
                    var updateIndex = _thread.QueuedInputs.FindIndex(q => string.Equals(q.Id, record.QueuedInputUpdated.QueuedInput.Id, StringComparison.Ordinal));
                    if (updateIndex >= 0)
                        _thread.QueuedInputs[updateIndex] = record.QueuedInputUpdated.QueuedInput;
                    _thread.LastActiveAt = record.QueuedInputUpdated.LastActiveAt;
                    break;

                case "queued_input_reordered" when _thread != null && record.QueuedInputReordered != null:
                    var queuedById = _thread.QueuedInputs.ToDictionary(q => q.Id, StringComparer.Ordinal);
                    var seenQueuedIds = new HashSet<string>(StringComparer.Ordinal);
                    var reorderedQueue = new List<QueuedTurnInput>(_thread.QueuedInputs.Count);
                    foreach (var queuedInputId in record.QueuedInputReordered.OrderedQueuedInputIds)
                    {
                        if (seenQueuedIds.Add(queuedInputId) && queuedById.TryGetValue(queuedInputId, out var queuedInput))
                            reorderedQueue.Add(queuedInput);
                    }

                    reorderedQueue.AddRange(_thread.QueuedInputs.Where(q => !seenQueuedIds.Contains(q.Id)));
                    _thread.QueuedInputs = reorderedQueue;
                    _thread.LastActiveAt = record.QueuedInputReordered.LastActiveAt;
                    break;
            }
        }

        public SessionThread? Build()
        {
            if (_thread == null)
                return null;

            _thread.Turns = _turns.Values.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
            _thread.TurnSequenceHighWatermark = Math.Max(
                _turnSequenceHighWatermark,
                SessionIdGenerator.LastTurnSequence(_thread.Turns));
            return _thread;
        }
    }

    private static void ApplyRollback(Dictionary<string, SessionTurn> turns, int numTurns)
    {
        if (numTurns <= 0 || turns.Count == 0)
            return;

        var idsToRemove = turns.Values
            .OrderBy(t => t.StartedAt)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .TakeLast(numTurns)
            .Select(t => t.Id)
            .ToList();

        foreach (var id in idsToRemove)
            turns.Remove(id);
    }
}

internal sealed record RolloutAppendResult(string Path, RolloutWriteReceipt Receipt);

internal sealed record TurnCompactionCommit(
    string Trigger,
    string Mode,
    long TokensBefore,
    long TokensAfter,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ModelHistoryMessage> ReplacementHistory);

internal sealed class ThreadRolloutRecord
{
    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public ThreadOpenedPayload? ThreadOpened { get; init; }

    public TurnStartedPayload? TurnStarted { get; init; }

    public ItemAppendedPayload? ItemAppended { get; init; }

    public TurnCompletedPayload? TurnCompleted { get; init; }

    public RolloutThreadStatusChangedPayload? ThreadStatusChanged { get; init; }

    public ThreadNameUpdatedPayload? ThreadNameUpdated { get; init; }

    public ThreadRolledBackPayload? ThreadRolledBack { get; init; }

    public ContextCompactedPayload? ContextCompacted { get; init; }

    public ModelHistoryMessagesAppendedPayload? ModelHistoryMessagesAppended { get; init; }

    public ProviderHistoryItemsAppendedPayload? ProviderHistoryItemsAppended { get; init; }

    public ProviderHistoryReplacedPayload? ProviderHistoryReplaced { get; init; }

    public ProviderHistoryAttemptAbortedPayload? ProviderHistoryAttemptAborted { get; init; }

    public TurnStateReplacedPayload? TurnStateReplaced { get; init; }

    public QueuedInputAddedPayload? QueuedInputAdded { get; init; }

    public QueuedInputRemovedPayload? QueuedInputRemoved { get; init; }

    public QueuedInputUpdatedPayload? QueuedInputUpdated { get; init; }

    public QueuedInputReorderedPayload? QueuedInputReordered { get; init; }
}

internal sealed class ThreadOpenedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string? UserId { get; init; }

    public string OriginChannel { get; init; } = string.Empty;

    public string? ChannelContext { get; init; }

    public PersistedThreadSource? Source { get; init; }

    public string? ForkedFromId { get; init; }

    public bool Ephemeral { get; init; }

    public ThreadWorktreeInfo? Worktree { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }

    public int ProviderHistorySchemaVersion { get; init; }

    public int TurnSequenceHighWatermark { get; init; }
}

internal sealed class TurnStartedPayload
{
    public SessionTurn Turn { get; init; } = new();
}

internal sealed class ItemAppendedPayload
{
    public string TurnId { get; init; } = string.Empty;

    public SessionItem Item { get; init; } = new();
}

internal sealed class TurnCompletedPayload
{
    public string TurnId { get; init; } = string.Empty;

    public TurnStatus Status { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public TokenUsageInfo? TokenUsage { get; init; }

    public string? Error { get; init; }

    public string? OriginChannel { get; init; }

    public TurnInitiatorContext? Initiator { get; init; }
}

internal sealed class RolloutThreadStatusChangedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public ThreadStatus Status { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class ThreadNameUpdatedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}

internal sealed class ThreadRolledBackPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public int NumTurns { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class ContextCompactedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string CoveredThroughTurnId { get; init; } = string.Empty;

    public string CheckpointId { get; init; } = string.Empty;

    public string Trigger { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public long TokensBefore { get; init; }

    public long TokensAfter { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public List<ModelHistoryMessage> ReplacementHistory { get; init; } = [];
}

internal sealed record ThreadCompactionCheckpoint(
    string ThreadId,
    string CoveredThroughTurnId,
    string CheckpointId,
    string Trigger,
    string Mode,
    long TokensBefore,
    long TokensAfter,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ModelHistoryMessage> ReplacementHistory);

internal sealed class ModelHistoryMessagesAppendedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public List<ModelHistoryMessage> Messages { get; init; } = [];
}

internal sealed class TurnStateReplacedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public SessionTurn Turn { get; init; } = new();

    public ThreadStatus ThreadStatus { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public string? DisplayName { get; init; }
}

internal sealed class QueuedInputAddedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public QueuedTurnInput QueuedInput { get; init; } = new();
}

internal sealed class QueuedInputRemovedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public string QueuedInputId { get; init; } = string.Empty;

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class QueuedInputUpdatedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public QueuedTurnInput QueuedInput { get; init; } = new();

    public DateTimeOffset LastActiveAt { get; init; }
}

internal sealed class QueuedInputReorderedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public List<string> OrderedQueuedInputIds { get; init; } = [];

    public DateTimeOffset LastActiveAt { get; init; }
}
