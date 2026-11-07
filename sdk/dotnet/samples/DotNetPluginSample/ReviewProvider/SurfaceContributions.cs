using DotCraft.Tracing;

namespace Acme.ReviewCore;

/// <summary>Observes trace events after the store has accepted them; a sink can neither alter nor suppress one.</summary>
/// <remarks>Nothing is recorded at all while <c>Tracing.Enabled</c> is false, so a sink is silent with tracing off.</remarks>
internal sealed class ReviewTraceSink(ReviewJournal journal) : ITraceSink
{
    /// <summary>The prefix every observed trace event is journalled under.</summary>
    internal const string LinePrefix = "trace event";

    /// <inheritdoc />
    public string Name => "acme-review-trace";

    /// <inheritdoc />
    public void Record(TraceEvent evt) => journal.Write($"{LinePrefix} {evt.Type}");
}
