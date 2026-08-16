using System.Text.Json.Serialization;

namespace DotCraft.TraceViewer.Analysis;

public enum TraceReviewStatus
{
    NotAnalyzed,
    Analyzing,
    Ready,
    TraceUpdated,
    Cancelled,
    Failed,
    ProviderUnavailable
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum TraceFindingSeverity { Major, Minor, Suggestion }

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum TraceFindingBasis { Confirmed, Inferred }

internal sealed record TraceEvidenceReference(string EventId, string? EndEventId, string Label);

internal sealed class TraceEvidenceSubmission
{
    public required string EventId { get; init; }
    public string? EndEventId { get; init; }
    public required string Label { get; init; }
}

internal sealed record TraceFindingSubmission(
    string Id,
    string Severity,
    string Dimension,
    string Title,
    string Body,
    string Impact,
    string Recommendation,
    string Basis,
    IReadOnlyList<TraceEvidenceSubmission> Evidence);

internal sealed record TraceFinding(
    string Id,
    TraceFindingSeverity Severity,
    string Dimension,
    string Title,
    string Body,
    string Impact,
    string Recommendation,
    TraceFindingBasis Basis,
    IReadOnlyList<TraceEvidenceReference> Evidence);

internal sealed record TraceReview(
    int SchemaVersion,
    string SessionKey,
    string Revision,
    DateTimeOffset GeneratedAt,
    string ModelId,
    string Summary,
    IReadOnlyList<TraceFinding> Findings,
    string AnalystThreadId);

internal sealed record TraceConversationMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp);

internal sealed record StoredTraceReview(
    TraceReview Review,
    TraceSnapshot Snapshot,
    IReadOnlyList<TraceConversationMessage> Conversation);

internal sealed record TraceSnapshot(
    string WorkspacePath,
    string SessionKey,
    string Revision,
    DateTimeOffset LastActivityAt,
    IReadOnlyList<Tracing.TraceEvent> Events)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, Tracing.TraceEvent> EventsById { get; } =
        Events.ToDictionary(item => item.Id, StringComparer.Ordinal);
}
