using DotCraft.Context.WorldState;
using DotCraft.Visualizations;
using DotCraft.Sessions;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Core.Tests.Visualizations;

public sealed class VisualizationStateSectionTests : IAsyncLifetime
{
    private const string ClosingTag = "</untrusted_visualization_state>";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-visualization-state-" + Guid.NewGuid().ToString("N"));
    private ThreadStore _store = null!;
    private SessionThread _thread = null!;
    private WorldState _world = null!;
    private WorldStateSnapshot? _baseline;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _store = new ThreadStore(Path.Combine(_root, ".craft"));
        var now = DateTimeOffset.UtcNow;
        _thread = new SessionThread { Id = "thread_state", WorkspacePath = _root, CreatedAt = now, LastActiveAt = now };
        await _store.SaveThreadAsync(_thread);
        _world = new WorldState();
        _world.AddSection(new VisualizationStateSection(_store.VisualizationWidgetStates));
    }

    [Fact]
    public void ModelContent_IsDeliveredOncePerChangeAndClearedOnce()
    {
        const string chart = "Sales_报告 v2.HTML";
        const string map = "map.html";

        Save(chart, "{\"region\":\"north\"}", "{\"scroll\":120}");
        Save(map, null, "{\"zoom\":3}");
        var first = Render();
        Assert.Contains("\"region\":\"north\"", first, StringComparison.Ordinal);
        Assert.Contains(chart, first, StringComparison.Ordinal);
        Assert.DoesNotContain("scroll", first, StringComparison.Ordinal);
        Assert.DoesNotContain("zoom", first, StringComparison.Ordinal);

        Save(chart, "{\"region\":\"north\"}", "{\"scroll\":480}");
        Assert.Null(RenderOrNull());

        Save(map, $"{{\"country\":\"{ClosingTag}\"}}", "{\"zoom\":3}");
        var second = Render();
        Assert.Contains("country", second, StringComparison.Ordinal);
        Assert.DoesNotContain("region", second, StringComparison.Ordinal);
        Assert.Equal(second.IndexOf(ClosingTag, StringComparison.Ordinal), second.LastIndexOf(ClosingTag, StringComparison.Ordinal));

        Save(chart, null, null);
        var cleared = Render();
        Assert.Contains(chart + "\":null", cleared, StringComparison.Ordinal);
        Assert.DoesNotContain("country", cleared, StringComparison.Ordinal);
        Assert.Null(RenderOrNull());
    }

    [Fact]
    public async Task Snapshots_UseExactFileNamesAndRemainIsolatedByThread()
    {
        const string name = "Sales_报告 v2.HTML";
        var lowerCaseName = name.ToLowerInvariant();
        var otherThread = new SessionThread
        {
            Id = "thread_other",
            WorkspacePath = _root,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };
        await _store.SaveThreadAsync(otherThread);
        var states = _store.VisualizationWidgetStates;

        Save(name, "{\"value\":1}", "{\"scroll\":10}");
        Save(lowerCaseName, "{\"value\":2}", null);
        states.Save(otherThread.Id, name, new VisualizationWidgetState("{\"value\":3}", null));

        Assert.Equal(new VisualizationWidgetState("{\"value\":1}", "{\"scroll\":10}"), states.Load(_thread.Id, name));
        Assert.Equal(new VisualizationWidgetState("{\"value\":2}", null), states.Load(_thread.Id, lowerCaseName));
        Assert.Equal(new VisualizationWidgetState("{\"value\":3}", null), states.Load(otherThread.Id, name));
        Assert.Equal(
            new[] { name, lowerCaseName }.Order(StringComparer.Ordinal),
            states.LoadModelContents(_thread.Id).Select(content => content.Path));
        var rendered = Render();
        Assert.Contains(name, rendered, StringComparison.Ordinal);
        Assert.Contains(lowerCaseName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":3", rendered, StringComparison.Ordinal);

        Save(name, null, null);
        Assert.Null(states.Load(_thread.Id, name));
        Assert.NotNull(states.Load(_thread.Id, lowerCaseName));
        Assert.NotNull(states.Load(otherThread.Id, name));
    }

    private void Save(string path, string? modelContent, string? privateContent) =>
        _store.VisualizationWidgetStates.Save(_thread.Id, path, new VisualizationWidgetState(modelContent, privateContent));

    private string Render() => RenderOrNull() ?? throw new InvalidOperationException("Nothing was delivered.");

    private string? RenderOrNull()
    {
        var context = new WorldStateContext { Thread = _thread };
        var render = _baseline is null
            ? _world.RenderFull(context)
            : _world.Render(context, _baseline, new HashSet<string> { VisualizationStateSection.SectionId });
        _baseline = render.Snapshot;
        return render.Fragments.Count == 0 ? null : Assert.Single(render.Fragments).Text;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
