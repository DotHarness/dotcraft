using Acme.ReviewCore.Api;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace Acme.ReviewCore;

/// <summary>A Tier-B replacement of the built-in <c>local-summary</c> summarizer: it decides what the summary
/// says, and the kernel keeps every decision around it.</summary>
/// <remarks>One contract covers both shapes, because <see cref="CompactionSummaryRequest.Scope"/> travels on the
/// request. Declining a <see cref="CompactionSummaryScope.Partial"/> request with <c>no_summarizable_prefix</c>
/// leaves the history untouched; the kernel then asks for the <see cref="CompactionSummaryScope.Full"/> one.</remarks>
internal sealed class ReviewCompactionSummarizer(IReviewService service, ReviewJournal journal)
    : ICompactionSummarizer
{
    /// <summary>The first line of every summary this plugin produces.</summary>
    internal const string Heading = "## Review summary";

    /// <inheritdoc />
    public Task<CompactionSummaryAttempt> SummarizeAsync(
        CompactionSummaryRequest request,
        CancellationToken cancellationToken)
    {
        journal.Write($"compaction summary requested scope={request.Scope}");
        if (request.History.Count == 0)
            return Task.FromResult(new CompactionSummaryAttempt(null, "empty_history"));

        // Interface-targeted collection expressions use a compiler-generated wrapper in this assembly.
        // Return a BCL array so the Host never retains a plugin-defined collection type.
        IReadOnlyList<ChatMessage> preserved = request.Scope == CompactionSummaryScope.Full
            ? Array.Empty<ChatMessage>()
            : new[] { request.History[^1] };
        var summary = string.Join(
            Environment.NewLine,
            [
                Heading,
                $"{request.History.Count - preserved.Count} messages replaced against {service.Checklist.Count} checklist items."
            ]);
        return Task.FromResult(new CompactionSummaryAttempt(new CompactionSummary(summary, preserved), null));
    }
}

/// <summary>Lets the microcompact pass clear this plugin's own stale Tool results, which the built-in
/// allow-list defers on rather than denying.</summary>
internal sealed class ReviewCompactableToolPolicy : ICompactableToolPolicy
{
    /// <inheritdoc />
    public string Name => "acme-review-tools";

    /// <inheritdoc />
    public bool? IsCompactable(string toolName) =>
        toolName.StartsWith("review.", StringComparison.Ordinal) ? true : null;
}
