using System.Text.RegularExpressions;

namespace DotCraft.Memory;

/// <summary>Durable knowledge belongs to whoever accumulates it: a Thread that names a scope reads and
/// writes its own store instead of the workspace's, and a scope redirects memory and its history alone.</summary>
public static partial class MemoryScopes
{
    /// <summary>The scope's store, or <see langword="null"/> when this Thread uses the workspace store.</summary>
    public static MemoryStore? Resolve(string? scope, string dataPath) =>
        string.IsNullOrEmpty(scope) ? null : new MemoryStore(RootFor(scope, dataPath));

    private static string RootFor(string scope, string dataPath)
    {
        if (!Segment().IsMatch(scope))
            throw new ArgumentException($"Memory scope '{scope}' is not a single safe path segment.", nameof(scope));
        return Path.Combine(dataPath, "scopes", scope);
    }

    [GeneratedRegex(@"^(?!\.\.?$)[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex Segment();
}
