using DotCraft.Tracing;
using DotCraft.TraceViewer.ViewModels;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class TrajectoryProjectionTests
{
    [Fact]
    public void ResolveRange_uses_elapsed_time_in_duration_mode()
    {
        var start = DateTimeOffset.UnixEpoch;
        var markers = new[]
        {
            Marker("event-1", start),
            Marker("event-2", start.AddSeconds(10)),
            Marker("event-3", start.AddSeconds(100)),
        };

        var range = TrajectoryProjection.ResolveRange(markers, TimelineScaleMode.Duration, 0.09, 0.11);

        Assert.Equal(("event-2", "event-2"), range);
    }

    [Fact]
    public void ResolveRange_uses_event_positions_in_sequence_mode()
    {
        var start = DateTimeOffset.UnixEpoch;
        var markers = new[]
        {
            Marker("event-1", start.AddSeconds(100)),
            Marker("event-2", start),
            Marker("event-3", start.AddSeconds(10)),
        };

        var range = TrajectoryProjection.ResolveRange(markers, TimelineScaleMode.Sequence, 0.09, 0.11);

        Assert.Equal(("event-1", "event-1"), range);
    }

    [Fact]
    public void Project_CorrelatesToolStartAndCompletionIntoOneRow()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var result = TrajectoryProjection.Project(
        [
            Event(TraceEventType.ToolCallStarted, startedAt, callId: "call-1", toolName: "read_file", arguments: "{\"path\":\"a.cs\"}"),
            Event(TraceEventType.ToolCallCompleted, startedAt.AddMilliseconds(25), callId: "call-1", toolName: "read_file", result: "content", durationMs: 25),
        ], beginsMidSession: false);

        var row = Assert.Single(result.Rows);
        Assert.Equal("read_file", row.Title);
        Assert.Equal("{\"path\":\"a.cs\"}", row.Summary);
        Assert.Equal(2, row.SourceEvents.Count);
        Assert.Contains(row.Detail.Sections, section => section.Label == "Content"
            && section.Blocks.Any(block => block.Label == "Arguments")
            && section.Blocks.Any(block => block.Label == "Result"));
        Assert.Single(result.Timeline, marker => marker.Lane == TimelineLane.Tools);
    }

    [Fact]
    public void Project_SeparatesUserContentFromInjectedRuntimeContext()
    {
        const string request = "Fix the tests\n\n<system-reminder>\n## Environment\nCurrentDate: 2030-01-02\nTimeZone: Etc/UTC\n## Mode\nCurrentMode: default\n## Mode Action\nImplement\n</system-reminder>";
        var result = TrajectoryProjection.Project(
            [Event(TraceEventType.Request, DateTimeOffset.UtcNow, content: request)],
            beginsMidSession: false);

        var row = Assert.Single(result.Rows);
        Assert.Equal("User request", row.Title);
        Assert.Equal("Fix the tests", row.Summary);
        Assert.Contains(row.Detail.Sections, section => section.Label == "Context"
            && section.Blocks.Any(block => block.Label == "Injected runtime context"));
    }

    [Fact]
    public void Project_UsesSpansOnlyForRecordedDurations()
    {
        var now = DateTimeOffset.UtcNow;
        var result = TrajectoryProjection.Project(
        [
            Event(TraceEventType.Response, now, durationMs: 90),
            Event(TraceEventType.ProviderResponseDiagnostic, now.AddSeconds(1), durationMs: 60, metadata: "{\"eventType\":\"stream_attempt\"}"),
            Event(TraceEventType.TurnCompleted, now.AddSeconds(2), durationMs: 200),
        ], beginsMidSession: false);

        Assert.Null(result.Timeline.Single(marker => marker.Title == "Model response").End);
        Assert.NotNull(result.Timeline.Single(marker => marker.Title == "Provider response").End);
        Assert.DoesNotContain(result.Timeline, marker => marker.Title == "Turn completed");
    }

    [Fact]
    public void Project_PresentsEveryKnownEventTypeWithRawFallback()
    {
        var events = Enum.GetValues<TraceEventType>()
            .Select((type, index) => Event(type, DateTimeOffset.UtcNow.AddMilliseconds(index)))
            .ToArray();

        var result = TrajectoryProjection.Project(events, beginsMidSession: false);

        Assert.Equal(events.Length, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Contains(row.Detail.Sections, section => section.Label == "Raw"));
    }

    [Fact]
    public void Project_CarriesTurnAndModelCallIdentityIntoTheViewerModel()
    {
        var now = DateTimeOffset.UtcNow;
        var result = TrajectoryProjection.Project(
        [
            Event(TraceEventType.Request, now, requestIndex: 3),
            Event(TraceEventType.Thinking, now.AddMilliseconds(20), requestIndex: 3, llmCallIndex: 2),
            Event(TraceEventType.Response, now.AddMilliseconds(40), requestIndex: 3, llmCallIndex: 2),
            Event(TraceEventType.TurnCompleted, now.AddMilliseconds(60)),
        ], beginsMidSession: false);

        var turn = Assert.Single(result.Turns);
        Assert.Equal("turn:0", turn.Key);
        Assert.Equal(2, result.Rows.Single(row => row.Title == "Model response").ModelCallIndex);
        Assert.All(result.Timeline, marker =>
        {
            Assert.Equal(turn.Key, marker.TurnKey);
            Assert.Equal(turn.Title, marker.TurnTitle);
        });
    }

    [Fact]
    public void Project_PlacesRuntimeActivityOnTheRuntimeLane()
    {
        var result = TrajectoryProjection.Project(
            [Event(TraceEventType.ToolInjection, DateTimeOffset.UtcNow)],
            beginsMidSession: false);

        Assert.Equal(TimelineLane.Runtime, Assert.Single(result.Rows).Lane);
    }

    [Fact]
    public void Project_PreservesUnicodeInHumanReadableJson()
    {
        var result = TrajectoryProjection.Project(
            [Event(TraceEventType.Response, DateTimeOffset.UtcNow, content: "这里是中文内容", metadata: "{\"note\":\"中文元数据\"}")],
            beginsMidSession: false);

        var row = Assert.Single(result.Rows);
        var raw = row.Detail.Sections.Single(section => section.Label == "Raw").Blocks.Single().Value;
        var metadata = row.Detail.Sections.Single(section => section.Label == "Diagnostics").Blocks.Single().Value;
        Assert.Contains("这里是中文内容", raw);
        Assert.Contains("中文元数据", metadata);
        Assert.DoesNotContain("\\u", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u", metadata, StringComparison.OrdinalIgnoreCase);
    }

    private static TraceEvent Event(
        TraceEventType type,
        DateTimeOffset timestamp,
        string? content = null,
        string? callId = null,
        string? toolName = null,
        string? arguments = null,
        string? result = null,
        double? durationMs = null,
        string? metadata = null,
        int? requestIndex = null,
        int? llmCallIndex = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionKey = "session",
            Type = type,
            Timestamp = timestamp,
            Content = content,
            CallId = callId,
            ToolName = toolName,
            ToolArguments = arguments,
            ToolResult = result,
            DurationMs = durationMs,
            MetadataJson = metadata,
            RequestIndex = requestIndex,
            LlmCallIndex = llmCallIndex,
        };

    private static TimelineMarkerItem Marker(string rowId, DateTimeOffset start) => new()
    {
        RowId = rowId,
        Title = rowId,
        Lane = TimelineLane.Model,
        Start = start,
        IsPartial = false,
        IsError = false,
        TurnKey = "turn-1",
        TurnTitle = "Turn 1",
    };
}
