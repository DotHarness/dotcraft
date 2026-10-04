using System.Collections.Concurrent;

namespace DotCraft.SessionImport;

/// <summary>Reads other coding agents' sessions recorded for one workspace without importing them.</summary>
public sealed class ExternalSessionReader
{
    private readonly IReadOnlyList<ISessionImportSource> _sources;
    private readonly ConcurrentDictionary<string, CachedSummary> _summaries = new(StringComparer.Ordinal);

    internal ExternalSessionReader(IEnumerable<ISessionImportSource> sources) => _sources = [.. sources];

    /// <summary>Reads the current user's stores, honouring <c>CLAUDE_CONFIG_DIR</c> and <c>CODEX_HOME</c>.</summary>
    public static ExternalSessionReader ForCurrentUser() => new(SessionImportModule.CurrentUserSources());

    public async Task<IReadOnlyList<ExternalSessionSummary>> ListAsync(
        SessionImportScope scope,
        CancellationToken ct = default)
    {
        var summaries = new List<ExternalSessionSummary>();
        foreach (var source in _sources)
        {
            foreach (var file in await CandidatesAsync(source, scope, ct).ConfigureAwait(false))
            {
                var key = $"{source.SourceId}\n{file.Path}";
                if (!_summaries.TryGetValue(key, out var cached) || cached.ModifiedAt != file.ModifiedAt)
                {
                    cached = new CachedSummary(file.ModifiedAt, Summarize(await LoadAsync(source, file, ct).ConfigureAwait(false)));
                    _summaries[key] = cached;
                }

                if (cached.Summary is { } summary)
                    summaries.Add(summary);
            }
        }

        return summaries.OrderByDescending(static summary => summary.UpdatedAt).ToArray();
    }

    /// <summary>Returns <see langword="null"/> when the session is no longer a candidate of the scope.</summary>
    public async Task<ImportedSession?> ReadAsync(
        SessionImportScope scope,
        string source,
        string sourceId,
        CancellationToken ct = default)
    {
        var reader = _sources.FirstOrDefault(entry => entry.SourceId == source);
        if (reader is null)
            return null;
        var file = (await CandidatesAsync(reader, scope, ct).ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.SourceId == sourceId);
        return file is null ? null : await LoadAsync(reader, file, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SessionImportCandidateFile>> CandidatesAsync(
        ISessionImportSource source,
        SessionImportScope scope,
        CancellationToken ct)
    {
        if (!source.IsAvailable)
            return [];
        try
        {
            return await source.EnumerateCandidatesAsync(scope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private static async Task<ImportedSession?> LoadAsync(
        ISessionImportSource source,
        SessionImportCandidateFile file,
        CancellationToken ct)
    {
        try
        {
            return await source.LoadAsync(file, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static ExternalSessionSummary? Summarize(ImportedSession? session) => session is null
        ? null
        : new ExternalSessionSummary(
            session.Source,
            session.SourceId,
            session.Title,
            session.Cwd,
            session.UpdatedAt,
            session.Turns.Count,
            ExternalSessionText.FirstRequest(session.Turns));

    private sealed record CachedSummary(DateTimeOffset ModifiedAt, ExternalSessionSummary? Summary);
}

public sealed record ExternalSessionSummary(
    string Source,
    string SourceId,
    string Title,
    string Cwd,
    DateTimeOffset UpdatedAt,
    int TurnCount,
    string FirstRequest);
