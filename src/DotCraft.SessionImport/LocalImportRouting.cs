namespace DotCraft.SessionImport;

internal sealed class LocalImportRouting
{
    private readonly string _chatPath;
    private readonly string _chatDirectory;
    private readonly string? _homeDirectory;
    private readonly string? _userDirectory;
    private readonly Dictionary<string, string> _registered = new(WorkspacePathMatcher.Comparer);
    private readonly Dictionary<string, LocalImportThreadState> _states = new(WorkspacePathMatcher.Comparer);
    private readonly string? _activeWorkspace;
    private readonly SessionImportLedgerDocument? _activeLedger;

    public LocalImportRouting(
        IReadOnlyList<string> registeredProjects, string chatPath, string home, string userData,
        string? activeWorkspace = null, string? activeDataPath = null, SessionImportLedgerDocument? activeLedger = null)
    {
        _chatPath = Path.GetFullPath(chatPath);
        _chatDirectory = WorkspacePathMatcher.ResolveExistingDirectory(chatPath) ?? _chatPath;
        _homeDirectory = WorkspacePathMatcher.ResolveExistingDirectory(home);
        _userDirectory = WorkspacePathMatcher.ResolveExistingDirectory(userData) ?? WorkspacePathMatcher.Normalize(userData);
        _activeWorkspace = activeWorkspace;
        _activeLedger = activeLedger;

        foreach (var path in registeredProjects)
        {
            var canonical = WorkspacePathMatcher.ResolveExistingDirectory(path);
            var normalized = WorkspacePathMatcher.Normalize(path);
            if (canonical == null || normalized == null || IsChat(canonical)) continue;
            _registered.TryAdd(canonical, normalized);
        }

        foreach (var path in _registered.Values.Append(_chatPath))
            _states[path] = new LocalImportThreadState(path,
                activeWorkspace != null && WorkspacePathMatcher.Comparer.Equals(activeWorkspace, path) && activeDataPath != null
                    ? activeDataPath : Path.Combine(path, ".craft"));
    }

    public IReadOnlyCollection<string> RegisteredProjects => _registered.Values;
    public bool IsKnownWorkspace(string root) =>
        WorkspacePathMatcher.Comparer.Equals(_chatPath, root) || _registered.Values.Contains(root, WorkspacePathMatcher.Comparer);

    public IReadOnlyList<string> CursorRoots(IEnumerable<string> discoveredPaths) =>
        _registered.Keys.Concat(discoveredPaths.Select(WorkspacePathMatcher.ResolveExistingDirectory).OfType<string>())
            .Concat(_registered.Values).Distinct(WorkspacePathMatcher.Comparer).ToArray();

    public void CollectExternalCliSessions(HashSet<string> ids, CancellationToken ct)
    {
        foreach (var state in _states.Values)
            state.CollectExternalCliSessions(ids, ct);
    }

    public LocalImportRoute ResolveDirectory(string? sourceCwd, bool unresolved = false) =>
        ResolveDirectory(sourceCwd, unresolved, exactProjectRoot: false);

    public string? ResolveExplicitProjectRoot(string sourceRoot) =>
        ResolveDirectory(sourceRoot, unresolved: false, exactProjectRoot: true) is { Destination: "newProject" } route
            ? route.ProjectPath : null;

    private LocalImportRoute ResolveDirectory(string? sourceCwd, bool unresolved, bool exactProjectRoot)
    {
        if (unresolved) return LocalImportRoute.Unavailable;
        if (string.IsNullOrWhiteSpace(sourceCwd)) return Chats();
        if (WorkspacePathMatcher.ResolveExistingDirectory(sourceCwd) is not { } cwd)
            return LocalImportRoute.Unavailable;

        if (IsChat(cwd)) return Chats();
        if (_userDirectory != null && WorkspacePathMatcher.IsWithin(cwd, _userDirectory))
            return LocalImportRoute.Unavailable;
        var owner = exactProjectRoot
            ? (_registered.ContainsKey(cwd) ? cwd : null)
            : _registered.Keys.Where(root => WorkspacePathMatcher.IsWithin(cwd, root)).OrderByDescending(root => root.Length).FirstOrDefault();
        if (owner != null)
            return new LocalImportRoute(_registered[owner], "project",
                WorkspacePathMatcher.Normalize(Path.Combine(_registered[owner], Path.GetRelativePath(owner, cwd)))!);
        if (WorkspacePathMatcher.Comparer.Equals(cwd, _homeDirectory)) return Chats();
        if (WorkspacePathMatcher.Comparer.Equals(Path.GetPathRoot(cwd), cwd)) return LocalImportRoute.Unavailable;
        return new LocalImportRoute(cwd, "newProject", cwd);
    }

    public async Task<LocalImportRoute> ResolveSessionAsync(
        string source, SessionImportCandidateFile file, CancellationToken ct)
    {
        var natural = ResolveDirectory(file.Cwd, file.ProjectUnresolved);
        var candidates = _states.Values.ToList();
        if (natural.ProjectPath != null && WorkspacePathMatcher.ResolveExistingDirectory(file.Cwd) is { } cwd)
        {
            for (var folder = cwd; folder != null; folder = Path.GetDirectoryName(folder))
            {
                var data = Path.Combine(folder, ".craft");
                if (Directory.Exists(data) && !WorkspacePathMatcher.Comparer.Equals(data, _userDirectory)
                    && !candidates.Any(s => WorkspacePathMatcher.Comparer.Equals(
                        WorkspacePathMatcher.ResolveExistingDirectory(s.WorkspacePath), folder)))
                    candidates.Add(new LocalImportThreadState(folder, data));
            }
        }

        LocalImportThreadState? found = null;
        SessionImportLedgerRecord? ledger = null;
        foreach (var project in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var record = WorkspacePathMatcher.Comparer.Equals(project.WorkspacePath, _activeWorkspace)
                ? _activeLedger?.Find(source, file.SourceId) : null;
            record ??= await project.FindAsync(source, file.SourceId, ct).ConfigureAwait(false);
            if (record == null) continue;
            if (ledger != null && (record.ImportedAt > ledger.ImportedAt
                || record.ImportedAt == ledger.ImportedAt && WorkspacePathMatcher.Comparer.Compare(project.WorkspacePath, found!.WorkspacePath) >= 0))
                continue;
            found = project;
            ledger = record;
        }

        if (found == null) return natural;
        var path = found.WorkspacePath;
        var destination = WorkspacePathMatcher.Comparer.Equals(path, _chatPath) ? "chats"
            : IsKnownWorkspace(path) ? "project" : "newProject";
        return new LocalImportRoute(path, destination,
            WorkspacePathMatcher.Comparer.Equals(path, natural.ProjectPath) ? natural.Cwd : path, found, ledger);
    }

    private LocalImportRoute Chats() => new(_chatPath, "chats", _chatPath);
    private bool IsChat(string canonical) => WorkspacePathMatcher.IsWithin(canonical, _chatDirectory);
}

internal sealed record LocalImportRoute(string? ProjectPath, string Destination, string Cwd,
    LocalImportThreadState? State = null, SessionImportLedgerRecord? Record = null)
{
    public static LocalImportRoute Unavailable { get; } = new(null, "unavailable", "");
}
