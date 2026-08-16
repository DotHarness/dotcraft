using System.Globalization;
using DotCraft.Tracing;

namespace DotCraft.TraceViewer.ViewModels;

internal static class TrajectoryProjection
{
    internal static (string StartId, string EndId)? ResolveRange(
        IReadOnlyList<TimelineMarkerItem> markers,
        TimelineScaleMode scaleMode,
        double startRatio,
        double endRatio)
    {
        if (markers.Count == 0)
            return null;

        var ordered = markers
            .Select((item, index) => (Item: item, Index: index))
            .OrderBy(entry => entry.Item.Start)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToArray();
        if (scaleMode == TimelineScaleMode.Sequence)
        {
            var startIndex = Math.Clamp((int)Math.Floor(startRatio * ordered.Length), 0, ordered.Length - 1);
            var endIndex = Math.Clamp((int)Math.Ceiling(endRatio * ordered.Length) - 1, startIndex, ordered.Length - 1);
            return (ordered[startIndex].RowId, ordered[endIndex].RowId);
        }

        var minimum = ordered.Min(item => item.Start);
        var maximum = ordered.Max(item => item.End ?? item.Start);
        var duration = maximum - minimum;
        var selectedStart = minimum + TimeSpan.FromTicks((long)(duration.Ticks * Math.Clamp(startRatio, 0, 1)));
        var selectedEnd = minimum + TimeSpan.FromTicks((long)(duration.Ticks * Math.Clamp(endRatio, 0, 1)));
        var intersections = ordered
            .Select((item, index) => (Item: item, Index: index))
            .Where(entry => entry.Item.Start <= selectedEnd && (entry.Item.End ?? entry.Item.Start) >= selectedStart)
            .Select(entry => entry.Index)
            .ToArray();
        if (intersections.Length > 0)
            return (ordered[intersections[0]].RowId, ordered[intersections[^1]].RowId);

        var before = Array.FindLastIndex(ordered, item => item.Start <= selectedStart);
        var after = Array.FindIndex(ordered, item => item.Start >= selectedEnd);
        var rangeStart = before >= 0 ? before : 0;
        var rangeEnd = after >= 0 ? after : ordered.Length - 1;
        rangeEnd = Math.Max(rangeStart, rangeEnd);
        return (ordered[rangeStart].RowId, ordered[rangeEnd].RowId);
    }

