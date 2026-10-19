using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsStoreFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dotcraft-dreams-store-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private string Store => Path.Combine(_root, ".craft", "memory");

    [Fact]
    public void Read_IncludesMarkdownFilesWhateverTheExtensionCase()
    {
        DreamsTestWorkspace.WriteFile(Store, "Deploy.MD", "Deploy on Fridays.");
        DreamsTestWorkspace.WriteFile(Store, "notes.txt", "Not a memory.");

        Assert.True(DreamsStoreFiles.HasMemoryFiles(Store));
        Assert.Equal(["Deploy.MD"], DreamsStoreFiles.Read(Store).Keys);
    }

    [Fact]
    public async Task Replacement_WaitsForAMemoryWriteAndThenSeesTheStoreChanged()
    {
        DreamsTestWorkspace.WriteFile(Store, "kept.md", "kept");
        var baseline = DreamsStoreFiles.Read(Store);
        Task<bool>? replacement = null;
        var replacedDuringWrite = true;
        var tools = new FileTools(_root, memoryStores: new ThreadMemoryStores(new MemoryStore(Path.GetDirectoryName(Store)!), []))
        {
            ChangeReporter = change =>
            {
                replacement = Task.Run(() => DreamsStoreFiles.TryReplace(Store, baseline, new Dictionary<string, byte[]>()));
                replacedDuringWrite = replacement.Wait(TimeSpan.FromMilliseconds(200));
                return FileChangeStructuredContent.Build(change);
            }
        };

        var written = await tools.WriteFile(Path.Combine(Store, "new.md"), "Saved while Dreams applied a run.");

        Assert.True(written.Success);
        Assert.False(replacedDuringWrite);
        Assert.False(await replacement!);
        Assert.Equal(["kept.md", "new.md"], DreamsStoreFiles.Read(Store).Keys.Order(StringComparer.Ordinal));
    }
}
