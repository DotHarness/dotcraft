using DotCraft.Security;

namespace DotCraft.Memory;

/// <summary>Every store a memory-enabled Thread uses: its own and the ones attached beside it.</summary>
public sealed class ThreadMemoryStores(MemoryStore own, IReadOnlyList<AttachedMemoryStore> attached)
{
    public MemoryStore Own { get; } = own;

    public IReadOnlyList<AttachedMemoryStore> Attached { get; } = attached;

    public IEnumerable<string> WritableDirectories =>
        [Own.MemoryDirectoryPath, .. Attached.Where(static store => store.Writable).Select(static store => store.DirectoryPath)];

    public IEnumerable<string> ReadOnlyDirectories =>
        Attached.Where(static store => !store.Writable).Select(static store => store.DirectoryPath);

    public MemoryStoreTarget? Classify(string fullPath)
    {
        if (WorkspaceBoundary.IsWithin(fullPath, Own.MemoryDirectoryPath))
            return new MemoryStoreTarget(Own.MemoryDirectoryPath, null);
        return Attached.FirstOrDefault(store => WorkspaceBoundary.IsWithin(fullPath, store.DirectoryPath)) is { } attached
            ? new MemoryStoreTarget(attached.DirectoryPath, attached)
            : null;
    }
}

/// <param name="Attached">The attached store, or null for the Thread's own.</param>
public sealed record MemoryStoreTarget(string DirectoryPath, AttachedMemoryStore? Attached)
{
    public bool Writable => Attached?.Writable ?? true;
}
