using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

/// <summary>A SubAgent runtime added alongside the built-in ones, with the profiles it ships as defaults.</summary>
public interface ISubAgentRuntimeSource : IContributionContract
{
    /// <summary>Gets the runtime. Its <see cref="ISubAgentRuntime.RuntimeType"/> is the name profiles reference.</summary>
    ISubAgentRuntime Runtime { get; }

    /// <summary>Gets the profiles this runtime ships; a workspace-configured profile of the same name overrides one.</summary>
    IReadOnlyList<SubAgentProfile> Profiles => [];
}

/// <summary>
/// The one profile and runtime set every SubAgent construction site reads, so the prompt, the
/// protocol list, the validator and the dispatcher cannot disagree about which profiles exist.
/// </summary>
public sealed class SubAgentProfileCatalog
{
    private SubAgentProfileCatalog(
        IReadOnlyList<SubAgentProfile> builtInProfiles,
        IReadOnlyList<string> knownRuntimeTypes,
        IReadOnlyList<ISubAgentRuntime> contributedRuntimes)
    {
        BuiltInProfiles = builtInProfiles;
        KnownRuntimeTypes = knownRuntimeTypes;
        ContributedRuntimes = contributedRuntimes;
    }

    /// <summary>Gets the host's own set, used wherever the registry is not reachable (startup validation).</summary>
    public static SubAgentProfileCatalog BuiltIn { get; } = new(
        SubAgentProfileRegistry.CreateBuiltInProfiles(),
        SubAgentProfileRegistry.KnownRuntimeTypes,
        []);

    /// <summary>Gets the built-in profiles plus the profiles contributed runtimes ship.</summary>
    public IReadOnlyList<SubAgentProfile> BuiltInProfiles { get; }

    /// <summary>Gets every runtime type a profile may reference without being flagged unknown.</summary>
    public IReadOnlyList<string> KnownRuntimeTypes { get; }

    /// <summary>Gets the contributed runtimes, in resolved order.</summary>
    public IReadOnlyList<ISubAgentRuntime> ContributedRuntimes { get; }

    /// <summary>Folds the <see cref="ISubAgentRuntimeSource"/> contribution point over the host's set.</summary>
    /// <remarks>A contribution that throws while being read is logged and skipped; an empty contribution point returns <see cref="BuiltIn"/>.</remarks>
    public static SubAgentProfileCatalog Resolve(
        IContributionView? contributions,
        string? threadId = null,
        ILogger? logger = null)
    {
        var contributed = contributions?.Resolve<ISubAgentRuntimeSource>(threadId);
        if (contributed is not { Count: > 0 })
            return BuiltIn;

        var profiles = new List<SubAgentProfile>(BuiltIn.BuiltInProfiles);
        var runtimes = new List<ISubAgentRuntime>(contributed.Count);
        var runtimeTypes = new List<string>(BuiltIn.KnownRuntimeTypes);
        var seenTypes = new HashSet<string>(runtimeTypes, StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in contributed)
        {
            ISubAgentRuntime runtime;
            IReadOnlyList<SubAgentProfile> shipped;
            try
            {
                runtime = contribution.Runtime;
                shipped = contribution.Profiles ?? [];
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "SubAgent runtime contribution '{Contribution}' threw while being read and was skipped.",
                    contribution.GetType().Name);
                continue;
            }

            if (runtime is null || string.IsNullOrWhiteSpace(runtime.RuntimeType))
                continue;

            // Host-provided runtime types win: a contribution adds a type, it never shadows one the host serves.
            if (!seenTypes.Add(runtime.RuntimeType))
            {
                logger?.LogWarning(
                    "SubAgent runtime contribution declares runtime type '{RuntimeType}', which is already served; it was skipped.",
                    runtime.RuntimeType);
                continue;
            }

            runtimeTypes.Add(runtime.RuntimeType);
            runtimes.Add(runtime);
            foreach (var profile in shipped)
            {
                if (!string.IsNullOrWhiteSpace(profile?.Name))
                    profiles.Add(profile);
            }
        }

        return new SubAgentProfileCatalog(profiles, runtimeTypes, runtimes);
    }

    /// <summary>Builds the profile registry for one construction site from this catalog's set.</summary>
    /// <param name="knownRuntimeTypes">Narrows the known set to the runtimes a site actually holds; defaults to the whole catalog.</param>
    public SubAgentProfileRegistry CreateRegistry(
        IEnumerable<SubAgentProfile>? configuredProfiles,
        IEnumerable<string>? knownRuntimeTypes = null,
        IEnumerable<string>? disabledProfiles = null) =>
        new(configuredProfiles, BuiltInProfiles, knownRuntimeTypes ?? KnownRuntimeTypes, disabledProfiles);
}
