using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.TraceViewer.ViewModels;
using DotCraft.TraceViewer.Analysis;

namespace DotCraft.TraceViewer.Services;

internal sealed class WorkspaceTraceSource : IDisposable
{
    private readonly WorkspaceStateDatabase _database;
    private bool _disposed;

    private WorkspaceTraceSource(
        string workspacePath,
        WorkspaceStateDatabase database,
        TraceStore traceStore)
    {
        WorkspacePath = workspacePath;
        _database = database;
        TraceStore = traceStore;
    }

    public string WorkspacePath { get; }

    public string DataPath { get; private init; } = string.Empty;

    private TraceStore TraceStore { get; }

    public static WorkspaceTraceSource Open(string workspacePath) =>
        Open(workspacePath, DiscoverDataPath(workspacePath));

    public static WorkspaceTraceSource Open(string workspacePath, string dataPath)
    {
        var resolved = ResolveDataPath(workspacePath, dataPath);
        var stateDbPath = Path.Combine(resolved.DataPath, "state.db");
        if (!File.Exists(stateDbPath))
            throw new FileNotFoundException("DotCraft workspace state database was not found.", stateDbPath);

        WorkspaceStateDatabase? database = null;
        try
        {
            database = new WorkspaceStateDatabase(resolved.DataPath, readOnly: true);
            return new WorkspaceTraceSource(
                resolved.WorkspacePath,
                database,
                new TraceStore(database, maxEventsPerSession: 5000))
            {
                DataPath = resolved.DataPath
            };
        }
        catch
        {
            database?.Dispose();
            throw;
        }
    }

    public WorkspaceTraceSnapshot ReadSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TraceStore.RefreshFromDisk();

        var sessions = TraceStore.GetSessions();
        var relationships = TraceStore.DescribeSessionRelationships(sessions.Select(session => session.SessionKey));
        var items = sessions.Select(session =>
        {
            relationships.TryGetValue(session.SessionKey, out var relationship);
            return SessionListItem.FromTrace(session, relationship);
        }).ToArray();

        return new WorkspaceTraceSnapshot(TraceStore.GetSummary(), items);
    }

    public TraceEventPage ReadEventPage(string sessionKey, int limit, string? beforeCursor = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        TraceStore.RefreshFromDisk();
        return TraceStore.GetEventPage(sessionKey, limit, beforeCursor);
    }

    public TraceSnapshot CreateSnapshot(string sessionKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TraceStore.RefreshFromDisk();
        var session = TraceStore.GetSessions().FirstOrDefault(item =>
            string.Equals(item.SessionKey, sessionKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Trace session '{sessionKey}' was not found.");
        var events = new List<TraceEvent>();
        string? cursor = null;
        do
        {
            var page = TraceStore.GetEventPage(sessionKey, 500, cursor);
            events.InsertRange(0, page.Events);
            cursor = page.HasMore ? page.OldestCursor : null;
        }
        while (cursor is not null);

        var revision = $"{events.Count}:{events.LastOrDefault()?.Id ?? "empty"}:{session.LastActivityAt:O}";
        return new TraceSnapshot(WorkspacePath, sessionKey, revision, session.LastActivityAt, events.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _database.Dispose();
    }

    internal static ResolvedWorkspaceTracePath ResolveDataPath(string workspacePath, string dataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);

        var normalizedWorkspace = Normalize(workspacePath);
        if (!Directory.Exists(normalizedWorkspace))
            throw new DirectoryNotFoundException($"Workspace directory was not found: {normalizedWorkspace}");

        var normalizedData = Normalize(Path.IsPathRooted(dataPath)
            ? dataPath
            : Path.Combine(normalizedWorkspace, dataPath));
        EnsureDirectChild(normalizedWorkspace, normalizedData);
        EnsureExistingLinkDoesNotEscape(normalizedWorkspace, normalizedData);
        return new ResolvedWorkspaceTracePath(normalizedWorkspace, normalizedData);
    }

    internal static string DiscoverDataPath(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var normalizedWorkspace = Normalize(workspacePath);
        if (!Directory.Exists(normalizedWorkspace))
            throw new DirectoryNotFoundException($"Workspace directory was not found: {normalizedWorkspace}");

        var defaultDataPath = Path.Combine(normalizedWorkspace, ".craft");
        if (File.Exists(Path.Combine(defaultDataPath, "state.db")))
            return defaultDataPath;

        var candidates = Directory.EnumerateDirectories(normalizedWorkspace)
            .Where(path => File.Exists(Path.Combine(path, "state.db")))
            .Take(2)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new FileNotFoundException(
                "No DotCraft state database was found in a direct child of the workspace."),
            _ => throw new InvalidDataException(
                "Multiple DotCraft data directories were found. Keep one state database under the workspace or use .craft."),
        };
    }

    private static void EnsureDirectChild(string workspacePath, string dataPath)
    {
        var relative = Path.GetRelativePath(workspacePath, dataPath);
        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "Data directory must identify a direct child of the workspace.",
                nameof(dataPath));
        }
    }

    private static void EnsureExistingLinkDoesNotEscape(string workspacePath, string dataPath)
    {
        var dataInfo = new DirectoryInfo(dataPath);
        if (!dataInfo.Exists || (dataInfo.Attributes & FileAttributes.ReparsePoint) == 0)
            return;

        var target = dataInfo.ResolveLinkTarget(returnFinalTarget: true);
        if (target is null)
            return;

        var workspaceInfo = new DirectoryInfo(workspacePath);
        var resolvedWorkspace = workspaceInfo.Exists && (workspaceInfo.Attributes & FileAttributes.ReparsePoint) != 0
            ? Normalize(workspaceInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? workspacePath)
            : workspacePath;
        var relative = Path.GetRelativePath(resolvedWorkspace, Normalize(target.FullName));
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Data directory must not link outside the workspace.",
                nameof(dataPath));
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

internal sealed record ResolvedWorkspaceTracePath(string WorkspacePath, string DataPath);

internal sealed record WorkspaceTraceSnapshot(
    TraceSummary Summary,
    IReadOnlyList<SessionListItem> Sessions);
