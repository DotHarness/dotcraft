using DotCraft.Contributions;

namespace DotCraft.Tools;

/// <summary>Reads the <see cref="IToolSource"/> contribution point on behalf of the Host's own planning.</summary>
internal static class ToolSourceContributions
{
    /// <summary>Resolves host sources directly; plugin sources enter only through the gated aggregate.</summary>
    internal static IReadOnlyList<IToolSource> ResolveHostOwned(
        this IContributionView contributions,
        string? threadId = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var entries = contributions.ResolveEntries<IToolSource>(threadId);
        var sources = new List<IToolSource>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Origin.Kind != ContributionOriginKind.Plugin)
                sources.Add(entry.Contribution);
        }

        return sources;
    }
}
