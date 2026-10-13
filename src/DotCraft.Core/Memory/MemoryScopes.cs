using System.Text.RegularExpressions;
using DotCraft.Sessions;

namespace DotCraft.Memory;

/// <summary>Durable knowledge belongs to whoever accumulates it: a Thread that names a scope reads and
/// writes its own store instead of the workspace's, and a scope redirects memory and its history alone.</summary>
public static partial class MemoryScopes
{
    public static MemoryStore ForThread(ThreadConfiguration? configuration, MemoryStore workspaceStore, string dataPath) =>
        OwnDataRoot(configuration, dataPath) is { } root ? new MemoryStore(root) : workspaceStore;

    /// <summary>The directory of the store a Thread with this configuration uses, without creating it.</summary>
    public static string StoreDirectory(ThreadConfiguration? configuration, string dataPath) =>
        MemoryStore.DirectoryOf(OwnDataRoot(configuration, dataPath) ?? dataPath);

    /// <summary>Empties the workspace store and every scope store beside it, keeping their directories.</summary>
    public static void ClearAll(MemoryStore workspaceStore)
    {
        workspaceStore.ClearAll();
        var scopesRoot = Path.Combine(Path.GetDirectoryName(workspaceStore.MemoryDirectoryPath)!, "scopes");
        if (!Directory.Exists(scopesRoot))
            return;

        foreach (var scopeRoot in Directory.EnumerateDirectories(scopesRoot))
        {
            if (Segment().IsMatch(Path.GetFileName(scopeRoot))
                && (File.GetAttributes(scopeRoot) & FileAttributes.ReparsePoint) == 0
                && Directory.Exists(MemoryStore.DirectoryOf(scopeRoot)))
            {
                new MemoryStore(scopeRoot).ClearAll();
            }
        }
    }

    private static string? OwnDataRoot(ThreadConfiguration? configuration, string dataPath)
    {
        if (!string.IsNullOrEmpty(configuration?.MemoryScope))
            return ScopeRoot(configuration.MemoryScope, dataPath);
        return string.IsNullOrEmpty(configuration?.WorkspaceOverride)
            ? null
            : Path.Combine(configuration.WorkspaceOverride, Path.GetFileName(dataPath));
    }

    private static string ScopeRoot(string scope, string dataPath)
    {
        if (!Segment().IsMatch(scope))
            throw new ArgumentException($"Memory scope '{scope}' is not a single safe path segment.", nameof(scope));
        return Path.Combine(dataPath, "scopes", scope);
    }

    [GeneratedRegex(@"^(?!\.\.?$)[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex Segment();
}
