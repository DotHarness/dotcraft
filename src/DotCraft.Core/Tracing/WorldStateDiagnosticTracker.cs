using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Tracing;

/// <summary>
/// A step that emits no context is the steady state, so it still records an event. Without one,
/// "the model was told nothing" and "the section never ran" look identical after the fact.
/// </summary>
internal sealed class WorldStateDiagnosticTracker(TraceStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Record(
        string sessionKey,
        string turnId,
        string status,
        IReadOnlyList<string> changedSections,
        string? emitted,
        IReadOnlyList<string> unchangedSections,
        IReadOnlyList<WorldStateSectionSuppression> suppressedSections,
        string baselineSource) =>
        store.Record(new TraceEvent
        {
            Type = TraceEventType.WorldStateDiagnostic,
            SessionKey = sessionKey,
            Content = emitted,
            MetadataJson = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    status,
                    turnId,
                    baselineSource,
                    changedSections,
                    unchangedSections,
                    suppressedSections = suppressedSections
                        .Select(static section => new { id = section.SectionId, reason = section.Reason })
                        .ToArray()
                },
                JsonOptions)
        });
}

internal readonly record struct WorldStateSectionSuppression(string SectionId, string Reason);
