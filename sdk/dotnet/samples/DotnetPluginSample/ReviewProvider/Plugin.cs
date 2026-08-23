using Acme.ReviewCore.Api;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;

namespace Acme.ReviewCore;

/// <summary>The sample provider plugin: one contribution on every contribution point the Host opens to plugins.</summary>
public sealed class Plugin : IDotCraftPlugin, IDisposable
{
    private ReviewJournal? _journal;

    /// <inheritdoc />
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        // Teardown revokes contribution handles before in-flight Tool calls drain, so shared state
        // belongs on the generation lifetime, not on the contributions that write to it.
        var journal = new ReviewJournal(context.DataRoot);
        context.Lifetime.Own(journal);
        _journal = journal;
        journal.Write("plugin activated");

        var service = new ReviewService();
        context.Exports.Add<IReviewService>(service);

        var settings = new ReviewSettings(context);
        journal.Write("settings loaded");

        AddPromptContributions(context, service, settings, journal);
        AddPipelineContributions(context, journal);
        AddToolContributions(context, service, settings, journal);
        AddSessionContributions(context, service, journal);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _journal?.Write("entry disposed");

    private static void AddPromptContributions(
        IPluginActivationContext context,
        ReviewService service,
        ReviewSettings settings,
        ReviewJournal journal)
    {
        var contributions = context.Contributions;

        // A Tier-A addition slots between built-ins; a Tier-B replacement shadows a named built-in
        // for as long as its handle lives, and returning null from one suppresses the built-in outright.
        contributions.Add<ISystemPromptSection>(
            new ReviewChecklistSection(service, settings),
            new ContributionOptions(Order: 1250));
        contributions.Add<ISystemPromptSection>(
            new ReviewResponseStyleSection(settings),
            new ContributionOptions(ReplaceTarget: SystemPromptSectionNames.ResponseStyle));

        // Tier C receives the whole default-assembled prompt and returns the final one.
        contributions.Add<ISystemPromptAssembler>(new ReviewPromptAssembler());

        contributions.Add<IChatContextProvider>(new ReviewChatContext(service));
        contributions.Add<IThreadSystemPromptContextProvider>(new ReviewThreadContext());

        contributions.Add<ICompactionSummarizer>(
            new ReviewCompactionSummarizer(service, journal),
            new ContributionOptions(ReplaceTarget: CompactionSummarizerCatalog.BuiltInTargetName));
        contributions.Add<ICompactableToolPolicy>(new ReviewCompactableToolPolicy());
    }

    private static void AddPipelineContributions(IPluginActivationContext context, ReviewJournal journal)
    {
        var contributions = context.Contributions;

        // The agent-context contribution is a factory because a provider captures its host identity
        // when it is created. Ordered behind the built-in memory entry so it sees the finished prompt.
        contributions.Add<IAgentContextSource>(
            new AgentContextSource(_ => new ReviewAgentContext(journal)),
            new ContributionOptions(Order: 200));

        contributions.Add<IChatMiddleware>(
            new ReviewObserverMiddleware(journal),
            new ContributionOptions(Order: 50));
    }

    private static void AddToolContributions(
        IPluginActivationContext context,
        ReviewService service,
        ReviewSettings settings,
        ReviewJournal journal)
    {
        var contributions = context.Contributions;

        // One source is one contribution, so a plugin can revoke a group of Tools on its own.
        contributions.Add<IToolSource>(new SummaryTool(service, journal));
        contributions.Add<IToolSource>(new PublishTool(journal));

        contributions.Add<IToolPolicyEvaluator>(
            new ReviewInputLengthPolicy(settings),
            new ContributionOptions(Order: 500));
        contributions.Add<IToolApprovalEvaluator>(
            new ReviewPublishApproval(),
            new ContributionOptions(Order: 500));
        // Recorders are a fan-out, so this one joins the Host's own without replacing it.
        contributions.Add<IToolInvocationRecorder>(
            new ReviewToolRecorder(journal),
            new ContributionOptions(Order: 500));
        // Normalizers fold, so ordering behind the Host's own is what makes this one stamp its output.
        contributions.Add<IToolResultNormalizer>(
            new ReviewResultStamp(),
            new ContributionOptions(Order: 500));
        contributions.Add<IToolRestriction>(new ReviewToolRestriction(settings));
    }

    private static void AddSessionContributions(
        IPluginActivationContext context,
        ReviewService service,
        ReviewJournal journal)
    {
        var contributions = context.Contributions;

        contributions.Add<IThreadLifecycleContributor>(new ReviewThreadLifecycle(journal));
        contributions.Add<ITurnLifecycleContributor>(new ReviewTurnLifecycle(journal));
        contributions.Add<IThreadRuntimeSignalContributor>(new ReviewRuntimeSignals(journal));

        contributions.Add<ICommitMessageSuggester>(
            new ReviewCommitMessageSuggester(service),
            new ContributionOptions(ReplaceTarget: SuggestionServiceNames.CommitMessageSuggest));
        contributions.Add<IWelcomeSuggester>(
            new ReviewWelcomeSuggester(),
            new ContributionOptions(ReplaceTarget: SuggestionServiceNames.WelcomeSuggestions));
        contributions.Add<ISubAgentRuntimeSource>(new ReviewSubAgentRuntimeSource());

        contributions.Add<ICodeCommand>(new ReviewCommand(service));
        contributions.Add<ITraceSink>(new ReviewTraceSink(journal));
    }

}
