using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.SessionImport;

internal sealed class CodexSessionSource(string root) : ISessionImportSource
{
    public string SourceId => SessionImportSources.Codex;

    public bool IsAvailable => Directory.Exists(root);

    public Task<IReadOnlyList<SessionImportCandidateFile>> EnumerateCandidatesAsync(
        SessionImportScope scope,
        CancellationToken ct = default) =>
        Task.Run(() => Enumerate(scope, ct), ct);

    public Task<ImportedSession?> LoadAsync(SessionImportCandidateFile candidate, CancellationToken ct = default) =>
        Task.Run(() => Load(candidate, ct), ct);

    private IReadOnlyList<SessionImportCandidateFile> Enumerate(SessionImportScope scope, CancellationToken ct)
    {
        var files = SessionFileWindow.Files(Path.Combine(root, "sessions"), "rollout-*.jsonl", SessionFileWindow.Recursive);
        var byThread = new Dictionary<string, SessionImportCandidateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, modifiedAt) in SessionFileWindow.SelectNewest(files, scope))
        {
            ct.ThrowIfCancellationRequested();
            CodexSessionMeta? meta;
            try
            {
                meta = ReadMeta(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (meta is not { IsUserSession: true }
                || scope.ExternalCliSessionIds.Contains(meta.Id)
                || WorkspacePathMatcher.ResolveMemberCwd(meta.Cwd, scope.WorkspaceRoot) is not { } memberCwd)
            {
                continue;
            }

            var candidate = new SessionImportCandidateFile(meta.Id, path, modifiedAt, memberCwd);
            if (!byThread.TryGetValue(meta.Id, out var existing) || IsNewerRollout(candidate, existing))
                byThread[meta.Id] = candidate;
        }

        return byThread.Values.OrderByDescending(static candidate => candidate.ModifiedAt).ToArray();
    }

    private ImportedSession? Load(SessionImportCandidateFile candidate, CancellationToken ct)
    {
        if (ReadMeta(candidate.Path) is not { HistoryMode: "legacy" or "paginated" } meta
            || ResolveLineage(candidate.Path, meta) is not { } segments)
        {
            return null;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var converter = new CodexRecordConverter(meta.HistoryMode == "paginated", candidate.ModifiedAt);
        foreach (var segment in segments)
        {
            using var stream = JsonlRecordReader.OpenShared(segment.Path);
            foreach (var record in JsonlRecordReader.ReadRecords(stream, hash, segment.ByteLimit))
            {
                ct.ThrowIfCancellationRequested();
                converter.Consume(record);
            }
        }

        var turns = converter.Build();
        if (converter.SawImportMarker || turns.Count == 0)
            return null;

        return new ImportedSession
        {
            Source = SourceId,
            SourceId = meta.Id,
            SourcePath = candidate.Path,
            Cwd = candidate.Cwd,
            Title = ExternalSessionText.SelectTitle(turns, IndexedTitle(meta.Id)),
            UpdatedAt = candidate.ModifiedAt,
            Turns = turns,
            ContentSha256 = JsonlRecordReader.ToHex(hash)
        };
    }

    private IReadOnlyList<(string Path, long ByteLimit)>? ResolveLineage(string currentPath, CodexSessionMeta current)
    {
        var segments = new List<(string Path, long ByteLimit)> { (currentPath, long.MaxValue) };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ParseRolloutName(currentPath)?.RolloutId ?? current.Id };
        var meta = current;
        while (meta.HistoryBase is { } historyBase)
        {
            if (!visited.Add(historyBase.RolloutId) || FindRollout(historyBase.RolloutId) is not { } ancestorPath)
                return null;
            if (ReadMeta(ancestorPath) is not { } ancestorMeta || new FileInfo(ancestorPath).Length < historyBase.EndByteOffset)
                return null;
            segments.Insert(0, (ancestorPath, historyBase.EndByteOffset));
            meta = ancestorMeta;
        }

        return segments;
    }

    private string? FindRollout(string rolloutId) =>
        SessionFileWindow.Files(Path.Combine(root, "sessions"), "rollout-*.jsonl", SessionFileWindow.Recursive)
            .Concat(SessionFileWindow.Files(Path.Combine(root, "archived_sessions"), "rollout-*.jsonl", SessionFileWindow.TopLevel))
            .FirstOrDefault(path => ParseRolloutName(path) is { } name
                && string.Equals(name.RolloutId, rolloutId, StringComparison.OrdinalIgnoreCase));

    private string? IndexedTitle(string threadId)
    {
        var path = Path.Combine(root, "session_index.jsonl");
        if (!File.Exists(path))
            return null;
        string? title = null;
        try
        {
            using var stream = JsonlRecordReader.OpenShared(path);
            foreach (var record in JsonlRecordReader.ReadRecords(stream))
            {
                if (string.Equals(record.ReadString("id"), threadId, StringComparison.OrdinalIgnoreCase))
                    title = record.ReadString("thread_name") ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return title;
    }

    private static CodexSessionMeta? ReadMeta(string path)
    {
        using var stream = JsonlRecordReader.OpenShared(path);
        foreach (var record in JsonlRecordReader.ReadRecords(stream))
            return CodexSessionMeta.TryParse(record);
        return null;
    }

    private static bool IsNewerRollout(SessionImportCandidateFile candidate, SessionImportCandidateFile existing)
    {
        var order = string.CompareOrdinal(ParseRolloutName(candidate.Path)?.Timestamp, ParseRolloutName(existing.Path)?.Timestamp);
        return order != 0 ? order > 0 : candidate.ModifiedAt > existing.ModifiedAt;
    }

    /// <summary>Parses <c>rollout-&lt;timestamp&gt;-&lt;threadId&gt;[_&lt;rolloutId&gt;].jsonl</c>; the rollout id defaults to the thread id.</summary>
    private static (string Timestamp, string RolloutId)? ParseRolloutName(string path)
    {
        const string prefix = "rollout-";
        const string suffix = ".jsonl";
        const int timestampLength = 19;
        var name = Path.GetFileName(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        var core = name[prefix.Length..^suffix.Length];
        if (core.Length <= timestampLength + 1 || core[timestampLength] != '-')
            return null;
        var ids = core[(timestampLength + 1)..];
        var separator = ids.IndexOf('_');
        return (core[..timestampLength], separator < 0 ? ids : ids[(separator + 1)..]);
    }
}

internal sealed record CodexHistoryBase(string RolloutId, long EndByteOffset);

internal sealed record CodexSessionMeta(string Id, string? Cwd, string HistoryMode, CodexHistoryBase? HistoryBase, bool IsUserSession)
{
    public static CodexSessionMeta? TryParse(JsonElement record)
    {
        if (record.ReadString("type") != "session_meta"
            || !record.TryReadProperty("payload", JsonValueKind.Object, out var payload)
            || payload.ReadString("id") is not { Length: > 0 } id)
        {
            return null;
        }

        CodexHistoryBase? historyBase = null;
        if (payload.TryReadProperty("history_base", JsonValueKind.Object, out var baseElement)
            && baseElement.ReadString("thread_id") is { Length: > 0 } rolloutId
            && baseElement.ReadInt64("end_byte_offset") is { } endByteOffset)
        {
            historyBase = new CodexHistoryBase(rolloutId, endByteOffset);
        }

        return new CodexSessionMeta(
            id,
            payload.ReadString("cwd"),
            payload.ReadString("history_mode") ?? "legacy",
            historyBase,
            IsUserSource(payload));
    }

    private static bool IsUserSource(JsonElement payload)
    {
        var sourceAllowed = !payload.TryGetProperty("source", out var source) || source.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => source.GetString() is "cli" or "vscode",
            JsonValueKind.Object => source.TryGetProperty("custom", out _),
            _ => false
        };
        var threadSource = payload.ReadProperty("thread_source");
        var threadSourceAllowed = threadSource.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || (threadSource.ValueKind == JsonValueKind.String && threadSource.GetString() == "user");
        return sourceAllowed && threadSourceAllowed;
    }
}
