using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsFileChangeTests : IDisposable
{
    private readonly string _workspace = CreateDirectory();
    private readonly string _outside = CreateDirectory();
    private readonly ToolResultAttachments _attachments = new();

    public void Dispose()
    {
        DeleteDirectory(_workspace);
        DeleteDirectory(_outside);
    }

    [Fact]
    public async Task WriteFile_NewFile_AttachesAddWithNewFileDiff()
    {
        var result = await InvokeAsync(tools => tools.WriteFile("src/new.txt", "one\ntwo\n"));

        Assert.Equal("Successfully wrote 8 bytes (3 lines) to src/new.txt", result);
        var change = SingleChange();
        Assert.Equal(["path", "kind", "diff", "additions", "deletions"], change.EnumerateObject().Select(p => p.Name));
        Assert.Equal("src/new.txt", change.GetProperty("path").GetString());
        Assert.Equal("add", change.GetProperty("kind").GetString());
        Assert.StartsWith("diff --git a/src/new.txt b/src/new.txt\nnew file mode 100644\n", Diff(change), StringComparison.Ordinal);
        Assert.Equal((2, 0), Counts(change));
    }

    [Fact]
    public async Task WriteFile_ExistingFile_AttachesUpdateCountingOnlyChangedLines()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "a\nb\nc\nd\n");

        var result = await InvokeAsync(tools => tools.WriteFile("notes.txt", "a\nB\nc\nd\n"));

        Assert.Equal("Successfully wrote 8 bytes (5 lines) to notes.txt", result);
        var change = SingleChange();
        Assert.Equal("update", change.GetProperty("kind").GetString());
        Assert.Contains("--- a/notes.txt\n+++ b/notes.txt\n", Diff(change), StringComparison.Ordinal);
        Assert.Contains("-b\n+B\n", Diff(change), StringComparison.Ordinal);
        Assert.Equal((1, 1), Counts(change));
    }

    [Fact]
    public async Task EditFile_AttachesUpdateWithExactCounts()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "a\nb\nc\n");

        var result = await InvokeAsync(tools => tools.EditFile("notes.txt", "b", "B1\nB2"));

        Assert.Equal("Successfully edited notes.txt at line 2 (1 -> 2 lines)", result);
        var change = SingleChange();
        Assert.Equal("notes.txt", change.GetProperty("path").GetString());
        Assert.Equal("update", change.GetProperty("kind").GetString());
        Assert.Contains("-b\n+B1\n+B2\n", Diff(change), StringComparison.Ordinal);
        Assert.Equal((2, 1), Counts(change));
    }

    [Fact]
    public async Task WriteFile_RelativePathWithBackslashes_ReportsForwardSlashes()
    {
        var result = await InvokeAsync(tools => tools.WriteFile(@"src\nested\file.txt", "x\n"));

        Assert.Equal(@"Successfully wrote 2 bytes (2 lines) to src\nested\file.txt", result);
        Assert.Equal("src/nested/file.txt", SingleChange().GetProperty("path").GetString());
    }

    [Fact]
    public async Task WriteFile_OutsideWorkspace_ReportsAbsoluteForwardSlashPath()
    {
        var file = Path.Combine(_outside, "outside.txt");

        await InvokeAsync(tools => tools.WriteFile(file, "x\n"));

        var expected = Path.GetFullPath(file).Replace('\\', '/');
        var change = SingleChange();
        Assert.Equal(expected, change.GetProperty("path").GetString());
        Assert.StartsWith($"diff --git a/{expected} b/{expected}\n", Diff(change), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditFile_CrLfFile_KeepsCrLfInDiffAndOnDisk()
    {
        var file = Path.Combine(_workspace, "crlf.txt");
        await File.WriteAllTextAsync(file, "a\r\nb\r\nc\r\n");

        await InvokeAsync(tools => tools.EditFile("crlf.txt", "b", "B"));

        var change = SingleChange();
        Assert.Contains(" a\r\n-b\r\n+B\r\n c\r\n", Diff(change), StringComparison.Ordinal);
        Assert.Equal((1, 1), Counts(change));
        Assert.Equal("a\r\nB\r\nc\r\n", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task EditFile_Utf8BomFile_ReportsTextWithoutBom()
    {
        var file = Path.Combine(_workspace, "bom.txt");
        await File.WriteAllTextAsync(file, "one\ntwo\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await InvokeAsync(tools => tools.EditFile("bom.txt", "two", "2"));

        var diff = Diff(SingleChange());
        Assert.DoesNotContain("\uFEFF", diff, StringComparison.Ordinal);
        Assert.Contains(" one\n-two\n+2\n", diff, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetPreamble(), (await File.ReadAllBytesAsync(file))[..3]);
    }

    [Fact]
    public async Task WriteFile_NoTrailingNewline_MarksDiff()
    {
        await InvokeAsync(tools => tools.WriteFile("plain.txt", "x"));

        Assert.EndsWith("+x\n\\ No newline at end of file\n", Diff(SingleChange()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditFile_FuzzyMatch_DiffsTheRealFileText()
    {
        var file = Path.Combine(_workspace, "a.cs");
        const string original = "class A\n{\n    void M()\n    {\n    }\n}\n";
        await File.WriteAllTextAsync(file, original);

        var result = await InvokeAsync(tools => tools.EditFile("a.cs", "void M()\n{\n}", "void N()\n{\n}"));

        Assert.EndsWith("fallback)", result, StringComparison.Ordinal);
        var change = SingleChange();
        var written = await File.ReadAllTextAsync(file);
        Assert.Contains($"index {GitBlobOid.Compute(original)}..{GitBlobOid.Compute(written)}\n", Diff(change), StringComparison.Ordinal);
        Assert.Contains("-    void M()\n", Diff(change), StringComparison.Ordinal);
        Assert.Contains("+void N()\n", Diff(change), StringComparison.Ordinal);
        Assert.Equal((3, 3), Counts(change));
    }

    [Fact]
    public async Task WriteFile_SameContent_AttachesEmptyUpdateWithoutDiff()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "same.txt"), "same\n");

        await InvokeAsync(tools => tools.WriteFile("same.txt", "same\n"));

        var change = SingleChange();
        Assert.Equal(["path", "kind", "additions", "deletions"], change.EnumerateObject().Select(p => p.Name));
        Assert.Equal("update", change.GetProperty("kind").GetString());
        Assert.Equal((0, 0), Counts(change));
    }

    [Fact]
    public async Task WriteFile_OversizedDiff_ReportsTruncatedWithCounts()
    {
        var content = string.Concat(Enumerable.Repeat("line of text\n", 12_000));

        await InvokeAsync(tools => tools.WriteFile("big.txt", content));

        var change = SingleChange();
        Assert.Equal(["path", "kind", "additions", "deletions", "truncated"], change.EnumerateObject().Select(p => p.Name));
        Assert.True(change.GetProperty("truncated").GetBoolean());
        Assert.Equal((12_000, 0), Counts(change));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("notFound")]
    [InlineData("ambiguous")]
    [InlineData("blocked")]
    public async Task ErrorResults_AttachNothing(string failure)
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "x\nx\ny\n");

        var result = failure switch
        {
            "missing" => await InvokeAsync(tools => tools.EditFile("missing.txt", "x", "z")),
            "notFound" => await InvokeAsync(tools => tools.EditFile("notes.txt", "absent", "z")),
            "ambiguous" => await InvokeAsync(tools => tools.EditFile("notes.txt", "x", "z")),
            _ => await InvokeAsync(
                tools => tools.WriteFile(Path.Combine(_outside, "blocked.txt"), "z"),
                new FileTools(_workspace, requireApprovalOutsideWorkspace: false)),
        };

        Assert.StartsWith("Error", result, StringComparison.Ordinal);
        Assert.Null(_attachments.StructuredContent);
    }

    [Fact]
    public async Task WithoutScope_WritesAndEditsWithUnchangedResults()
    {
        var tools = new FileTools(_workspace);

        Assert.Null(ToolResultAttachmentScope.Current);
        Assert.Equal("Successfully wrote 2 bytes (2 lines) to a.txt", await tools.WriteFile("a.txt", "x\n"));
        Assert.Equal("Successfully edited a.txt at line 1 (1 -> 1 lines)", await tools.EditFile("a.txt", "x", "y"));
        Assert.Equal("y\n", await File.ReadAllTextAsync(Path.Combine(_workspace, "a.txt")));
    }

    [Fact]
    public async Task WriteFile_InsideTurnDiffScope_TracksTheChange()
    {
        var tracker = new TurnDiffTracker();

        using (TurnDiffTrackerScope.Set(tracker))
            await new FileTools(_workspace).WriteFile("tracked.txt", "t\n");

        Assert.True(tracker.TryTakeSnapshot(out var diff));
        Assert.StartsWith("diff --git a/tracked.txt b/tracked.txt\nnew file mode 100644\n", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedWriteFile_ThroughRuntime_KeepsContentAndAttachesChange()
    {
        var function = GeneratedToolFunctions.FileTools_WriteFile(new FileTools(_workspace));
        var context = new ToolInvocationContext(
            "thread_test",
            "turn_test",
            "call_test",
            ToolInvocationAudience.Model,
            new ToolName(null, "WriteFile"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("WriteFile")),
            new RuntimeBindingId("native:test:WriteFile:1"),
            1,
            DateTimeOffset.UtcNow);

        var result = await new AIFunctionToolRuntime(function).InvokeAsync(
            context,
            new JsonObject { ["path"] = "gen.txt", ["content"] = "g\n" });

        Assert.True(result.Success);
        Assert.Equal("Successfully wrote 2 bytes (2 lines) to gen.txt", result.Content);
        Assert.True(FileChangeStructuredContent.IsFileChange(result.StructuredContent));
        var change = Assert.Single(result.StructuredContent!.Value.GetProperty("changes").EnumerateArray());
        Assert.Equal("gen.txt", change.GetProperty("path").GetString());
    }

    private async Task<string> InvokeAsync(Func<FileTools, Task<string>> call, FileTools? tools = null)
    {
        using var scope = ToolResultAttachmentScope.Set(_attachments);
        return await call(tools ?? new FileTools(_workspace));
    }

    private JsonElement SingleChange()
    {
        Assert.True(FileChangeStructuredContent.IsFileChange(_attachments.StructuredContent));
        return Assert.Single(_attachments.StructuredContent!.Value.GetProperty("changes").EnumerateArray());
    }

    private static string Diff(JsonElement change) => change.GetProperty("diff").GetString()!;

    private static (int Additions, int Deletions) Counts(JsonElement change) =>
        (change.GetProperty("additions").GetInt32(), change.GetProperty("deletions").GetInt32());

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotcraft-filetools-change-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
