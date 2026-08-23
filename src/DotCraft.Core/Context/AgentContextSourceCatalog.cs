using DotCraft.Agents;
using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>Registers the kernel's memory provider as an ordinary named contribution on the pre-send context contribution point.</summary>
internal static class AgentContextSourceCatalog
{
    /// <summary>The order the built-in memory provider is registered at.</summary>
    public const int MemoryOrder = 100;

    /// <summary>Gets the built-in contribution instance, so a consumer can materialize the memory provider without the registry.</summary>
    internal static IAgentContextSource BuiltInMemory { get; } = new BuiltInMemoryContextContribution();

    /// <summary>Registers the built-in memory provider into a registry.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handle; when omitted the contribution is attributed to <see cref="ContributionOrigin.Builtin"/> and lives for the registry's lifetime.</param>
    /// <returns>The registration handle.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var options = new ContributionOptions(Order: MemoryOrder)
        {
            TargetName = AgentContextSourceNames.Memory,
            OwnsContribution = false
        };
        return [registrar is null
            ? registry.Add(BuiltInMemory, options)
            : registrar.Add(BuiltInMemory, options)];
    }

    /// <summary>Stateless: every per-build input travels on the request, so one instance serves every agent in the process.</summary>
    private sealed class BuiltInMemoryContextContribution : IAgentContextSource
    {
        public AIContextProvider CreateProvider(AgentContextRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return request.RequireBuiltInProvider()();
        }
    }
}
