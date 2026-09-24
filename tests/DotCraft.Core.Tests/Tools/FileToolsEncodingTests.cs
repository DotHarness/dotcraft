using System.Text;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsEncodingTests : IDisposable
{
    // "title: 中文\nneedle\n" in GBK, which is not valid UTF-8.
    private static readonly byte[] GbkBytes = [.. "title: "u8, 0xD6, 0xD0, 0xCE, 0xC4, .. "\nneedle\n"u8];

    private const string GbkRefusal = "Error: gbk.txt is not valid utf-8 text.";

    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-filetools-encoding-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ToolResultAttachments _attachments = new();

    public FileToolsEncodingTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    public async Task ReadFile_GbkFile_ReturnsError(int offset, int limit)
    {
        await WriteGbkFileAsync();

        var result = await Tools().ReadFile("gbk.txt", offset, limit);

        Assert.StartsWith(GbkRefusal, Assert.Single(result.OfType<TextContent>()).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditFile_GbkFile_RefusesWithoutWritingOrAttaching()
    {
        var file = await WriteGbkFileAsync();

        var result = await InvokeAsync(tools => tools.EditFile("gbk.txt", "needle", "pin"));

        Assert.StartsWith(GbkRefusal, result, StringComparison.Ordinal);
        Assert.Equal(GbkBytes, await File.ReadAllBytesAsync(file));
        Assert.Null(_attachments.StructuredContent);
    }

    [Fact]
    public async Task WriteFile_OverGbkFile_RefusesWithoutWritingOrAttaching()
    {
        var file = await WriteGbkFileAsync();

        var result = await InvokeAsync(tools => tools.WriteFile("gbk.txt", "replacement"));

        Assert.StartsWith(GbkRefusal, result, StringComparison.Ordinal);
        Assert.Equal(GbkBytes, await File.ReadAllBytesAsync(file));
        Assert.Null(_attachments.StructuredContent);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16le-bom")]
    public async Task ValidTextFiles_ReadAndEditInTheirOwnEncoding(string name)
    {
        Encoding encoding = name switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            _ => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        };
        var file = Path.Combine(_workspace, "valid.txt");
        await File.WriteAllBytesAsync(file, [.. encoding.GetPreamble(), .. encoding.GetBytes("中文\nsecond\n")]);
        var tools = Tools();

        foreach (var (offset, limit) in new[] { (0, 0), (1, 10) })
        {
            var text = Assert.Single((await tools.ReadFile("valid.txt", offset, limit)).OfType<TextContent>()).Text;
            Assert.StartsWith($"1: 中文{Environment.NewLine}2: second{Environment.NewLine}", text, StringComparison.Ordinal);
        }

        var result = await tools.EditFile("valid.txt", "second", "2nd");

        Assert.Equal("Successfully edited valid.txt at line 2 (1 -> 1 lines)", result);
        byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes("中文\n2nd\n")];
        Assert.Equal(expected, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task EditFile_UnencodableReplacement_LeavesFileIntact()
    {
        var file = Path.Combine(_workspace, "notes.txt");
        await File.WriteAllTextAsync(file, "a\nb\n");

        var result = await InvokeAsync(tools => tools.EditFile("notes.txt", "b", "\\uD800"));

        Assert.StartsWith("Error", result, StringComparison.Ordinal);
        Assert.Equal("a\nb\n", await File.ReadAllTextAsync(file));
        Assert.Null(_attachments.StructuredContent);
    }

    [Fact]
    public async Task GrepFiles_ManagedSearch_StillMatchesAroundGbkFile()
    {
        await WriteGbkFileAsync();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "valid.txt"), "needle here\n");
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false, managedSearchOnly: true);

        var result = await tools.GrepFiles("needle");

        Assert.Contains("valid.txt:", result, StringComparison.Ordinal);
        Assert.Contains("gbk.txt:", result, StringComparison.Ordinal);
    }

    private FileTools Tools() => new(_workspace, requireApprovalOutsideWorkspace: false);

    private async Task<string> WriteGbkFileAsync()
    {
        var file = Path.Combine(_workspace, "gbk.txt");
        await File.WriteAllBytesAsync(file, GbkBytes);
        return file;
    }

    private async Task<string> InvokeAsync(Func<FileTools, Task<string>> call, FileTools? tools = null)
    {
        using var scope = ToolResultAttachmentScope.Set(_attachments);
        return await call(tools ?? Tools());
    }
}
