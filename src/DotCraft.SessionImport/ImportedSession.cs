using DotCraft.Sessions;

namespace DotCraft.SessionImport;

public sealed record ImportedSession
{
    public required string Source { get; init; }

    public required string SourceId { get; init; }

    public required string SourcePath { get; init; }

    public required string Cwd { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required IReadOnlyList<ImportedTurnInput> Turns { get; init; }

    public required string ContentSha256 { get; init; }
}

public sealed record SessionImportCandidateFile(string SourceId, string Path, DateTimeOffset ModifiedAt, string Cwd);

public sealed record SessionImportScope
{
    public required string WorkspaceRoot { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required TimeSpan MaxAge { get; init; }

    public required int MaxSessions { get; init; }

    public required IReadOnlySet<string> ExternalCliSessionIds { get; init; }
}
