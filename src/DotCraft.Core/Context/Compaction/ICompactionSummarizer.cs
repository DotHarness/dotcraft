using DotCraft.Contributions;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>How much of the history one summary replaces.</summary>
public enum CompactionSummaryScope
{
    /// <summary>Summarize an older prefix and keep a recent tail verbatim behind the summary.</summary>
    Partial,

    /// <summary>Summarize the whole history; nothing is kept verbatim.</summary>
    Full
}

/// <summary>The replacement summary, and the messages that stay verbatim behind it.</summary>
public sealed record CompactionSummary(
    string FormattedSummary,
    IReadOnlyList<ChatMessage> PreservedTail);

/// <summary>A summary, or a machine-readable reason why none is available.</summary>
public sealed record CompactionSummaryAttempt(CompactionSummary? Result, string? Reason);

/// <summary>The per-pipeline compactors the built-in summarizer delegates to; only the compaction pipeline can supply them.</summary>
internal sealed record CompactionSummaryHostInputs(PartialCompactor Partial, FullCompactor Full);

/// <summary>The history the kernel has decided to compact, and the shape of summary it is asking for.</summary>
public sealed record CompactionSummaryRequest(
    CompactionSummaryScope Scope,
    IReadOnlyList<ChatMessage> History,
    string ThreadId)
{
    /// <summary>Gets the captured request shape a summarizer may fork from, or <see langword="null"/> when the turn left none.</summary>
    public PromptRequestSnapshot? Snapshot { get; init; }

    /// <summary>Gets the tools to advertise to the summary call when the snapshot carries none.</summary>
    public IReadOnlyList<AITool>? FallbackTools { get; init; }

    /// <summary>Carries the kernel's own compactors; only the compaction pipeline can supply them.</summary>
    internal CompactionSummaryHostInputs? Host { get; init; }

    internal CompactionSummaryHostInputs RequireHost() => Host ?? throw new InvalidOperationException(
        "This compaction summary request was not created by the compaction pipeline and carries no "
        + $"built-in compactors, so the '{CompactionSummarizerCatalog.BuiltInTargetName}' target has nothing to run.");
}

/// <summary>Produces the summary that replaces a thread's history once the kernel has decided to compact it.</summary>
/// <remarks>
/// The kernel keeps every decision around the summary — thresholds, the microcompact pass, failure tracking,
/// token accounting and the history replacement itself. A contribution decides only what the summary says and
/// what stays verbatim behind it. On a <see cref="CompactionSummaryScope.Partial"/> request the reasons
/// <c>empty_history</c> and <c>no_summarizable_prefix</c> leave the history untouched; every other reason,
/// and every exception, fails the attempt.
/// </remarks>
public interface ICompactionSummarizer : IContributionContract
{
    /// <summary>Summarizes the requested scope, or reports why no summary is available.</summary>
    Task<CompactionSummaryAttempt> SummarizeAsync(
        CompactionSummaryRequest request,
        CancellationToken cancellationToken);
}
