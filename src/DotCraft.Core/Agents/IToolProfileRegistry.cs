using DotCraft.Abstractions;

namespace DotCraft.Agents;

/// <summary>
/// Stores named tool-provider sets that threads can reference by profile name.
/// Profiles are registered at startup or by automation sources.
/// </summary>
public interface IToolProfileRegistry
{
    /// <summary>
    /// Registers or replaces a named profile. Thread-safe.
    /// </summary>
    void Register(string profileName, IReadOnlyList<IAgentToolProvider> providers);

    /// <summary>
    /// Attempts to get the providers for a profile name.
    /// </summary>
    bool TryGet(string profileName, out IReadOnlyList<IAgentToolProvider>? providers);
}
