namespace DotCraft.SessionImport;

internal sealed record LocalImportHostScan(
    IReadOnlyDictionary<string, IReadOnlyList<SessionImportCandidateFile>> Files,
    IReadOnlyCollection<string> ProjectPaths)
{
    public static async Task<LocalImportHostScan> DiscoverAsync(
        IReadOnlyList<ISessionImportSource> sources,
        LocalImportRouting routing,
        string workspaceRoot,
        SessionImportConfig limits,
        IReadOnlyList<string> sourceProjectPaths,
        IReadOnlySet<string> runtimeExternalCliIds,
        CancellationToken ct)
    {
        var paths = new HashSet<string>(sourceProjectPaths, WorkspacePathMatcher.Comparer);
        var excluded = new HashSet<string>(runtimeExternalCliIds, StringComparer.OrdinalIgnoreCase);
        routing.CollectExternalCliSessions(excluded, ct);
        var scope = new SessionImportScope
        {
            WorkspaceRoot = workspaceRoot,
            Now = DateTimeOffset.UtcNow,
            MaxAge = TimeSpan.FromDays(limits.MaxSessionAgeDays),
            MaxSessions = limits.MaxSessionsPerSource,
            ExternalCliSessionIds = excluded
        };
        var files = new Dictionary<string, IReadOnlyList<SessionImportCandidateFile>>(StringComparer.Ordinal);
        foreach (var source in sources.OrderBy(s => s.SourceId == SessionImportSources.Cursor))
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<SessionImportCandidateFile> candidates = [];
            if (source.IsAvailable)
            {
                try
                {
                    candidates = await source.EnumerateCandidatesAsync(
                        scope with { HostProjects = source.SourceId == SessionImportSources.Cursor ? routing.CursorRoots(paths) : [] }, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (LocalImportConfiguration.IsUnavailable(ex)) { }
            }

            files[source.SourceId] = candidates;
            foreach (var file in candidates)
            {
                if (!string.IsNullOrWhiteSpace(file.Cwd)) paths.Add(file.Cwd);
            }
        }
        return new LocalImportHostScan(files, paths);
    }
}
