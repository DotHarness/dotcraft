using DotCraft.Sessions;

namespace DotCraft.SessionImport.Tests;

internal sealed class FakeImportSource(string sourceId) : ISessionImportSource
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
    private readonly Dictionary<string, ImportedSession> _sessions = new(StringComparer.Ordinal);

    public string SourceId { get; } = sourceId;

    public bool IsAvailable { get; set; } = true;

    public SessionImportScope? LastScope { get; private set; }

    public void Put(string sessionId, int turnCount, string hash, string cwd, DateTimeOffset modifiedAt) =>
        _sessions[sessionId] = new ImportedSession
        {
            Source = SourceId,
            SourceId = sessionId,
            SourcePath = $"/sessions/{sessionId}.jsonl",
            Cwd = cwd,
            Title = $"Title {sessionId}",
            UpdatedAt = modifiedAt,
            Turns = Enumerable.Range(1, turnCount).Select(Turn).ToArray(),
            ContentSha256 = hash
        };

    public static ImportedTurnInput Turn(int index) => new()
    {
        UserText = $"question {index}",
        AgentTexts = [$"answer {index}"],
        StartedAt = BaseTime.AddMinutes(index),
        CompletedAt = BaseTime.AddMinutes(index).AddSeconds(30)
    };

    public Task<IReadOnlyList<SessionImportCandidateFile>> EnumerateCandidatesAsync(
        SessionImportScope scope,
        CancellationToken ct = default)
    {
        LastScope = scope;
        IReadOnlyList<SessionImportCandidateFile> candidates = _sessions.Values
            .Select(static session => new SessionImportCandidateFile(session.SourceId, session.SourcePath, session.UpdatedAt, session.Cwd))
            .ToArray();
        return Task.FromResult(candidates);
    }

    public Task<ImportedSession?> LoadAsync(SessionImportCandidateFile candidate, CancellationToken ct = default) =>
        Task.FromResult<ImportedSession?>(_sessions[candidate.SourceId]);
}
