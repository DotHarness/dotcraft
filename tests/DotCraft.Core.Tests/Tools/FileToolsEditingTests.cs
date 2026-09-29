using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsEditingTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "dotcraft-filetools-editing-tests", Guid.NewGuid().ToString("N"));

    public FileToolsEditingTests() => Directory.CreateDirectory(_workspace);

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
    [InlineData(@"\u0041", @"\u000a\u0020\u0042")]
    [InlineData(@"\\u0041", @"\\\u0042")]
    [InlineData("中文🙂", "日本語é🚀")]
    public async Task EditFile_LiteralArguments_PreservesOldAndNewText(string oldText, string newText)
    {
        var path = await SeedAsync("before\n" + oldText + "\nafter\n");

        var result = await Tools().EditFile("edit.txt", oldText, newText);

        Assert.Null(result.Error);
        Assert.Equal("before\n" + newText + "\nafter\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task GeneratedEditFile_ParsedJson_PreservesEscapesAndUnicodeOnDisk()
    {
        const string oldText = @"\u0041 \\u0042 中文";
        const string newText = "first\n" + @"\u000a \u0020 \\\u0043 日本語🙂" + "\nlast";
        var path = await SeedAsync("prefix\n" + oldText + "\nsuffix\n");
        var json = JsonSerializer.Serialize(new { path = "edit.txt", oldText, newText });
        var arguments = JsonNode.Parse(json)!.AsObject();
        var function = GeneratedToolFunctions.FileTools_EditFile(Tools());
        var context = new ToolInvocationContext(
            "thread_test", "turn_test", "call_test", ToolInvocationAudience.Model,
            new ToolName(null, "EditFile"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("EditFile")),
            new RuntimeBindingId("native:test:EditFile:1"), 1, DateTimeOffset.UtcNow);

        var result = await new AIFunctionToolRuntime(function).InvokeAsync(context, arguments);

        Assert.True(result.Success);
        Assert.Equal(Encoding.UTF8.GetBytes("prefix\n" + newText + "\nsuffix\n"),
            await File.ReadAllBytesAsync(path));
        Assert.Single(result.StructuredContent!.Value.GetProperty("changes").EnumerateArray());
    }

    [Theory]
    [InlineData("prefix target suffix\n target\n", "  target  ", "prefix target suffix\nreplacement\n", "- target\n+replacement\n")]
    [InlineData("prefix target suffix\ntarget \n", "target\t", "prefix target suffix\nreplacement\n", "-target \n+replacement\n")]
    [InlineData("prefix a–b suffix\na–b\n", "a-b", "prefix a–b suffix\nreplacement\n", "-a–b\n+replacement\n")]
    public async Task EditFile_FuzzyMatch_UsesMatchedLineNotEarlierSubstring(
        string original, string oldText, string expected, string expectedDiff)
    {
        var path = await SeedAsync(original);

        var result = await Tools().EditFile("edit.txt", oldText, "replacement");

        Assert.Null(result.Error);
        Assert.Contains("at line 2", result.Content, StringComparison.Ordinal);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
        var change = Assert.Single(result.StructuredContent!.Value.GetProperty("changes").EnumerateArray());
        var diff = change.GetProperty("diff").GetString()!;
        Assert.Contains(expectedDiff, diff, StringComparison.Ordinal);
        Assert.Contains(" " + original.Split('\n')[0] + "\n", diff, StringComparison.Ordinal);
        Assert.Equal(1, change.GetProperty("additions").GetInt32());
        Assert.Equal(1, change.GetProperty("deletions").GetInt32());
    }

    [Theory]
    [InlineData("target \n  target\n", "target\t", "replacement\n  target\n", 1)]
    [InlineData("target \n  target\n", "  target", "target \nreplacement\n", 2)]
    [InlineData("a–b\na-b\n", "a-b", "a–b\nreplacement\n", 2)]
    public async Task EditFile_MorePreciseMatch_WinsOverBroaderCandidates(
        string original, string oldText, string expected, int line)
    {
        var path = await SeedAsync(original);

        var result = await Tools().EditFile("edit.txt", oldText, "replacement");

        Assert.Null(result.Error);
        Assert.Contains($"at line {line}", result.Content, StringComparison.Ordinal);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("target \ntarget  \n", "target\t")]
    [InlineData(" target\n\ttarget\n", "  target  ")]
    [InlineData("a–b\na—b\n", "a-b")]
    [InlineData("  a\n    b\n    a\n    b\n", "a\n  b")]
    public async Task EditFile_AmbiguousFuzzyMatch_RefusesWithoutWriting(string original, string oldText)
    {
        var path = await SeedAsync(original);
        var bytes = await File.ReadAllBytesAsync(path);

        var result = await Tools().EditFile("edit.txt", oldText, "replacement");

        Assert.NotNull(result.Error);
        Assert.Contains("more context", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("notApplied", result.StructuredContent!.Value.GetProperty("writeState").GetString());
        Assert.Empty(result.StructuredContent.Value.GetProperty("changes").EnumerateArray());
    }

    [Theory]
    [InlineData("value = 'a  b'\n", "value = 'a b'", false)]
    [InlineData("value = 'a\tb'\n", "value = 'a b'", false)]
    [InlineData("value = 'a　 b'\n", "value = 'a b'", false)]
    [InlineData(" target\n", "target\t", true)]
    public async Task EditFile_DisallowedApproximation_RefusesWithoutWriting(
        string original, string oldText, bool replaceAll)
    {
        var path = await SeedAsync(original);

        var result = await Tools().EditFile("edit.txt", oldText, "replacement", replaceAll);

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Equal("notApplied", result.StructuredContent!.Value.GetProperty("writeState").GetString());
        Assert.Empty(result.StructuredContent.Value.GetProperty("changes").EnumerateArray());
    }

    [Theory]
    [InlineData("aaa", "aa", "next", false, "nexta")]
    [InlineData("aaa", "aa", "next", true, "nexta")]
    [InlineData("prefix target suffix", "target", "", false, "prefix  suffix")]
    [InlineData("target target\n target ", "target", "next", true, "next next\n next ")]
    [InlineData("head\n target ", "target\t", "next", false, "head\nnext")]
    [InlineData("head\n target \n", "target\t", "", false, "head\n\n")]
    public async Task EditFile_Replacement_PreservesUnaffectedTextAndFinalNewline(
        string original, string oldText, string newText, bool replaceAll, string expected)
    {
        var path = await SeedAsync(original);

        var result = await Tools().EditFile("edit.txt", oldText, newText, replaceAll);

        Assert.Null(result.Error);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EditFile_MultilineFuzzyMatch_PreservesRangeAndEncoding(string eol)
    {
        var original = $"head{eol}  first{eol}    second{eol}tail";
        var path = Path.Combine(_workspace, "edit.txt");
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        await File.WriteAllTextAsync(path, original, encoding);

        var result = await Tools().EditFile("edit.txt", "first\n  second", "changed");

        Assert.Null(result.Error);
        Assert.Contains("at line 2", result.Content, StringComparison.Ordinal);
        var expected = $"head{eol}changed{eol}tail";
        byte[] expectedBytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(expected)];
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("foo \nbar\n", false, "foo \nbar\n")]
    [InlineData("foo \n \nbar\n", true, "next\n\nbar\n")]
    [InlineData("foo \n", true, "next\n")]
    public async Task EditFile_TrailingSnippetNewline_MatchesEmptyLineSegment(
        string original, bool succeeds, string expected)
    {
        var path = await SeedAsync(original);

        var result = await Tools().EditFile("edit.txt", "foo\n", "next\n");

        Assert.Equal(succeeds, result.Error is null);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
    }

    private FileTools Tools() => new(_workspace, requireApprovalOutsideWorkspace: false);

    private async Task<string> SeedAsync(string content)
    {
        var path = Path.Combine(_workspace, "edit.txt");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
