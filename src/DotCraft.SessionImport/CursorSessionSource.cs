using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.SessionImport;

internal sealed class CursorSessionSource(string root) : ISessionImportSource
{
    private const int MaxTranscriptDepth = 3;

    public string SourceId => SessionImportSources.Cursor;

    public bool IsAvailable => Directory.Exists(root);

    public Task<IReadOnlyList<SessionImportCandidateFile>> EnumerateCandidatesAsync(
        SessionImportScope scope,
        CancellationToken ct = default) =>
        Task.Run(() => Enumerate(scope, ct), ct);

    public Task<ImportedSession?> LoadAsync(SessionImportCandidateFile candidate, CancellationToken ct = default) =>
        Task.Run(() => Load(candidate, ct), ct);

    private IReadOnlyList<SessionImportCandidateFile> Enumerate(SessionImportScope scope, CancellationToken ct)
    {
        if (WorkspacePathMatcher.Normalize(scope.WorkspaceRoot) is not { } workspaceRoot)
            return [];
        var projectByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in SessionFileWindow.Directories(Path.Combine(root, "projects")))
            CollectTranscripts(Path.Combine(project, "agent-transcripts"), Path.GetFileName(project), projectByPath, depth: 0);

        var cwdByProject = new Dictionary<string, string?>(StringComparer.Ordinal);
        var candidates = new List<SessionImportCandidateFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, modifiedAt) in SessionFileWindow.SelectNewest(projectByPath.Keys, scope))
        {
            ct.ThrowIfCancellationRequested();
            var project = projectByPath[path];
            if (!cwdByProject.TryGetValue(project, out var cwd))
                cwdByProject[project] = cwd = WorkspacePathMatcher.ResolveCursorProjectCwd(project, workspaceRoot);
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (cwd is null || scope.ExternalCliSessionIds.Contains(sessionId) || !seen.Add(sessionId))
                continue;

            candidates.Add(new SessionImportCandidateFile(sessionId, path, modifiedAt, cwd));
        }

        return candidates;
    }

    private static void CollectTranscripts(string directory, string project, Dictionary<string, string> projectByPath, int depth)
    {
        foreach (var file in SessionFileWindow.Files(directory, "*.jsonl", SessionFileWindow.TopLevel))
            projectByPath[Path.GetFullPath(file)] = project;
        if (depth >= MaxTranscriptDepth)
            return;
        foreach (var child in SessionFileWindow.Directories(directory))
        {
            if (!string.Equals(Path.GetFileName(child), "subagents", StringComparison.OrdinalIgnoreCase))
                CollectTranscripts(child, project, projectByPath, depth + 1);
        }
    }

    private ImportedSession? Load(SessionImportCandidateFile candidate, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var builder = new ImportTurnBuilder(candidate.ModifiedAt);
        using (var stream = JsonlRecordReader.OpenShared(candidate.Path))
        {
            foreach (var record in JsonlRecordReader.ReadRecords(stream, hash))
            {
                ct.ThrowIfCancellationRequested();
                var role = record.ReadString("role");
                if (role is not ("user" or "assistant")
                    || record.ReadBool("isMeta")
                    || record.ReadBool("isSidechain")
                    || !record.TryReadProperty("message", JsonValueKind.Object, out var message)
                    || !message.TryGetProperty("content", out var content)
                    || ExternalSessionText.ExtractMessage(content) is not { } extracted)
                {
                    continue;
                }

                if (role == "assistant" || extracted.OnlyToolResult)
                    builder.AddAgent(extracted.Text, candidate.ModifiedAt);
                else
                    builder.AddUser(ExternalSessionText.UnwrapCursorUserQuery(extracted.Text), candidate.ModifiedAt);
            }
        }

        var turns = builder.Build();
        if (turns.Count == 0)
            return null;

        return new ImportedSession
        {
            Source = SourceId,
            SourceId = candidate.SourceId,
            SourcePath = candidate.Path,
            Cwd = candidate.Cwd,
            Title = ExternalSessionText.SelectTitle(turns),
            UpdatedAt = candidate.ModifiedAt,
            Turns = turns,
            ContentSha256 = JsonlRecordReader.ToHex(hash)
        };
    }
}
