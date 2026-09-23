using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal sealed class ClaudeCodeSessionSource(string root) : ISessionImportSource
{
    public string SourceId => SessionImportSources.ClaudeCode;

    public bool IsAvailable => Directory.Exists(root);

    public Task<IReadOnlyList<SessionImportCandidateFile>> EnumerateCandidatesAsync(
        SessionImportScope scope,
        CancellationToken ct = default) =>
        Task.Run(() => Enumerate(scope, ct), ct);

    public Task<ImportedSession?> LoadAsync(SessionImportCandidateFile candidate, CancellationToken ct = default) =>
        Task.Run(() => Load(candidate, ct), ct);

    private IReadOnlyList<SessionImportCandidateFile> Enumerate(SessionImportScope scope, CancellationToken ct)
    {
        var files = SessionFileWindow.Directories(Path.Combine(root, "projects"))
            .SelectMany(static directory => SessionFileWindow.Files(directory, "*.jsonl", SessionFileWindow.TopLevel));
        var candidates = new List<SessionImportCandidateFile>();
        foreach (var (path, modifiedAt) in SessionFileWindow.SelectNewest(files, scope))
        {
            ct.ThrowIfCancellationRequested();
            string? cwd;
            try
            {
                cwd = ReadSessionCwd(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (WorkspacePathMatcher.ResolveMemberCwd(cwd, scope.WorkspaceRoot) is { } memberCwd)
                candidates.Add(new SessionImportCandidateFile(Path.GetFileNameWithoutExtension(path), path, modifiedAt, memberCwd));
        }

        return candidates;
    }

    private static string? ReadSessionCwd(string path)
    {
        using var stream = JsonlRecordReader.OpenShared(path);
        foreach (var record in JsonlRecordReader.ReadRecords(stream))
        {
            if (record.ReadString("type") is "user" or "assistant")
                return record.ReadString("cwd");
        }

        return null;
    }

    private ImportedSession? Load(SessionImportCandidateFile candidate, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var transcript = new Transcript(candidate.SourceId);
        using (var stream = JsonlRecordReader.OpenShared(candidate.Path))
        {
            foreach (var record in JsonlRecordReader.ReadRecords(stream, hash))
            {
                ct.ThrowIfCancellationRequested();
                transcript.Add(record);
            }
        }

        var turns = transcript.BuildTurns(candidate.ModifiedAt);
        if (turns.Count == 0)
            return null;

        return new ImportedSession
        {
            Source = SourceId,
            SourceId = candidate.SourceId,
            SourcePath = candidate.Path,
            Cwd = candidate.Cwd,
            Title = ExternalSessionText.SelectTitle(turns, transcript.CustomTitle, transcript.AiTitle),
            UpdatedAt = candidate.ModifiedAt,
            Turns = turns,
            ContentSha256 = JsonlRecordReader.ToHex(hash)
        };
    }

    private sealed class Transcript(string sessionId)
    {
        private readonly HashSet<string> _seenUuids = new(StringComparer.Ordinal);
        private readonly List<Message> _messages = [];
        private readonly Dictionary<string, Message> _assistantByMessageId = new(StringComparer.Ordinal);

        public string? CustomTitle { get; private set; }

        public string? AiTitle { get; private set; }

        public void Add(JsonElement record)
        {
            var type = record.ReadString("type");
            switch (type)
            {
                case "custom-title":
                    CustomTitle = OwnTitle(record, "customTitle") ?? CustomTitle;
                    return;
                case "ai-title":
                    AiTitle = OwnTitle(record, "aiTitle") ?? AiTitle;
                    return;
                case "user" or "assistant":
                    break;
                default:
                    return;
            }

            if (record.ReadBool("isMeta") || record.ReadBool("isSidechain") || record.ReadBool("isCompactSummary"))
                return;
            if (record.ReadString("uuid") is { } uuid && !_seenUuids.Add(uuid))
                return;
            if (!record.TryReadProperty("message", JsonValueKind.Object, out var message)
                || !message.TryGetProperty("content", out var content)
                || ExternalSessionText.ExtractMessage(content) is not { } extracted)
            {
                return;
            }

            var timestamp = record.ReadTimestamp();
            if (type == "assistant")
            {
                var messageId = message.ReadString("id");
                if (messageId is not null && _assistantByMessageId.TryGetValue(messageId, out var existing))
                {
                    existing.Append(extracted.Parts, timestamp);
                    return;
                }

                var assistant = new Message(false, extracted.Parts, timestamp);
                _messages.Add(assistant);
                if (messageId is not null)
                    _assistantByMessageId[messageId] = assistant;
                return;
            }

            if (extracted.OnlyToolResult)
            {
                _messages.Add(new Message(false, extracted.Parts, timestamp));
                return;
            }

            _messages.Add(new Message(true, [ExternalSessionText.UnwrapUserQuery(extracted.Text)], timestamp));
            _assistantByMessageId.Clear();
        }

        public IReadOnlyList<ImportedTurnInput> BuildTurns(DateTimeOffset fallbackTime)
        {
            var builder = new ImportTurnBuilder(fallbackTime);
            foreach (var message in _messages)
            {
                if (message.IsUser)
                    builder.AddUser(message.Parts[0], message.Timestamp);
                else
                    builder.AddAgent(ExternalSessionText.JoinParts(message.Parts), message.Timestamp);
            }

            return builder.Build();
        }

        /// <summary>Returns the row's title when it belongs to this file's session; an empty value is kept so it can fall back.</summary>
        private string? OwnTitle(JsonElement record, string field) =>
            string.Equals(record.ReadString("sessionId"), sessionId, StringComparison.OrdinalIgnoreCase)
                ? record.ReadString(field)?.Trim() ?? string.Empty
                : null;
    }

    private sealed class Message(bool isUser, IEnumerable<string> parts, DateTimeOffset? timestamp)
    {
        public bool IsUser { get; } = isUser;

        public List<string> Parts { get; } = [.. parts];

        public DateTimeOffset? Timestamp { get; private set; } = timestamp;

        public void Append(IEnumerable<string> parts, DateTimeOffset? timestamp)
        {
            Parts.AddRange(parts);
            if (timestamp is { } value && (Timestamp is null || value > Timestamp))
                Timestamp = value;
        }
    }
}
