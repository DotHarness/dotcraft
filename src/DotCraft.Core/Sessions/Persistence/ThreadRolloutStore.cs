using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Sessions;

internal sealed partial class ThreadRolloutStore
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
        : this(botPath, beforeDeleteAsync, new OrderedRolloutWriter(JsonOptions))
    {
    }

    internal ThreadRolloutStore(
        string botPath,
        Func<string, CancellationToken, Task>? beforeDeleteAsync,
        IRolloutWriter writer)
    {
        _activeDir = Path.Combine(botPath, "threads", "active");
        _archivedDir = Path.Combine(botPath, "threads", "archived");
        _writer = writer;
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
            throw new ArgumentException("Thread path must resolve directly under the configured data directory's threads/active or threads/archived directory.", nameof(path));
        if (!File.Exists(normalizedPath))
            return null;
        return await ReadThreadFileAsync(normalizedPath, ct);
    }

    internal static async Task<SessionThread?> ReadThreadFileAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            return null;
        var replay = new ThreadReplay();
        await Utf8JsonlReader.ReadAsync(path, replay.Apply, ct);
        return WithWorkspace(replay.Build(), path);
    }

    private static SessionThread? WithWorkspace(SessionThread? thread, string rolloutPath)
    {
        if (thread != null)
            thread.WorkspacePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(rolloutPath)!, "..", "..", ".."));
        return thread;
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
            return await AppendRecordsAsync(thread.Id, targetPath, records, ct);
        }

        return new RolloutAppendResult(
            targetPath,
            new RolloutWriteReceipt(File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0, 0, new Dictionary<string, long>()), []);
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
            Kind = RolloutKinds.ThreadRolledBack,
            Timestamp = thread.LastActiveAt,
            ThreadRolledBack = new ThreadRolledBackPayload
            {
                ThreadId = thread.Id,
                NumTurns = numTurns,
                LastActiveAt = thread.LastActiveAt
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        return await AppendRecordsAsync(thread.Id, targetPath, [record], ct);
    }

    /// <summary>Appends change records to the thread's current rollout, or returns null when it must be saved whole.</summary>
    public async Task<RolloutAppendResult?> AppendChangeRecordsAsync(
        SessionThread thread,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(thread.Id);
        if (path == null
            || !string.Equals(path, GetExpectedPath(thread.Id, thread.Status == ThreadStatus.Archived), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await AppendRecordsAsync(thread.Id, path, records, ct);
    }

    public async Task<RolloutAppendResult> AppendThreadBaselineAsync(SessionThread thread, CancellationToken ct = default)
    {
        var path = ResolveExistingPath(thread.Id);
        if (path == null)
            return await SaveThreadAsync(thread, previous: null, ct);

        return await AppendRecordsAsync(thread.Id, path, [CreateThreadOpenedRecord(thread)], ct);
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
            Kind = RolloutKinds.TurnStateReplaced,
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

        return await AppendRecordsAsync(thread.Id, existingPath, [record], ct);
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
            Kind = RolloutKinds.ContextCompacted,
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
        return await AppendRecordsAsync(threadId, existingPath, [record], ct);
    }

    public async Task<RolloutAppendResult> AppendWorldStateAsync(
        string threadId,
        string turnId,
        bool full,
        JsonObject state,
        CancellationToken ct = default)
    {
        var existingPath = ResolveExistingPath(threadId);
        if (existingPath == null)
            throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.WorldState,
            Timestamp = DateTimeOffset.UtcNow,
            WorldState = new WorldStatePayload
            {
                ThreadId = threadId,
                TurnId = turnId,
                Full = full,
                State = state
            }
        };

        return await AppendRecordsAsync(threadId, existingPath, [record], ct);
    }

    public async Task<IReadOnlyList<ThreadCompactionCheckpoint>> LoadCompactionCheckpointsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return [];

        var checkpoints = new List<ThreadCompactionCheckpoint>();
        await Utf8JsonlReader.ReadAsync(path, line =>
        {
            if (Utf8JsonlReader.IsWhiteSpace(line))
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

            if (record is not { Kind: RolloutKinds.ContextCompacted, ContextCompacted: { } checkpoint } ||
                !string.Equals(checkpoint.ThreadId, threadId, StringComparison.Ordinal))
            {
                return;
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
        }, ct);

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
                new RolloutWriteReceipt(new FileInfo(existingPath).Length, 0, new Dictionary<string, long>()), []);

        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ModelHistoryMessagesAppended,
            Timestamp = DateTimeOffset.UtcNow,
            ModelHistoryMessagesAppended = new ModelHistoryMessagesAppendedPayload
            {
                ThreadId = threadId,
                TurnId = turnId,
                Messages = [.. messages]
            }
        };

        return await AppendRecordsAsync(threadId, existingPath, [record], ct);
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
                new RolloutWriteReceipt(new FileInfo(existingPath).Length, 0, new Dictionary<string, long>()), []);
        }

        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ProviderHistoryItemsAppended,
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryItemsAppended = payload
        };
        return await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
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
            Kind = RolloutKinds.ProviderHistoryReplaced,
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryReplaced = payload
        };
        return await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
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
            Kind = RolloutKinds.ProviderHistoryAttemptAborted,
            Timestamp = DateTimeOffset.UtcNow,
            ProviderHistoryAttemptAborted = payload
        };
        return await AppendRecordsAsync(payload.ThreadId, existingPath, [record], ct);
    }

    public async Task<IReadOnlyList<ThreadRolloutRecord>> LoadProviderHistoryRecordsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var path = ResolveExistingPath(threadId);
        if (path == null)
            return [];

        var records = new List<ThreadRolloutRecord>();
        await Utf8JsonlReader.ReadAsync(path, line =>
        {
            if (line.IndexOf("provider_history_"u8) < 0)
                return;

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

            if (record?.Kind is RolloutKinds.ProviderHistoryItemsAppended
                or RolloutKinds.ProviderHistoryReplaced
                or RolloutKinds.ProviderHistoryAttemptAborted)
            {
                records.Add(record);
            }
        }, ct);

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
        IReadOnlyList<ThreadRolloutRecord> openedRecords = [];
        if (existingPath == null)
        {
            var opened = await SaveThreadAsync(thread, previous: null, ct);
            existingPath = opened.Path;
            openedRecords = opened.Records;
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
                Kind = RolloutKinds.TurnStateReplaced,
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
                Kind = RolloutKinds.ContextCompacted,
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
                Kind = RolloutKinds.ModelHistoryMessagesAppended,
                Timestamp = DateTimeOffset.UtcNow,
                ModelHistoryMessagesAppended = new ModelHistoryMessagesAppendedPayload
                {
                    ThreadId = thread.Id,
                    TurnId = turn.Id,
                    Messages = [.. modelHistory]
                }
            });
        }

        var result = await AppendRecordsAsync(thread.Id, existingPath, records, ct);
        return openedRecords.Count == 0 ? result : result with { Records = [.. openedRecords, .. records] };
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

    public Task ShutdownAsync(CancellationToken ct = default) => _writer.ShutdownAllAsync(ct);

    private async Task<RolloutAppendResult> AppendRecordsAsync(
        string threadId,
        string path,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct)
    {
        await _writer.AddBatchAsync(threadId, path, records, ct);
        return new RolloutAppendResult(path, await _writer.FlushAsync(threadId, ct), records);
    }
}
