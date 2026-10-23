using System.Text.Json;

namespace DotCraft.Sessions;

internal static class ThreadSummaryScanner
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public static async Task<ThreadSummary?> ScanAsync(string path, CancellationToken ct)
    {
        var scan = new Scan();
        await Utf8JsonlReader.ReadAsync(path, scan.Apply, ct).ConfigureAwait(false);
        if (scan.Thread is not { } thread)
            return null;
        thread.WorkspacePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
        var summary = ThreadSummary.FromThread(thread);
        summary.TurnCount = scan.TurnCount;
        return summary;
    }

    public static string? ReadThreadId(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var line = new MemoryStream();
        int next;
        while ((next = stream.ReadByte()) >= 0 && next != '\n')
            line.WriteByte((byte)next);
        try
        {
            var threadId = JsonSerializer.Deserialize<SummaryRecord>(line.ToArray(), JsonOptions)?.ThreadOpened?.ThreadId;
            return string.IsNullOrWhiteSpace(threadId) ? null : threadId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class Scan
    {
        private readonly List<string> _turnIds = [];
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public SessionThread? Thread { get; private set; }

        public int TurnCount => _turnIds.Count;

        public void Apply(ReadOnlySpan<byte> line)
        {
            if (Utf8JsonlReader.IsWhiteSpace(line))
                return;
            var kind = RolloutJsonEnvelopeReader.ReadKind(line);
            if (kind is null || !RolloutKinds.Domain.Contains(kind))
            {
                if (Thread is null)
                    throw new InvalidDataException("The rollout does not begin with a canonical thread header.");
                return;
            }

            SummaryRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<SummaryRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                if (Thread is null)
                    throw new InvalidDataException("The canonical thread header is unreadable.", ex);
                return;
            }

            if (record?.ThreadOpened is { } opened)
            {
                ApplyHeader(opened);
                return;
            }
            if (Thread is null)
                throw new InvalidDataException("The rollout does not begin with a canonical thread header.");

            switch (kind)
            {
                case RolloutKinds.ThreadNameUpdated when record?.ThreadNameUpdated is { } name:
                    Thread.DisplayName = name.DisplayName;
                    break;
                case RolloutKinds.ThreadStatusChanged when record?.ThreadStatusChanged is { } status:
                    Thread.Status = status.Status;
                    Thread.LastActiveAt = status.LastActiveAt;
                    break;
                case RolloutKinds.TurnStateReplaced when record?.TurnStateReplaced is { } replaced:
                    AddTurn(replaced.Turn?.Id);
                    Thread.Status = replaced.ThreadStatus;
                    Thread.LastActiveAt = replaced.LastActiveAt;
                    Thread.DisplayName = replaced.DisplayName;
                    break;
                case RolloutKinds.TurnStarted when record?.TurnStarted is { } started:
                    AddTurn(started.Turn?.Id);
                    break;
                case RolloutKinds.ItemAppended when record?.ItemAppended is { } item:
                    AddTurn(item.TurnId);
                    break;
                case RolloutKinds.ThreadRolledBack when record?.ThreadRolledBack is { } rollback:
                    var removed = Math.Clamp(rollback.NumTurns, 0, _turnIds.Count);
                    foreach (var id in _turnIds.GetRange(_turnIds.Count - removed, removed))
                        _seen.Remove(id);
                    _turnIds.RemoveRange(_turnIds.Count - removed, removed);
                    Thread.LastActiveAt = rollback.LastActiveAt;
                    break;
                case RolloutKinds.QueuedInputRemoved or RolloutKinds.QueuedInputUpdated or RolloutKinds.QueuedInputReordered
                    when record?.LastActiveAt(kind) is { } lastActiveAt:
                    Thread.LastActiveAt = lastActiveAt;
                    break;
            }
        }

        private void ApplyHeader(ThreadOpenedPayload opened)
        {
            Thread ??= new SessionThread();
            Thread.Id = opened.ThreadId;
            Thread.UserId = opened.UserId;
            Thread.OriginChannel = opened.OriginChannel;
            Thread.ChannelContext = opened.ChannelContext;
            Thread.Source = PersistedThreadSourceCodec.Decode(
                opened.Source ?? throw new InvalidDataException("The canonical thread header has no source."));
            Thread.ForkedFromId = opened.ForkedFromId;
            Thread.Ephemeral = opened.Ephemeral;
            Thread.Worktree = opened.Worktree;
            Thread.GitInfo = opened.GitInfo;
            Thread.CreatedAt = opened.CreatedAt;
            Thread.LastActiveAt = opened.LastActiveAt;
            Thread.Metadata = new Dictionary<string, string>(opened.Metadata);
            Thread.HistoryMode = opened.HistoryMode;
            Thread.Configuration = opened.Configuration;
        }

        private void AddTurn(string? turnId)
        {
            if (!string.IsNullOrEmpty(turnId) && _seen.Add(turnId))
                _turnIds.Add(turnId);
        }
    }

    private sealed class SummaryRecord
    {
        public ThreadOpenedPayload? ThreadOpened { get; init; }

        public ThreadNameUpdatedPayload? ThreadNameUpdated { get; init; }

        public RolloutThreadStatusChangedPayload? ThreadStatusChanged { get; init; }

        public ThreadRolledBackPayload? ThreadRolledBack { get; init; }

        public TurnReference? TurnStarted { get; init; }

        public TurnStateReference? TurnStateReplaced { get; init; }

        public ItemReference? ItemAppended { get; init; }

        public ActivityReference? QueuedInputRemoved { get; init; }

        public ActivityReference? QueuedInputUpdated { get; init; }

        public ActivityReference? QueuedInputReordered { get; init; }

        public DateTimeOffset? LastActiveAt(string kind) => kind switch
        {
            RolloutKinds.QueuedInputRemoved => QueuedInputRemoved?.LastActiveAt,
            RolloutKinds.QueuedInputUpdated => QueuedInputUpdated?.LastActiveAt,
            RolloutKinds.QueuedInputReordered => QueuedInputReordered?.LastActiveAt,
            _ => null
        };
    }

    private sealed class TurnIdentity
    {
        public string? Id { get; init; }
    }

    private sealed class TurnReference
    {
        public TurnIdentity? Turn { get; init; }
    }

    private sealed class TurnStateReference
    {
        public TurnIdentity? Turn { get; init; }

        public ThreadStatus ThreadStatus { get; init; }

        public DateTimeOffset LastActiveAt { get; init; }

        public string? DisplayName { get; init; }
    }

    private sealed class ItemReference
    {
        public string? TurnId { get; init; }
    }

    private sealed class ActivityReference
    {
        public DateTimeOffset LastActiveAt { get; init; }
    }
}
