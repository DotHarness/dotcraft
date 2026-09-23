namespace DotCraft.SessionImport;

/// <summary>Read-only adapter over one external agent's on-disk session store.</summary>
public interface ISessionImportSource
{
    string SourceId { get; }

    bool IsAvailable { get; }

    /// <summary>Lists the newest session files in the window that belong to the scope's workspace.</summary>
    Task<IReadOnlyList<SessionImportCandidateFile>> EnumerateCandidatesAsync(
        SessionImportScope scope,
        CancellationToken ct = default);

    /// <summary>Returns <see langword="null"/> when the candidate yields no import turn or is excluded.</summary>
    Task<ImportedSession?> LoadAsync(SessionImportCandidateFile candidate, CancellationToken ct = default);
}
