using DotCraft.Persistence;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tracing;

/// <summary>
/// Usage-source attribution recorded on trace events at invocation time (spec §27A.3
/// <c>toolSource</c>), and the last-response model used to attribute completed Turns.
/// </summary>
public sealed class ToolUsageSourceTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceStateDatabase _db;

    public ToolUsageSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tool-usage-source-tests", Guid.NewGuid().ToString("N"));
        _db = new WorkspaceStateDatabase(Path.Combine(_root, ".craft"));
    }

    [Theory]
    [InlineData(ToolSourceKind.CoreNative, "core", "builtin")]
    [InlineData(ToolSourceKind.Mcp, "github", "mcp:github")]
    [InlineData(ToolSourceKind.PluginNative, "browser", "plugin:browser")]
    [InlineData(ToolSourceKind.RuntimeDynamic, "desktop", "client")]
    public void FromProvenance_KeysBySourceFamily(ToolSourceKind kind, string sourceId, string expected)
    {
        Assert.Equal(expected, ToolUsageSource.FromProvenance(new ToolProvenance(kind, sourceId)));
    }

    [Fact]
    public void TraceCollector_AttachesNotedSourceToCompletedCall_AndPersistsColumn()
    {
        var store = new TraceStore(_db, maxEventsPerSession: 5000, synchronousPersist: true);
        var collector = new TraceCollector(store);

        collector.NoteToolCallSource("call-1", "mcp:github");
        collector.RecordToolCallStarted("thread-1", new FunctionCallContent("call-1", "list_issues"));
        collector.RecordToolCallCompleted("thread-1", new FunctionResultContent("call-1", "ok"), "list_issues", 42);
        collector.RecordToolCallCompleted("thread-1", new FunctionResultContent("call-2", "ok"), "orphan", 1);

        var events = store.GetEvents("thread-1").Where(e => e.Type == TraceEventType.ToolCallCompleted).ToList();
        Assert.Equal("mcp:github", events.Single(e => e.CallId == "call-1").ToolSource);
        Assert.Null(events.Single(e => e.CallId == "call-2").ToolSource);

        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tool_source FROM trace_events WHERE type = 'ToolCallCompleted' AND call_id = 'call-1'";
        Assert.Equal("mcp:github", command.ExecuteScalar());
    }

    [Fact]
    public void TraceCollector_ReportsModelOfMostRecentResponse()
    {
        var store = new TraceStore(_db, maxEventsPerSession: 5000, synchronousPersist: true);
        var collector = new TraceCollector(store);
        Assert.Null(collector.GetLastResponseModel("thread-1"));

        collector.RecordResponse("thread-1", "first", "r1", null, "atlas-4", "stop", reasoningEffort: "high");
        collector.RecordResponse("thread-1", "second", "r2", null, "boreal-mini", "stop", reasoningEffort: null);

        var last = collector.GetLastResponseModel("thread-1");
        Assert.NotNull(last);
        Assert.Equal("boreal-mini", last.Value.ModelId);
        Assert.Null(last.Value.ReasoningEffort);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
