using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.TraceViewer.Services;
using DotCraft.TraceViewer.ViewModels;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class WorkspaceTraceSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-trace-viewer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Open_reads_custom_data_path_without_modifying_workspace()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        var dataPath = Path.Combine(workspacePath, ".agents");
        Directory.CreateDirectory(workspacePath);

        using (var database = new WorkspaceStateDatabase(dataPath))
        {
            var writer = new TraceStore(database, 5000, synchronousPersist: true);
            writer.Record(new TraceEvent
            {
                Type = TraceEventType.Request,
                SessionKey = "thread-1",
                Content = "Inspect the trace",
                InputTokens = 12,
            });
            writer.Record(new TraceEvent
            {
                Type = TraceEventType.ToolCallCompleted,
                SessionKey = "thread-1",
                ToolName = "ReadFile",
                DurationMs = 4,
            });
        }

        var before = SnapshotApplicationFiles(workspacePath);
        WorkspaceTraceSnapshot snapshot;
        using (var source = WorkspaceTraceSource.Open(workspacePath, ".agents"))
            snapshot = source.ReadSnapshot();
        var after = SnapshotApplicationFiles(workspacePath);

        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("thread-1", session.SessionKey);
        Assert.Equal(1, snapshot.Summary.SessionCount);
        Assert.Equal(1, snapshot.Summary.TotalRequests);
        Assert.Equal(1, snapshot.Summary.TotalToolCalls);
        Assert.Equal(before, after);
    }

    [Fact]
    public void ResolveDataPath_rejects_nested_directory()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspacePath);

        var exception = Assert.Throws<ArgumentException>(() =>
            WorkspaceTraceSource.ResolveDataPath(workspacePath, Path.Combine("state", ".agents")));

        Assert.Contains("direct child", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDataPath_rejects_directory_outside_workspace()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        var outsidePath = Path.Combine(_root, "outside");
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(outsidePath);

        Assert.Throws<ArgumentException>(() =>
            WorkspaceTraceSource.ResolveDataPath(workspacePath, outsidePath));
    }

    [Fact]
    public void DiscoverDataPath_prefers_craft_when_multiple_state_directories_exist()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        var craftPath = CreateStateDirectory(workspacePath, ".craft");
        CreateStateDirectory(workspacePath, ".agents");

        Assert.Equal(craftPath, WorkspaceTraceSource.DiscoverDataPath(workspacePath));
    }

    [Fact]
    public void DiscoverDataPath_accepts_one_custom_state_directory()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        var agentsPath = CreateStateDirectory(workspacePath, ".agents");

        Assert.Equal(agentsPath, WorkspaceTraceSource.DiscoverDataPath(workspacePath));
    }

    [Fact]
    public void DiscoverDataPath_rejects_ambiguous_custom_state_directories()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        CreateStateDirectory(workspacePath, ".agents");
        CreateStateDirectory(workspacePath, ".trace");

        Assert.Throws<InvalidDataException>(() =>
            WorkspaceTraceSource.DiscoverDataPath(workspacePath));
    }

    [Fact]
    public void Open_reports_missing_state_database_without_creating_data_directory()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspacePath);

        Assert.Throws<FileNotFoundException>(() =>
            WorkspaceTraceSource.Open(workspacePath, ".agents"));
        Assert.False(Directory.Exists(Path.Combine(workspacePath, ".agents")));
    }

    [Fact]
    public void ReadEventPage_returns_newest_events_and_supports_older_pages()
    {
        var workspacePath = Path.Combine(_root, "workspace");
        var dataPath = Path.Combine(workspacePath, ".agents");
        Directory.CreateDirectory(workspacePath);

        using (var database = new WorkspaceStateDatabase(dataPath))
        {
            var writer = new TraceStore(database, 5000, synchronousPersist: true);
            for (var index = 0; index < 5; index++)
            {
                writer.Record(new TraceEvent
                {
                    Id = $"event-{index}",
                    Type = TraceEventType.Request,
                    SessionKey = "thread-page",
                    Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(index),
                    Content = $"Request {index}",
                });
            }
        }

        using var source = WorkspaceTraceSource.Open(workspacePath, ".agents");
        var newest = source.ReadEventPage("thread-page", limit: 3);
        var older = source.ReadEventPage("thread-page", limit: 3, newest.OldestCursor);

        Assert.Equal(["event-2", "event-3", "event-4"], newest.Events.Select(item => item.Id));
        Assert.True(newest.HasMore);
        Assert.Equal(["event-0", "event-1"], older.Events.Select(item => item.Id));
        Assert.False(older.HasMore);
    }

    [Fact]
    public void CreateSnapshot_reads_every_page_into_stable_chronological_revision()
    {
        var workspacePath = Path.Combine(_root, "snapshot-workspace");
        var dataPath = Path.Combine(workspacePath, ".agents");
        Directory.CreateDirectory(workspacePath);
        using (var database = new WorkspaceStateDatabase(dataPath))
        {
            var writer = new TraceStore(database, 5000, synchronousPersist: true);
            for (var index = 0; index < 620; index++)
            {
                writer.Record(new TraceEvent
                {
                    Id = $"event-{index:D3}",
                    Type = TraceEventType.Request,
                    SessionKey = "thread-snapshot",
                    Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(index)
                });
            }
        }

        using var source = WorkspaceTraceSource.Open(workspacePath, ".agents");
        var snapshot = source.CreateSnapshot("thread-snapshot");

        Assert.Equal(620, snapshot.Events.Count);
        Assert.Equal("event-000", snapshot.Events[0].Id);
        Assert.Equal("event-619", snapshot.Events[^1].Id);
        Assert.StartsWith("620:event-619:", snapshot.Revision, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static IReadOnlyList<string> SnapshotApplicationFiles(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                       && !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
        .Select(path =>
        {
            var info = new FileInfo(path);
            return $"{Path.GetRelativePath(root, path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        })
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static string CreateStateDirectory(string workspacePath, string name)
    {
        var dataPath = Path.Combine(workspacePath, name);
        Directory.CreateDirectory(dataPath);
        File.WriteAllText(Path.Combine(dataPath, "state.db"), string.Empty);
        return dataPath;
    }
}
