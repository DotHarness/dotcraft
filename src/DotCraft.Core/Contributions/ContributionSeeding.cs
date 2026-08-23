using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Contributions;

/// <summary>Seeds dependency-injection multi-registrations into the contribution registry at workspace start.</summary>
internal static class ContributionSeeding
{
    private static readonly ContributionOptions SeedOptions =
        ContributionOptions.Default with { OwnsContribution = false };

    /// <summary>Adds every <typeparamref name="TContract"/> resolved from the service provider to the registry, as one batch in container-resolved order.</summary>
    /// <param name="registrar">
    /// An optional registrar whose disposal group the seeded handles join; otherwise they are
    /// attributed to <see cref="ContributionOrigin.Builtin"/> and owned by the caller.
    /// </param>
    public static IReadOnlyList<IContributionHandle> SeedContributions<TContract>(
        this IContributionRegistry registry,
        IServiceProvider services,
        IContributionRegistrar? registrar = null)
        where TContract : class, IContributionContract
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);

        var contributions = services.GetServices<TContract>().ToArray();
        if (contributions.Length == 0)
            return [];

        using var batch = registry.BeginBatch();
        var handles = new IContributionHandle[contributions.Length];
        for (var index = 0; index < contributions.Length; index++)
        {
            handles[index] = registrar is null
                ? registry.Add(contributions[index], SeedOptions)
                : registrar.Add(contributions[index], SeedOptions);
        }

        return handles;
    }
}
