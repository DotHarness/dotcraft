namespace DotCraft.Contributions;

/// <summary>The read side of the contribution registry, resolved per use on hot paths rather than captured at construction time.</summary>
public interface IContributionView
{
    /// <summary>Resolves the ordered effective contributions of a contribution point, layering the thread-scoped set on top of the workspace-scoped one.</summary>
    IReadOnlyList<TContract> Resolve<TContract>(string? threadId = null) where TContract : class, IContributionContract;

    /// <summary>Resolves the same effective list <see cref="Resolve{TContract}"/> returns, element for element, with each contribution's identity, origin, and order attached.</summary>
    IReadOnlyList<ContributionEntry<TContract>> ResolveEntries<TContract>(string? threadId = null)
        where TContract : class, IContributionContract;

    /// <summary>Gets the monotonic revision of a contribution point, or zero when nothing has ever been registered to it.</summary>
    long GetRevision<TContract>() where TContract : class, IContributionContract;
}

/// <summary>Registers contributions on behalf of one origin and disposes their handles as a group, in reverse registration order.</summary>
public interface IContributionRegistrar : IDisposable
{
    /// <summary>Gets the origin every registration of this registrar is attributed to.</summary>
    ContributionOrigin Origin { get; }

    /// <summary>Registers a contribution owned by this registrar.</summary>
    IContributionHandle Add<TContract>(TContract contribution, ContributionOptions? options = null)
        where TContract : class, IContributionContract;
}

/// <summary>The kernel service that turns registrations into live behavior.</summary>
internal interface IContributionRegistry : IContributionView
{
    /// <summary>Registers a contribution attributed to <see cref="ContributionOrigin.Builtin"/>.</summary>
    IContributionHandle Add<TContract>(TContract contribution, ContributionOptions? options = null)
        where TContract : class, IContributionContract;

    /// <summary>Creates a registrar bound to one origin.</summary>
    IContributionRegistrar CreateRegistrar(ContributionOrigin origin);

    /// <summary>Suspends change notifications until the returned scope is disposed, coalescing a batch of mutations into one <see cref="Changed"/> event.</summary>
    /// <remarks>Batches nest; the event is raised when the outermost scope is disposed.</remarks>
    IDisposable BeginBatch();

    /// <summary>Raised once per mutation batch, outside the registry's locks, naming the contracts whose contribution set changed.</summary>
    event EventHandler<ContributionsChangedEventArgs>? Changed;

    /// <summary>Disposes every contribution scoped to the given thread as one coalesced batch, returning how many were disposed.</summary>
    int ReleaseThread(string threadId);
}

/// <summary>Describes one coalesced registry mutation batch.</summary>
internal sealed class ContributionsChangedEventArgs : EventArgs
{
    /// <summary>Creates the event payload.</summary>
    public ContributionsChangedEventArgs(IReadOnlyCollection<Type> changedContracts, long version)
    {
        ChangedContracts = changedContracts ?? throw new ArgumentNullException(nameof(changedContracts));
        Version = version;
    }

    /// <summary>Gets the contract types whose contribution set changed during the batch.</summary>
    public IReadOnlyCollection<Type> ChangedContracts { get; }

    /// <summary>Gets the registry-global mutation version, monotonic across all contribution points.</summary>
    public long Version { get; }

    /// <summary>Determines whether a contribution point changed in this batch.</summary>
    public bool Includes<TContract>() where TContract : class, IContributionContract => Includes(typeof(TContract));

    /// <summary>Determines whether a contribution point changed in this batch.</summary>
    public bool Includes(Type contractType) => ChangedContracts.Contains(contractType);
}
