using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context.Compaction;

/// <summary>Decides whether a tool's stale results may be cleared by the microcompact pass.</summary>
public interface ICompactableToolPolicy : IContributionContract
{
    /// <summary>Gets the stable, kebab-case policy name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns an opinion for one tool name, or <see langword="null"/> to defer to the next policy.</summary>
    bool? IsCompactable(string toolName);
}

/// <summary>Registers the built-in allow-list as an ordinary contribution and reads the contribution point per compaction.</summary>
public static class CompactableToolPolicyCatalog
{
    /// <summary>The Tier-B target name of the built-in allow-list policy.</summary>
    public const string BuiltInTargetName = "builtin-tool-names";

    /// <summary>The order the built-in policy is registered at.</summary>
    public const int BuiltInOrder = 100;

    private static readonly Lazy<IContributionView> LazyDefaultView = new(CreateDefaultView, isThreadSafe: true);

    /// <summary>Gets the immutable process-wide view containing only the built-in policy.</summary>
    public static IContributionView DefaultView => LazyDefaultView.Value;

    /// <summary>Registers the built-in policy into a registry.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handle; when omitted the policy is attributed to <see cref="ContributionOrigin.Builtin"/> and lives for the registry's lifetime.</param>
    /// <returns>The registration handle.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var options = new ContributionOptions(Order: BuiltInOrder) { TargetName = BuiltInTargetName };
        var policy = new BuiltInCompactableToolPolicy();
        return [registrar is null
            ? registry.Add<ICompactableToolPolicy>(policy, options)
            : registrar.Add<ICompactableToolPolicy>(policy, options)];
    }

    /// <summary>Resolves the contribution point and returns the first opinion. No opinion means the results stay.</summary>
    /// <remarks>An empty contribution point falls back to the built-in allow-list, so a registry that never registered it behaves as before.</remarks>
    public static bool IsCompactable(
        IContributionView? contributions,
        string? threadId,
        string? toolName,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(toolName))
            return false;

        var policies = contributions?.Resolve<ICompactableToolPolicy>(threadId);
        if (policies is not { Count: > 0 })
            policies = DefaultView.Resolve<ICompactableToolPolicy>();

        return ContributionRead.FirstOpinion(
            policies,
            policy => policy.IsCompactable(toolName),
            (policy, ex) => logger?.LogWarning(
                ex,
                "Compactable tool policy '{Policy}' threw for '{Tool}' and was skipped.",
                SafeName(policy),
                toolName)) ?? false;
    }

    private static string SafeName(ICompactableToolPolicy policy)
    {
        try
        {
            return policy.Name;
        }
        catch
        {
            return policy.GetType().Name;
        }
    }

    private static IContributionView CreateDefaultView()
    {
        var registry = new ContributionRegistry();
        RegisterBuiltIns(registry);
        return registry;
    }

    /// <summary>Unknown names defer instead of denying, so a contributed policy can still prune plugin tool results.</summary>
    private sealed class BuiltInCompactableToolPolicy : ICompactableToolPolicy
    {
        public string Name => BuiltInTargetName;

        public bool? IsCompactable(string toolName) =>
            CompactableToolNames.IsCompactable(toolName) ? true : null;
    }
}
