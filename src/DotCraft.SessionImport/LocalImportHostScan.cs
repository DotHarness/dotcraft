namespace DotCraft.SessionImport;

internal sealed record LocalImportHostScan(
    IReadOnlyDictionary<string, IReadOnlyList<SessionImportCandidateFile>> Files,
    IReadOnlyCollection<string> ProjectPaths)
{
    public static async Task<LocalImportHostScan> DiscoverAsync(
        IReadOnlyList<ISessionImportSource> sources,
        LocalImportRouting routing,
        SessionImportScope scope,
        IReadOnlyList<string> sourceProjectPaths,
        CancellationToken ct)
    {
        var paths = new HashSet<string>(sourceProjectPaths, WorkspacePathMatcher.Comparer);
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
