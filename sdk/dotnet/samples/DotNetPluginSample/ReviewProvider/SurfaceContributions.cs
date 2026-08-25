using Acme.ReviewCore.Api;
using DotCraft.Commands.Core;
using DotCraft.Tracing;

namespace Acme.ReviewCore;

/// <summary>A code-backed slash command, listed and invoked wherever a markdown custom command is.</summary>
/// <remarks>Expansion only: it returns the text the turn runs on, never a direct client reply. Returning
/// <see langword="null"/> declines so the next contribution may answer.</remarks>
internal sealed class ReviewCommand(IReviewService service) : ICodeCommand
{
    /// <inheritdoc />
    public string Name => "review";

    /// <inheritdoc />
    public string Description => "Review the given text against the review-core checklist.";

    /// <inheritdoc />
    public IReadOnlyList<string> Aliases => ["rv"];

    /// <inheritdoc />
    public string? Expand(CommandInvocation invocation) =>
        string.IsNullOrWhiteSpace(invocation.Arguments)
            ? null
            : string.Join(
                Environment.NewLine,
                [
                    "Review the following against the checklist:",
                    invocation.Arguments,
                    string.Empty,
                    .. service.Checklist.Select(static item => $"- {item}")
                ]);
}

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
