using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsLineEndingTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("dotcraft-line-endings-").FullName;

    [Fact]
    public async Task EditFile_KeepsLineEndingsOutsideTheEditAndUsesTheFirstForNewLines()
    {
        var content = await EditAsync("a\r\nb\nc\r\n", "b", "B\nX");

        Assert.Equal("a\r\nB\r\nX\nc\r\n", content);
    }

    [Fact]
    public async Task EditFile_MatchesFilesThatBreakLinesWithCarriageReturns()
    {
        var content = await EditAsync("x\ry\rz\r", "y\rz", "Y\nZ");

        Assert.Equal("x\rY\rZ\r", content);
    }

    [Fact]
    public async Task EditFile_NotFound_EchoesTheSearchedText()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "alpha");

        var result = await new FileTools(_workspace, requireApprovalOutsideWorkspace: false)
            .EditFile("notes.txt", "missing text", "beta");

        Assert.False(result.Success);
        Assert.Contains("oldText: missing text", result.Content, StringComparison.Ordinal);
    }

    private async Task<string> EditAsync(string original, string oldText, string newText)
    {
        var path = Path.Combine(_workspace, "notes.txt");
        await File.WriteAllTextAsync(path, original);
        var result = await new FileTools(_workspace, requireApprovalOutsideWorkspace: false)
            .EditFile("notes.txt", oldText, newText);
        Assert.True(result.Success, result.Content);
        return await File.ReadAllTextAsync(path);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);
}
