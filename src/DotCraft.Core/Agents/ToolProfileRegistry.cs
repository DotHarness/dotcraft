using System.Collections.Concurrent;
using DotCraft.Abstractions;

namespace DotCraft.Agents;

/// <summary>
/// Thread-safe registry of tool profiles keyed by name.
/// </summary>
public sealed class ToolProfileRegistry : IToolProfileRegistry
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<IAgentToolProvider>> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register(string profileName, IReadOnlyList<IAgentToolProvider> providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(providers);
        _profiles[profileName] = providers;
    }

    /// <inheritdoc />
    public bool TryGet(string profileName, out IReadOnlyList<IAgentToolProvider>? providers)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            providers = null;
            return false;
        }

        return _profiles.TryGetValue(profileName, out providers);
    }
}
