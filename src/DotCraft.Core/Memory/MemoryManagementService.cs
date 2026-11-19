using DotCraft.Agents;
using DotCraft.Workspaces;

namespace DotCraft.Memory;

public static class MemoryStoreKinds
{
    public const string Workspace = "workspace";
    public const string Scope = "scope";
    public const string Attached = "attached";
}

public sealed record MemoryStoreEntry(string Kind, string? Scope, string? Id, string Path, string DisplayName, bool Writable);

public sealed class MemoryManagementService(DotCraftPaths paths, MemoryStore workspaceStore)
{
    public IReadOnlyList<MemoryStoreEntry> ListStores()
    {
        var dataPath = paths.Data.RootPath;
        var profileNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in new AgentProfileStore(dataPath, paths.UserData.RootPath).List(includeInvalid: false))
        {
            if (!entry.Shadowed && entry.CompiledConfiguration?.MemoryScope is { } scope)
                profileNames.TryAdd(scope, entry.Name ?? entry.Id);
        }

        var scopes = new SortedSet<string>(profileNames.Keys, StringComparer.Ordinal);
        foreach (var directory in ChildDirectories(Path.Combine(dataPath, "scopes")))
        {
            if (Directory.Exists(MemoryStore.DirectoryOf(directory)))
                scopes.Add(System.IO.Path.GetFileName(directory));
        }

        var stores = new List<MemoryStoreEntry>
        {
            new(MemoryStoreKinds.Workspace, null, null, workspaceStore.MemoryDirectoryPath, MemoryStoreKinds.Workspace, true)
        };
        foreach (var scope in scopes)
        {
            var directory = MemoryStore.DirectoryOf(Path.Combine(dataPath, "scopes", scope));
            stores.Add(new MemoryStoreEntry(MemoryStoreKinds.Scope, scope, null, directory, profileNames.GetValueOrDefault(scope, scope), true));
        }

        foreach (var directory in ChildDirectories(Path.Combine(dataPath, "memory-stores")).Order(StringComparer.Ordinal))
        {
            var id = System.IO.Path.GetFileName(directory);
            stores.Add(new MemoryStoreEntry(MemoryStoreKinds.Attached, null, id, directory, id, false));
        }

        return stores;
    }

    private static IEnumerable<string> ChildDirectories(string root) =>
        Directory.Exists(root) ? Directory.EnumerateDirectories(root) : [];
}
