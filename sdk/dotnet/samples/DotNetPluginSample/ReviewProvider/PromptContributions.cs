using Acme.ReviewCore.Api;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Contributions;

namespace Acme.ReviewCore;

/// <summary>A Tier-A prompt section, slotted between two built-in sections, sized by the plugin's own settings.</summary>
internal sealed class ReviewChecklistSection(IReviewService service, ReviewSettings settings) : ISystemPromptSection
{
    /// <inheritdoc />
    public string Name => "review-checklist";

    /// <inheritdoc />
    public string? GetContent(SystemPromptSectionContext context) =>
        string.Join(
            Environment.NewLine,
            [
                "## Review checklist",
                .. service.Checklist.Take(settings.ChecklistLimit).Select(static item => $"- {item}")
            ]);
}

/// <summary>A Tier-B replacement of a named built-in section, registered with <see cref="ContributionOptions.ReplaceTarget"/>.</summary>
internal sealed class ReviewResponseStyleSection(ReviewSettings settings) : ISystemPromptSection
{
    /// <inheritdoc />
    public string Name => "review-response-style";

    /// <inheritdoc />
    public string? GetContent(SystemPromptSectionContext context) =>
        $"""
        ## Review Response Style

        Answer as a reviewer in a {settings.Tone} tone: lead with the finding, then the evidence,
        then the suggested change.
        """;
}

/// <summary>A Tier-C takeover: it receives the whole default-assembled prompt and returns the final one.</summary>
/// <remarks>Only the last assembler of the resolved list is applied, so a takeover that neither out-orders nor
/// replaces its predecessor is simply inert.</remarks>
internal sealed class ReviewPromptAssembler : ISystemPromptAssembler
{
    /// <summary>The trailer the assembled prompt ends with while this plugin is active.</summary>
    internal const string Trailer = "<!-- assembled by acme.review-core -->";

    /// <inheritdoc />
    public string Assemble(string prompt, SystemPromptSectionContext context) =>
        $"{prompt}\n\n{Trailer}";
}

/// <summary>A channel-style context contribution surfaced through the chat-context section.</summary>
internal sealed class ReviewChatContext(IReviewService service) : IChatContextProvider
{
    /// <inheritdoc />
    /// <remarks>Stable within a session, so the provider-visible prompt prefix stays cacheable.</remarks>
    public string? GetSystemPromptSection() =>
        $"Review checklist has {service.Checklist.Count} items; the review.summary Tool returns them.";

    /// <inheritdoc />
    public IEnumerable<string> GetRuntimeContextLines() => [];
}

/// <summary>A thread-scoped prompt page contributed through the thread-context section.</summary>
internal sealed class ReviewThreadContext : IThreadSystemPromptContextProvider
{
    /// <inheritdoc />
    public ContextPageKey ContextPageKey { get; } = new("plugin", "acme-review", "v1");

    /// <inheritdoc />
    public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
        "Review plugin is active for this thread.";
}

/// <summary>A pre-send context transform appended to the agent's provider list.</summary>
internal sealed class ReviewAgentContext(ReviewJournal journal)
    : AIContextProvider
{
    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        journal.Write("agent context invoked");
        return ValueTask.FromResult(new AIContext
        {
            Instructions = "Prefer review.summary when the user asks for a review of pasted text."
        });
    }
}
