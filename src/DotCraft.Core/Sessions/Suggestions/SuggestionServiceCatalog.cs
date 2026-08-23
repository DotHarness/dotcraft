using DotCraft.Contributions;

namespace DotCraft.Sessions;

/// <summary>Tier-B target names of the auxiliary suggestion generators.</summary>
public static class SuggestionServiceNames
{
    /// <summary>The target name the built-in source-control summary generator registers under.</summary>
    public const string CommitMessageSuggest = "commit-message-suggest";

    /// <summary>The target name the built-in welcome suggestion generator registers under.</summary>
    public const string WelcomeSuggestions = "welcome-suggestions";
}

/// <summary>Registers the workspace runtime's suggestion generators as named contributions and reads them per call.</summary>
internal static class SuggestionServiceCatalog
{
    /// <summary>The order both built-ins are registered at.</summary>
    public const int BuiltInOrder = 100;

    /// <summary>Registers the supplied generators under their target names. The instances belong to the runtime, so disposing a handle only removes the contribution.</summary>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        ICommitMessageSuggester? commitMessageSuggest,
        IWelcomeSuggester? welcomeSuggestions,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using var batch = registry.BeginBatch();
        var handles = new List<IContributionHandle>(2);
        if (commitMessageSuggest is not null)
            handles.Add(Add(registry, registrar, commitMessageSuggest, SuggestionServiceNames.CommitMessageSuggest));
        if (welcomeSuggestions is not null)
            handles.Add(Add(registry, registrar, welcomeSuggestions, SuggestionServiceNames.WelcomeSuggestions));

        return handles;
    }

    /// <summary>Returns the effective generator: the contribution point's authority, falling back to the built-in when it is empty.</summary>
    /// <remarks>Both generators are invoked without a thread in hand, so the contribution point is addressed at workspace scope.</remarks>
    public static TContract Resolve<TContract>(IContributionView? contributions, TContract builtIn)
        where TContract : class, IContributionContract
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        return ContributionRead.Authority(contributions?.Resolve<TContract>(), builtIn);
    }

    private static IContributionHandle Add<TContract>(
        IContributionRegistry registry,
        IContributionRegistrar? registrar,
        TContract generator,
        string targetName)
        where TContract : class, IContributionContract
    {
        var options = new ContributionOptions(Order: BuiltInOrder)
        {
            TargetName = targetName,
            OwnsContribution = false
        };
        return registrar is null ? registry.Add(generator, options) : registrar.Add(generator, options);
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

/// <summary>Resolves the effective welcome suggestion generator per call, on the same terms as <see cref="ContributedCommitMessageSuggestService"/>.</summary>
internal sealed class ContributedWelcomeSuggestionService(
    IContributionView contributions,
    IWelcomeSuggester builtIn) : IWelcomeSuggester
{
    private IWelcomeSuggester Effective => SuggestionServiceCatalog.Resolve(contributions, builtIn);

    /// <inheritdoc />
    public Task<WelcomeSuggestionSnapshot> SuggestAsync(
        WelcomeSuggestionRequest parameters,
        CancellationToken cancellationToken = default) =>
        Effective.SuggestAsync(parameters, cancellationToken);

    /// <inheritdoc />
    public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null) =>
        Effective.ScheduleRefresh(workspacePath, triggerThreadId);

    /// <inheritdoc />
    public void ClearWorkspaceCache(string workspacePath) =>
        Effective.ClearWorkspaceCache(workspacePath);
}
