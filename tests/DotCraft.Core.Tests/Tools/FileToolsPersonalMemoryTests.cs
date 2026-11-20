using DotCraft.Memory;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsPersonalMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-personal-memory", Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly string _userData;
    private readonly MemoryStore _own;

    public FileToolsPersonalMemoryTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _userData = Path.Combine(_root, "home", ".craft");
        _own = new MemoryStore(Path.Combine(_workspace, ".craft"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public void PersonalStore_ResolvesUnderTheHomeOnlyWhileOnAndDistinctFromTheOwnStore()
    {
        var personal = MemoryScopes.PersonalStore(true, _userData, _own);

        Assert.Equal(Path.Combine(_userData, "memory"), personal?.MemoryDirectoryPath);
        Assert.True(Directory.Exists(personal!.MemoryDirectoryPath));
        Assert.Null(MemoryScopes.PersonalStore(false, _userData, _own));
        Assert.Null(MemoryScopes.PersonalStore(true, null, _own));
        Assert.Null(MemoryScopes.PersonalStore(true, Path.Combine(_workspace, ".craft"), _own));
    }

    [Fact]
    public async Task PersonalStore_IsWritableOutsideTheWorkspaceWithoutPassingTheGate()
    {
        var gate = new RejectingGate();
        var tools = Tools(personal: true, gate);
        var path = Path.Combine(_userData, "memory", "role.md");

        var result = await tools.WriteFile(path, "Works in worktrees.");

        Assert.True(result.Success);
        Assert.Equal("Works in worktrees.", File.ReadAllText(path));
        Assert.Equal(0, gate.Calls);
        Assert.False((await tools.WriteFile(Path.Combine(_userData, "config.json"), "{}")).Success);
    }

    [Fact]
    public async Task PersonalStore_IsNotWritableWhileOff()
    {
        Directory.CreateDirectory(Path.Combine(_userData, "memory"));
        var path = Path.Combine(_userData, "memory", "role.md");

        var result = await Tools(personal: false, new RejectingGate()).WriteFile(path, "Works in worktrees.");

        Assert.False(result.Success);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task WriteFile_EmptyingAPersonalMemory_DeletesItButEmptyingTheIndexKeepsIt()
    {
        var tools = Tools(personal: true, new RejectingGate());
        var memory = Path.Combine(_userData, "memory", "old.md");
        var index = Path.Combine(_userData, "memory", MemoryStore.IndexFileName);
        File.WriteAllText(memory, "stale");
        File.WriteAllText(index, "- [Old](old.md) — stale");

        Assert.True((await tools.WriteFile(memory, "")).Success);
        Assert.True((await tools.WriteFile(index, "")).Success);

        Assert.False(File.Exists(memory));
        Assert.True(File.Exists(index));
    }

    private FileTools Tools(bool personal, IMemoryWriteGate gate) =>
        new(
            _workspace,
            requireApprovalOutsideWorkspace: false,
            memoryStores: new ThreadMemoryStores(_own, MemoryScopes.PersonalStore(personal, _userData, _own), []),
            memoryWriteGates: () => [gate]);

    private sealed class RejectingGate : IMemoryWriteGate
    {
        public int Calls { get; private set; }

        public ValueTask<MemoryWriteDecision> EvaluateAsync(MemoryWriteRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(MemoryWriteDecision.Reject("no"));
        }
    }
}
