using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsAttachedMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-attached-memory", Guid.NewGuid().ToString("N"));
    private readonly string _dataPath;
    private readonly MemoryStore _own;

    public FileToolsAttachedMemoryTests()
    {
        _dataPath = Path.Combine(_root, ".craft");
        _own = new MemoryStore(_dataPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task WriteFile_ReadOnlyStore_IsRejectedAndLeavesTheStoreUnchanged()
    {
        var store = Attach("reference", MemoryStoreAccess.Read);
        var gate = new RecordingGate(MemoryWriteDecision.Admit);
        var path = Path.Combine(store.DirectoryPath, "fact.md");

        var write = await Tools([store], gate).WriteFile(path, "a fact");
        File.WriteAllText(path, "original");
        var edit = await Tools([store], gate).EditFile(path, "original", "changed");

        Assert.False(write.Success);
        Assert.False(edit.Success);
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task WriteFile_RejectedByTheGate_FailsWithItsReasonAndWritesNothing()
    {
        var store = Attach("org", MemoryStoreAccess.ReadWrite);
        var gate = new RecordingGate(MemoryWriteDecision.Reject("it names a customer"));
        var path = Path.Combine(store.DirectoryPath, "phrasing.md");

        var result = await Tools([store], gate).WriteFile(path, "Call Ms. Tanaka by her first name.");

        Assert.False(result.Success);
        Assert.Contains("it names a customer", result.Content, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task WriteAndEdit_AdmittedByTheGate_ApplyAndDescribeEachWrite()
    {
        var store = Attach("org", MemoryStoreAccess.ReadWrite);
        var gate = new RecordingGate(MemoryWriteDecision.Admit);
        var tools = Tools([store], gate);
        var path = Path.Combine(store.DirectoryPath, "notes", "phrasing.md");

        Assert.True((await tools.WriteFile(path, "Say \"order\".")).Success);
        Assert.True((await tools.EditFile(path, "order", "purchase")).Success);

        Assert.Equal("Say \"purchase\".", File.ReadAllText(path));
        Assert.Collection(
            gate.Requests,
            create =>
            {
                Assert.Equal(("org", "notes/phrasing.md", MemoryWriteOperation.Create), (create.StoreId, create.RelativePath, create.Operation));
                Assert.Null(create.PreviousContent);
                Assert.Equal("Say \"order\".", create.Content);
            },
            edit =>
            {
                Assert.Equal(MemoryWriteOperation.Edit, edit.Operation);
                Assert.Equal("Say \"order\".", edit.PreviousContent);
                Assert.Equal("Say \"purchase\".", edit.Content);
            });
    }

    [Fact]
    public async Task WriteFile_EmptyingAnAttachedMemory_DeletesItButEmptyingTheIndexKeepsIt()
    {
        var store = Attach("org", MemoryStoreAccess.ReadWrite);
        var gate = new RecordingGate(MemoryWriteDecision.Admit);
        var memory = Path.Combine(store.DirectoryPath, "old.md");
        var index = Path.Combine(store.DirectoryPath, MemoryStore.IndexFileName);
        File.WriteAllText(memory, "stale");
        File.WriteAllText(index, "- [Old](old.md) — stale");

        Assert.True((await Tools([store], gate).WriteFile(memory, "")).Success);
        Assert.True((await Tools([store], gate).WriteFile(index, "")).Success);

        Assert.False(File.Exists(memory));
        Assert.True(File.Exists(index));
        Assert.Equal(MemoryWriteOperation.Delete, gate.Requests[0].Operation);
        Assert.Equal("stale", gate.Requests[0].PreviousContent);
        Assert.Null(gate.Requests[0].Content);
        Assert.Equal(MemoryWriteOperation.Edit, gate.Requests[1].Operation);
    }

    [Fact]
    public async Task OwnStore_NeverPassesTheGate()
    {
        var store = Attach("org", MemoryStoreAccess.ReadWrite);
        var gate = new RecordingGate(MemoryWriteDecision.Reject("no"));

        var result = await Tools([store], gate).WriteFile(Path.Combine(_own.MemoryDirectoryPath, "mine.md"), "mine");

        Assert.True(result.Success);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task WriteFile_ThrowingGate_RejectsTheWrite()
    {
        var store = Attach("org", MemoryStoreAccess.ReadWrite);
        var path = Path.Combine(store.DirectoryPath, "fact.md");

        var result = await Tools([store], new ThrowingGate()).WriteFile(path, "a fact");

        Assert.False(result.Success);
        Assert.False(File.Exists(path));
    }

    private AttachedMemoryStore Attach(string id, string access) =>
        Assert.Single(MemoryScopes.AttachedStores(
            new ThreadConfiguration { MemoryStores = [new MemoryStoreAttachment(id, id, $"Shared {id} knowledge.", access)] },
            _dataPath));

    private FileTools Tools(IReadOnlyList<AttachedMemoryStore> stores, IMemoryWriteGate gate) =>
        new(
            _root,
            requireApprovalOutsideWorkspace: true,
            memoryStores: new ThreadMemoryStores(_own, stores),
            memoryWriteGates: () => [gate]);

    private sealed class RecordingGate(MemoryWriteDecision decision) : IMemoryWriteGate
    {
        public List<MemoryWriteRequest> Requests { get; } = [];

        public ValueTask<MemoryWriteDecision> EvaluateAsync(MemoryWriteRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class ThrowingGate : IMemoryWriteGate
    {
        public ValueTask<MemoryWriteDecision> EvaluateAsync(MemoryWriteRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("offline");
    }
}
