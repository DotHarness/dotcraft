using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>What the sample bundle suite proves about one contribution point.</summary>
internal enum SampleContributionEvidence
{
    /// <summary>The sample contributes and the suite asserts the observable consequence.</summary>
    AssertedEffect,

    /// <summary>The sample contributes, but the kernel's only reader is unreachable from this harness.</summary>
    RegistrationOnly,

    /// <summary>An exported contract that is not a contribution point of its own.</summary>
    NotAContributionPoint
}

/// <summary>One row of the sample's declared coverage: the observable asserted, or why there is none.</summary>
internal sealed record SampleContributionPoint(
    Type Contract,
    SampleContributionEvidence Evidence,
    string Note);

/// <summary>
/// The sample bundles' declared coverage of the kernel's contribution point catalog, and the reflection
/// census that keeps the declaration honest.
/// </summary>
/// <remarks>Every contract <c>DotCraft.Core</c> exports must appear here, so adding a contribution point
/// fails <c>DotnetPluginSampleBundleTests</c> until it is dispositioned.</remarks>
internal static class DotnetPluginSampleCoverage
{
    /// <summary>One row per exported contribution contract, in catalog order.</summary>
    internal static IReadOnlyList<SampleContributionPoint> Points { get; } =
    [
        Effect<ISystemPromptSection>("The Tier-A section and the Tier-B replacement's text in the assembled prompt; the replaced built-in's text absent."),
        Effect<ISystemPromptAssembler>("The assembled prompt ends with the takeover's trailer."),
        Effect<IChatContextProvider>("The provider's line inside the chat-context section of the assembled prompt."),
        Effect<IThreadSystemPromptContextProvider>("The provider's page inside the thread-context section of the assembled prompt."),
        Effect<ICompactionSummarizer>("The summary CompactionSummarizerCatalog.Resolve returns, and the built-in returning once the plugin stops."),
        Effect<ICompactableToolPolicy>("CompactableToolPolicyCatalog.IsCompactable answering true for a plugin Tool the built-in allow-list defers on."),
        Effect<IChatMiddleware>("A model call driven through the folded pipeline reaching the inner client and being observed on the way."),
        Effect<IToolSource>("Both bundles' Tools dispatching to a result through ToolDispatcher."),
        Effect<IToolPolicyEvaluator>("A dispatch denied with the plugin's own code while its settings freeze Tools."),
        Effect<IToolApprovalEvaluator>("A dispatch denied without the acknowledgement argument and allowed with it."),
        Effect<IToolInvocationRecorder>("The plugin's journal line for a dispatch the Host's own recorder also recorded."),
        Effect<IToolResultNormalizer>("The plugin's stamp on the content the Host's normalizer produced."),
        Effect<IToolRestriction>("A masked Tool absent from the assembled registrations and NotFound at dispatch; a rewritten description on the one kept."),
        Effect<IThreadRuntimeSignalContributor>("The plugin's journal line for a signal published through ThreadRuntimeSignalDispatcher."),
        Effect<ICommitMessageSuggester>("The message ContributedCommitMessageSuggestService returns while the replacement is registered."),
        Effect<IWelcomeSuggester>("The snapshot ContributedWelcomeSuggestionService returns while the replacement is registered."),
        Effect<ISubAgentRuntimeSource>("The contributed runtime type and profile in the catalog every SubAgent site reads."),
        Effect<ICodeCommand>("The command in CommandContributions.List, and its expansion through CommandContributions.Expand."),
        Effect<ITraceSink>("The plugin's journal line for an event recorded on TraceStore."),

        RegistrationOnly<IAgentContextSource>(
            "AgentContextProviderComposer is internal to DotCraft.Core and AgentContextRequest carries the "
            + "built-in memory provider internally, so no caller outside the kernel can compose an agent's provider list."),
        RegistrationOnly<IThreadLifecycleContributor>(
            "The only reader is SessionService's private lifecycle coordinator, which needs a whole workspace "
            + "session runtime the plugin harness does not build."),
        RegistrationOnly<ITurnLifecycleContributor>(
            "Read by the same private coordinator as IThreadLifecycleContributor, and only a real turn fires it."),

        new SampleContributionPoint(
            typeof(IThreadLifecycleObserver),
            SampleContributionEvidence.NotAContributionPoint,
            "The dependency-injection form of IThreadLifecycleContributor, dispatched through the same contribution point.")
    ];

    /// <summary>Every contribution contract <c>DotCraft.Core</c> exports, excluding the marker itself.</summary>
    internal static IReadOnlyList<Type> KernelContracts() =>
    [
        .. typeof(IContributionContract).Assembly.GetExportedTypes()
            .Where(static type => type.IsInterface
                && type != typeof(IContributionContract)
                && typeof(IContributionContract).IsAssignableFrom(type))
    ];

    private static SampleContributionPoint Effect<TContract>(string observable)
        where TContract : class, IContributionContract =>
        new(typeof(TContract), SampleContributionEvidence.AssertedEffect, observable);

    private static SampleContributionPoint RegistrationOnly<TContract>(string reason)
        where TContract : class, IContributionContract =>
        new(typeof(TContract), SampleContributionEvidence.RegistrationOnly, reason);
}

/// <summary>Records what a run actually asserted about each contribution point, so the declared coverage
/// cannot drift away from the assertions that back it.</summary>
internal sealed class SampleCoverageLedger
{
    private readonly HashSet<Type> _proved = [];
    private readonly HashSet<Type> _registered = [];

    /// <summary>Marks the contribution point whose observable consequence was just asserted.</summary>
    internal void Prove<TContract>() where TContract : class, IContributionContract =>
        _proved.Add(typeof(TContract));

    /// <summary>Marks a contribution point the run could only check for a registration.</summary>
    internal void Registered<TContract>() where TContract : class, IContributionContract =>
        _registered.Add(typeof(TContract));

    /// <summary>Requires the run to have checked each contribution point exactly the way the table claims.</summary>
    internal void AssertMatchesDeclaredCoverage()
    {
        AssertSetsAgree(SampleContributionEvidence.AssertedEffect, _proved, "asserted an effect on");
        AssertSetsAgree(SampleContributionEvidence.RegistrationOnly, _registered, "checked a registration on");
    }

    private static void AssertSetsAgree(
        SampleContributionEvidence evidence,
        IReadOnlySet<Type> observed,
        string verb)
    {
        var declared = DotnetPluginSampleCoverage.Points
            .Where(point => point.Evidence == evidence)
            .Select(static point => point.Contract)
            .ToHashSet();

        var unbacked = Names(declared.Except(observed));
        Assert.True(
            unbacked.Length == 0,
            $"The coverage table lists these as {evidence}, but this run never {verb} them: {Join(unbacked)}");

        var undeclared = Names(observed.Except(declared));
        Assert.True(
            undeclared.Length == 0,
            $"This run {verb} contribution points the coverage table does not list as {evidence}: {Join(undeclared)}");
    }

    private static string[] Names(IEnumerable<Type> types) =>
        [.. types.Select(static type => type.Name).Order(StringComparer.Ordinal)];

    private static string Join(string[] names) => string.Join(", ", names);
}
