using System.Text.Json;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class WorldStateDiagnosticsTests
{
    [Fact]
    public void WorldState_RecordsTheStepThatSentNothingAndWhy()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordWorldState(
            "thread-1",
            "turn_002",
            "unchanged",
            changedSections: [],
            emitted: null,
            unchangedSections: ["environment", "mode"],
            suppressedSections: [],
            baselineSource: "memory");

        var diagnostic = Assert.Single(Events(store, "thread-1"));
        Assert.Null(diagnostic.Content);
        var root = Metadata(diagnostic);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("unchanged", root.GetProperty("status").GetString());
        Assert.Equal("turn_002", root.GetProperty("turnId").GetString());
        Assert.Equal("memory", root.GetProperty("baselineSource").GetString());
        Assert.Empty(root.GetProperty("changedSections").EnumerateArray());
        Assert.Equal(
            ["environment", "mode"],
            root.GetProperty("unchangedSections").EnumerateArray().Select(static value => value.GetString()));
    }

    [Fact]
    public void WorldState_NamesTheSectionsItSentAndTheOnesItCouldNot()
    {
        var store = new TraceStore();
        var collector = new TraceCollector(store);

        collector.RecordWorldState(
            "thread-1",
            "turn_001",
            "full",
            changedSections: ["environment", "mode"],
            emitted: Emitted,
            unchangedSections: [],
            suppressedSections: [new WorldStateSectionSuppression("acme_review", "render_failed")],
            baselineSource: "none");

        var diagnostic = Assert.Single(Events(store, "thread-1"));
        Assert.Equal(Emitted, diagnostic.Content);
        var root = Metadata(diagnostic);
        Assert.Equal("none", root.GetProperty("baselineSource").GetString());
        var suppressed = Assert.Single(root.GetProperty("suppressedSections").EnumerateArray());
        Assert.Equal("acme_review", suppressed.GetProperty("id").GetString());
        Assert.Equal("render_failed", suppressed.GetProperty("reason").GetString());
    }

    private static readonly string Emitted =
        "## Environment" + Environment.NewLine + "Cwd: /work"
        + Environment.NewLine + Environment.NewLine
        + "## Mode" + Environment.NewLine + "Current mode: Agent";

    private static JsonElement Metadata(TraceEvent diagnostic) =>
        JsonDocument.Parse(diagnostic.MetadataJson!).RootElement;

    private static IReadOnlyList<TraceEvent> Events(TraceStore store, string sessionKey) =>
        store.GetEvents(sessionKey);
}
