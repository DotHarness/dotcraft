using DotCraft.Tools;

namespace DotCraft.Agents;

/// <summary>
/// Stores named tool-source sets that threads can reference by profile name.
/// Profiles are registered at startup or by automation sources.
/// </summary>
public interface IToolProfileRegistry
{
    /// <summary>
    /// Registers or replaces a named profile. Thread-safe.
    /// </summary>
    void Register(string profileName, IReadOnlyList<IToolSource> sources);

    /// <summary>
    /// Attempts to get the sources for a profile name.
    /// </summary>
    bool TryGet(string profileName, out IReadOnlyList<IToolSource>? sources);
}
