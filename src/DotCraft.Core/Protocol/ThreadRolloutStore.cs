using System.Text.Json;

namespace DotCraft.Protocol;

internal sealed class ThreadRolloutStore
{
    private readonly string _activeDir;
    private readonly string _archivedDir;

    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public ThreadRolloutStore(string botPath)
    {
        _activeDir = Path.Combine(botPath, "threads", "active");
        _archivedDir = Path.Combine(botPath, "threads", "archived");
        Directory.CreateDirectory(_activeDir);
        Directory.CreateDirectory(_archivedDir);
    }

    public string GetExpectedPath(string threadId, bool archived)
    {
        var safe = MakeSafe(threadId);
        return Path.Combine(archived ? _archivedDir : _activeDir, $"{safe}.jsonl");
    }

    public string? ResolveExistingPath(string threadId)
    {
        var active = GetExpectedPath(threadId, archived: false);
        if (File.Exists(active))
            return active;

        var archived = GetExpectedPath(threadId, archived: true);
        return File.Exists(archived) ? archived : null;
    }

    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return null;

        return await LoadThreadFromPathAsync(path, ct);
    }

    public async Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await ReplayAsync(File.ReadLinesAsync(path, ct), ct);
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

    public async Task<string> SaveThreadAsync(SessionThread thread, SessionThread? previous, CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath != null && !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            existingPath = PromoteToCanonicalPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
        }

        var records = BuildRecords(previous, thread);
        if (records.Count == 0 && !File.Exists(targetPath))
            records.Add(CreateThreadOpenedRecord(thread));

        if (records.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var payload = string.Join(
                Environment.NewLine,
                records.Select(r => JsonSerializer.Serialize(r, JsonOptions))) + Environment.NewLine;
            await File.AppendAllTextAsync(targetPath, payload, ct);
        }

        return targetPath;
    }

    public async Task<string> AppendRollbackAsync(
        SessionThread thread,
        int numTurns,
        CancellationToken ct = default)
    {
        var targetPath = GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived);
        var existingPath = ResolveExistingPath(thread.Id);
        if (existingPath != null && !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            existingPath = PromoteToCanonicalPath(thread.Id, thread.Status == ThreadStatus.Archived, existingPath);
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
        var payload = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(targetPath, payload, ct);
        return targetPath;
    }

    public async Task<string> AppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        JsonElement replacementHistory,
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
                ReplacementHistory = replacementHistory.Clone()
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        var payload = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(existingPath, payload, ct);
        return existingPath;
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
                checkpoint.ReplacementHistory.Clone()));
        }

        return checkpoints;
    }

    public void DeleteThread(string threadId)
    {
        foreach (var path in EnumerateCandidatePaths(threadId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public string PromoteToCanonicalPath(string threadId, bool archived, string existingPath)
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
        => path.StartsWith(_archivedDir, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EnumerateCandidatePaths(string threadId)
    {
        yield return GetExpectedPath(threadId, archived: false);
        yield return GetExpectedPath(threadId, archived: true);
    }

    private static List<ThreadRolloutRecord> BuildRecords(SessionThread? previous, SessionThread current)
    {
        var records = new List<ThreadRolloutRecord>();

        if (previous == null || ThreadBaselineChanged(previous, current))
            records.Add(CreateThreadOpenedRecord(current));

        var previousTurns = previous?.Turns.ToDictionary(t => t.Id, StringComparer.Ordinal) ?? [];
        foreach (var turn in current.Turns)
        {
            if (!previousTurns.TryGetValue(turn.Id, out var previousTurn) || !TurnsEquivalent(previousTurn, turn))
            {
                records.Add(CreateTurnStartedRecord(turn));
                foreach (var item in turn.Items)
                    records.Add(CreateItemAppendedRecord(turn.Id, item));

                if (turn.Status != TurnStatus.Running)
                    records.Add(CreateTurnCompletedRecord(turn));
            }
        }

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
        if (!JsonEquals(previous.Configuration, current.Configuration))
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
                CreatedAt = thread.CreatedAt,
                LastActiveAt = thread.LastActiveAt,
                Metadata = new Dictionary<string, string>(thread.Metadata),
                HistoryMode = thread.HistoryMode,
                Configuration = thread.Configuration
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

        public void Apply(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions);
            }
            catch
            {
                return;
            }

            if (record == null)
                return;

            switch (record.Kind)
            {
                case "thread_opened" when record.ThreadOpened != null:
                    _thread ??= new SessionThread();
                    _thread.Id = record.ThreadOpened.ThreadId;
                    _thread.WorkspacePath = record.ThreadOpened.WorkspacePath;
                    _thread.UserId = record.ThreadOpened.UserId;
                    _thread.OriginChannel = record.ThreadOpened.OriginChannel;
                    _thread.ChannelContext = record.ThreadOpened.ChannelContext;
                    _thread.CreatedAt = record.ThreadOpened.CreatedAt;
                    _thread.LastActiveAt = record.ThreadOpened.LastActiveAt;
                    _thread.Metadata = new Dictionary<string, string>(record.ThreadOpened.Metadata);
                    _thread.HistoryMode = record.ThreadOpened.HistoryMode;
                    _thread.Configuration = record.ThreadOpened.Configuration;
                    break;

                case "thread_name_updated" when _thread != null && record.ThreadNameUpdated != null:
                    _thread.DisplayName = record.ThreadNameUpdated.DisplayName;
                    break;

                case "thread_status_changed" when _thread != null && record.ThreadStatusChanged != null:
                    _thread.Status = record.ThreadStatusChanged.Status;
                    _thread.LastActiveAt = record.ThreadStatusChanged.LastActiveAt;
                    break;

                case "turn_started" when _thread != null && record.TurnStarted != null:
                    var started = record.TurnStarted.Turn;
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
            return _thread;
        }
    }

    private static string MakeSafe(string key) => string.Concat(key.Split(Path.GetInvalidFileNameChars()));

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

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }
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

    public JsonElement ReplacementHistory { get; init; }
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
    JsonElement ReplacementHistory);

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
