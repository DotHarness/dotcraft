using DotCraft.Security;

namespace DotCraft.Memory;

public sealed class ThreadMemoryStores(MemoryStore own, MemoryStore? personal, IReadOnlyList<AttachedMemoryStore> attached)
{
    public const string PersonalStoreId = "personal";

    public const string PersonalStoreName = "Personal";

    public MemoryStore Own { get; } = own;

    public MemoryStore? Personal { get; } = personal;

    public IReadOnlyList<AttachedMemoryStore> Attached { get; } = attached;

    public IEnumerable<string> WritableDirectories =>
    [
        Own.MemoryDirectoryPath,
        .. Personal is null ? [] : new[] { Personal.MemoryDirectoryPath },
        .. Attached.Where(static store => store.Writable).Select(static store => store.DirectoryPath)
    ];

    public IEnumerable<string> ReadOnlyDirectories =>
        Attached.Where(static store => !store.Writable).Select(static store => store.DirectoryPath);

    public MemoryStoreTarget? Classify(string fullPath)
    {
        if (WorkspaceBoundary.IsWithin(fullPath, Own.MemoryDirectoryPath))
            return new MemoryStoreTarget(MemoryStoreTargetKind.Own, Own.MemoryDirectoryPath, null);
        if (Personal is not null && WorkspaceBoundary.IsWithin(fullPath, Personal.MemoryDirectoryPath))
            return new MemoryStoreTarget(MemoryStoreTargetKind.Personal, Personal.MemoryDirectoryPath, null);
        return Attached.FirstOrDefault(store => WorkspaceBoundary.IsWithin(fullPath, store.DirectoryPath)) is { } attached
            ? new MemoryStoreTarget(MemoryStoreTargetKind.Attached, attached.DirectoryPath, attached)
            : null;
    }
}

public enum MemoryStoreTargetKind
{
    Own,
    Personal,
    Attached
}

public sealed record MemoryStoreTarget(MemoryStoreTargetKind Kind, string DirectoryPath, AttachedMemoryStore? Attached)
{
    public bool Writable => Attached?.Writable ?? true;

    public (string Id, string Name)? Label => Kind switch
    {
        MemoryStoreTargetKind.Personal => (ThreadMemoryStores.PersonalStoreId, ThreadMemoryStores.PersonalStoreName),
        MemoryStoreTargetKind.Attached => (Attached!.Attachment.Id, Attached.Attachment.Name),
        _ => null
    };
}
