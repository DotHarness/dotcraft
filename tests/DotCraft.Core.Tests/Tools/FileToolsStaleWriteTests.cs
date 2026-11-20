using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsStaleWriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-stale-write", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly FileTools _tools;

    public FileToolsStaleWriteTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "notes.md");
        File.WriteAllText(_path, "first\nsecond\nthird\n");
        _tools = new FileTools(_root, fileVersions: new ThreadFileVersions());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task ReadThenChangedOnDisk_RejectsTheWriteAndLeavesTheFile()
    {
        await _tools.ReadFile(_path, offset: 1, limit: 1);
        File.WriteAllText(_path, "changed elsewhere\n");

        var result = await _tools.WriteFile(_path, "mine\n");

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.Error?.Code);
        Assert.Equal("changed elsewhere\n", File.ReadAllText(_path));

        await _tools.ReadFile(_path);
        Assert.True((await _tools.WriteFile(_path, "mine\n")).Success);
    }

    [Fact]
    public async Task OwnWriteThenChangedOnDisk_RejectsTheNextWrite()
    {
        Assert.True((await _tools.WriteFile(_path, "mine\n")).Success);
        Assert.True((await _tools.WriteFile(_path, "mine again\n")).Success);
        File.WriteAllText(_path, "changed elsewhere\n");

        Assert.False((await _tools.WriteFile(_path, "overwrite\n")).Success);
        Assert.Equal("changed elsewhere\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task ReadEditThenWrite_IsNotRejectedByTheThreadsOwnEdit()
    {
        await _tools.ReadFile(_path);
        Assert.True((await _tools.EditFile(_path, "second", "middle")).Success);

        Assert.True((await _tools.WriteFile(_path, "mine\n")).Success);
    }

    [Fact]
    public async Task ReadThenDeletedOnDisk_RejectsWriteUntilReRead()
    {
        await _tools.ReadFile(_path);
        File.Delete(_path);

        var rejected = await _tools.WriteFile(_path, "recreated\n");
        Assert.False(rejected.Success);
        Assert.False(File.Exists(_path));

        await _tools.ReadFile(_path);
        Assert.True((await _tools.WriteFile(_path, "recreated\n")).Success);
        Assert.Equal("recreated\n", File.ReadAllText(_path));
    }

    [Fact]
    public async Task EditFile_IsNotChecked()
    {
        await _tools.ReadFile(_path);
        File.WriteAllText(_path, "first\nsecond\nchanged elsewhere\n");

        Assert.True((await _tools.EditFile(_path, "second", "middle")).Success);
        Assert.Equal("first\nmiddle\nchanged elsewhere\n", File.ReadAllText(_path));
    }
}
