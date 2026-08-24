using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace DotCraft.Contributions;

/// <summary>One registered contribution and the options it was registered with.</summary>
internal sealed class ContributionEntry
{
    internal ContributionEntry(
        ContributionId id,
        Type contractType,
        object contribution,
        ContributionOrigin origin,
        ContributionOptions options)
    {
        Id = id;
        ContractType = contractType;
        Contribution = contribution;
        Origin = origin;
        Options = options;
    }

    internal ContributionId Id { get; }
    internal Type ContractType { get; }
    internal object Contribution { get; }
    internal ContributionOrigin Origin { get; }
    internal ContributionOptions Options { get; }

    /// <summary>Gets the handle that owns this entry, assigned immediately after construction.</summary>
    internal IContributionHandle Handle { get; set; } = null!;

    internal long Sequence => Id.Value;
    internal ContributionScope Scope => Options.Scope;
    internal string? ThreadId => Options.ThreadId;
    internal int Order => Options.Order;
    internal string? ReplaceTarget => Options.ReplaceTarget;
    internal string? TargetName => Options.TargetName;

    internal bool AppliesTo(string? threadId) =>
        Scope == ContributionScope.Workspace
        || (threadId is not null && string.Equals(ThreadId, threadId, StringComparison.Ordinal));
}

/// <summary>The non-generic base the registry keys contribution points by, so one dictionary holds every contract.</summary>
internal abstract class ContributionPoint
{
    /// <summary>Gets the monotonic revision, incremented on every add or removal.</summary>
    internal abstract long Revision { get; }

    internal abstract void Add(ContributionEntry entry, ILogger? logger);

    internal abstract bool Remove(ContributionEntry entry, ILogger? logger);

    /// <summary>Collects the entries owned by one thread, newest registration first.</summary>
    internal abstract List<ContributionEntry> CollectThreadEntries(string threadId);
}

