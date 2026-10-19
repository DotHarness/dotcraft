namespace DotCraft.Memory;

/// <param name="Id">A single safe path segment naming the store's directory under <c>memory-stores/</c>.</param>
/// <param name="Description">What the store holds and who else uses it; the model relies on it to decide what belongs there.</param>
public sealed record MemoryStoreAttachment(string Id, string Name, string Description, string Access);

public static class MemoryStoreAccess
{
    public const string Read = "read";

    public const string ReadWrite = "readWrite";
}

public sealed record AttachedMemoryStore(MemoryStoreAttachment Attachment, string DirectoryPath)
{
    public bool Writable => Attachment.Access == MemoryStoreAccess.ReadWrite;
}
