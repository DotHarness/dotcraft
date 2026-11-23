using Acme.ReviewCore.Api;
using DotCraft.Sessions;

namespace Acme.ReviewCore;

/// <summary>A Tier-B replacement of the built-in source-control summary generator, read per call.</summary>
/// <remarks>The Host owns the built-in instance, so disposing this contribution's handle only removes the
/// contribution and the built-in becomes effective again.</remarks>
internal sealed class ReviewCommitMessageSuggester(IReviewService service) : ICommitMessageSuggester
{
    /// <summary>The first line of every message this generator produces.</summary>
    internal const string Subject = "review: summarize the staged change";

    /// <inheritdoc />
    public Task<CommitMessageSuggestionResult> SuggestAsync(
        CommitMessageSuggestionRequest parameters,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CommitMessageSuggestionResult(string.Join(
            Environment.NewLine,
            [Subject, string.Empty, .. service.Checklist.Select(static item => $"- {item}")])));
}
