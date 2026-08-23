using Acme.ReviewCore.Api;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using SubAgentRunResult = DotCraft.Agents.SubAgentRunResult;

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

/// <summary>A Tier-B replacement of the built-in welcome suggestion generator.</summary>
internal sealed class ReviewWelcomeSuggester : IWelcomeSuggester
{
    /// <summary>The source name this generator stamps its snapshots with.</summary>
    internal const string SourceName = "acme.review-core";

    /// <inheritdoc />
    public Task<WelcomeSuggestionSnapshot> SuggestAsync(
        WelcomeSuggestionRequest parameters,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WelcomeSuggestionSnapshot
        {
            Source = SourceName,
            GeneratedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new WelcomeSuggestion
                {
                    Title = "Review the working tree",
                    Prompt = "Review my uncommitted changes against the review checklist.",
                    Reason = "The review plugin is active."
                }
            ]
        });

    /// <inheritdoc />
    public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
    {
    }

    /// <inheritdoc />
    public void ClearWorkspaceCache(string workspacePath)
    {
    }
}

/// <summary>Adds one SubAgent runtime alongside the Host's own, with the profile it ships as a default.</summary>
/// <remarks>Host-served runtime types win a name collision, so a contribution adds a type and never shadows one.</remarks>
internal sealed class ReviewSubAgentRuntimeSource : ISubAgentRuntimeSource
{
    /// <summary>The runtime type a profile references to reach this runtime.</summary>
    internal const string RuntimeTypeName = "acme-review-pass";

    /// <summary>The profile this runtime ships as a default.</summary>
    internal const string ProfileName = "review-pass";

    /// <inheritdoc />
    public ISubAgentRuntime Runtime { get; } = new ReviewSubAgentRuntime();

    /// <inheritdoc />
    public IReadOnlyList<SubAgentProfile> Profiles { get; } =
    [
        new SubAgentProfile
        {
            Name = ProfileName,
            Runtime = RuntimeTypeName,
            WorkingDirectoryMode = "workspace"
        }
    ];
}

/// <summary>A deterministic stand-in runtime, so the sample profile runs without a second process.</summary>
internal sealed class ReviewSubAgentRuntime : ISubAgentRuntime
{
    /// <inheritdoc />
    public string RuntimeType => ReviewSubAgentRuntimeSource.RuntimeTypeName;

    /// <inheritdoc />
    public Task<SubAgentSessionHandle> CreateSessionAsync(
        SubAgentProfile profile,
        SubAgentLaunchContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

    /// <inheritdoc />
    public Task<SubAgentRunResult> RunAsync(
        SubAgentSessionHandle session,
        SubAgentTaskRequest request,
        ISubAgentEventSink sink,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubAgentRunResult { Text = $"Reviewed: {request.Task}" });

    /// <inheritdoc />
    public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
