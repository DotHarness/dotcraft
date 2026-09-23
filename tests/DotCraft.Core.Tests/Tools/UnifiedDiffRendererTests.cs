using System.Diagnostics;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public class UnifiedDiffRendererTests
{
    [Fact]
    public void Render_NewFile_MatchesGitNewFileDiff()
    {
        var result = Render("a.txt", null, "foo\n");

        Assert.Equal(Lines(
            "diff --git a/a.txt b/a.txt",
            "new file mode 100644",
            "index 0000000000000000000000000000000000000000..257cc5642cb1a054f08cc83f2d943e56fd3ebe99",
            "--- /dev/null",
            "+++ b/a.txt",
            "@@ -0,0 +1 @@",
            "+foo"), result.Diff);
        Assert.Equal((1, 0, false), (result.Additions, result.Deletions, result.Truncated));
    }

    [Fact]
    public void Render_NewMultiLineFile_AddsEveryLine()
    {
        var result = Render("a.txt", null, "foo\nbar\n");

        Assert.Equal(Lines(
            "diff --git a/a.txt b/a.txt",
            "new file mode 100644",
            $"index {GitBlobOid.Zero}..{GitBlobOid.Compute("foo\nbar\n")}",
            "--- /dev/null",
            "+++ b/a.txt",
            "@@ -0,0 +1,2 @@",
            "+foo",
            "+bar"), result.Diff);
        Assert.Equal((2, 0), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_NewEmptyFile_EmitsHeaderWithoutHunks()
    {
        var result = Render("empty.txt", null, "");

        Assert.Equal(Lines(
            "diff --git a/empty.txt b/empty.txt",
            "new file mode 100644",
            "index 0000000000000000000000000000000000000000..e69de29bb2d1d6434b8b29ae775ad8c2e48c5391"), result.Diff);
        Assert.Equal((0, 0), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_DeletedFile_MatchesGitDeletedFileDiff()
    {
        var result = Render("b.txt", "x\n", null);

        Assert.Equal(Lines(
            "diff --git a/b.txt b/b.txt",
            "deleted file mode 100644",
            $"index {GitBlobOid.Compute("x\n")}..{GitBlobOid.Zero}",
            "--- a/b.txt",
            "+++ /dev/null",
            "@@ -1 +0,0 @@",
            "-x"), result.Diff);
        Assert.Equal((0, 1), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_UpdateInMiddle_ShowsThreeContextLinesEachSide()
    {
        var before = Numbered(10);
        var after = Numbered(10, (5, "changed 5"));

        var result = Render("f.txt", before, after);

        Assert.Equal(Lines(
            "diff --git a/f.txt b/f.txt",
            $"index {GitBlobOid.Compute(before)}..{GitBlobOid.Compute(after)}",
            "--- a/f.txt",
            "+++ b/f.txt",
            "@@ -2,7 +2,7 @@",
            " line 2",
            " line 3",
            " line 4",
            "-line 5",
            "+changed 5",
            " line 6",
            " line 7",
            " line 8"), result.Diff);
        Assert.Equal((1, 1, false), (result.Additions, result.Deletions, result.Truncated));
    }

    [Fact]
    public void Render_DistantChanges_ProduceSeparateHunksClampedToFile()
    {
        var result = Render("f.txt", Numbered(20), Numbered(20, (2, "changed 2"), (18, "changed 18")));

        Assert.Equal(Lines(
            "@@ -1,5 +1,5 @@",
            " line 1",
            "-line 2",
            "+changed 2",
            " line 3",
            " line 4",
            " line 5",
            "@@ -15,6 +15,6 @@",
            " line 15",
            " line 16",
            " line 17",
            "-line 18",
            "+changed 18",
            " line 19",
            " line 20"), Hunks(result));
        Assert.Equal((2, 2), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_ChangesFiveLinesApart_MergeIntoOneHunk()
    {
        var result = Render("f.txt", Numbered(20), Numbered(20, (5, "changed 5"), (11, "changed 11")));

        Assert.Equal(Lines(
            "@@ -2,13 +2,13 @@",
            " line 2",
            " line 3",
            " line 4",
            "-line 5",
            "+changed 5",
            " line 6",
            " line 7",
            " line 8",
            " line 9",
            " line 10",
            "-line 11",
            "+changed 11",
            " line 12",
            " line 13",
            " line 14"), Hunks(result));
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    public void Render_ChangesMergeOnlyWhenGapFitsBothContexts(int gap, int expectedHunks)
    {
        var result = Render("f.txt", Numbered(30), Numbered(30, (10, "x"), (11 + gap, "y")));

        Assert.Equal(expectedHunks, Hunks(result).Split('\n').Count(line => line.StartsWith("@@ ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("a\nb", "a\nb\n", "@@ -1,2 +1,2 @@\n a\n-b\n\\ No newline at end of file\n+b\n")]
    [InlineData("a\nb\n", "a\nb", "@@ -1,2 +1,2 @@\n a\n-b\n+b\n\\ No newline at end of file\n")]
    [InlineData("a\nb", "a\nc", "@@ -1,2 +1,2 @@\n a\n-b\n\\ No newline at end of file\n+c\n\\ No newline at end of file\n")]
    [InlineData("a\nb\nc", "A\nb\nc", "@@ -1,3 +1,3 @@\n-a\n+A\n b\n c\n\\ No newline at end of file\n")]
    public void Render_MissingTrailingNewline_IsMarkedAfterTheLine(string before, string after, string expectedHunks)
    {
        Assert.Equal(expectedHunks, Hunks(Render("f.txt", before, after)));
    }

    [Fact]
    public void Render_CrlfFile_KeepsLineTerminatorsVerbatim()
    {
        var result = Render("crlf.txt", "one\r\ntwo\r\nthree\r\n", "one\r\nTWO\r\nthree\r\n");

        Assert.StartsWith("diff --git a/crlf.txt b/crlf.txt\nindex ", result.Diff);
        Assert.Contains("\n--- a/crlf.txt\n+++ b/crlf.txt\n", result.Diff);
        Assert.Equal("@@ -1,3 +1,3 @@\n one\r\n-two\r\n+TWO\r\n three\r\n", Hunks(result));
        Assert.Equal((1, 1), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_LineEndingConversion_IsAChange()
    {
        var result = Render("f.txt", "a\r\nb\r\n", "a\nb\n");

        Assert.Equal("@@ -1,2 +1,2 @@\n-a\r\n-b\r\n+a\n+b\n", Hunks(result));
        Assert.Equal((2, 2), (result.Additions, result.Deletions));
    }

    [Fact]
    public void Render_WindowsDisplayPath_UsesForwardSlashes()
    {
        var result = Render(@"src\Foo.cs", "a\n", "b\n");

        Assert.Equal(Lines(
            "diff --git a/src/Foo.cs b/src/Foo.cs",
            $"index {GitBlobOid.Compute("a\n")}..{GitBlobOid.Compute("b\n")}",
            "--- a/src/Foo.cs",
            "+++ b/src/Foo.cs",
            "@@ -1 +1 @@",
            "-a",
            "+b"), result.Diff);
    }

    [Theory]
    [InlineData("same\n", "same\n")]
    [InlineData(null, null)]
    public void Render_UnchangedContent_ReturnsNoDiff(string? before, string? after)
    {
        Assert.Equal(new UnifiedDiffResult(null, 0, 0, false), Render("f.txt", before, after));
    }

    [Fact]
    public void Render_MiddleBeyondMyersCap_FallsBackToOneReplaceBlock()
    {
        const string before = "h\n1\n2\n3\n4\n5\nt\n";
        const string after = "h\n1\nX\n3\nY\n5\nZ\nt\n";

        var precise = UnifiedDiffRenderer.Render("f.txt", before, after, new UnifiedDiffLimits(1024, MaxMyersLines: 5));
        var coarse = UnifiedDiffRenderer.Render("f.txt", before, after, new UnifiedDiffLimits(1024, MaxMyersLines: 4));

        Assert.Equal((3, 2), (precise.Additions, precise.Deletions));
        Assert.Equal(Lines(
            "@@ -1,7 +1,8 @@",
            " h",
            " 1",
            "-2",
            "-3",
            "-4",
            "-5",
            "+X",
            "+3",
            "+Y",
            "+5",
            "+Z",
            " t"), Hunks(coarse));
        Assert.Equal((5, 4), (coarse.Additions, coarse.Deletions));
    }

    [Fact]
    public void Render_LargeRewrite_ReturnsPromptlyWithExactCounts()
    {
        var before = string.Concat(Enumerable.Range(0, 50_000).Select(i => $"old line {i:D5}\n"));
        var after = string.Concat(Enumerable.Range(0, 50_000).Select(i => $"new line {i:D5}\n"));

        var stopwatch = Stopwatch.StartNew();
        var result = UnifiedDiffRenderer.Render("large.txt", before, after, UnifiedDiffLimits.Aggregate);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Large rewrite took {stopwatch.Elapsed}.");
        Assert.Equal(new UnifiedDiffResult(null, 50_000, 50_000, true), result);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(120)]
    public void Render_DiffOverCharCap_KeepsCountsOnly(int maxDiffChars)
    {
        var result = UnifiedDiffRenderer.Render(
            "f.txt", "one\ntwo\nthree\n", "one\nTWO\nthree\n", new UnifiedDiffLimits(maxDiffChars, 4_000));

        Assert.Equal(new UnifiedDiffResult(null, 1, 1, true), result);
    }

    [Theory]
    [InlineData("foo\n", "257cc5642cb1a054f08cc83f2d943e56fd3ebe99")]
    [InlineData("", "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391")]
    [InlineData("héllo 世界\n", "fe67831147fa170e25d9d28aeb9a9ac1dc3285b8")]
    public void GitBlobOid_MatchesGitHashObject(string text, string expected)
    {
        Assert.Equal(expected, GitBlobOid.Compute(text));
    }

    private static UnifiedDiffResult Render(string path, string? before, string? after) =>
        UnifiedDiffRenderer.Render(path, before, after, UnifiedDiffLimits.PerCall);

    private static string Hunks(UnifiedDiffResult result)
    {
        Assert.NotNull(result.Diff);
        return result.Diff[result.Diff.IndexOf("@@ ", StringComparison.Ordinal)..];
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + "\n"));

    private static string Numbered(int count, params (int Line, string Text)[] changes) =>
        string.Concat(Enumerable.Range(1, count).Select(i =>
            (changes.FirstOrDefault(change => change.Line == i).Text ?? $"line {i}") + "\n"));
}
