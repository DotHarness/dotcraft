namespace DotCraft.Contributions;

/// <summary>Marker interface implemented by every contribution point contract.</summary>
public interface IContributionContract;

/// <summary>The registry-assigned identity of a contribution; the value doubles as the registry-global registration order.</summary>
public readonly record struct ContributionId(long Value)
{
    /// <summary>Returns a stable diagnostic representation such as <c>contribution:42</c>.</summary>
    public override string ToString() => $"contribution:{Value}";
}

/// <summary>One entry of a resolved contribution point: the contribution together with the provenance it was registered with.</summary>
public readonly record struct ContributionEntry<TContract>(
    TContract Contribution,
    ContributionId Id,
    ContributionOrigin Origin,
    int Order)
    where TContract : class, IContributionContract
{
    /// <summary>Gets the Tier-B target name this contribution is registered under, when it is a named default.</summary>
    public string? TargetName { get; init; }

    /// <summary>Gets the Tier-B target this contribution replaces, when it is a replacement.</summary>
    public string? ReplaceTarget { get; init; }

    /// <summary>Determines whether this entry occupies the given Tier-B slot, as the named default or as its replacement.</summary>
    public bool Occupies(string targetName) =>
        string.Equals(TargetName, targetName, StringComparison.Ordinal)
        || string.Equals(ReplaceTarget, targetName, StringComparison.Ordinal);
}

/// <summary>The lifetime scope a contribution applies to.</summary>
public enum ContributionScope
{
    /// <summary>The contribution applies to every thread in the workspace.</summary>
    Workspace,

    /// <summary>The contribution applies only to the thread named by <see cref="ContributionOptions.ThreadId"/>.</summary>
    Thread
}

/// <summary>Registration options for a single contribution.</summary>
/// <param name="Order">
/// The ordering key within one contribution point, lowest first. It orders the resolved list and nothing else;
/// Tier-B replacement conflicts are settled by scope and registration order.
/// </param>
public sealed record ContributionOptions(
    ContributionScope Scope = ContributionScope.Workspace,
    string? ThreadId = null,
    int Order = 0,
    string? ReplaceTarget = null)
{
    /// <summary>Gets the shared default options: workspace scope, order zero, no replacement.</summary>
    public static ContributionOptions Default { get; } = new();

    /// <summary>Gets the name, unique within the contribution point, this contribution is registered under as a Tier-B replacement target.</summary>
    public string? TargetName { get; init; }

    /// <summary>Gets whether disposing the handle also disposes the contribution instance when it implements <see cref="IDisposable"/>.</summary>
    public bool OwnsContribution { get; init; } = true;

    /// <summary>Creates thread-scoped options for the given thread.</summary>
    public static ContributionOptions ForThread(string threadId, int order = 0) =>
        new(ContributionScope.Thread, threadId, order);
}

/// <summary>The disposable handle returned by every registration; disposal is idempotent and never throws outward.</summary>
public interface IContributionHandle : IDisposable
{
    /// <summary>Gets the contribution identity.</summary>
    ContributionId Id { get; }
}