/// <summary>One contribution point's registered entries plus the immutable views republished on every mutation.</summary>
internal sealed class ContributionPoint<TContract> : ContributionPoint
    where TContract : class, IContributionContract
{
    private static readonly ResolvedView EmptyView = new([], []);
    private static readonly Dictionary<string, ResolvedView> NoThreads = new(StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly List<ContributionEntry> _entries = [];
    // Replacement conflicts are recomputed on every publish; report each losing contribution once.
    private readonly HashSet<long> _reportedConflicts = [];
    private long _revision;
    private ResolvedViews _views = new(EmptyView, NoThreads);

    /// <inheritdoc />
    internal override long Revision => Interlocked.Read(ref _revision);

    /// <inheritdoc />
    internal override void Add(ContributionEntry entry, ILogger? logger)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            Publish(logger);
        }
    }

    /// <inheritdoc />
    internal override bool Remove(ContributionEntry entry, ILogger? logger)
    {
        lock (_gate)
        {
            if (!_entries.Remove(entry))
                return false;
            _reportedConflicts.Remove(entry.Id.Value);
            Publish(logger);
            return true;
        }
    }

    /// <inheritdoc />
    internal override List<ContributionEntry> CollectThreadEntries(string threadId)
    {
        lock (_gate)
        {
            var matches = _entries
                .Where(entry => entry.Scope == ContributionScope.Thread
                    && string.Equals(entry.ThreadId, threadId, StringComparison.Ordinal))
                .ToList();
            matches.Sort(static (left, right) => right.Sequence.CompareTo(left.Sequence));
            return matches;
        }
    }

    internal IReadOnlyList<TContract> Resolve(string? threadId) => View(threadId).Contributions;

    internal IReadOnlyList<ContributionEntry<TContract>> ResolveEntries(string? threadId) =>
        View(threadId).Entries;

    /// <summary>A thread with no thread-scoped entry resolves to exactly the workspace list, so it is
    /// served that same instance rather than a copy.</summary>
    private ResolvedView View(string? threadId)
    {
        var views = Volatile.Read(ref _views);
        return threadId is not null && views.Threads.TryGetValue(threadId, out var view)
            ? view
            : views.Workspace;
    }

    /// <summary>Rebuilds every view and swaps the set in as one reference. Called under <see cref="_gate"/>.</summary>
    private void Publish(ILogger? logger)
    {
        Interlocked.Increment(ref _revision);
        HashSet<string>? threadIds = null;
        foreach (var entry in _entries)
        {
            if (entry.Scope == ContributionScope.Thread && entry.ThreadId is { } threadId)
                (threadIds ??= new HashSet<string>(StringComparer.Ordinal)).Add(threadId);
        }

        var workspace = Materialize(null, logger);
        var threads = NoThreads;
        if (threadIds is not null)
        {
            threads = new Dictionary<string, ResolvedView>(threadIds.Count, StringComparer.Ordinal);
            foreach (var threadId in threadIds)
                threads[threadId] = Materialize(threadId, logger);
        }

        Volatile.Write(ref _views, new ResolvedViews(workspace, threads));
    }

    /// <summary>Builds one effective list: layer the scopes, resolve Tier-B replacements, then order by
    /// ascending <see cref="ContributionOptions.Order"/> with registration order breaking ties.</summary>
    private ResolvedView Materialize(string? threadId, ILogger? logger)
    {
        var candidates = new List<ContributionEntry>(_entries.Count);
        foreach (var entry in _entries)
        {
            if (entry.AppliesTo(threadId))
                candidates.Add(entry);
        }

        if (candidates.Count == 0)
            return EmptyView;

        var winners = ResolveReplacements(candidates, logger);
        var effective = new List<ContributionEntry>(candidates.Count);
        foreach (var entry in candidates)
        {
            if (entry.ReplaceTarget is { } replaced
                && (!winners.TryGetValue(replaced, out var winner) || !ReferenceEquals(winner, entry)))
            {
                continue;
            }

            if (entry.TargetName is { } target && winners.ContainsKey(target))
                continue;

            effective.Add(entry);
        }

        effective.Sort(static (left, right) =>
        {
            var byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0 ? byOrder : left.Sequence.CompareTo(right.Sequence);
        });

        var contributions = ImmutableArray.CreateBuilder<TContract>(effective.Count);
        var entries = ImmutableArray.CreateBuilder<ContributionEntry<TContract>>(effective.Count);
        foreach (var entry in effective)
        {
            var contribution = (TContract)entry.Contribution;
            contributions.Add(contribution);
            entries.Add(new ContributionEntry<TContract>(
                contribution,
                entry.Id,
                entry.Origin,
                entry.Order)
            {
                TargetName = entry.TargetName,
                ReplaceTarget = entry.ReplaceTarget
            });
        }

        return new ResolvedView(contributions.MoveToImmutable(), entries.MoveToImmutable());
    }

    /// <summary>Picks the winning replacement per target: thread scope beats workspace scope, otherwise
    /// the later registration. <see cref="ContributionOptions.Order"/> takes no part.</summary>
    private Dictionary<string, ContributionEntry> ResolveReplacements(
        List<ContributionEntry> candidates,
        ILogger? logger)
    {
        Dictionary<string, ContributionEntry>? winners = null;
        List<ContributionEntry>? contested = null;

        foreach (var entry in candidates)
        {
            if (entry.ReplaceTarget is not { } target)
                continue;

            winners ??= new Dictionary<string, ContributionEntry>(StringComparer.Ordinal);
            if (!winners.TryGetValue(target, out var incumbent))
            {
                winners[target] = entry;
                continue;
            }

            contested ??= [];
            if (Beats(entry, incumbent))
            {
                winners[target] = entry;
                contested.Add(incumbent);
            }
            else
            {
                contested.Add(entry);
            }
        }

        if (contested is not null)
        {
            foreach (var loser in contested)
            {
                var target = loser.ReplaceTarget!;
                if (!_reportedConflicts.Add(loser.Id.Value))
                    continue;
                logger?.LogWarning(
                    "{Code}: contribution {Contribution} from {Origin} lost the replacement of target '{Target}' on contribution point {Contract} and is inactive.",
                    ContributionDiagnosticCodes.ReplaceConflict,
                    loser.Id,
                    loser.Origin,
                    target,
                    typeof(TContract));
            }
        }

        return winners ?? [];
    }

    private static bool Beats(ContributionEntry candidate, ContributionEntry incumbent) =>
        candidate.Scope != incumbent.Scope
            ? candidate.Scope == ContributionScope.Thread
            : candidate.Sequence > incumbent.Sequence;

    private sealed class ResolvedView(
        ImmutableArray<TContract> contributions,
        ImmutableArray<ContributionEntry<TContract>> entries)
    {
        internal IReadOnlyList<TContract> Contributions { get; } = contributions;
        internal IReadOnlyList<ContributionEntry<TContract>> Entries { get; } = entries;
    }

    /// <summary>The published set: the workspace view plus one view per thread that has a thread-scoped
    /// entry. Never mutated after publication.</summary>
    private sealed class ResolvedViews(ResolvedView workspace, Dictionary<string, ResolvedView> threads)
    {
        internal ResolvedView Workspace { get; } = workspace;
        internal Dictionary<string, ResolvedView> Threads { get; } = threads;
    }
}
