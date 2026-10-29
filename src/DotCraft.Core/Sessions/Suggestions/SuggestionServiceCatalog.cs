using DotCraft.Contributions;

namespace DotCraft.Sessions;

/// <summary>Tier-B target names of the auxiliary suggestion generators.</summary>
public static class SuggestionServiceNames
{
    /// <summary>The target name the built-in source-control summary generator registers under.</summary>
    public const string CommitMessageSuggest = "commit-message-suggest";
}

internal static class SuggestionServiceCatalog
{
    private const int BuiltInOrder = 100;

    /// <summary>The runtime owns the instance, so disposing the handle only removes the contribution.</summary>
    internal static IContributionHandle RegisterBuiltIn(
        IContributionRegistry registry,
        ICommitMessageSuggester commitMessageSuggest,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(commitMessageSuggest);

        var options = new ContributionOptions(Order: BuiltInOrder)
        {
            TargetName = SuggestionServiceNames.CommitMessageSuggest,
            OwnsContribution = false
        };
        return registrar is null
            ? registry.Add(commitMessageSuggest, options)
            : registrar.Add(commitMessageSuggest, options);
    }

    /// <remarks>Generators are invoked without a thread in hand, so the contribution point is addressed at workspace scope.</remarks>
    public static TContract Resolve<TContract>(IContributionView? contributions, TContract builtIn)
        where TContract : class, IContributionContract
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        return ContributionRead.Authority(contributions?.Resolve<TContract>(), builtIn);
    }
}

/// <summary>Resolves the effective source-control summary generator per call, so a replacement registered after a client connected is honored by it.</summary>
internal sealed class ContributedCommitMessageSuggestService(
    IContributionView contributions,
    ICommitMessageSuggester builtIn) : ICommitMessageSuggester
{
    private ICommitMessageSuggester Effective => SuggestionServiceCatalog.Resolve(contributions, builtIn);

    /// <inheritdoc />
    public Task<CommitMessageSuggestionResult> SuggestAsync(
        CommitMessageSuggestionRequest parameters,
        CancellationToken cancellationToken = default) =>
        Effective.SuggestAsync(parameters, cancellationToken);
}
