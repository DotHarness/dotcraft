using System.Collections.Concurrent;
using DotCraft.Tools;

namespace DotCraft.Agents;

/// <summary>
/// Thread-safe registry of tool profiles keyed by name.
/// </summary>
public sealed class ToolProfileRegistry : IToolProfileRegistry
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<IToolSource>> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register(string profileName, IReadOnlyList<IToolSource> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(sources);
        _profiles[profileName] = sources;
    }

    /// <inheritdoc />
    public bool TryGet(string profileName, out IReadOnlyList<IToolSource>? sources)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            sources = null;
            return false;
        }

        return _profiles.TryGetValue(profileName, out sources);
    }
}
