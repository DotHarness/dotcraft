using DotCraft.Memory;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Memory;

public sealed class MemoryStoreAttachmentTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "dotcraft-attachments", Guid.NewGuid().ToString("N"), ".craft");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dataPath)!, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void AttachedStores_ResolveInOrderUnderTheDataPathAndExist()
    {
        var stores = MemoryScopes.AttachedStores(
            new ThreadConfiguration
            {
                MemoryStores =
                [
                    new MemoryStoreAttachment("business", "Business", "Facts about the business.", MemoryStoreAccess.Read),
                    new MemoryStoreAttachment("customers", "Customers", "How we talk to customers.", MemoryStoreAccess.ReadWrite)
                ]
            },
            _dataPath);

        Assert.Equal(["business", "customers"], stores.Select(static store => store.Attachment.Id));
        Assert.All(stores, static store => Assert.True(Directory.Exists(store.DirectoryPath)));
        Assert.Equal(Path.Combine(_dataPath, "memory-stores", "customers"), stores[1].DirectoryPath);
        Assert.Equal([false, true], stores.Select(static store => store.Writable));
    }

    [Fact]
    public void NoAttachments_ResolveToNone()
    {
        Assert.Empty(MemoryScopes.AttachedStores(new ThreadConfiguration(), _dataPath));
        Assert.Empty(MemoryScopes.AttachedStores(null, _dataPath));
    }

    public static TheoryData<MemoryStoreAttachment[]> InvalidAttachments => new()
    {
        new[] { new MemoryStoreAttachment("..", "Up", "Escapes.", MemoryStoreAccess.Read) },
        new[] { new MemoryStoreAttachment("a/b", "Nested", "Nested.", MemoryStoreAccess.Read) },
        new[] { new MemoryStoreAttachment("a", "A", "First.", MemoryStoreAccess.Read), new MemoryStoreAttachment("A", "A", "Again.", MemoryStoreAccess.Read) },
        new[] { new MemoryStoreAttachment("a", "", "No name.", MemoryStoreAccess.Read) },
        new[] { new MemoryStoreAttachment("a", "A", " ", MemoryStoreAccess.Read) },
        new[] { new MemoryStoreAttachment("a", "A", "Bad access.", "write") },
        Enumerable.Range(0, MemoryScopes.MaxAttachedStores + 1)
            .Select(static i => new MemoryStoreAttachment($"s{i}", $"S{i}", "Too many.", MemoryStoreAccess.Read))
            .ToArray()
    };

    [Theory]
    [MemberData(nameof(InvalidAttachments))]
    public void InvalidAttachments_AreRejected(MemoryStoreAttachment[] attachments) =>
        Assert.Throws<ArgumentException>(() => MemoryScopes.ValidateAttachments(attachments));
}
