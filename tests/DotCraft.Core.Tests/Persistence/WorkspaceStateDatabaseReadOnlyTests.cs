using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Persistence;

public sealed class WorkspaceStateDatabaseReadOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-read-only-state-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadOnly_connection_does_not_make_subsequent_writer_read_only()
    {
        Directory.CreateDirectory(_root);
        using (var initial = new WorkspaceStateDatabase(_root))
        {
        }

        using var readOnlyDatabase = new WorkspaceStateDatabase(_root, readOnly: true);
        var reader = new TraceStore(readOnlyDatabase, 5000);
        Assert.Empty(reader.GetSessions());

        using (var writableDatabase = new WorkspaceStateDatabase(_root))
        {
            var writer = new TraceStore(writableDatabase, 5000, synchronousPersist: true);
            writer.Record(new TraceEvent
            {
                Type = TraceEventType.Request,
                SessionKey = "thread-new",
                Content = "New committed request",
            });
        }

        Assert.Equal("thread-new", Assert.Single(reader.GetSessions()).SessionKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
