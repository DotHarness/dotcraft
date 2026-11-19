using System.Security.Cryptography;
using System.Text;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed record LocalImportSource(string Source, bool Available, IReadOnlyList<LocalImportItem> Items, int ImportableCount);

public sealed record LocalImportItem(string Key, ImportCandidate Candidate, string? ProjectPath, string? ProjectName, string Destination);

public sealed class LocalImportDiscovery
{
    private readonly string _userDataPath;
    private readonly string _chatPath;
    private readonly string _home;
    private readonly IReadOnlyList<ISessionImportSource> _sources;
    private readonly IReadOnlyDictionary<string, string> _roots;
    private readonly string? _cursorWorkspaceStorage;

    public LocalImportDiscovery(string userDataPath, string chatPath)
        : this(userDataPath, chatPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            SessionImportModule.CurrentUserSources(), SessionImportModule.CurrentUserSetupRoots(),
            LocalImportConfiguration.CurrentCursorWorkspaceStorage()) { }

    internal LocalImportDiscovery(string userDataPath, string chatPath, string home,
        IReadOnlyList<ISessionImportSource> sources, IReadOnlyDictionary<string, string> roots, string? cursorWorkspaceStorage = null)
    {
        _userDataPath = userDataPath;
        _chatPath = chatPath;
        _home = home;
        _sources = sources;
        _roots = roots;
        _cursorWorkspaceStorage = cursorWorkspaceStorage;
    }

    public async Task<IReadOnlyList<LocalImportSource>> DetectAsync(
        IReadOnlyList<string> registeredProjects, CancellationToken ct = default)
    {
        var limits = LocalImportConfiguration.ReadLimits(_userDataPath);
        if (!limits.Enabled) return [];
        var routing = new LocalImportRouting(registeredProjects, _chatPath, _home, _userDataPath);
        var scan = await LocalImportHostScan.DiscoverAsync(_sources, routing, _chatPath, limits,
            LocalImportConfiguration.ReadSourceProjectPaths(_roots, _cursorWorkspaceStorage), new HashSet<string>(), ct).ConfigureAwait(false);
        var projectPaths = new HashSet<string>(routing.RegisteredProjects, WorkspacePathMatcher.Comparer);
        foreach (var cwd in scan.ProjectPaths)
        {
            if (routing.ResolveDirectory(cwd) is { ProjectPath: { } path, Destination: "newProject" })
                projectPaths.Add(path);
        }

        var detections = new List<LocalImportSource>();
        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var items = new List<LocalImportItem>();
            foreach (var item in Scanner(_chatPath).Scan(source.SourceId, scopeFilter: "user"))
                items.Add(Wrap(item.Candidate, "personal", null));

            foreach (var file in scan.Files[source.SourceId].DistinctBy(f => f.SourceId, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var route = await routing.ResolveSessionAsync(source.SourceId, file, ct).ConfigureAwait(false);
                if (await DetectSessionAsync(source, file, route, ct).ConfigureAwait(false) is { } item)
                    items.Add(item);
            }

            foreach (var project in projectPaths.Order(WorkspacePathMatcher.Comparer))
            {
                ct.ThrowIfCancellationRequested();
                var destination = routing.IsKnownWorkspace(project) ? "project" : "newProject";
                if (!Enabled(project)) continue;
                foreach (var item in Scanner(project).Scan(source.SourceId, scopeFilter: "workspace"))
                    items.Add(Wrap(item.Candidate, destination, project));
            }
            detections.Add(new LocalImportSource(source.SourceId, source.IsAvailable || items.Count > 0, items,
                items.Count(item => item.Candidate.State is "new" or "changed")));
        }
        return detections;
    }

    public static IReadOnlyList<ImportCompletedNotification> ReadHistory(string userDataPath, string workspaceDataPath) =>
        new ImportHistoryStore(userDataPath, workspaceDataPath).Read();

    private async Task<LocalImportItem?> DetectSessionAsync(
        ISessionImportSource source, SessionImportCandidateFile file, LocalImportRoute route, CancellationToken ct)
    {
        var record = route.Record;
        if (record is { ContentSha256: not null, SourceModifiedAt: { } modifiedAt }
            && SessionImportLedger.SameInstant(modifiedAt, file.ModifiedAt))
            return null;
        ImportedSession? session;
        try
        {
            session = await source.LoadAsync(file, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (LocalImportConfiguration.IsUnavailable(ex))
        {
            return null;
        }
        if (session is null) return null;

        var reason = "";
        string state;
        if (route.ProjectPath == null)
        {
            state = "unsupported";
            reason = "import_project_unavailable";
        }
        else if (!Enabled(route.ProjectPath))
        {
            state = "unsupported";
            reason = "import_project_disabled";
        }
        else if (record == null)
            state = "new";
        else if (string.Equals(record.ContentSha256, session.ContentSha256, StringComparison.OrdinalIgnoreCase)
            || record.ContentSha256 is null && session.Turns.Count <= record.TurnCount)
            state = "current";
        else
            state = session.Turns.Count >= record.TurnCount
                && await route.State!.CanExtendAsync(record, ct).ConfigureAwait(false) ? "changed" : "deferred";

        return Wrap(new ImportCandidate
        {
            Source = source.SourceId,
            SourceId = file.SourceId,
            SourcePath = file.Path,
            Cwd = route.ProjectPath != null ? route.Cwd : session.Cwd,
            TargetPath = route.ProjectPath != null ? Path.Combine(route.ProjectPath, ".craft", "threads") : "",
            Title = session.Title,
            UpdatedAt = session.UpdatedAt,
            TurnCount = session.Turns.Count,
            Fingerprint = session.ContentSha256,
            State = state,
            Reason = reason,
            FallbackText = reason.Length == 0 ? "" : ImportDiagnostics.Fallback(reason)
        }, route.Destination, route.ProjectPath);
    }

    private bool Enabled(string workspace) =>
        LocalImportConfiguration.ReadLimits(Path.Combine(workspace, ".craft")).Enabled;

    private SetupImportScanner Scanner(string workspace) =>
        new(new SetupImportPaths(workspace, Path.Combine(workspace, ".craft"), _userDataPath, _home, _roots));

    private static LocalImportItem Wrap(ImportCandidate candidate, string destination, string? path)
    {
        var identity = OperatingSystem.IsWindows() ? path?.ToUpperInvariant() : path;
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{candidate.Source}\n{candidate.Category}\n{candidate.SourceId.ToUpperInvariant()}\n{identity}")));
        return new LocalImportItem(key, candidate, path, path == null ? null : Path.GetFileName(path), destination);
    }
}
