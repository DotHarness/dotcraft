using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed class ThreadReplay
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    private readonly Dictionary<string, (long Ordinal, SessionTurn Turn)> _turns = new(StringComparer.Ordinal);
    private SessionThread? _thread;
    private int _turnSequenceHighWatermark;
    private bool _hasCanonicalHeader;
    private long _nextOrdinal = 1;

    public static ThreadReplay FromProjection(
        SessionThread thread,
        IReadOnlyList<(long Ordinal, SessionTurn Turn)> turns)
    {
        var replay = new ThreadReplay
        {
            _thread = thread,
            _turnSequenceHighWatermark = thread.TurnSequenceHighWatermark,
            _hasCanonicalHeader = true
        };
        thread.Turns = [];
        foreach (var (ordinal, turn) in turns)
        {
            replay._turns[turn.Id] = (ordinal, turn);
            replay._nextOrdinal = Math.Max(replay._nextOrdinal, ordinal + 1);
        }

        return replay;
    }

    public void Apply(ReadOnlySpan<byte> line)
    {
        if (Utf8JsonlReader.IsWhiteSpace(line))
            return;

        var kind = RolloutJsonEnvelopeReader.ReadKind(line);
        if (_hasCanonicalHeader && kind is not null && RolloutKinds.IsIgnoredByProjection(kind))
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

        Apply(record);
    }

    private void Apply(ThreadRolloutRecord record)
    {
        if (record.Kind == RolloutKinds.ThreadOpened && record.ThreadOpened == null)
            throw new InvalidDataException("A canonical thread baseline record is incomplete.");

        if (!_hasCanonicalHeader
            && (record.Kind != RolloutKinds.ThreadOpened || record.ThreadOpened == null))
        {
            throw new InvalidDataException("The rollout does not begin with a canonical thread header.");
        }

        switch (record.Kind)
        {
            case RolloutKinds.ThreadOpened when record.ThreadOpened != null:
                ApplyThreadOpened(record.ThreadOpened);
                break;

            case RolloutKinds.ThreadNameUpdated when _thread != null && record.ThreadNameUpdated != null:
                _thread.DisplayName = record.ThreadNameUpdated.DisplayName;
                break;

            case RolloutKinds.ThreadStatusChanged when _thread != null && record.ThreadStatusChanged != null:
                _thread.Status = record.ThreadStatusChanged.Status;
                _thread.LastActiveAt = record.ThreadStatusChanged.LastActiveAt;
                break;

            case RolloutKinds.TurnStateReplaced when _thread != null && record.TurnStateReplaced != null:
                var replacement = record.TurnStateReplaced;
                var replacementTurn = replacement.Turn;
                _turnSequenceHighWatermark = Math.Max(
                    _turnSequenceHighWatermark,
                    SessionIdGenerator.LastTurnSequence([replacementTurn.Id]));
                replacementTurn.Input ??= replacementTurn.Items.FirstOrDefault(static item =>
                    item.Type == ItemType.UserMessage);
                SetTurn(replacementTurn);
                _thread.Status = replacement.ThreadStatus;
                _thread.LastActiveAt = replacement.LastActiveAt;
                _thread.DisplayName = replacement.DisplayName;
                break;

            case RolloutKinds.TurnStarted when _thread != null && record.TurnStarted != null:
                var started = record.TurnStarted.Turn;
                _turnSequenceHighWatermark = Math.Max(
                    _turnSequenceHighWatermark,
                    SessionIdGenerator.LastTurnSequence([started.Id]));
                started.Items = [];
                started.Input = null;
                SetTurn(started);
                break;

            case RolloutKinds.ItemAppended when record.ItemAppended != null:
                ApplyItem(record.ItemAppended, record.Timestamp);
                break;

            case RolloutKinds.TurnCompleted when record.TurnCompleted != null
                                                && _turns.TryGetValue(record.TurnCompleted.TurnId, out var completed):
                var completedTurn = completed.Turn;
                completedTurn.Status = record.TurnCompleted.Status;
                completedTurn.CompletedAt = record.TurnCompleted.CompletedAt;
                completedTurn.TokenUsage = record.TurnCompleted.TokenUsage;
                completedTurn.Error = record.TurnCompleted.Error;
                completedTurn.OriginChannel = record.TurnCompleted.OriginChannel;
                completedTurn.Initiator = record.TurnCompleted.Initiator;
                break;

            case RolloutKinds.ThreadRolledBack when _thread != null && record.ThreadRolledBack != null:
                foreach (var id in _turns
                             .OrderByDescending(static entry => entry.Value.Ordinal)
                             .Take(Math.Max(0, record.ThreadRolledBack.NumTurns))
                             .Select(static entry => entry.Key)
                             .ToList())
                {
                    _turns.Remove(id);
                }
                _thread.LastActiveAt = record.ThreadRolledBack.LastActiveAt;
                break;

            case RolloutKinds.QueuedInputAdded when _thread != null && record.QueuedInputAdded != null:
                if (_thread.QueuedInputs.All(q => !string.Equals(q.Id, record.QueuedInputAdded.QueuedInput.Id, StringComparison.Ordinal)))
                    _thread.QueuedInputs.Add(record.QueuedInputAdded.QueuedInput);
                break;

            case RolloutKinds.QueuedInputRemoved when _thread != null && record.QueuedInputRemoved != null:
                _thread.QueuedInputs.RemoveAll(q => string.Equals(q.Id, record.QueuedInputRemoved.QueuedInputId, StringComparison.Ordinal));
                _thread.LastActiveAt = record.QueuedInputRemoved.LastActiveAt;
                break;

            case RolloutKinds.QueuedInputUpdated when _thread != null && record.QueuedInputUpdated != null:
                var updateIndex = _thread.QueuedInputs.FindIndex(q => string.Equals(q.Id, record.QueuedInputUpdated.QueuedInput.Id, StringComparison.Ordinal));
                if (updateIndex >= 0)
                    _thread.QueuedInputs[updateIndex] = record.QueuedInputUpdated.QueuedInput;
                _thread.LastActiveAt = record.QueuedInputUpdated.LastActiveAt;
                break;

            case RolloutKinds.QueuedInputReordered when _thread != null && record.QueuedInputReordered != null:
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

    public void RetainTurns(IEnumerable<SessionTurn> turns)
    {
        var retained = turns.Select(static turn => turn.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _turns.Keys.Where(id => !retained.Contains(id)).ToList())
            _turns.Remove(id);
    }

    public bool Holds(IEnumerable<SessionTurn> turns) => turns.All(turn => _turns.ContainsKey(turn.Id));

    public SessionThread? Build()
    {
        if (_thread == null)
            return null;

        _thread.Turns = _turns.Values
            .OrderBy(static entry => entry.Ordinal)
            .Select(static entry => entry.Turn)
            .ToList();
        _thread.TurnSequenceHighWatermark = Math.Max(
            _turnSequenceHighWatermark,
            SessionIdGenerator.LastTurnSequence(_thread.Turns));
        return _thread;
    }

    private void ApplyThreadOpened(ThreadOpenedPayload opened)
    {
        _thread ??= new SessionThread();
        _thread.Id = opened.ThreadId;
        _thread.UserId = opened.UserId;
        _thread.OriginChannel = opened.OriginChannel;
        _thread.ChannelContext = opened.ChannelContext;
        _thread.Source = PersistedThreadSourceCodec.Decode(
            opened.Source
            ?? throw new InvalidDataException("The canonical thread header has no source."));
        _thread.ForkedFromId = opened.ForkedFromId;
        _thread.Ephemeral = opened.Ephemeral;
        _thread.Worktree = opened.Worktree;
        _thread.GitInfo = opened.GitInfo;
        _thread.CreatedAt = opened.CreatedAt;
        _thread.LastActiveAt = opened.LastActiveAt;
        _thread.Metadata = new Dictionary<string, string>(opened.Metadata);
        _thread.HistoryMode = opened.HistoryMode;
        _thread.Configuration = opened.Configuration;
        _thread.ProviderHistorySchemaVersion = opened.ProviderHistorySchemaVersion;
        _turnSequenceHighWatermark = Math.Max(_turnSequenceHighWatermark, opened.TurnSequenceHighWatermark);
        _hasCanonicalHeader = true;
    }

    private void ApplyItem(ItemAppendedPayload appended, DateTimeOffset timestamp)
    {
        if (!_turns.TryGetValue(appended.TurnId, out var entry))
        {
            SetTurn(new SessionTurn
            {
                Id = appended.TurnId,
                ThreadId = _thread?.Id ?? string.Empty,
                Status = TurnStatus.Running,
                StartedAt = timestamp
            });
            entry = _turns[appended.TurnId];
        }

        if (appended.Item is not { } item)
            return;

        var turn = entry.Turn;
        var existingIdx = turn.Items.FindIndex(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal));
        if (existingIdx >= 0)
            turn.Items[existingIdx] = item;
        else
            turn.Items.Add(item);

        if (item.Type == ItemType.UserMessage && turn.Input == null)
            turn.Input = item;
    }

    private void SetTurn(SessionTurn turn)
    {
        var ordinal = _turns.TryGetValue(turn.Id, out var existing) ? existing.Ordinal : _nextOrdinal++;
        _turns[turn.Id] = (ordinal, turn);
    }
}