    public static TrajectoryProjectionResult Project(IReadOnlyList<TraceEvent> events, bool beginsMidSession)
    {
        if (events.Count == 0)
            return new([], [], []);

        var completions = events
            .Where(static e => e.Type == TraceEventType.ToolCallCompleted && !string.IsNullOrWhiteSpace(e.CallId))
            .GroupBy(static e => e.CallId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        var starts = events
            .Where(static e => e.Type == TraceEventType.ToolCallStarted && !string.IsNullOrWhiteSpace(e.CallId))
            .GroupBy(static e => e.CallId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var rawGroups = SplitTurns(events, beginsMidSession);
        var turns = new List<TurnGroupItem>(rawGroups.Count);
        var allRows = new List<EventRowItem>(events.Count);
        var timeline = new List<TimelineMarkerItem>();

        for (var turnIndex = 0; turnIndex < rawGroups.Count; turnIndex++)
        {
            var rawGroup = rawGroups[turnIndex];
            var turnKey = $"turn:{turnIndex}";
            var rows = new List<EventRowItem>();
            var callNumber = 0;
            int? activeCall = null;
            var callCompleted = false;
            foreach (var traceEvent in rawGroup.Events)
            {
                if (traceEvent.Type == TraceEventType.ToolCallStarted
                    && traceEvent.CallId is { } startId
                    && completions.ContainsKey(startId))
                {
                    continue;
                }

                starts.TryGetValue(traceEvent.CallId ?? string.Empty, out var toolStart);
                if (StartsModelCall(traceEvent) && (activeCall is null || callCompleted))
                {
                    callNumber++;
                    activeCall = traceEvent.LlmCallIndex ?? traceEvent.RequestIndex ?? callNumber;
                    callCompleted = false;
                }

                var row = EventPresentationFactory.Create(traceEvent, traceEvent.Type == TraceEventType.ToolCallCompleted ? toolStart : null);
                if (activeCall is not null && BelongsToModelCall(traceEvent))
                    row.ModelCallIndex = activeCall;
                rows.Add(row);
                allRows.Add(row);
                if (IsTimelineEvent(traceEvent))
                {
                    timeline.Add(ToMarker(
                        row,
                        turnKey,
                        rawGroup.Title,
                        toolStart is null && traceEvent.Type == TraceEventType.ToolCallCompleted));
                }

                if (traceEvent.Type == TraceEventType.ResponseTerminal)
                    callCompleted = true;
            }

            if (rows.Count == 0)
                continue;

            var turn = new TurnGroupItem
            {
                Key = turnKey,
                Title = rawGroup.Title,
                Summary = BuildTurnSummary(rawGroup.Events),
            };
            foreach (var row in rows)
                turn.Add(row);
            turns.Add(turn);

        }

        return new(turns, allRows, timeline);
    }

    private static List<RawTurnGroup> SplitTurns(IReadOnlyList<TraceEvent> events, bool beginsMidSession)
    {
        var groups = new List<RawTurnGroup>();
        var current = new List<TraceEvent>();
        var first = true;
        foreach (var traceEvent in events)
        {
            current.Add(traceEvent);
            if (traceEvent.Type != TraceEventType.TurnCompleted)
                continue;

            groups.Add(CreateRawGroup(current, groups.Count + 1, beginsMidSession && first));
            current = [];
            first = false;
        }

        if (current.Count > 0)
            groups.Add(CreateRawGroup(current, groups.Count + 1, beginsMidSession && first, trailingPartial: true));
        return groups;
    }

    private static RawTurnGroup CreateRawGroup(
        List<TraceEvent> events,
        int turnNumber,
        bool beginsPartial,
        bool trailingPartial = false)
    {
        var title = $"Turn {turnNumber.ToString(CultureInfo.CurrentCulture)} · {events[0].Timestamp.ToLocalTime():HH:mm:ss}";
        var partial = beginsPartial || trailingPartial && events.All(static e => e.Type != TraceEventType.TurnCompleted);
        return new RawTurnGroup(events, partial ? title + " · partial" : title, partial);
    }

    private static string BuildTurnSummary(IReadOnlyList<TraceEvent> events)
    {
        var calls = events.Count(static e => e.Type == TraceEventType.ResponseTerminal);
        var tools = events.Count(static e => e.Type == TraceEventType.ToolCallCompleted);
        var tokens = events.Where(static e => e.Type == TraceEventType.TokenUsage).Sum(static e => e.TotalTokens ?? 0);
        var duration = events.LastOrDefault(static e => e.Type == TraceEventType.TurnCompleted)?.DurationMs;
        var parts = new List<string>();
        if (duration is not null)
            parts.Add($"{duration:N0} ms");
        if (calls > 0)
            parts.Add($"{calls:N0} model call{(calls == 1 ? string.Empty : "s")}");
        if (tools > 0)
            parts.Add($"{tools:N0} tool{(tools == 1 ? string.Empty : "s")}");
        if (tokens > 0)
            parts.Add($"{tokens:N0} tokens");
        return parts.Count == 0 ? "Recorded activity" : string.Join(" · ", parts);
    }

    private static TimelineMarkerItem ToMarker(
        EventRowItem row,
        string turnKey,
        string turnTitle,
        bool partial)
    {
        var traceEvent = row.SourceEvents[^1];
        DateTimeOffset? end = null;
        var start = traceEvent.Timestamp;
        if (traceEvent.DurationMs is { } duration
            && (traceEvent.Type == TraceEventType.ToolCallCompleted || IsStreamAttempt(traceEvent)))
        {
            end = traceEvent.Timestamp;
            start = row.SourceEvents.Count > 1 ? row.SourceEvents[0].Timestamp : traceEvent.Timestamp.AddMilliseconds(-duration);
        }

        return new TimelineMarkerItem
        {
            RowId = row.Id,
            Title = row.Title,
            Lane = row.Lane,
            Start = start,
            End = end,
            IsPartial = partial,
            IsError = row.IsError,
            TurnKey = turnKey,
            TurnTitle = turnTitle,
        };
    }

    private static bool IsStreamAttempt(TraceEvent traceEvent) =>
        traceEvent.Type == TraceEventType.ProviderResponseDiagnostic
        && TraceMetadata.Parse(traceEvent.MetadataJson).Get("eventType") == "stream_attempt";

    private static bool StartsModelCall(TraceEvent traceEvent) => traceEvent.Type is
        TraceEventType.Thinking or
        TraceEventType.Response or
        TraceEventType.ProviderResponseDiagnostic or
        TraceEventType.PromptCacheRequestShape;

    private static bool BelongsToModelCall(TraceEvent traceEvent) => traceEvent.Type is
        TraceEventType.Thinking or
        TraceEventType.Response or
        TraceEventType.ResponseTerminal or
        TraceEventType.TokenUsage or
        TraceEventType.ToolCallStarted or
        TraceEventType.ToolCallCompleted or
        TraceEventType.ProviderError or
        TraceEventType.ProviderResponseDiagnostic or
        TraceEventType.PromptCachePoint or
        TraceEventType.PromptCacheDiagnostic or
        TraceEventType.PromptCacheRequestShape;

    private static bool IsTimelineEvent(TraceEvent traceEvent) => traceEvent.Type switch
    {
        TraceEventType.Request or
        TraceEventType.Thinking or
        TraceEventType.Response or
        TraceEventType.ResponseTerminal or
        TraceEventType.TokenUsage or
        TraceEventType.ToolCallStarted or
        TraceEventType.ToolCallCompleted => true,
        TraceEventType.ProviderResponseDiagnostic => IsStreamAttempt(traceEvent),
        TraceEventType.Error or
        TraceEventType.ToolInjection or
        TraceEventType.DeferredToolLoading or
        TraceEventType.SkillReferenced or
        TraceEventType.ContextCompaction or
        TraceEventType.ThreadRollback => true,
        _ => false,
    };

    private sealed record RawTurnGroup(IReadOnlyList<TraceEvent> Events, string Title, bool IsPartial);
}
